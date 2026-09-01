#Requires -Version 5.1
<#
.SYNOPSIS
    Generates a shared API token for SwLicenseWatcher API, Worker, and Watchdog.

.DESCRIPTION
    Prints a cryptographically random token of at least 32 characters (API minimum).
    Use the same value for Security:Token, Agent:ApiToken, and Watchdog:ApiToken.

.PARAMETER ByteLength
    Number of random bytes before Base64 encoding. Must be 32 or greater.

.EXAMPLE
    $token = .\New-ApiToken.ps1
#>
[CmdletBinding()]
param(
    [ValidateRange(32, 128)]
    [int] $ByteLength = 32
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$bytes = [byte[]]::new($ByteLength)
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
try {
    $rng.GetBytes($bytes)
}
finally {
    $rng.Dispose()
}

$token = [Convert]::ToBase64String($bytes)
Write-Output $token
Write-Host "Use this same value for API Security:Token, Worker Agent:ApiToken, and Watchdog Watchdog:ApiToken." -ForegroundColor Yellow
