[CmdletBinding(SupportsShouldProcess)]
param([string]$ServiceName = 'SchoolSignage')
$ErrorActionPreference = 'Stop'
$service = Get-Service -Name $ServiceName -ErrorAction Stop
if ($PSCmdlet.ShouldProcess($ServiceName, 'Stop and remove Windows Service')) {
    if ($service.Status -ne 'Stopped') { Stop-Service -Name $ServiceName -Force }
    & sc.exe delete $ServiceName
    if ($LASTEXITCODE -ne 0) { throw 'Service deletion failed.' }
}
