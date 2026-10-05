[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$ReleaseRoot,
    [Parameter(Mandatory)] [string]$InstallRoot,
    [string]$ServiceName = 'SchoolSignage'
)
$ErrorActionPreference = 'Stop'
$incoming = [IO.Path]::GetFullPath($ReleaseRoot)
$install = [IO.Path]::GetFullPath($InstallRoot)
$current = Join-Path $install 'current'; $previous = Join-Path $install 'previous'
if (-not (Test-Path -LiteralPath (Join-Path $incoming 'app\Signage.Web.dll'))) { throw 'Incoming release is invalid.' }
$service = Get-Service -Name $ServiceName -ErrorAction Stop
if ($service.Status -ne 'Stopped') { Stop-Service -Name $ServiceName; $service.WaitForStatus('Stopped', [TimeSpan]::FromMinutes(2)) }
try {
    if (Test-Path -LiteralPath $previous) { Remove-Item -LiteralPath $previous -Recurse -Force }
    if (Test-Path -LiteralPath $current) { Move-Item -LiteralPath $current -Destination $previous }
    Copy-Item -LiteralPath $incoming -Destination $current -Recurse
    if (Test-Path -LiteralPath (Join-Path $previous 'data')) { Copy-Item -LiteralPath (Join-Path $previous 'data') -Destination $current -Recurse -Force }
    if (Test-Path -LiteralPath (Join-Path $previous 'app\appsettings.Production.json')) { Copy-Item -LiteralPath (Join-Path $previous 'app\appsettings.Production.json') -Destination (Join-Path $current 'app\appsettings.Production.json') -Force }
    $env:ASPNETCORE_ENVIRONMENT = 'Production'
    Push-Location (Join-Path $current 'app')
    try { & dotnet '.\Signage.Web.dll' --migrate }
    finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) { throw 'Database migration failed.' }
    Start-Service -Name $ServiceName
}
catch {
    if (Test-Path -LiteralPath $current) { Remove-Item -LiteralPath $current -Recurse -Force }
    if (Test-Path -LiteralPath $previous) { Move-Item -LiteralPath $previous -Destination $current }
    throw
}
