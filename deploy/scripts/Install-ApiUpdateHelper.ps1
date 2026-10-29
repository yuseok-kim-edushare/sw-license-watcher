#Requires -Version 5.1
#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Manually installs the Windows API update helper.

.DESCRIPTION
    Does not install or change the API service itself. Run this once, elevated, when an
    administrator wants /admin to apply staged server packages. Windows Service and IIS
    use the same script. Pass -AppPool for an IIS site.

.PARAMETER InstallPath
    Directory that contains the running API (SwLicenseWatcher.Api.exe or SwLicenseWatcher.Api.dll).

.PARAMETER AppPool
    IIS application pool name. When set, the helper restarts this pool instead of a Windows service
    and grants that pool identity write access only to the staging directory.

.PARAMETER ServiceName
    Windows service name. Used when -AppPool is omitted.

.EXAMPLE
    .\Install-ApiUpdateHelper.ps1 -InstallPath "C:\Program Files\SwLicenseWatcher\Api"

.EXAMPLE
    .\Install-ApiUpdateHelper.ps1 -InstallPath "C:\inetpub\sw-license-watcher" -AppPool "SwLicenseWatcher"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $InstallPath,

    [string] $AppPool,

    [string] $ServiceName = "SwLicenseWatcher.Api",

    [string] $StagingPath = "C:\ProgramData\SwLicenseWatcher\api-update",

    [string] $HealthUrl,

    [string] $HelperDirectory = "C:\Program Files\SwLicenseWatcher\ApiUpdate",

    [string] $TaskName = "SwLicenseWatcher Api Update"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Failure {
    param([Parameter(Mandatory)] [string] $Message)
    Write-Host "FAILED: $Message" -ForegroundColor Red
}

function Grant-Directory {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string[]] $Grants
    )
    & icacls.exe $Path /inheritance:r | Out-Null
    foreach ($grant in $Grants) {
        & icacls.exe $Path /grant $grant | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "icacls failed for $grant"
        }
    }
}

$resolvedInstall = [System.IO.Path]::GetFullPath($InstallPath)
$resolvedStaging = [System.IO.Path]::GetFullPath($StagingPath)
$root = [System.IO.Path]::GetPathRoot($resolvedInstall)
if ($resolvedInstall.TrimEnd('\') -eq $root.TrimEnd('\')) {
    Write-Failure "InstallPath must be the API directory, not the drive root."
    exit 1
}

$exe = Join-Path $resolvedInstall "SwLicenseWatcher.Api.exe"
$dll = Join-Path $resolvedInstall "SwLicenseWatcher.Api.dll"
if (-not (Test-Path -LiteralPath $exe) -and -not (Test-Path -LiteralPath $dll)) {
    Write-Failure "InstallPath does not contain SwLicenseWatcher.Api.exe or SwLicenseWatcher.Api.dll."
    exit 1
}

$sourceScript = Join-Path $PSScriptRoot "Apply-ApiUpdate.ps1"
if (-not (Test-Path -LiteralPath $sourceScript)) {
    Write-Failure "Apply-ApiUpdate.ps1 was not found next to this installer."
    exit 1
}

$hostKind = "windows-service"
if (-not [string]::IsNullOrWhiteSpace($AppPool)) {
    $hostKind = "iis"
    if ($AppPool -notmatch '^[A-Za-z0-9 ._-]{1,80}$') {
        Write-Failure "AppPool contains characters this installer will not write into helper.conf."
        exit 1
    }
}
elseif ($ServiceName -notmatch '^[A-Za-z0-9._-]{1,80}$') {
    Write-Failure "ServiceName contains characters this installer will not write into helper.conf."
    exit 1
}

if (-not [string]::IsNullOrWhiteSpace($HealthUrl)) {
    $uri = $null
    if (-not [Uri]::TryCreate($HealthUrl, [UriKind]::Absolute, [ref] $uri) -or
        ($uri.Scheme -ne "http" -and $uri.Scheme -ne "https")) {
        Write-Failure "HealthUrl must be an absolute http or https URL."
        exit 1
    }
}

New-Item -ItemType Directory -Force -Path $HelperDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $resolvedStaging | Out-Null
Copy-Item -LiteralPath $sourceScript -Destination (Join-Path $HelperDirectory "Apply-ApiUpdate.ps1") -Force

$conf = @(
    "HostKind=$hostKind"
    "InstallPath=$resolvedInstall"
    "ServiceName=$ServiceName"
    "AppPoolName=$AppPool"
    "StagingPath=$resolvedStaging"
    "HealthUrl=$HealthUrl"
    "ServiceUser="
    "ServiceGroup="
    "TaskName=$TaskName"
    "UpdateUnitName=sw-license-watcher-api-update.service"
) -join "`n"
$utf8 = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText((Join-Path $HelperDirectory "helper.conf"), $conf + "`n", $utf8)

$helperGrants = @("SYSTEM:(OI)(CI)F", "Administrators:(OI)(CI)F")
$stagingGrants = @("SYSTEM:(OI)(CI)F", "Administrators:(OI)(CI)F")
if ($hostKind -eq "iis") {
    $identity = "IIS APPPOOL\$AppPool"
    $helperGrants += "${identity}:(OI)(CI)RX"
    $stagingGrants += "${identity}:(OI)(CI)M"
}

Grant-Directory -Path $HelperDirectory -Grants $helperGrants
Grant-Directory -Path $resolvedStaging -Grants $stagingGrants

$scriptPath = Join-Path $HelperDirectory "Apply-ApiUpdate.ps1"
$action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File `"$scriptPath`""
$principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Hours 1)
Register-ScheduledTask -TaskName $TaskName -Action $action -Principal $principal -Settings $settings -Force | Out-Null

if ($hostKind -eq "iis") {
    $taskFile = Join-Path $env:SystemRoot "System32\Tasks\$TaskName"
    if (Test-Path -LiteralPath $taskFile) {
        & icacls.exe $taskFile /grant "IIS APPPOOL\${AppPool}:(RX)" | Out-Null
    }
}

Write-Host "OK: Update helper installed for $resolvedInstall ($hostKind)." -ForegroundColor Green
Write-Host "The API installer does not register this helper. Remove it with Uninstall-ApiUpdateHelper.ps1."
