param(
    [Parameter(Mandatory = $true)][string]$SptRoot,
    [string]$DotnetRoot = '',
    [int]$WaitSeconds = 20
)

$ErrorActionPreference = 'Stop'
$launcher = Join-Path $SptRoot 'SPT\SPT.Launcher.exe'
if (-not (Test-Path $launcher)) { throw "找不到 $launcher" }

$log = Join-Path $env:TEMP 'spt-launcher-probe.log'
$env:SPT_PROBE_LOG = $log
Remove-Item $log -Force -ErrorAction SilentlyContinue

Write-Host "构建探针 ..."
& dotnet build (Join-Path $PSScriptRoot 'SptProbe.csproj') -c Release -o (Join-Path $PSScriptRoot 'out') --nologo
if ($LASTEXITCODE -ne 0) { throw '探针构建失败' }
$hook = Join-Path $PSScriptRoot 'out\SptProbe.dll'

if ($DotnetRoot) {
    $env:DOTNET_ROOT = $DotnetRoot
    $env:DOTNET_ROOT_X64 = $DotnetRoot
    $env:PATH = "$DotnetRoot;$env:PATH"
}
$env:DOTNET_STARTUP_HOOKS = $hook   # 只对下面启动的这个进程生效

Get-Process SPT.Launcher -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2

Write-Host "用探针启动启动器（$WaitSeconds 秒后读结果）..."
Start-Process -FilePath $launcher -WorkingDirectory (Split-Path $launcher) | Out-Null
Start-Sleep -Seconds $WaitSeconds

Remove-Item Env:\DOTNET_STARTUP_HOOKS -ErrorAction SilentlyContinue
Write-Host "===== $log ====="
Get-Content $log -ErrorAction SilentlyContinue
