#Requires -Version 5.1
#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Stops and removes Worker and Watchdog Windows Services.

.PARAMETER InstallRoot
    Parent install directory. Agent folders under this path are removed unless -KeepFiles is set.

.PARAMETER StateRoot
    ProgramData root. Deleted only when -RemoveState is specified.

.PARAMETER KeepFiles
    Leave install directories in place after deleting the services.

.PARAMETER RemoveState
    Also delete the snapshot queue, health file, staging, and backup directories.

.EXAMPLE
    .\Uninstall-Agent.ps1

.EXAMPLE
    .\Uninstall-Agent.ps1 -RemoveState
#>
[CmdletBinding()]
param(
    [string] $InstallRoot = "C:\Program Files\SwLicenseWatcher",

    [string] $StateRoot = "C:\ProgramData\SwLicenseWatcher",

    [switch] $KeepFiles,

    [switch] $RemoveState
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$workerServiceName = "SwLicenseWatcher.Agent.Worker"
$watchdogServiceName = "SwLicenseWatcher.Agent.Watchdog"

function Remove-WindowsService {
    param([Parameter(Mandatory)] [string] $Name)

    $svc = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if ($null -eq $svc) {
        return
    }

    if ($svc.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
        Stop-Service -Name $Name -Force
        try {
            $svc.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromMinutes(1))
        }
        catch {
            throw "Service $Name did not stop in time."
        }
    }

    & sc.exe delete $Name | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "sc.exe delete $Name failed with exit code $LASTEXITCODE."
    }

    $deadline = [datetime]::UtcNow.AddSeconds(30)
    while ([datetime]::UtcNow -lt $deadline) {
        if ($null -eq (Get-Service -Name $Name -ErrorAction SilentlyContinue)) {
            return
        }
        Start-Sleep -Milliseconds 300
    }
    throw "Service $Name was not deleted in time."
}

Remove-WindowsService -Name $watchdogServiceName
Remove-WindowsService -Name $workerServiceName
Write-Host "Services $watchdogServiceName and $workerServiceName removed."

if (-not $KeepFiles) {
    foreach ($leaf in @("Agent.Watchdog", "Agent.Worker")) {
        $dir = Join-Path $InstallRoot $leaf
        if (Test-Path -LiteralPath $dir) {
            Remove-Item -LiteralPath $dir -Recurse -Force
            Write-Host "Removed $dir"
        }
    }

    $parentEmpty = (Test-Path -LiteralPath $InstallRoot) -and
        $null -eq (Get-ChildItem -LiteralPath $InstallRoot -Force | Select-Object -First 1)
    if ($parentEmpty) {
        Remove-Item -LiteralPath $InstallRoot -Force
    }
}

if ($RemoveState -and (Test-Path -LiteralPath $StateRoot)) {
    Remove-Item -LiteralPath $StateRoot -Recurse -Force
    Write-Host "Removed state directory $StateRoot"
}
elseif (-not $RemoveState) {
    Write-Host "Left $StateRoot in place. Pass -RemoveState to delete the queue, health file, staging, and backups."
}
