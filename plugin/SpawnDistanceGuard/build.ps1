param(
    [string]$SptRoot = 'D:\ETF\SPT-4.0.13',
    [switch]$Deploy
)

$ErrorActionPreference = 'Stop'
$proj = Join-Path $PSScriptRoot 'SpawnDistanceGuard.csproj'

Write-Host "SptRoot = $SptRoot"
if (-not (Test-Path (Join-Path $SptRoot 'EscapeFromTarkov_Data\Managed\Assembly-CSharp.dll'))) {
    throw "找不到 $SptRoot\EscapeFromTarkov_Data\Managed\Assembly-CSharp.dll，请用 -SptRoot 指定 SPT 根目录"
}

Push-Location $PSScriptRoot
try {
    & dotnet build $proj -c Release -p:SptRoot="$SptRoot" --nologo
    if ($LASTEXITCODE -ne 0) { throw "编译失败 (exit=$LASTEXITCODE)" }
} finally {
    Pop-Location
}

$dll = Join-Path $PSScriptRoot 'bin\Release\net472\SpawnDistanceGuard.dll'
if (-not (Test-Path $dll)) { throw "没找到产物 $dll" }
Write-Host "构建成功: $dll"

if ($Deploy) {
    $dst = Join-Path $SptRoot 'BepInEx\plugins\SpawnDistanceGuard'
    New-Item -ItemType Directory -Force -Path $dst | Out-Null
    Copy-Item $dll $dst -Force
    Write-Host "已部署到: $dst"
}
