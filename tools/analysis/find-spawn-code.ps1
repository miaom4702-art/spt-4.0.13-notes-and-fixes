param(
    [string]$SptRoot = 'C:\Games\SPT',
    [string]$TypeName = 'SpawnPointMarker',
    [string]$CallRegex = 'GClass3586::IsValid\(EFT\.Game\.Spawning\.ISpawnPoint'
)

<#
  用 BepInEx 自带的 Mono.Cecil 在客户端程序集里做静态交叉引用分析，
  用途：找到"AI 刷点选择"这类逻辑的准确 Harmony 补丁点。

  运行前不需要反编译整个程序集，直接读元数据/IL，几秒出结果。
#>

$ErrorActionPreference = 'Stop'
$cecil = Join-Path $SptRoot 'BepInEx\core\Mono.Cecil.dll'
$dll   = Join-Path $SptRoot 'EscapeFromTarkov_Data\Managed\Assembly-CSharp.dll'
foreach ($p in @($cecil, $dll)) { if (-not (Test-Path $p)) { throw "找不到 $p" } }

Add-Type -Path $cecil
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($dll)
$mod = $asm.MainModule

function AllTypes($t) { $t; foreach ($n in $t.NestedTypes) { AllTypes $n } }
$types = $mod.Types | ForEach-Object { AllTypes $_ }
Write-Host "types: $($types.Count)"

Write-Host ''
Write-Host "=== 1) 引用 $TypeName 的方法 ==="
foreach ($t in $types) {
    foreach ($m in $t.Methods) {
        if (-not $m.HasBody) { continue }
        foreach ($ins in $m.Body.Instructions) {
            if ($ins.Operand -and $ins.Operand.ToString() -match $TypeName) {
                "  {0}::{1}   [{2} {3}]" -f $t.FullName, $m.Name, $ins.OpCode.Name, $ins.Operand
                break
            }
        }
    }
}

Write-Host ''
Write-Host "=== 2) 调用点及"它前面 3 条指令"（用来看到传入的常量，例如 ldc.r4 3） ==="
foreach ($t in $types) {
    foreach ($m in $t.Methods) {
        if (-not $m.HasBody) { continue }
        $prev = @()
        foreach ($ins in $m.Body.Instructions) {
            if ($ins.OpCode.Name -eq 'call' -and $ins.Operand -and $ins.Operand.ToString() -match $CallRegex) {
                $ctx = ($prev | Select-Object -Last 3 | ForEach-Object { "$($_.OpCode.Name) $($_.Operand)" }) -join ' | '
                "  {0}::{1}" -f $t.FullName, $m.Name
                "        之前: $ctx"
            }
            $prev += $ins
        }
    }
}

Write-Host ''
Write-Host '=== 3) 反编译具体类型（需要 ilspycmd） ==='
Write-Host "  dotnet tool install --global ilspycmd"
Write-Host "  ilspycmd -r `"$SptRoot\EscapeFromTarkov_Data\Managed`" -o .\decomp -t GClass3586 `"$dll`""
Write-Host "  ilspycmd -r `"$SptRoot\EscapeFromTarkov_Data\Managed`" -o .\decomp -t SpawnSystemClass `"$dll`""
