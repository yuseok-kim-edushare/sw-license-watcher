#Requires -Version 5.1
<#
.SYNOPSIS
    Generates a cryptographically random API token for SwLicenseWatcher.

.DESCRIPTION
    Prints a cryptographically random token of at least 32 characters (API minimum).
    Run the script twice so AgentToken and AdminToken are different.

    Recommended:
      Security:AgentToken  = Worker Agent:ApiToken and Watchdog Watchdog:ApiToken
      Security:AdminToken  = inventory queries, policy CRUD, violations, design/schema
    Legacy: a single Security:Token still authorizes every endpoint when AgentToken and
    AdminToken are left empty.

.PARAMETER ByteLength
    Number of random bytes before Base64 encoding. Must be 32 or greater.

.EXAMPLE
    $agentToken = .\New-ApiToken.ps1
    $adminToken = .\New-ApiToken.ps1
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
Write-Host "Assign this value to Security:AgentToken (and Agent:ApiToken / Watchdog:ApiToken) or Security:AdminToken. Run again for the other role. A single Security:Token still grants all endpoints when AgentToken and AdminToken are empty." -ForegroundColor Yellow
