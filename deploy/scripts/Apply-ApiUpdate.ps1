#Requires -Version 5.1
#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Applies a staged API package. The scheduled task runs this script as SYSTEM.

.DESCRIPTION
    Reads helper.conf beside this script. The install path, service, and app pool come from that
    file, not from the staged request. appsettings*.json and device-ca*.json stay in place.
    A failed start restores the backup.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$utf8 = New-Object System.Text.UTF8Encoding $false

function Read-Conf {
    param([Parameter(Mandatory)] [string] $Path)
    $map = @{}
    foreach ($line in [System.IO.File]::ReadAllLines($Path)) {
        $trimmed = $line.Trim()
        if ($trimmed.Length -eq 0 -or $trimmed.StartsWith('#')) {
            continue
        }

        $split = $trimmed.IndexOf('=')
        if ($split -lt 1) {
            throw "Invalid helper setting: $trimmed"
        }

        $map[$trimmed.Substring(0, $split).Trim()] = $trimmed.Substring($split + 1).Trim()
    }

    return $map
}

function Write-Result {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $Status,
        [Parameter(Mandatory)] [string] $Version,
        [Parameter(Mandatory)] [string] $Message
    )
    $text = "Status=$Status`nVersion=$Version`nMessage=$Message`n"
    [System.IO.File]::WriteAllText($Path, $text, $utf8)
}

function Test-PreservedFile {
    param([Parameter(Mandatory)] [string] $Name)
    return $Name -like 'appsettings*.json' -or $Name -like 'device-ca*.json'
}

function Test-SafeVersion {
    param([Parameter(Mandatory)] [string] $Version)
    return $Version -match '^[A-Za-z0-9._-]{1,32}$' -and $Version -ne '.' -and $Version -ne '..'
}

function Copy-Payload {
    param(
        [Parameter(Mandatory)] [string] $Source,
        [Parameter(Mandatory)] [string] $Destination
    )
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    foreach ($entry in Get-ChildItem -LiteralPath $Source -Force) {
        if ($entry.Name -eq 'update-helper') {
            continue
        }

        $target = Join-Path $Destination $entry.Name
        if (-not $entry.PSIsContainer -and (Test-PreservedFile $entry.Name)) {
            continue
        }

        if ($entry.PSIsContainer) {
            Copy-Payload -Source $entry.FullName -Destination $target
        }
        else {
            Copy-Item -LiteralPath $entry.FullName -Destination $target -Force
        }
    }
}

function Copy-Tree {
    param(
        [Parameter(Mandatory)] [string] $Source,
        [Parameter(Mandatory)] [string] $Destination
    )
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Copy-Item -Path (Join-Path $Source '*') -Destination $Destination -Recurse -Force
}

function Reset-Directory {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $Backup
    )
    Get-ChildItem -LiteralPath $Path -Force | Remove-Item -Recurse -Force
    Copy-Tree -Source $Backup -Destination $Path
}

function Stop-ApiHost {
    param($Conf)
    if ($Conf['HostKind'] -eq 'iis') {
        Import-Module WebAdministration
        Stop-WebAppPool -Name $Conf['AppPoolName']
        Wait-AppPool -Name $Conf['AppPoolName'] -State 'Stopped'
        return
    }

    Stop-Service -Name $Conf['ServiceName'] -Force
    $service = Get-Service -Name $Conf['ServiceName']
    $service.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromSeconds(60))
}

function Start-ApiHost {
    param($Conf)
    if ($Conf['HostKind'] -eq 'iis') {
        Import-Module WebAdministration
        Start-WebAppPool -Name $Conf['AppPoolName']
        Wait-AppPool -Name $Conf['AppPoolName'] -State 'Started'
        return
    }

    Start-Service -Name $Conf['ServiceName']
    $service = Get-Service -Name $Conf['ServiceName']
    $service.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Running, [TimeSpan]::FromSeconds(60))
}

function Wait-AppPool {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $State
    )
    $deadline = (Get-Date).AddSeconds(60)
    do {
        $current = (Get-WebAppPoolState -Name $Name).Value
        if ("$current" -eq $State) {
            return
        }

        Start-Sleep -Seconds 1
    } while ((Get-Date) -lt $deadline)

    throw "IIS app pool $Name did not reach $State."
}

function Test-HealthEndpoint {
    param([string] $Url)
    if ([string]::IsNullOrWhiteSpace($Url)) {
        return $true
    }

    try {
        $request = [System.Net.HttpWebRequest]::Create($Url)
        $request.Timeout = 5000
        $request.AllowAutoRedirect = $true
        $request.ServerCertificateValidationCallback = { $true }
        $response = $request.GetResponse()
        $response.Close()
        return $true
    }
    catch [System.Net.WebException] {
        $webResponse = $_.Exception.Response
        if ($null -ne $webResponse) {
            $webResponse.Close()
            return $true
        }

        return $false
    }
    catch {
        return $false
    }
}

function Wait-ApiHealth {
    param($Conf)
    $deadline = (Get-Date).AddSeconds(60)
    do {
        $hostUp = $true
        if ($Conf['HostKind'] -eq 'iis') {
            $hostUp = "$((Get-WebAppPoolState -Name $Conf['AppPoolName']).Value)" -eq 'Started'
        }
        else {
            $hostUp = (Get-Service -Name $Conf['ServiceName']).Status -eq 'Running'
        }

        if ($hostUp -and (Test-HealthEndpoint $Conf['HealthUrl'])) {
            return
        }

        Start-Sleep -Seconds 2
    } while ((Get-Date) -lt $deadline)

    throw "The API did not accept connections after the update."
}

$configPath = Join-Path $PSScriptRoot 'helper.conf'
if (-not (Test-Path -LiteralPath $configPath)) {
    throw "Missing $configPath. Run Install-ApiUpdateHelper.ps1 first."
}

$conf = Read-Conf $configPath
foreach ($key in @('HostKind', 'InstallPath', 'StagingPath')) {
    if ([string]::IsNullOrWhiteSpace($conf[$key])) {
        throw "helper.conf is missing $key."
    }
}

if ($conf['HostKind'] -ne 'windows-service' -and $conf['HostKind'] -ne 'iis') {
    throw "helper.conf HostKind must be windows-service or iis."
}

$installPath = [System.IO.Path]::GetFullPath($conf['InstallPath'])
$stagingPath = [System.IO.Path]::GetFullPath($conf['StagingPath'])
$root = [System.IO.Path]::GetPathRoot($installPath)
if ($installPath.TrimEnd('\') -eq $root.TrimEnd('\')) {
    throw "Refusing to update the drive root $installPath."
}

$requestPath = Join-Path $stagingPath 'request.conf'
$resultPath = Join-Path $stagingPath 'result.conf'
$payload = Join-Path $stagingPath 'payload'
$backup = Join-Path $stagingPath 'backup'
if (-not (Test-Path -LiteralPath $requestPath)) {
    throw "Missing $requestPath."
}

$request = Read-Conf $requestPath
$version = "$($request['Version'])"
if (-not (Test-SafeVersion $version)) {
    throw "request.conf Version is not safe."
}

if (-not (Test-Path -LiteralPath $payload)) {
    throw "Staged payload is missing."
}

Start-Sleep -Seconds 3
$stopped = $false
try {
    Stop-ApiHost $conf
    $stopped = $true
    if (Test-Path -LiteralPath $backup) {
        Remove-Item -LiteralPath $backup -Recurse -Force
    }

    Copy-Tree -Source $installPath -Destination $backup
    Copy-Payload -Source $payload -Destination $installPath
    Start-ApiHost $conf
    Wait-ApiHealth $conf
    Write-Result -Path $resultPath -Status 'succeeded' -Version $version -Message "Applied $version."
}
catch {
    $message = $_.Exception.Message
    if ($stopped -and (Test-Path -LiteralPath $backup)) {
        try {
            Reset-Directory -Path $installPath -Backup $backup
            Start-ApiHost $conf
            $message = "Rolled back after: $message"
        }
        catch {
            $message = "Rollback failed after: $message $($_.Exception.Message)"
        }
    }

    Write-Result -Path $resultPath -Status 'failed' -Version $version -Message $message
    throw
}
