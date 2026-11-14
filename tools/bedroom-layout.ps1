param([string]$Scene = "DawnAndDusk\Assets\_LocalTest\ArchViz\3D SCENE\ApartmentDemo.unity")

$lines = Get-Content -LiteralPath $Scene -Encoding UTF8

$goName = @{}
$trGo = @{}
$trPos = @{}
$trFather = @{}

$type = ''
$id = 0
foreach ($line in $lines) {
    if ($line -match '^--- !u!(\d+) &(\d+)') {
        $type = $matches[1]; $id = $matches[2]; continue
    }
    if ($type -eq '1') {
        if ($line -match '^\s+m_Name: (.+)$') { $goName[$id] = $matches[1].Trim(); continue }
        continue
    }
    if ($type -eq '4') {
        if ($line -match '^\s+m_GameObject: \{fileID: (\d+)\}') { $trGo[$id] = $matches[1]; continue }
        if ($line -match '^\s+m_LocalPosition: \{x: ([-\d.eE]+), y: ([-\d.eE]+), z: ([-\d.eE]+)\}') {
            $trPos[$id] = @([double]$matches[1], [double]$matches[2], [double]$matches[3]); continue
        }
        if ($line -match '^\s+m_Father: \{fileID: (\d+)\}') { $trFather[$id] = $matches[1]; continue }
        continue
    }
}

$goTr = @{}
foreach ($k in $trGo.Keys) { if (-not $goTr.ContainsKey($trGo[$k])) { $goTr[$trGo[$k]] = $k } }

function Get-WorldPos([string]$trId) {
    $x = 0.0; $y = 0.0; $z = 0.0; $cur = $trId; $guard = 0
    while ($cur -ne '' -and $cur -ne '0' -and $guard -lt 30) {
        if (-not $trPos.ContainsKey($cur)) { break }
        $x += $trPos[$cur][0]; $y += $trPos[$cur][1]; $z += $trPos[$cur][2]
        $cur = $trFather[$cur]
        $guard++
    }
    return @($x, $y, $z)
}

$keywords = 'TV|Closet|Wardrobe|Cabinet|Curtain|Desk|Shelf_Bedroom|Wall_Bedroom|Dresser'
$rows = @()
foreach ($gid in $goName.Keys) {
    $name = $goName[$gid]
    if ($name -notmatch $keywords) { continue }
    if (-not $goTr.ContainsKey($gid)) { continue }
    $p = Get-WorldPos $goTr[$gid]
    if ($p[0] -lt 1.5 -or $p[0] -gt 5.5 -or $p[2] -lt -0.5 -or $p[2] -gt 4.5) { continue }
    $rows += [pscustomobject]@{ name = $name; x = [math]::Round($p[0], 2); y = [math]::Round($p[1], 2); z = [math]::Round($p[2], 2) }
}

$rows | Sort-Object z, x | Format-Table -AutoSize | Out-String -Width 120
