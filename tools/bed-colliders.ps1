param([string]$Scene = "DawnAndDusk\Assets\_LocalTest\ArchViz\3D SCENE\ApartmentDemo.unity")

$lines = Get-Content -LiteralPath $Scene -Encoding UTF8
$goName = @{}
$trPos = @{}
$trGo = @{}
$col = @{}

$type = ''
$id = 0
foreach ($line in $lines) {
    if ($line -match '^--- !u!(\d+) &(\d+)') {
        $type = $matches[1]
        $id = $matches[2]
        continue
    }
    if ($type -eq '1' -and $line -match '^\s+m_Name: (.+)$') {
        $goName[$id] = $matches[1].Trim()
        continue
    }
    if ($type -eq '4') {
        if ($line -match '^\s+m_GameObject: \{fileID: (\d+)\}') { $trGo[$id] = $matches[1]; continue }
        if ($line -match '^\s+m_LocalPosition: \{x: ([-\d.eE]+), y: ([-\d.eE]+), z: ([-\d.eE]+)\}') {
            $trPos[$id] = @([double]$matches[1], [double]$matches[2], [double]$matches[3])
            continue
        }
    }
    if ($type -eq '65') {
        if ($line -match '^\s+m_GameObject: \{fileID: (\d+)\}') { $col[$id] = @{ go = $matches[1]; center = @(0.0, 0.0, 0.0); size = @(0.0, 0.0, 0.0) }; continue }
        if ($col.ContainsKey($id)) {
            if ($line -match '^\s+m_Center: \{x: ([-\d.eE]+), y: ([-\d.eE]+), z: ([-\d.eE]+)\}') {
                $col[$id].center = @([double]$matches[1], [double]$matches[2], [double]$matches[3]); continue
            }
            if ($line -match '^\s+m_Size: \{x: ([-\d.eE]+), y: ([-\d.eE]+), z: ([-\d.eE]+)\}') {
                $col[$id].size = @([double]$matches[1], [double]$matches[2], [double]$matches[3]); continue
            }
        }
    }
}

# gameObjectId -> transform (取第一个)
$goTr = @{}
foreach ($k in $trGo.Keys) { if (-not $goTr.ContainsKey($trGo[$k])) { $goTr[$trGo[$k]] = $k } }

Write-Output "名字含 Bed 的 BoxCollider（世界包围盒，未计旋转）："
foreach ($k in $col.Keys) {
    $go = $col[$k].go
    if (-not $goName.ContainsKey($go)) { continue }
    $name = $goName[$go]
    if ($name -notmatch 'Bed') { continue }
    if (-not $goTr.ContainsKey($go)) { continue }
    $tp = $trPos[$goTr[$go]]
    $c = $col[$k].center
    $s = $col[$k].size
    $wx = $tp[0] + $c[0]; $wy = $tp[1] + $c[1]; $wz = $tp[2] + $c[2]
    $min = "{0:N2},{1:N2},{2:N2}" -f ($wx - $s[0] / 2), ($wy - $s[1] / 2), ($wz - $s[2] / 2)
    $max = "{0:N2},{1:N2},{2:N2}" -f ($wx + $s[0] / 2), ($wy + $s[1] / 2), ($wz + $s[2] / 2)
    Write-Output ("{0,-34} center=({1:N2},{2:N2},{3:N2}) size=({4:N2},{5:N2},{6:N2})  min={7}  max={8}" -f $name, $wx, $wy, $wz, $s[0], $s[1], $s[2], $min, $max)
}
