#Requires -Version 5.1
#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs or upgrades the API as a Kestrel Windows Service from a Native AOT publish folder.

.DESCRIPTION
    Copies api/win-x64 (Native AOT Kestrel) onto the server, writes appsettings.json from the
    company template with connection string and tokens, then registers SwLicenseWatcher.Api
    as an Automatic Windows Service with restart-on-failure recovery.
    This script does not install the IIS in-process publish output (api/iis/win-x64).

.PARAMETER SourcePath
    Unzipped GitHub Release folder or the api/win-x64 publish directory. api/iis/win-x64 is rejected.

.PARAMETER ConnectionString
    SQL Server connection string written to Storage:SqlServer:ConnectionString.

.PARAMETER AgentToken
    Bearer token for agent snapshot/heartbeat/manifest calls (32+ characters).
    Same value as PC Agent:ApiToken / Watchdog:ApiToken.

.PARAMETER AdminToken
    Bearer token for inventory queries, policy CRUD, violations, and design/schema (32+ characters).
    Must differ from AgentToken.

.PARAMETER ListenUrl
    Optional Kestrel listen URL written to Kestrel:Endpoints:Https:Url (or Http for loopback).
    HTTPS certificate store settings are not changed; see docs/company-deployment.md.

.PARAMETER InstallRoot
    Parent install directory. The API is copied to InstallRoot\Api.

.PARAMETER ServiceName
    Windows Service name.

.PARAMETER FirewallPort
    Optional TCP port for an inbound Windows Firewall allow rule. Omitted means no firewall change.

.PARAMETER ApplySchemaOnStartup
    Set Database:ApplySchemaOnStartup to true so the API applies idempotent DDL on start.

.PARAMETER ExampleConfigDirectory
    Folder containing appsettings.api.company.json.

.EXAMPLE
    .\Install-ApiServer.ps1 -SourcePath D:\SwLicenseWatcher-1.0.1 -ConnectionString $cs -AgentToken $agentToken -AdminToken $adminToken

.EXAMPLE
    .\Install-ApiServer.ps1 -SourcePath D:\SwLicenseWatcher-1.0.1\api\win-x64 -ConnectionString $cs -AgentToken $agentToken -AdminToken $adminToken -ListenUrl https://0.0.0.0:443 -FirewallPort 443
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $SourcePath,

    [Parameter(Mandatory)]
    [string] $ConnectionString,

    [Parameter(Mandatory)]
    [string] $AgentToken,

    [Parameter(Mandatory)]
    [string] $AdminToken,

    [string] $ListenUrl,

    [string] $InstallRoot = "C:\Program Files\SwLicenseWatcher",

    [string] $ServiceName = "SwLicenseWatcher.Api",

    [int] $FirewallPort,

    [switch] $ApplySchemaOnStartup,

    [string] $ExampleConfigDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$exeFileName = "SwLicenseWatcher.Api.exe"
$firewallRuleName = "SW License Watcher API"
$serviceDisplayName = "SW License Watcher API"
$serviceDescription = "Hosts the SW License Watcher ASP.NET Core API (Kestrel)."

function Write-Failure {
    param([Parameter(Mandatory)] [string] $Message)
    Write-Host "FAILED: $Message" -ForegroundColor Red
}

function Write-Success {
    param([Parameter(Mandatory)] [string] $Message)
    Write-Host "OK: $Message" -ForegroundColor Green
}

function Test-IsWindows {
    if ($PSVersionTable.PSVersion.Major -ge 6) {
        return [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
            [System.Runtime.InteropServices.OSPlatform]::Windows)
    }
    return $true
}

function Test-HasText {
    param($Value)
    return -not [string]::IsNullOrWhiteSpace([string] $Value)
}

function Resolve-ComponentPath {
    param(
        [Parameter(Mandatory)] [string] $Root,
        [Parameter(Mandatory)] [string] $RelativePath
    )

    $resolvedRoot = (Resolve-Path -LiteralPath $Root).Path
    $direct = Join-Path $resolvedRoot $RelativePath
    if (Test-Path -LiteralPath $direct) {
        return $direct
    }

    Get-ChildItem -LiteralPath $resolvedRoot -Directory -ErrorAction SilentlyContinue | ForEach-Object {
        $nested = Join-Path $_.FullName $RelativePath
        if (Test-Path -LiteralPath $nested) {
            $nested
        }
    } | Select-Object -First 1
}

function Import-AppSettings {
    param([Parameter(Mandatory)] [string] $Path)
    Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
}

function Export-AppSettings {
    param(
        [Parameter(Mandatory)] $Object,
        [Parameter(Mandatory)] [string] $Path
    )
    $json = $Object | ConvertTo-Json -Depth 20
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($Path, $json + [Environment]::NewLine, $utf8)
}

function Copy-PublishOutput {
    param(
        [Parameter(Mandatory)] [string] $From,
        [Parameter(Mandatory)] [string] $To
    )
    New-Item -ItemType Directory -Path $To -Force | Out-Null
    Copy-Item -Path (Join-Path $From '*') -Destination $To -Recurse -Force
}

function Get-OrAddProperty {
    param(
        [Parameter(Mandatory)] $Object,
        [Parameter(Mandatory)] [string] $Name
    )

    $prop = $Object.PSObject.Properties[$Name]
    if ($null -eq $prop -or $null -eq $prop.Value) {
        $value = [pscustomobject]@{}
        $Object | Add-Member -NotePropertyName $Name -NotePropertyValue $value -Force
        return $value
    }

    return $prop.Value
}

function Set-NotePropertyValue {
    param(
        [Parameter(Mandatory)] $Object,
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] $Value
    )

    if ($null -eq $Object.PSObject.Properties[$Name]) {
        $Object | Add-Member -NotePropertyName $Name -NotePropertyValue $Value
        return
    }

    $Object.$Name = $Value
}

function Assert-ListenUrl {
    param([Parameter(Mandatory)] [string] $Url)

    $uri = [Uri]::new($Url)
    if (-not $uri.IsAbsoluteUri) {
        throw "ListenUrl must be an absolute URL."
    }

    $https = $uri.Scheme -eq [Uri]::UriSchemeHttps
    $httpLoopback = $uri.Scheme -eq [Uri]::UriSchemeHttp -and $uri.IsLoopback
    if (-not ($https -or $httpLoopback)) {
        throw "ListenUrl must use HTTPS (HTTP is allowed only for loopback diagnostics)."
    }
}

function Set-KestrelListenUrl {
    param(
        [Parameter(Mandatory)] $Settings,
        [Parameter(Mandatory)] [string] $Url
    )

    $uri = [Uri]::new($Url)
    $endpointName = if ($uri.Scheme -eq [Uri]::UriSchemeHttps) { "Https" } else { "Http" }
    $kestrel = Get-OrAddProperty -Object $Settings -Name "Kestrel"
    $endpoints = Get-OrAddProperty -Object $kestrel -Name "Endpoints"
    $endpoint = Get-OrAddProperty -Object $endpoints -Name $endpointName
    Set-NotePropertyValue -Object $endpoint -Name "Url" -Value $Url
}

function Stop-ServiceIfPresent {
    param([Parameter(Mandatory)] [string] $Name)

    $svc = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if ($null -eq $svc) {
        return
    }
    if ($svc.Status -eq [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
        return
    }
    Stop-Service -Name $Name -Force
    $svc.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromMinutes(1))
}

function Set-ServiceRestartRecovery {
    param([Parameter(Mandatory)] [string] $Name)

    & sc.exe failure $Name reset= 86400 actions= restart/5000/restart/30000/restart/60000 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "sc.exe failure $Name failed with exit code $LASTEXITCODE."
    }

    & sc.exe failureflag $Name 1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "sc.exe failureflag $Name failed with exit code $LASTEXITCODE."
    }
}

function Install-OrUpdateService {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $DisplayName,
        [Parameter(Mandatory)] [string] $Description,
        [Parameter(Mandatory)] [string] $ExePath
    )

    $binPath = '"{0}"' -f $ExePath
    $existing = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if ($null -ne $existing) {
        Stop-ServiceIfPresent -Name $Name
        $quotedDisplayName = '"{0}"' -f $DisplayName
        & sc.exe config $Name binPath= $binPath start= auto DisplayName= $quotedDisplayName | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "sc.exe config $Name failed with exit code $LASTEXITCODE."
        }
        $quotedDescription = '"{0}"' -f $Description
        & sc.exe description $Name $quotedDescription | Out-Null
        Set-ServiceRestartRecovery -Name $Name
        return
    }

    New-Service -Name $Name -BinaryPathName $binPath -DisplayName $DisplayName -StartupType Automatic -Description $Description | Out-Null
    Set-ServiceRestartRecovery -Name $Name
}

function Import-SettingsOrPublish {
    param(
        [Parameter(Mandatory)] [string] $ExamplePath,
        [Parameter(Mandatory)] [string] $PublishSettingsPath,
        [Parameter(Mandatory)] [string] $Label
    )

    if (Test-Path -LiteralPath $ExamplePath) {
        return Import-AppSettings -Path $ExamplePath
    }
    if (Test-Path -LiteralPath $PublishSettingsPath) {
        Write-Host "Company example not found for $Label; overlaying $PublishSettingsPath"
        return Import-AppSettings -Path $PublishSettingsPath
    }
    throw "No $Label appsettings template found at '$ExamplePath' and no appsettings.json in the publish output."
}

function Resolve-ApiSourcePath {
    param([Parameter(Mandatory)] [string] $Root)

    if (-not (Test-Path -LiteralPath $Root)) {
        throw "SourcePath not found: $Root"
    }

    $resolvedRoot = (Resolve-Path -LiteralPath $Root).Path
    $directExe = Join-Path $resolvedRoot $exeFileName
    if (Test-Path -LiteralPath $directExe) {
        return $resolvedRoot
    }

    $nested = Resolve-ComponentPath -Root $resolvedRoot -RelativePath "api\win-x64"
    if ((Test-HasText $nested) -and (Test-Path -LiteralPath (Join-Path $nested $exeFileName))) {
        return $nested
    }

    $iisNested = Resolve-ComponentPath -Root $resolvedRoot -RelativePath "api\iis\win-x64"
    if (Test-HasText $iisNested) {
        throw "Found IIS publish output at '$iisNested'. Install-ApiServer.ps1 registers a Kestrel Windows Service and requires api\win-x64. For IIS, follow docs\company-deployment.md."
    }

    $webConfig = Join-Path $resolvedRoot "web.config"
    if (Test-Path -LiteralPath $webConfig) {
        throw "This folder looks like the IIS publish output (web.config without $exeFileName). Use api\win-x64 for this script, or follow the IIS steps in docs\company-deployment.md."
    }

    throw "Could not find $exeFileName under '$Root'. Pass the unzipped Release folder or api\win-x64."
}

function Set-InboundFirewallPort {
    param(
        [Parameter(Mandatory)] [string] $RuleName,
        [Parameter(Mandatory)] [int] $Port
    )

    if ($null -ne (Get-Command New-NetFirewallRule -ErrorAction SilentlyContinue)) {
        $existing = Get-NetFirewallRule -DisplayName $RuleName -ErrorAction SilentlyContinue
        if ($null -ne $existing) {
            Remove-NetFirewallRule -DisplayName $RuleName
        }

        New-NetFirewallRule -DisplayName $RuleName -Direction Inbound -Action Allow -Protocol Tcp -LocalPort $Port -Profile Any | Out-Null
        return
    }

    & netsh advfirewall firewall delete rule name=$RuleName | Out-Null
    $output = & netsh advfirewall firewall add rule name=$RuleName dir=in action=allow protocol=TCP localport=$Port profile=any
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to add firewall rule '$RuleName' for TCP port $Port. $output"
    }
}

try {
    if (-not (Test-IsWindows)) {
        throw "Install-ApiServer.ps1 can only run on Windows."
    }

    if ([string]::IsNullOrWhiteSpace($ServiceName)) {
        throw "ServiceName is required."
    }

    if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
        throw "ConnectionString is required."
    }

    if ([string]::IsNullOrWhiteSpace($AgentToken) -or $AgentToken.Length -lt 32) {
        throw "AgentToken must be at least 32 characters. Generate one with New-ApiToken.ps1."
    }
    if ([string]::IsNullOrWhiteSpace($AdminToken) -or $AdminToken.Length -lt 32) {
        throw "AdminToken must be at least 32 characters. Generate one with New-ApiToken.ps1."
    }
    if ($AgentToken -eq $AdminToken) {
        throw "AgentToken must differ from AdminToken."
    }

    $hasListenUrl = Test-HasText $ListenUrl
    if ($hasListenUrl) {
        Assert-ListenUrl -Url $ListenUrl
    }

    $hasFirewallPort = $PSBoundParameters.ContainsKey("FirewallPort")
    if ($hasFirewallPort -and ($FirewallPort -lt 1 -or $FirewallPort -gt 65535)) {
        throw "FirewallPort must be between 1 and 65535."
    }

    $apiSource = Resolve-ApiSourcePath -Root $SourcePath
    $apiDir = Join-Path $InstallRoot "Api"
    $apiExe = Join-Path $apiDir $exeFileName

    if ([string]::IsNullOrWhiteSpace($ExampleConfigDirectory)) {
        $ExampleConfigDirectory = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\examples"))
    }

    Write-Host "Installing API from $apiSource"
    Write-Host "Install directory: $apiDir"
    Write-Host "Service: $ServiceName (Automatic, restart on failure)"
    if ($hasListenUrl) {
        Write-Host "ListenUrl=$ListenUrl"
    }
    if ($hasFirewallPort) {
        Write-Host "Firewall inbound TCP $FirewallPort (rule '$firewallRuleName')"
    }

    Stop-ServiceIfPresent -Name $ServiceName
    Copy-PublishOutput -From $apiSource -To $apiDir
    if (-not (Test-Path -LiteralPath $apiExe)) {
        throw "Expected executable was not copied: $apiExe"
    }

    $settings = Import-SettingsOrPublish `
        -ExamplePath (Join-Path $ExampleConfigDirectory "appsettings.api.company.json") `
        -PublishSettingsPath (Join-Path $apiDir "appsettings.json") `
        -Label "API"

    $security = Get-OrAddProperty -Object $settings -Name "Security"
    Set-NotePropertyValue -Object $security -Name "AgentToken" -Value $AgentToken
    Set-NotePropertyValue -Object $security -Name "AdminToken" -Value $AdminToken

    $storage = Get-OrAddProperty -Object $settings -Name "Storage"
    $sqlServer = Get-OrAddProperty -Object $storage -Name "SqlServer"
    Set-NotePropertyValue -Object $sqlServer -Name "ConnectionString" -Value $ConnectionString

    if ($ApplySchemaOnStartup) {
        $database = Get-OrAddProperty -Object $settings -Name "Database"
        Set-NotePropertyValue -Object $database -Name "ApplySchemaOnStartup" -Value $true
    }

    if ($hasListenUrl) {
        Set-KestrelListenUrl -Settings $settings -Url $ListenUrl
    }

    Export-AppSettings -Object $settings -Path (Join-Path $apiDir "appsettings.json")

    Install-OrUpdateService -Name $ServiceName -DisplayName $serviceDisplayName -Description $serviceDescription -ExePath $apiExe

    if ($hasFirewallPort) {
        Set-InboundFirewallPort -RuleName $firewallRuleName -Port $FirewallPort
        Write-Host "Firewall rule '$firewallRuleName' allows inbound TCP $FirewallPort."
    }

    Start-Service -Name $ServiceName
    try {
        (Get-Service -Name $ServiceName).WaitForStatus(
            [System.ServiceProcess.ServiceControllerStatus]::Running,
            [TimeSpan]::FromMinutes(2))
    }
    catch {
        throw "Service $ServiceName did not reach Running. Check the Application event log and $apiDir\appsettings.json."
    }

    Write-Success "Service $ServiceName is running."
    Write-Host "Install directory: $apiDir"
    Write-Host "Confirm with: Invoke-RestMethod -Uri https://<server>/health"
    Write-Host "HTTPS certificates are not installed by this script. Place a certificate in LocalMachine\My and match Kestrel:Endpoints:Https:Certificate:Subject. See docs\company-deployment.md."
}
catch {
    Write-Failure $_.Exception.Message
    throw
}
