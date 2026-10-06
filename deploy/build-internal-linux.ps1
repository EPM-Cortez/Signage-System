[CmdletBinding()]
param([Parameter(Mandatory)][ValidatePattern('^linux-ui-[0-9]{8}-[0-9]{6}$')][string]$ReleaseId)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$release = Join-Path $repository "artifacts/$ReleaseId"
if (Test-Path -LiteralPath $release) { throw 'Release output already exists.' }
$env:DOTNET_CLI_HOME = Join-Path $repository '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $repository '.nuget/packages'
$env:MSBUILDDISABLENODEREUSE = '1'
$env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
New-Item -ItemType Directory -Path (Join-Path $release 'app'), (Join-Path $release 'converter') | Out-Null
& $dotnet publish (Join-Path $repository 'src/Signage.Web/Signage.Web.csproj') --artifacts-path (Join-Path $repository "artifacts/build-$ReleaseId") -c Release -m:1 -nr:false -p:UseAppHost=false -p:UseSharedCompilation=false -o (Join-Path $release 'app')
if ($LASTEXITCODE -ne 0) { throw 'Portable web publish failed.' }
Remove-Item -LiteralPath (Join-Path $release 'app/appsettings.Development.json') -Force -ErrorAction SilentlyContinue
foreach ($folder in @('dist', 'scripts')) {
    Copy-Item -LiteralPath (Join-Path $repository "src/Signage.Converter/$folder") -Destination (Join-Path $release "converter/$folder") -Recurse
}
foreach ($file in @('package.json', 'package-lock.json')) {
    Copy-Item -LiteralPath (Join-Path $repository "src/Signage.Converter/$file") -Destination (Join-Path $release 'converter')
}
Copy-Item -LiteralPath (Join-Path $repository 'fonts') -Destination $release -Recurse
Copy-Item -LiteralPath (Join-Path $repository 'THIRD_PARTY_NOTICES.md') -Destination $release
$sourceCommit = (& git -C $repository rev-parse --verify HEAD 2>$null)
if ($LASTEXITCODE -ne 0) { $sourceCommit = 'uncommitted' }
@{
    product = 'School Signage Publisher'
    releaseId = $ReleaseId
    platform = 'linux-x64'
    builtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    sourceCommit = $sourceCommit
    frameworkDependent = $true
    dotnetSdk = (& $dotnet --version)
    node = (& node --version)
    converterDependencies = 'Install on Linux with npm ci --omit=dev'
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $release 'release.json') -Encoding utf8
$lines = Get-ChildItem -LiteralPath $release -Recurse -File | Sort-Object FullName | ForEach-Object {
    $relative = $_.FullName.Substring($release.Length + 1).Replace('\', '/')
    '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $relative
}
$lines | Set-Content -LiteralPath (Join-Path $release 'SHA256SUMS') -Encoding ascii
& tar.exe -czf "$release.tar.gz" -C $release .
if ($LASTEXITCODE -ne 0) { throw 'Archive creation failed.' }
$hash = (Get-FileHash -LiteralPath "$release.tar.gz" -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $ReleaseId.tar.gz" | Set-Content -LiteralPath "$release.tar.gz.sha256" -Encoding ascii
Write-Output "Release: $ReleaseId; archive SHA256: $hash"
