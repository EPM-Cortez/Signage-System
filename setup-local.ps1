[CmdletBinding()]
param([switch]$SkipTests)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location -LiteralPath $repoRoot

$localDotnet = Join-Path $repoRoot '.dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { (Get-Command dotnet -ErrorAction Stop).Source }
$dotnetVersion = & $dotnet --version
if ($dotnetVersion -notmatch '^10\.' -or $dotnetVersion -match '(?i)(preview|rc)') {
    throw "Stable .NET 10 is required. Found '$dotnetVersion'."
}
$nodeVersion = (& node --version).TrimStart('v')
if ($nodeVersion -notmatch '^22\.') {
    throw "Node.js 22 is required. Found '$nodeVersion'."
}

$env:DOTNET_CLI_HOME = Join-Path $repoRoot '.dotnet-home'
$env:APPDATA = Join-Path $repoRoot '.dotnet-home\AppData\Roaming'
$env:NUGET_PACKAGES = Join-Path $repoRoot '.nuget\packages'
$env:npm_config_cache = Join-Path $repoRoot '.npm-cache'
$env:PLAYWRIGHT_BROWSERS_PATH = Join-Path $repoRoot 'tests\Signage.E2ETests\.browsers'
$env:SIGNAGE_DOTNET = $dotnet
New-Item -ItemType Directory -Force -Path $env:DOTNET_CLI_HOME, $env:APPDATA, $env:NUGET_PACKAGES, $env:npm_config_cache, $env:PLAYWRIGHT_BROWSERS_PATH, (Join-Path $repoRoot '.local-data\content'), (Join-Path $repoRoot '.local-data\temp') | Out-Null

& $dotnet restore 'Signage.slnx' --configfile (Join-Path $repoRoot 'NuGet.Config') --locked-mode
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }
& $dotnet tool restore --configfile (Join-Path $repoRoot 'NuGet.Config')
if ($LASTEXITCODE -ne 0) { throw 'dotnet tool restore failed.' }

Push-Location 'src/Signage.Converter'
try { & npm ci; if ($LASTEXITCODE -ne 0) { throw 'Converter npm install failed.' }; & npm run build; if ($LASTEXITCODE -ne 0) { throw 'Converter build failed.' } }
finally { Pop-Location }
Push-Location 'src/Signage.Player'
try { & npm ci; if ($LASTEXITCODE -ne 0) { throw 'Player npm install failed.' }; & npm run build; if ($LASTEXITCODE -ne 0) { throw 'Player build failed.' } }
finally { Pop-Location }
Push-Location 'tests/Signage.E2ETests'
try { & npm ci; if ($LASTEXITCODE -ne 0) { throw 'Browser test dependency install failed.' }; & npm exec -- playwright install chromium; if ($LASTEXITCODE -ne 0) { throw 'Chromium installation for browser tests failed.' } }
finally { Pop-Location }

& $dotnet build 'Signage.slnx' --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Solution build failed.' }
if (-not $SkipTests) {
    & $dotnet test 'Signage.slnx' --no-build --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    Push-Location 'src/Signage.Converter'
    try { & npm test; if ($LASTEXITCODE -ne 0) { throw 'Converter tests failed.' } }
    finally { Pop-Location }
    Push-Location 'src/Signage.Player'
    try { & npm test; if ($LASTEXITCODE -ne 0) { throw 'Player tests failed.' } }
    finally { Pop-Location }
    Push-Location 'tests/Signage.E2ETests'
    try { & npm test; if ($LASTEXITCODE -ne 0) { throw 'Browser tests failed.' } }
    finally { Pop-Location }
}

Write-Host "Local setup complete (.NET $dotnetVersion, Node $nodeVersion)."
Write-Host 'Run .\run-local.ps1 and open http://localhost:5179/dev-login.'
