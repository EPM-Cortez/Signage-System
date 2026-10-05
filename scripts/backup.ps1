[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$DataRoot,
    [Parameter(Mandatory)] [string]$BackupRoot,
    [string]$ServiceName,
    [string]$ConfigPath,
    [string]$HealthUrl = 'http://127.0.0.1:5080/health/ready'
)
$ErrorActionPreference = 'Stop'
$data = [IO.Path]::GetFullPath($DataRoot)
$backups = [IO.Path]::GetFullPath($BackupRoot)
if (-not (Test-Path -LiteralPath (Join-Path $data 'signage.db'))) { throw "SQLite database not found under $data" }
$service = if ($ServiceName) { Get-Service -Name $ServiceName -ErrorAction SilentlyContinue } else { $null }
$wasRunning = $service -and $service.Status -ne 'Stopped'
try {
    if ($wasRunning) { Stop-Service -Name $ServiceName; $service.WaitForStatus('Stopped', [TimeSpan]::FromMinutes(2)) }
    $lockPath = Join-Path $data 'signage.lock'
    $lock = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $lock.Dispose()
    $destination = Join-Path $backups ([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmssZ'))
    New-Item -ItemType Directory -Force -Path (Join-Path $destination 'data') | Out-Null
    Get-ChildItem -LiteralPath $data -Force | Copy-Item -Destination (Join-Path $destination 'data') -Recurse -Force
    if ($ConfigPath -and (Test-Path -LiteralPath $ConfigPath)) {
        New-Item -ItemType Directory -Force -Path (Join-Path $destination 'config') | Out-Null
        Copy-Item -LiteralPath $ConfigPath -Destination (Join-Path $destination 'config\appsettings.Production.json')
    }
    $inventory = foreach ($file in Get-ChildItem -LiteralPath $destination -File -Recurse) {
        $relative = $file.FullName.Substring($destination.Length + 1).Replace('\', '/')
        '{0}  {1}' -f (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $relative
    }
    $inventory | Sort-Object | Set-Content -LiteralPath (Join-Path $destination 'inventory.sha256') -Encoding ascii
    Write-Host "Backup complete: $destination"
}
finally {
    if ($wasRunning) {
        Start-Service -Name $ServiceName
        (Get-Service -Name $ServiceName).WaitForStatus('Running', [TimeSpan]::FromMinutes(2))
        try { Invoke-WebRequest -UseBasicParsing -Uri $HealthUrl -TimeoutSec 30 | Out-Null } catch { Write-Warning "Service restarted, but readiness verification failed: $($_.Exception.Message)" }
    }
}
