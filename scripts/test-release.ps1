[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$ReleaseRoot,
    [string]$FixturePath,
    [string]$DotnetPath
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$release = [IO.Path]::GetFullPath($ReleaseRoot)
$appRoot = Join-Path $release 'app'
$appDll = Join-Path $appRoot 'Signage.Web.dll'
if (-not (Test-Path -LiteralPath $appDll)) { throw 'Published app is missing.' }
if (Test-Path -LiteralPath (Join-Path $appRoot 'appsettings.Development.json')) { throw 'Development configuration must not ship.' }
if (-not $FixturePath) { $FixturePath = Join-Path $repoRoot 'output/playwright/fixtures/playwright-welcome.pptx' }
if (-not (Test-Path -LiteralPath $FixturePath)) { throw 'Run the end-to-end tests first to generate the non-private smoke-test presentation.' }
if (-not $DotnetPath) {
    $localDotnet = Join-Path $repoRoot '.dotnet/dotnet.exe'
    $DotnetPath = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { (Get-Command dotnet -ErrorAction Stop).Source }
}
$forbidden = @(Get-ChildItem -LiteralPath $release -Recurse -File | Where-Object {
    $_.Name -match '(?i)(^appsettings\.Production\.json$|^\.env($|\.)|\.(pptx|pptm|ppt|db|pfx|pem|key)$)'
})
if ($forbidden.Count -gt 0) { throw 'Unexpected private/configuration files in the release package.' }

$smokeRoot = Join-Path $repoRoot ('artifacts/release-smoke-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $smokeRoot | Out-Null
$settings = @{
    outputWidth = 1920; fontDirectories = @((Join-Path $release 'fonts')); useSystemFonts = $false
    fontMapping = @{ Arial = 'Arimo'; Calibri = 'Carlito' }; fitTextToBox = $true
    requestedSourceSlideNumbers = @(1); timeoutSeconds = 60; diagnosticVerbosity = 'normal'
}
$settingsPath = Join-Path $smokeRoot 'settings.json'
$settings | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $settingsPath -Encoding utf8
& node (Join-Path $release 'converter/dist/cli.js') health
if ($LASTEXITCODE -ne 0) { throw 'Packaged converter health failed.' }
& node (Join-Path $release 'converter/dist/cli.js') render --input $FixturePath --output (Join-Path $smokeRoot 'render') --settings $settingsPath
if ($LASTEXITCODE -ne 0) { throw 'Packaged converter rendering failed.' }
$png = [IO.File]::ReadAllBytes((Join-Path $smokeRoot 'render/slides/slide-0001.png'))
$width = $png[16] * 16777216 + $png[17] * 65536 + $png[18] * 256 + $png[19]
$height = $png[20] * 16777216 + $png[21] * 65536 + $png[22] * 256 + $png[23]
if ($width -ne 1920 -or $height -ne 1080 -or $png.Length -lt 10000) { throw 'Packaged renderer did not produce the expected 1920x1080 slide.' }

# A synthetic AD configuration starts the real Production pipeline without
# contacting a domain controller or using any school account/password.
$environment = @{
    ASPNETCORE_ENVIRONMENT = 'Production'; Authentication__Mode = 'ActiveDirectory'
    ActiveDirectory__Host = 'directory.example.invalid'; ActiveDirectory__DomainName = 'example.invalid'
    ActiveDirectory__UpnSuffix = 'example.invalid'; ActiveDirectory__BaseDn = 'DC=example,DC=invalid'
    ActiveDirectory__BootstrapAdminUpn = ''; ActiveDirectory__NetBiosDomain = 'EXAMPLE'
    ConnectionStrings__SignageDb = "Data Source=$smokeRoot/signage.db;Foreign Keys=True;Default Timeout=5"
    Database__InstanceLockPath = (Join-Path $smokeRoot 'signage.lock')
    Storage__RootPath = (Join-Path $smokeRoot 'content'); Storage__MinimumFreeBytes = '0'
    Rendering__TempRoot = (Join-Path $smokeRoot 'temp'); Rendering__NodeExecutable = (Get-Command node).Source
    Rendering__ConverterEntryPoint = (Join-Path $release 'converter/dist/cli.js')
    Rendering__WorkingDirectory = (Join-Path $release 'converter')
    Rendering__FontDirectories__0 = (Join-Path $release 'fonts')
    Backup__RootPath = (Join-Path $smokeRoot 'backups'); AllowedHosts = 'localhost;127.0.0.1'
    Logging__LogLevel__Default = 'Warning'
}
$savedEnvironment = @{}
$server = $null
$client = $null
try {
    foreach ($name in $environment.Keys) {
        $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        [Environment]::SetEnvironmentVariable($name, [string]$environment[$name], 'Process')
    }
    & $DotnetPath $appDll --migrate --contentRoot $appRoot
    if ($LASTEXITCODE -ne 0) { throw 'Packaged database migration failed.' }
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $port = $listener.LocalEndpoint.Port
    $listener.Stop()
    $server = Start-Process -FilePath $DotnetPath -ArgumentList @("`"$appDll`"", '--contentRoot', "`"$appRoot`"", '--urls', "http://127.0.0.1:$port") -WorkingDirectory $appRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $smokeRoot 'server.log') -RedirectStandardError (Join-Path $smokeRoot 'server-errors.log')
    $handler = [Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    $client = [Net.Http.HttpClient]::new($handler)
    $client.Timeout = [TimeSpan]::FromSeconds(5)
    $client.DefaultRequestHeaders.Add('X-Forwarded-Proto', 'https')
    $origin = "http://127.0.0.1:$port"
    $ready = $false
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(45)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if ($server.HasExited) { throw 'Packaged Production server exited unexpectedly. Inspect smoke-test logs.' }
        try {
            $response = $client.GetAsync("$origin/health/ready").GetAwaiter().GetResult()
            $ready = $response.IsSuccessStatusCode -and $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() -eq 'Healthy'
            $response.Dispose()
            if ($ready) { break }
        } catch { }
        Start-Sleep -Milliseconds 250
    }
    if (-not $ready) { throw 'Packaged Production server did not become ready.' }
    foreach ($probe in @(@('/health/live', 200), @('/dev-login', 404), @('/api/screen-groups/available', 401), @('/player/', 200), @('/Login', 200))) {
        $response = $client.GetAsync("$origin$($probe[0])").GetAwaiter().GetResult()
        try {
            if ([int]$response.StatusCode -ne $probe[1]) { throw "Production probe failed: $($probe[0]) returned $([int]$response.StatusCode)." }
            if ($probe[0] -eq '/Login') {
                $html = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                if ($html -notmatch 'type="password"' -or $html -match 'Development sign-in') { throw 'Production directory login is missing or development sign-in leaked.' }
            }
        } finally { $response.Dispose() }
    }
    Write-Host "Production smoke test passed: migrations, readiness, guarded APIs, no development login, player, AD login page, and packaged rendering ($width x $height). Logs: $smokeRoot"
} finally {
    if ($client) { $client.Dispose() }
    if ($server -and -not $server.HasExited) { $server.Kill(); $server.WaitForExit(10000) | Out-Null }
    foreach ($name in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process') }
}
