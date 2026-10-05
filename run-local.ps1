[CmdletBinding()]
param([switch]$SkipSetup)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $SkipSetup) { & (Join-Path $repoRoot 'setup-local.ps1') }
Set-Location -LiteralPath $repoRoot
$dotnet = if (Test-Path -LiteralPath (Join-Path $repoRoot '.dotnet\dotnet.exe')) { Join-Path $repoRoot '.dotnet\dotnet.exe' } else { (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_HOME = Join-Path $repoRoot '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $repoRoot '.nuget\packages'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
& $dotnet run --project 'src/Signage.Web' --no-build --no-restore --urls 'http://localhost:5179'
exit $LASTEXITCODE
