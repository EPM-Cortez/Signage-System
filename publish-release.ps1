[CmdletBinding()]
param(
    [string]$OutputDirectory = 'artifacts\school-signage',
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')] [string]$Version = '1.0.0',
    [switch]$SkipSetup
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location -LiteralPath $repoRoot
$releaseRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDirectory))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))
if (-not $releaseRoot.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputDirectory must be a child of the repository artifacts directory.'
}
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT -or [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne 'X64') {
    throw 'This script builds Windows x64 converter dependencies. Use publish-release.sh on Linux.'
}
if ((Test-Path -LiteralPath $releaseRoot) -or (Test-Path -LiteralPath "$releaseRoot.zip") -or (Test-Path -LiteralPath "$releaseRoot.zip.sha256")) {
    throw 'Release output already exists. Choose a new OutputDirectory; existing releases are never overwritten.'
}

if (-not $SkipSetup) { & (Join-Path $repoRoot 'setup-local.ps1') }
$dotnet = if (Test-Path -LiteralPath (Join-Path $repoRoot '.dotnet\dotnet.exe')) { Join-Path $repoRoot '.dotnet\dotnet.exe' } else { (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_HOME = Join-Path $repoRoot '.dotnet-home'
$env:APPDATA = Join-Path $repoRoot '.dotnet-home\AppData\Roaming'
$env:NUGET_PACKAGES = Join-Path $repoRoot '.nuget\packages'
$env:npm_config_cache = Join-Path $repoRoot '.npm-cache'
$appRoot = Join-Path $releaseRoot 'app'
$converterRoot = Join-Path $releaseRoot 'converter'
New-Item -ItemType Directory -Force -Path $appRoot, $converterRoot | Out-Null
& $dotnet publish 'src/Signage.Web/Signage.Web.csproj' -c Release --no-restore -p:Version=$Version -o $appRoot
if ($LASTEXITCODE -ne 0) { throw 'Release publish failed.' }
Remove-Item -LiteralPath (Join-Path $appRoot 'appsettings.Development.json') -Force -ErrorAction SilentlyContinue
Copy-Item -LiteralPath 'src/Signage.Converter/dist' -Destination (Join-Path $converterRoot 'dist') -Recurse
Copy-Item -LiteralPath 'src/Signage.Converter/scripts' -Destination (Join-Path $converterRoot 'scripts') -Recurse
Copy-Item -LiteralPath 'src/Signage.Converter/package.json', 'src/Signage.Converter/package-lock.json' -Destination $converterRoot
Push-Location $converterRoot
try { & npm ci --omit=dev; if ($LASTEXITCODE -ne 0) { throw 'Release converter dependency install failed.' } }
finally { Pop-Location }
Copy-Item -LiteralPath 'fonts', 'docs', 'deploy' -Destination $releaseRoot -Recurse
Copy-Item -LiteralPath 'scripts' -Destination $releaseRoot -Recurse
Copy-Item -LiteralPath 'README.md', 'THIRD_PARTY_NOTICES.md' -Destination $releaseRoot
Copy-Item -LiteralPath 'INSTALL.md' -Destination $releaseRoot
Copy-Item -LiteralPath 'deploy/appsettings.Production.sample.json' -Destination (Join-Path $appRoot 'appsettings.Production.sample.json')
New-Item -ItemType Directory -Force -Path (Join-Path $releaseRoot 'data\content'), (Join-Path $releaseRoot 'data\temp'), (Join-Path $releaseRoot 'backups') | Out-Null
$lockRoot = Join-Path $releaseRoot 'dependency-locks'
New-Item -ItemType Directory -Path $lockRoot | Out-Null
foreach ($project in @('Signage.Web', 'Signage.Infrastructure', 'Signage.Application', 'Signage.Domain')) {
    Copy-Item -LiteralPath (Join-Path $repoRoot "src/$project/packages.lock.json") -Destination (Join-Path $lockRoot "$project.packages.lock.json")
}
$sourceCommit = (& git -C $repoRoot rev-parse --verify HEAD 2>$null)
if ($LASTEXITCODE -ne 0) { $sourceCommit = 'uncommitted' }
@{
    product = 'School Signage Publisher'; version = $Version; platform = 'win-x64'
    sourceCommit = $sourceCommit; builtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    frameworkDependent = $true; dotnetSdk = (& $dotnet --version); node = (& node --version)
    requirements = @('Windows x64', 'ASP.NET Core Runtime 10.0.10 or newer 10.0 patch', 'Node.js 22.23.1 or newer 22.x patch', 'HTTPS for staff sign-in')
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $releaseRoot 'release.json') -Encoding utf8
Compress-Archive -Path (Join-Path $releaseRoot '*') -DestinationPath "$releaseRoot.zip" -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath "$releaseRoot.zip" -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($releaseRoot)).zip" | Set-Content -LiteralPath "$releaseRoot.zip.sha256" -Encoding ascii
Write-Host "Release created at $releaseRoot.zip (SHA-256 $hash)"
