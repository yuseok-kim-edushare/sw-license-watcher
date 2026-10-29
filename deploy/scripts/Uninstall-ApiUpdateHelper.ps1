#Requires -Version 5.1
#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Removes the manually installed Windows API update helper.

.DESCRIPTION
    Deletes the scheduled task and the helper program directory. The API installation is left in place.
    Pass -RemoveStaging to also delete the staged package directory.
#>
[CmdletBinding()]
param(
    [string] $HelperDirectory = "C:\Program Files\SwLicenseWatcher\ApiUpdate",

    [string] $StagingPath = "C:\ProgramData\SwLicenseWatcher\api-update",

    [string] $TaskName = "SwLicenseWatcher Api Update",

    [switch] $RemoveStaging
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$existing = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($null -ne $existing) {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}

if (Test-Path -LiteralPath $HelperDirectory) {
    Remove-Item -LiteralPath $HelperDirectory -Recurse -Force
}

if ($RemoveStaging -and (Test-Path -LiteralPath $StagingPath)) {
    Remove-Item -LiteralPath $StagingPath -Recurse -Force
}

Write-Host "OK: Update helper removed. The API installation was not changed." -ForegroundColor Green
