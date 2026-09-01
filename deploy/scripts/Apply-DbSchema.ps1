#Requires -Version 5.1
<#
.SYNOPSIS
    Applies the SwLicenseWatcher SQL Server schema DDL.

.DESCRIPTION
    Loads idempotent DDL either from a running API (GET /api/schema/sql with an
    AdminToken or legacy Token) or from a local .sql file, splits GO batches, and
    executes them against SQL Server.

    Prefers System.Data.SqlClient or Microsoft.Data.SqlClient loaded in PowerShell.
    Falls back to sqlcmd.exe when those types are unavailable. Invoke-Sqlcmd is not required.

.PARAMETER ConnectionString
    Full SQL Server connection string. Mutually exclusive with -Server/-Database.

.PARAMETER Server
    SQL Server host (and optional instance / port). Used with -Database when
    -ConnectionString is omitted.

.PARAMETER Database
    Target database name.

.PARAMETER UserName
    SQL authentication user. Omit for integrated (Windows) authentication.

.PARAMETER Password
    SQL authentication password. Ignored when -UserName is omitted.

.PARAMETER TrustServerCertificate
    Adds TrustServerCertificate=True to a built connection string.

.PARAMETER ApiBaseUrl
    Running API base URL. Fetches GET /api/schema/sql. Mutually exclusive with -SqlPath.

.PARAMETER ApiToken
    Bearer token for /api/schema/sql (AdminToken or legacy Token). Defaults to
    Security__AdminToken, then Security__Token.

.PARAMETER SqlPath
    Local UTF-8 .sql file. Mutually exclusive with -ApiBaseUrl.

.PARAMETER CommandTimeoutSeconds
    ADO.NET command timeout per batch. Default 120. Ignored for sqlcmd.exe.

.EXAMPLE
    .\Apply-DbSchema.ps1 -ConnectionString $cs -ApiBaseUrl https://license-watcher.contoso.local -ApiToken $adminToken

.EXAMPLE
    .\Apply-DbSchema.ps1 -Server sql.contoso.local -Database SwLicenseWatcher -SqlPath .\schema.sql -WhatIf
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [string] $ConnectionString,

    [string] $Server,

    [string] $Database,

    [string] $UserName,

    [string] $Password,

    [switch] $TrustServerCertificate,

    [string] $ApiBaseUrl,

    [string] $ApiToken,

    [string] $SqlPath,

    [ValidateRange(1, 3600)]
    [int] $CommandTimeoutSeconds = 120
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Failure {
    param([Parameter(Mandatory)] [string] $Message)
    Write-Host "FAILED: $Message" -ForegroundColor Red
}

function Write-Success {
    param([Parameter(Mandatory)] [string] $Message)
    Write-Host "OK: $Message" -ForegroundColor Green
}

function Test-HasText {
    param($Value)
    return -not [string]::IsNullOrWhiteSpace([string] $Value)
}

function Split-SqlBatches {
    param([Parameter(Mandatory)] [string] $Sql)

    $batches = New-Object 'System.Collections.Generic.List[string]'
    $current = New-Object System.Text.StringBuilder
    foreach ($line in $Sql -split "`r`n|`n|`r") {
        $repeat = 0
        if ($line -match '^\s*GO\s+(\d+)\s*$') {
            $repeat = [int] $Matches[1]
        }
        elseif ($line -match '^\s*GO\s*$') {
            $repeat = 1
        }

        if ($repeat -gt 0) {
            $text = $current.ToString().Trim()
            [void] $current.Clear()
            if ($text.Length -eq 0) {
                continue
            }

            for ($i = 0; $i -lt $repeat; $i++) {
                $batches.Add($text)
            }

            continue
        }

        [void] $current.AppendLine($line)
    }

    $trailing = $current.ToString().Trim()
    if ($trailing.Length -gt 0) {
        $batches.Add($trailing)
    }

    return , $batches.ToArray()
}

function Get-SqlConnectionType {
    try {
        Add-Type -AssemblyName System.Data -ErrorAction SilentlyContinue | Out-Null
    }
    catch {
    }

    $names = @(
        'System.Data.SqlClient.SqlConnection, System.Data',
        'Microsoft.Data.SqlClient.SqlConnection, Microsoft.Data.SqlClient'
    )
    foreach ($name in $names) {
        $type = [type]::GetType($name, $false)
        if ($null -ne $type) {
            return $type
        }
    }

    try {
        return [System.Data.SqlClient.SqlConnection]
    }
    catch {
        return $null
    }
}

function Find-SqlCmdPath {
    $cmd = Get-Command sqlcmd.exe -ErrorAction SilentlyContinue
    if ($null -ne $cmd) {
        return $cmd.Source
    }

    $patterns = @(
        (Join-Path $env:ProgramFiles 'Microsoft SQL Server\Client SDK\ODBC\*\Tools\Binn\sqlcmd.exe'),
        (Join-Path $env:ProgramFiles 'Microsoft SQL Server\*\Tools\Binn\sqlcmd.exe')
    )
    if (Test-HasText ${env:ProgramFiles(x86)}) {
        $patterns += (Join-Path ${env:ProgramFiles(x86)} 'Microsoft SQL Server\Client SDK\ODBC\*\Tools\Binn\sqlcmd.exe')
        $patterns += (Join-Path ${env:ProgramFiles(x86)} 'Microsoft SQL Server\*\Tools\Binn\sqlcmd.exe')
    }

    foreach ($pattern in $patterns) {
        $hit = Get-Item -Path $pattern -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($null -ne $hit) {
            return $hit.FullName
        }
    }

    return $null
}

function Get-ConnectionStringValue {
    param(
        [Parameter(Mandatory)] [string] $Text,
        [Parameter(Mandatory)] [string[]] $Keys
    )

    foreach ($key in $Keys) {
        $pattern = '(?i)(?:^|;)\s*{0}\s*=\s*([^;]*)' -f [regex]::Escape($key)
        $match = [regex]::Match($Text, $pattern)
        if ($match.Success) {
            return $match.Groups[1].Value.Trim()
        }
    }

    return $null
}

function Resolve-SqlConnectionString {
    if (Test-HasText $ConnectionString) {
        $resolved = $ConnectionString.Trim()
        if ($TrustServerCertificate -and $resolved -notmatch '(?i)TrustServerCertificate\s*=') {
            $resolved = $resolved.TrimEnd(';') + ';TrustServerCertificate=True'
        }

        return $resolved
    }

    if (-not (Test-HasText $Server) -or -not (Test-HasText $Database)) {
        throw "Specify -ConnectionString, or both -Server and -Database."
    }

    $parts = New-Object System.Collections.Generic.List[string]
    $parts.Add("Server=$($Server.Trim())")
    $parts.Add("Database=$($Database.Trim())")
    if (Test-HasText $UserName) {
        $parts.Add("User ID=$($UserName.Trim())")
        $parts.Add("Password=$Password")
    }
    else {
        $parts.Add('Integrated Security=True')
    }

    if ($TrustServerCertificate) {
        $parts.Add('TrustServerCertificate=True')
    }

    return [string]::Join(';', $parts.ToArray())
}

function Get-SqlTargetLabel {
    param([Parameter(Mandatory)] [string] $ResolvedConnectionString)

    $serverName = Get-ConnectionStringValue -Text $ResolvedConnectionString -Keys @('Data Source', 'Server', 'Address', 'Addr', 'Network Address')
    $databaseName = Get-ConnectionStringValue -Text $ResolvedConnectionString -Keys @('Initial Catalog', 'Database')
    if (-not (Test-HasText $serverName)) {
        $serverName = $Server
    }

    if (-not (Test-HasText $databaseName)) {
        $databaseName = $Database
    }

    if (-not (Test-HasText $serverName)) {
        $serverName = '(unknown server)'
    }

    if (-not (Test-HasText $databaseName)) {
        $databaseName = '(unknown database)'
    }

    return "$serverName / $databaseName"
}

function Get-SchemaSqlFromApi {
    param(
        [Parameter(Mandatory)] [string] $BaseUrl,
        [Parameter(Mandatory)] [string] $Token
    )

    $uri = $BaseUrl.TrimEnd('/') + '/api/schema/sql'
    Write-Host "Fetching schema DDL from $uri ..."
    $headers = @{ Authorization = "Bearer $Token" }
    $response = Invoke-WebRequest -Uri $uri -Headers $headers -UseBasicParsing
    if ([int] $response.StatusCode -ge 400) {
        throw "GET /api/schema/sql returned HTTP $($response.StatusCode)."
    }

    $sql = [string] $response.Content
    if (-not (Test-HasText $sql)) {
        throw "GET /api/schema/sql returned an empty script."
    }

    return $sql
}

function Get-SchemaSqlFromFile {
    param([Parameter(Mandatory)] [string] $Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "SQL file not found: $fullPath"
    }

    Write-Host "Reading schema DDL from $fullPath ..."
    $sql = [System.IO.File]::ReadAllText($fullPath)
    if (-not (Test-HasText $sql)) {
        throw "SQL file is empty: $fullPath"
    }

    return $sql
}

function Invoke-SqlBatchesWithAdo {
    param(
        [Parameter(Mandatory)] [type] $ConnectionType,
        [Parameter(Mandatory)] [string] $ResolvedConnectionString,
        [Parameter(Mandatory)] [string[]] $Batches
    )

    $connection = [Activator]::CreateInstance($ConnectionType, @($ResolvedConnectionString))
    try {
        $connection.Open()
        $index = 0
        foreach ($batch in $Batches) {
            $index++
            Write-Host "Applying batch $index of $($Batches.Length) ..."
            $command = $connection.CreateCommand()
            try {
                $command.CommandText = $batch
                $command.CommandTimeout = $CommandTimeoutSeconds
                [void] $command.ExecuteNonQuery()
            }
            catch {
                throw "Batch $index of $($Batches.Length) failed: $($_.Exception.Message)"
            }
            finally {
                $command.Dispose()
            }
        }
    }
    finally {
        $connection.Dispose()
    }
}

function Invoke-SqlBatchesWithSqlCmd {
    param(
        [Parameter(Mandatory)] [string] $SqlCmdPath,
        [Parameter(Mandatory)] [string] $ResolvedConnectionString,
        [Parameter(Mandatory)] [string] $Sql
    )

    $serverName = Get-ConnectionStringValue -Text $ResolvedConnectionString -Keys @('Data Source', 'Server', 'Address', 'Addr', 'Network Address')
    $databaseName = Get-ConnectionStringValue -Text $ResolvedConnectionString -Keys @('Initial Catalog', 'Database')
    if (-not (Test-HasText $serverName) -or -not (Test-HasText $databaseName)) {
        throw "sqlcmd.exe fallback requires Server and Database in the connection string."
    }

    $tempFile = [System.IO.Path]::ChangeExtension([System.IO.Path]::GetTempFileName(), '.sql')
    $sqlCmdArgs = @(
        '-S', $serverName,
        '-d', $databaseName,
        '-b',
        '-I',
        '-i', $tempFile
    )
    $user = Get-ConnectionStringValue -Text $ResolvedConnectionString -Keys @('User ID', 'UID')
    if (Test-HasText $user) {
        $sqlPassword = Get-ConnectionStringValue -Text $ResolvedConnectionString -Keys @('Password', 'PWD')
        $sqlCmdArgs += @('-U', $user, '-P', $sqlPassword)
    }
    else {
        $sqlCmdArgs += '-E'
    }

    try {
        [System.IO.File]::WriteAllText($tempFile, $Sql)
        Write-Host "Applying schema with sqlcmd.exe ..."
        & $SqlCmdPath @sqlCmdArgs
        if ($LASTEXITCODE -ne 0) {
            throw "sqlcmd.exe exited with code $LASTEXITCODE."
        }
    }
    finally {
        Remove-Item -LiteralPath $tempFile -Force -ErrorAction SilentlyContinue
    }
}

try {
    $hasApi = Test-HasText $ApiBaseUrl
    $hasFile = Test-HasText $SqlPath
    if ($hasApi -eq $hasFile) {
        throw "Specify exactly one DDL source: -ApiBaseUrl or -SqlPath."
    }

    if ((Test-HasText $ConnectionString) -and ((Test-HasText $Server) -or (Test-HasText $Database))) {
        throw "Use either -ConnectionString or -Server/-Database, not both."
    }

    $resolvedConnectionString = Resolve-SqlConnectionString
    $target = Get-SqlTargetLabel -ResolvedConnectionString $resolvedConnectionString

    if ($hasApi) {
        $token = $ApiToken
        if (-not (Test-HasText $token)) {
            $token = $env:Security__AdminToken
        }

        if (-not (Test-HasText $token)) {
            $token = $env:Security__Token
        }

        if (-not (Test-HasText $token)) {
            throw "Specify -ApiToken or set Security__AdminToken / Security__Token."
        }

        $sql = Get-SchemaSqlFromApi -BaseUrl $ApiBaseUrl -Token $token
    }
    else {
        $sql = Get-SchemaSqlFromFile -Path $SqlPath
    }

    $batches = @(Split-SqlBatches -Sql $sql)
    if ($batches.Count -eq 0) {
        throw "The schema script contains no executable SQL batches."
    }

    Write-Host "Loaded $($batches.Count) SQL batch(es) ($($sql.Length) characters) for $target."
    if ($WhatIfPreference) {
        $batchIndex = 0
        foreach ($batch in $batches) {
            $batchIndex++
            Write-Host "--- Batch $batchIndex ---"
            Write-Host $batch
        }
    }

    if (-not $PSCmdlet.ShouldProcess($target, "Apply $($batches.Count) SQL batch(es)")) {
        Write-Host "Preview only. No changes were applied."
        return
    }

    $connectionType = Get-SqlConnectionType
    if ($null -ne $connectionType) {
        Invoke-SqlBatchesWithAdo -ConnectionType $connectionType -ResolvedConnectionString $resolvedConnectionString -Batches $batches
    }
    else {
        $sqlCmdPath = Find-SqlCmdPath
        if (-not (Test-HasText $sqlCmdPath)) {
            throw "Neither System.Data.SqlClient/Microsoft.Data.SqlClient nor sqlcmd.exe is available."
        }

        Invoke-SqlBatchesWithSqlCmd -SqlCmdPath $sqlCmdPath -ResolvedConnectionString $resolvedConnectionString -Sql $sql
    }

    Write-Success "Schema applied to $target ($($batches.Count) batch(es))."
}
catch {
    Write-Failure $_.Exception.Message
    throw
}
