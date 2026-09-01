#Requires -Version 5.1
#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Stops and removes the API Windows Service.

.PARAMETER InstallRoot
    Parent install directory. The Api folder under this path is removed when -RemoveFiles is set.

.PARAMETER ServiceName
    Windows Service name.

.PARAMETER RemoveFiles
    Also delete the API install directory (InstallRoot\Api).

.PARAMETER RemoveFirewall
    Also delete the inbound firewall rule created by Install-ApiServer.ps1.

.EXAMPLE
    .\Uninstall-ApiServer.ps1

.EXAMPLE
    .\Uninstall-ApiServer.ps1 -RemoveFiles -RemoveFirewall
#>
[CmdletBinding()]
param(
    [string] $InstallRoot = "C:\Program Files\SwLicenseWatcher",

    [string] $ServiceName = "SwLicenseWatcher.Api",

    [switch] $RemoveFiles,

    [switch] $RemoveFirewall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$firewallRuleName = "SW License Watcher API"

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

function Remove-WindowsService {
    param([Parameter(Mandatory)] [string] $Name)

    $svc = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if ($null -eq $svc) {
        Write-Host "Service $Name is not installed."
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

function Remove-InboundFirewallRule {
    param([Parameter(Mandatory)] [string] $RuleName)

    if ($null -ne (Get-Command Get-NetFirewallRule -ErrorAction SilentlyContinue)) {
        $existing = Get-NetFirewallRule -DisplayName $RuleName -ErrorAction SilentlyContinue
        if ($null -eq $existing) {
            Write-Host "Firewall rule '$RuleName' is not present."
            return
        }

        Remove-NetFirewallRule -DisplayName $RuleName
        Write-Host "Removed firewall rule '$RuleName'."
        return
    }

    & netsh advfirewall firewall delete rule name=$RuleName | Out-Null
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Removed firewall rule '$RuleName'."
        return
    }

    Write-Host "Firewall rule '$RuleName' is not present."
}

try {
    if (-not (Test-IsWindows)) {
        throw "Uninstall-ApiServer.ps1 can only run on Windows."
    }

    if ([string]::IsNullOrWhiteSpace($ServiceName)) {
        throw "ServiceName is required."
    }

    Remove-WindowsService -Name $ServiceName
    Write-Host "Service $ServiceName removed."

    if ($RemoveFirewall) {
        Remove-InboundFirewallRule -RuleName $firewallRuleName
    }

    $apiDir = Join-Path $InstallRoot "Api"
    if ($RemoveFiles) {
        if (Test-Path -LiteralPath $apiDir) {
            Remove-Item -LiteralPath $apiDir -Recurse -Force
            Write-Host "Removed $apiDir"
        }

        $parentEmpty = (Test-Path -LiteralPath $InstallRoot) -and
            $null -eq (Get-ChildItem -LiteralPath $InstallRoot -Force | Select-Object -First 1)
        if ($parentEmpty) {
            Remove-Item -LiteralPath $InstallRoot -Force
        }
    }
    else {
        Write-Host "Left $apiDir in place. Pass -RemoveFiles to delete the install directory."
    }

    if (-not $RemoveFirewall) {
        Write-Host "Left firewall rule '$firewallRuleName' in place. Pass -RemoveFirewall to delete it."
    }

    Write-Success "API Windows Service uninstall finished."
}
catch {
    Write-Failure $_.Exception.Message
    throw
}
