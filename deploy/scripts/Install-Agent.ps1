#Requires -Version 5.1
#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs or upgrades Worker and Watchdog Windows Services from a release folder.

.DESCRIPTION
    Copies only agent-worker/win-x64 and agent-watchdog/win-x64 onto the PC. The API
    (api/win-x64) is a server web app and is never installed by this script, even if it
    is present in the same Release folder.
    Writes company appsettings.json for both agent processes, creates ProgramData
    directories, then starts Worker followed by Watchdog.
    DeviceCode defaults to the computer name so each PC reports as a distinct asset.

.PARAMETER SourcePath
    Unzipped GitHub Release folder. Only agent-worker/win-x64 and agent-watchdog/win-x64
    are used. api/win-x64 is ignored.

.PARAMETER ServerBaseUrl
    API base URL. Must be HTTPS unless it is loopback HTTP for diagnostics.

.PARAMETER ApiToken
    Agent bearer token (32+ characters). Same value as API Security:AgentToken
    (or legacy Security:Token).

.PARAMETER DeviceCode
    PC identity stored on the server. Defaults to the machine name.

.PARAMETER DomainName
    Directory domain or workgroup. Defaults to USERDOMAIN, or WORKGROUP if unset.

.PARAMETER InstallRoot
    Parent install directory. Worker and Watchdog go under this path.

.PARAMETER StateRoot
    ProgramData root for the snapshot queue, health file, staging, and backup.

.PARAMETER ExampleConfigDirectory
    Folder containing appsettings.worker.company.json and appsettings.watchdog.company.json.

.EXAMPLE
    .\Install-Agent.ps1 -SourcePath D:\SwLicenseWatcher-1.0.1 -ServerBaseUrl https://license-watcher.contoso.local -ApiToken $token
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $SourcePath,

    [Parameter(Mandatory)]
    [string] $ServerBaseUrl,

    [Parameter(Mandatory)]
    [string] $ApiToken,

    [string] $DeviceCode = $env:COMPUTERNAME,

    [string] $DomainName = $(if ([string]::IsNullOrWhiteSpace($env:USERDOMAIN)) { "WORKGROUP" } else { $env:USERDOMAIN }),

    [string] $InstallRoot = "C:\Program Files\SwLicenseWatcher",

    [string] $StateRoot = "C:\ProgramData\SwLicenseWatcher",

    [string] $ExampleConfigDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$workerServiceName = "SwLicenseWatcher.Agent.Worker"
$watchdogServiceName = "SwLicenseWatcher.Agent.Watchdog"

function Test-IsWindows {
    if ($PSVersionTable.PSVersion.Major -ge 6) {
        return [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
            [System.Runtime.InteropServices.OSPlatform]::Windows)
    }
    return $true
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

function Assert-ServerBaseUrl {
    param([Parameter(Mandatory)] [string] $Url)

    $uri = [Uri]::new($Url)
    if (-not $uri.IsAbsoluteUri) {
        throw "ServerBaseUrl must be an absolute URL."
    }

    $https = $uri.Scheme -eq [Uri]::UriSchemeHttps
    $httpLoopback = $uri.Scheme -eq [Uri]::UriSchemeHttp -and $uri.IsLoopback
    if (-not ($https -or $httpLoopback)) {
        throw "ServerBaseUrl must use HTTPS (HTTP is allowed only for loopback diagnostics)."
    }
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
        return
    }

    New-Service -Name $Name -BinaryPathName $binPath -DisplayName $DisplayName -StartupType Automatic -Description $Description | Out-Null
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

if (-not (Test-IsWindows)) {
    throw "Install-Agent.ps1 can only run on Windows."
}

if ([string]::IsNullOrWhiteSpace($ApiToken) -or $ApiToken.Length -lt 32) {
    throw "ApiToken must be at least 32 characters. Generate one with New-ApiToken.ps1."
}
if ([string]::IsNullOrWhiteSpace($DeviceCode)) {
    throw "DeviceCode is required."
}

Assert-ServerBaseUrl -Url $ServerBaseUrl

$workerSource = Resolve-ComponentPath -Root $SourcePath -RelativePath "agent-worker\win-x64"
$watchdogSource = Resolve-ComponentPath -Root $SourcePath -RelativePath "agent-watchdog\win-x64"
if ([string]::IsNullOrWhiteSpace($workerSource) -or [string]::IsNullOrWhiteSpace($watchdogSource)) {
    throw "Could not find agent-worker\win-x64 and agent-watchdog\win-x64 under '$SourcePath'."
}

$workerDir = Join-Path $InstallRoot "Agent.Worker"
$watchdogDir = Join-Path $InstallRoot "Agent.Watchdog"
$workerExe = Join-Path $workerDir "SwLicenseWatcher.Agent.Worker.exe"
$watchdogExe = Join-Path $watchdogDir "SwLicenseWatcher.Agent.Watchdog.exe"

$queueDir = Join-Path $StateRoot "state\queue"
$healthPath = Join-Path $StateRoot "state\worker-health.json"
$stagingDir = Join-Path $StateRoot "staging"
$backupDir = Join-Path $StateRoot "backup"

if ([string]::IsNullOrWhiteSpace($ExampleConfigDirectory)) {
    $ExampleConfigDirectory = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\examples"))
}

Write-Host "Installing Worker from $workerSource"
Write-Host "Installing Watchdog from $watchdogSource"
Write-Host "The API is not installed on this PC. Agents will call $ServerBaseUrl"
Write-Host "DeviceCode=$DeviceCode DomainName=$DomainName ServerBaseUrl=$ServerBaseUrl"

Stop-ServiceIfPresent -Name $watchdogServiceName
Stop-ServiceIfPresent -Name $workerServiceName

Copy-PublishOutput -From $workerSource -To $workerDir
Copy-PublishOutput -From $watchdogSource -To $watchdogDir
if (-not (Test-Path -LiteralPath $workerExe)) {
    throw "Expected executable was not copied: $workerExe"
}
if (-not (Test-Path -LiteralPath $watchdogExe)) {
    throw "Expected executable was not copied: $watchdogExe"
}

@($queueDir, (Split-Path -Parent $healthPath), $stagingDir, $backupDir) | ForEach-Object {
    New-Item -ItemType Directory -Path $_ -Force | Out-Null
}

$workerSettings = Import-SettingsOrPublish `
    -ExamplePath (Join-Path $ExampleConfigDirectory "appsettings.worker.company.json") `
    -PublishSettingsPath (Join-Path $workerDir "appsettings.json") `
    -Label "Worker"
$workerSettings.Agent.DeviceCode = $DeviceCode
$workerSettings.Agent.DomainName = $DomainName
$workerSettings.Agent.ServerBaseUrl = $ServerBaseUrl
$workerSettings.Agent.ApiToken = $ApiToken
$workerSettings.Agent.HealthFilePath = $healthPath
$workerSettings.LocalState.QueueDirectory = $queueDir
$workerSettings.LocalState.DpapiScope = "LocalMachine"
Export-AppSettings -Object $workerSettings -Path (Join-Path $workerDir "appsettings.json")

$watchdogSettings = Import-SettingsOrPublish `
    -ExamplePath (Join-Path $ExampleConfigDirectory "appsettings.watchdog.company.json") `
    -PublishSettingsPath (Join-Path $watchdogDir "appsettings.json") `
    -Label "Watchdog"
$watchdogSettings.Watchdog.DeviceCode = $DeviceCode
$watchdogSettings.Watchdog.ServerBaseUrl = $ServerBaseUrl
$watchdogSettings.Watchdog.ApiToken = $ApiToken
$watchdogSettings.Watchdog.WorkerServiceName = $workerServiceName
$watchdogSettings.Watchdog.WorkerInstallDirectory = $workerDir
$watchdogSettings.Watchdog.WorkerHealthFilePath = $healthPath
$watchdogSettings.Watchdog.StagingDirectory = $stagingDir
$watchdogSettings.Watchdog.BackupDirectory = $backupDir
Export-AppSettings -Object $watchdogSettings -Path (Join-Path $watchdogDir "appsettings.json")

Install-OrUpdateService -Name $workerServiceName -DisplayName "SW License Watcher Worker" -Description "Collects installed software and sends inventory snapshots." -ExePath $workerExe
Install-OrUpdateService -Name $watchdogServiceName -DisplayName "SW License Watcher Watchdog" -Description "Downloads and applies signed Worker updates." -ExePath $watchdogExe

Start-Service -Name $workerServiceName
(Get-Service -Name $workerServiceName).WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Running, [TimeSpan]::FromSeconds(30))
Start-Service -Name $watchdogServiceName
(Get-Service -Name $watchdogServiceName).WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Running, [TimeSpan]::FromSeconds(30))

Write-Host "Services $workerServiceName and $watchdogServiceName are running."
Write-Host "Health file: $healthPath"
Write-Host "Queue directory: $queueDir"
