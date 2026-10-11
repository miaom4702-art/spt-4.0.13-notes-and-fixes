param(
    [string]$SptRoot = 'C:\Games\SPT',
    [string[]]$Maps = @('bigmap','factory4_day','factory4_night','interchange','laboratory','labyrinth',
                        'lighthouse','rezervbase','sandbox','sandbox_high','shoreline','tarkovstreets','woods'),
    [int]$PeriodCheck = 90,
    [int]$CountStep = 1,
    [int]$MaxBotPerZone = 2,
    [switch]$ZeroTypeLimitsMin,
    [switch]$Restore
)

<#
  低侵入替代方案：不改客户端代码，只压"刷新频率 / 每次数量 / 单区域上限"，
  并可选地把 botTypeLimits 的 min 置 0（服务端不再为"凑够最低数量"而持续补刷）。

  注意：这**不是**距离检查，只是降低刷新强度。真正的距离检查见 plugin/SpawnDistanceGuard。
  改完必须重启 SPT.Server 才生效（数据库在服务端启动时导入）。

  每次运行都会把被改文件原样备份到 <SPT 根>\_botspawn_backup_<时间戳>，
  并生成 restore-botspawn.ps1，可一键还原。
#>

$ErrorActionPreference = 'Stop'
$sptData = Join-Path $SptRoot 'SPT\SPT_Data'
$utf8NoBom = New-Object Text.UTF8Encoding($false)

if ($Restore) {
    $bk = Get-ChildItem $SptRoot -Directory -Filter '_botspawn_backup_*' |
          Sort-Object Name -Descending | Select-Object -First 1
    if (-not $bk) { throw "没找到备份目录 $SptRoot\_botspawn_backup_*" }
    Copy-Item (Join-Path $bk.FullName 'location.json') (Join-Path $sptData 'configs\location.json') -Force
    Get-ChildItem (Join-Path $bk.FullName 'locations') -Filter '*.base.json' | ForEach-Object {
        $map = $_.Name -replace '\.base\.json$',''
        Copy-Item $_.FullName (Join-Path $sptData "database\locations\$map\base.json") -Force
        Write-Host "restored $map"
    }
    Write-Host "已还原自 $($bk.FullName)。记得重启 SPT.Server。"
    exit 0
}

$stamp  = Get-Date -Format 'yyyyMMdd_HHmmss'
$backup = Join-Path $SptRoot "_botspawn_backup_$stamp"
New-Item -ItemType Directory -Force -Path (Join-Path $backup 'locations') | Out-Null

# --- location.json: botTypeLimits 的 min ---
$locPath = Join-Path $sptData 'configs\location.json'
Copy-Item $locPath (Join-Path $backup 'location.json') -Force
if ($ZeroTypeLimitsMin) {
    $raw = Get-Content $locPath -Raw
    $n = ([regex]::Matches($raw, '"min"\s*:\s*\d+')).Count
    if ($n -ne 3) { throw "断言失败：location.json 里 `"min`" 出现 $n 次（预期 3），请人工确认" }
    [IO.File]::WriteAllText($locPath, [regex]::Replace($raw, '"min"\s*:\s*\d+', '"min": 0'), $utf8NoBom)
    Write-Host 'botTypeLimits min -> 0'
}

# --- 各图 base.json ---
foreach ($m in $Maps) {
    $f = Join-Path $sptData "database\locations\$m\base.json"
    if (-not (Test-Path $f)) { Write-Host "跳过 $m"; continue }
    $raw = Get-Content $f -Raw
    $new = $raw
    $changes = @()

    foreach ($spec in @(
        @{ key='BotSpawnPeriodCheck'; to=$PeriodCheck;  when='lt' },
        @{ key='BotSpawnCountStep';   to=$CountStep;    when='gt' },
        @{ key='MaxBotPerZone';       to=$MaxBotPerZone; when='gt' }
    )) {
        $rx = '"' + $spec.key + '"\s*:\s*(-?\d+)'
        $ms = [regex]::Matches($new, $rx)
        if ($ms.Count -ne 1) { throw "断言失败：$m 的 $($spec.key) 出现 $($ms.Count) 次" }
        $cur = [int]$ms[0].Groups[1].Value
        $need = ($spec.when -eq 'lt' -and $cur -lt $spec.to) -or ($spec.when -eq 'gt' -and $cur -gt $spec.to)
        if ($need) {
            $new = [regex]::Replace($new, $rx, ('"' + $spec.key + '": ' + $spec.to), 1)
            $changes += "$($spec.key) $cur->$($spec.to)"
        }
    }

    Copy-Item $f (Join-Path $backup "locations\$m.base.json") -Force
    [IO.File]::WriteAllText($f, $new, $utf8NoBom)
    Write-Host ("{0,-16} {1}" -f $m, ($changes -join ', '))
    # JSON 合法性校验
    Get-Content $f -Raw | ConvertFrom-Json | Out-Null
}

# --- 还原脚本 ---
$restore = @'
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$sptData = Join-Path (Split-Path (Split-Path $here -Parent) -Parent) 'SPT\SPT_Data'
Copy-Item (Join-Path $here 'location.json') (Join-Path $sptData 'configs\location.json') -Force
Get-ChildItem (Join-Path $here 'locations') -Filter '*.base.json' | ForEach-Object {
    $map = $_.Name -replace '\.base\.json$',''
    Copy-Item $_.FullName (Join-Path $sptData "database\locations\$map\base.json") -Force
    Write-Host "restored $map"
}
Write-Host 'done. 记得重启 SPT.Server。'
'@
[IO.File]::WriteAllText((Join-Path $backup 'restore-botspawn.ps1'), $restore, $utf8NoBom)
Write-Host ''
Write-Host "备份 + 还原脚本: $backup"
Write-Host '全部 JSON 校验通过。改完请重启 SPT.Server。'
