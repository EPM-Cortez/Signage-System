[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)] [string]$ReleaseRoot,
    [string]$ServiceName = 'SchoolSignage',
    [string]$DotnetPath = 'C:\Program Files\dotnet\dotnet.exe',
    [string]$ListenUrl = 'http://127.0.0.1:5080'
)
$ErrorActionPreference = 'Stop'
$release = [IO.Path]::GetFullPath($ReleaseRoot)
$appDll = Join-Path $release 'app\Signage.Web.dll'
if (-not (Test-Path -LiteralPath $appDll)) { throw "Release app not found: $appDll" }
if (-not (Test-Path -LiteralPath $DotnetPath)) { throw "dotnet not found: $DotnetPath" }
if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) { throw "Service '$ServiceName' already exists." }
$appRoot = Join-Path $release 'app'
if (-not (Test-Path -LiteralPath (Join-Path $appRoot 'appsettings.Production.json'))) { throw 'Configure app/appsettings.Production.json before installing the service.' }
$binary = ('"{0}" "{1}" --environment Production --contentRoot "{2}" --urls "{3}"' -f $DotnetPath, $appDll, $appRoot, $ListenUrl)
if ($PSCmdlet.ShouldProcess($ServiceName, 'Install and start Windows Service')) {
    New-Service -Name $ServiceName -BinaryPathName $binary -DisplayName 'School Signage Publisher' -Description 'Self-hosted PowerPoint browser signage publisher.' -StartupType Automatic
    Start-Service -Name $ServiceName
}
