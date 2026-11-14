<#
.SYNOPSIS
  对比"路径应有长度"和"实际走了多远"，量化起步/收势接缝多送出去的位移。

.DESCRIPTION
  game.jsonl 里每次 walk 都有 walk_accept（route 折线）和 walk_end。
  本脚本按 anim 事件的 pos 采样累加身体实际位移，和折线长度相减：
    差值 > 0  => 接缝多送了位移（叠加或姿态泄漏）
    差值 ≈ 0  => 位移与路径一致

  用途：A/B 实验。改 WalkStart→Walk 的混合时长后各跑一次，比 delta。
    - 差值与混合时长无关（都是 0.19 左右） => 姿态偏移泄漏
    - 差值随混合时长变大               => 根位移叠加

.EXAMPLE
  pwsh -File tools/walk-excess.ps1
  pwsh -File tools/walk-excess.ps1 -Path DawnAndDusk/Logs/game.jsonl
#>
param(
    [string]$Path = "DawnAndDusk\Logs\game.jsonl"
)

if (-not (Test-Path $Path)) {
    Write-Error "找不到日志：$Path"
    exit 1
}

$events = Get-Content $Path -Encoding UTF8 | ForEach-Object {
    try { $_ | ConvertFrom-Json } catch { $null }
} | Where-Object { $null -ne $_ }

if ($events.Count -eq 0) {
    Write-Error "日志为空或格式不对"
    exit 1
}

function Get-RouteLength([string]$route) {
    $pts = @()
    foreach ($seg in ($route -split ';')) {
        $q = $seg -split ','
        if ($q.Count -ge 2) { $pts += ,@([double]$q[0], [double]$q[1]) }
    }
    $len = 0.0
    for ($i = 1; $i -lt $pts.Count; $i++) {
        $len += [math]::Sqrt([math]::Pow($pts[$i][0] - $pts[$i - 1][0], 2) + [math]::Pow($pts[$i][1] - $pts[$i - 1][1], 2))
    }
    return $len
}

$positions = $events | Where-Object { $_.evt -eq 'anim' } | ForEach-Object {
    $q = $_.pos -split ','
    [pscustomobject]@{ t = [double]$_.t; x = [double]$q[0]; z = [double]$q[2] }
} | Sort-Object t

$walks = @()
$accepts = @($events | Where-Object { $_.evt -eq 'walk_accept' })
$ends = @($events | Where-Object { $_.evt -eq 'walk_end' })
$j = 0
foreach ($a in $accepts) {
    $e = $null
    while ($j -lt $ends.Count) {
        if ([double]$ends[$j].t -ge [double]$a.t) { $e = $ends[$j]; $j++; break }
        $j++
    }
    if ($null -eq $e) { continue }

    $t0 = [double]$a.t
    $t1 = [double]$e.t
    $seg = $positions | Where-Object { $_.t -ge $t0 -and $_.t -le $t1 }
    $sum = 0.0
    $peak = 0.0
    $prev = $null
    foreach ($c in $seg) {
        if ($null -ne $prev) {
            $dt = $c.t - $prev.t
            $d = [math]::Sqrt([math]::Pow($c.x - $prev.x, 2) + [math]::Pow($c.z - $prev.z, 2))
            $sum += $d
            if ($dt -gt 0.001) {
                $v = $d / $dt
                if ($v -gt $peak) { $peak = $v }
            }
        }
        $prev = $c
    }

    $routeLen = Get-RouteLength $a.route
    $walks += [pscustomobject]@{
        From    = $a.from
        To      = $a.to
        Route   = [math]::Round($routeLen, 2)
        Body    = [math]::Round($sum, 2)
        Excess  = [math]::Round($sum - $routeLen, 2)
        Percent = if ($routeLen -gt 0.01) { [math]::Round(($sum - $routeLen) / $routeLen * 100, 1) } else { 0 }
        Peak    = [math]::Round($peak, 2)
        Elapsed = [math]::Round([double]$e.elapsed, 2)
        Reached = $e.reached
    }
}

if ($walks.Count -eq 0) {
    Write-Host "日志里没有完整的 walk（walk_accept + walk_end）" -ForegroundColor Yellow
    exit 0
}

$walks | Format-Table From, To, Route, Body, Excess, Percent, Peak, Elapsed, Reached -AutoSize

# 接缝明细：每条 walk 里速度最高的几个采样
Write-Host ""
Write-Host "接缝事件（seam）：每 0.05 秒一条，v = 该窗口的根位移速度" -ForegroundColor Cyan
$seams = @($events | Where-Object { $_.evt -eq 'seam' })
if ($seams.Count -eq 0) {
    Write-Host "  （本次没有过渡日志，检查 RootMotionRelay.seamLog 是否打开）" -ForegroundColor DarkGray
} else {
    $seams | ForEach-Object {
        [pscustomobject]@{
            t      = [math]::Round([double]$_.t, 2)
            cur    = $_.cur
            next   = $_.next
            tNorm  = $_.tNorm
            sum    = $_.sum
            v      = $_.v
        }
    } | Format-Table -AutoSize
}
