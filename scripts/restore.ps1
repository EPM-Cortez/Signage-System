[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)] [string]$BackupDirectory,
    [Parameter(Mandatory)] [string]$DataRoot,
    [string]$ServiceName,
    [string]$ConfigDestination,
    [string]$HealthUrl = 'http://127.0.0.1:5080/health/ready'
)
$ErrorActionPreference = 'Stop'
$backup = [IO.Path]::GetFullPath($BackupDirectory)
$data = [IO.Path]::GetFullPath($DataRoot)
$inventoryPath = Join-Path $backup 'inventory.sha256'
if (-not (Test-Path -LiteralPath $inventoryPath)) { throw 'Backup inventory is missing.' }
foreach ($line in Get-Content -LiteralPath $inventoryPath) {
    $parts = $line -split '\s{2}', 2
    if ($parts.Count -ne 2 -or $parts[0] -notmatch '^[a-f0-9]{64}$') { throw "Invalid inventory line: $line" }
    $relative = $parts[1].Replace('/', [IO.Path]::DirectorySeparatorChar)
    if ([IO.Path]::IsPathRooted($relative) -or $relative.Split([IO.Path]::DirectorySeparatorChar) -contains '..') { throw "Unsafe inventory path: $relative" }
    $file = Join-Path $backup $relative
    if (-not (Test-Path -LiteralPath $file) -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $parts[0]) { throw "Backup verification failed: $relative" }
}
if (-not (Test-Path -LiteralPath (Join-Path $backup 'data\signage.db'))) { throw 'Verified backup does not contain signage.db.' }
$service = if ($ServiceName) { Get-Service -Name $ServiceName -ErrorAction SilentlyContinue } else { $null }
if ($service -and $service.Status -ne 'Stopped') { throw "Service '$ServiceName' must be stopped before restore." }
if (Test-Path -LiteralPath (Join-Path $data 'signage.lock')) {
    try { $lock = [IO.File]::Open((Join-Path $data 'signage.lock'), 'Open', 'ReadWrite', 'None'); $lock.Dispose() }
    catch { throw 'The data root is in use. Stop the application before restore.' }
}
$preRestore = "$data.pre-restore-$([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmssZ'))"
if ($PSCmdlet.ShouldProcess($data, "Restore verified backup and preserve current data at $preRestore")) {
    if (Test-Path -LiteralPath $data) { Move-Item -LiteralPath $data -Destination $preRestore }
    New-Item -ItemType Directory -Force -Path $data | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $backup 'data') -Force | Copy-Item -Destination $data -Recurse -Force
    if ($ConfigDestination -and (Test-Path -LiteralPath (Join-Path $backup 'config\appsettings.Production.json'))) {
        Copy-Item -LiteralPath (Join-Path $backup 'config\appsettings.Production.json') -Destination $ConfigDestination -Force
    }
    Write-Host "Restore complete. Pre-restore data: $preRestore"
    if ($service) {
        Start-Service -Name $ServiceName
        $service.WaitForStatus('Running', [TimeSpan]::FromMinutes(2))
        Invoke-WebRequest -UseBasicParsing -Uri $HealthUrl -TimeoutSec 30 | Out-Null
    }
}
