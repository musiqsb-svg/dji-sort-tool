# 프로그램 아이콘(app.ico) 만들기: 파란 바탕에 흰 폴더 + 날짜 칸 3개 (DJI 로고는 쓰지 않음)
# 16~256px 여러 크기를 PNG로 그려 ICO 하나에 담음
Add-Type -AssemblyName System.Drawing
$out   = Join-Path $PSScriptRoot 'app.ico'
$sizes = 256, 64, 48, 32, 24, 16

function New-Frame([int]$n) {
    $bmp = New-Object Drawing.Bitmap $n, $n
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
    $f = $n / 256.0
    function New-RoundPath($x, $y, $w, $h, $r) {
        $p = New-Object Drawing.Drawing2D.GraphicsPath
        $d = [single]($r * 2)
        $p.AddArc([single]$x, [single]$y, $d, $d, 180, 90)
        $p.AddArc([single]($x + $w - $d), [single]$y, $d, $d, 270, 90)
        $p.AddArc([single]($x + $w - $d), [single]($y + $h - $d), $d, $d, 0, 90)
        $p.AddArc([single]$x, [single]($y + $h - $d), $d, $d, 90, 90)
        $p.CloseFigure(); $p
    }
    # 바탕
    $bg = New-Object Drawing.Drawing2D.LinearGradientBrush (New-Object Drawing.Point 0, 0), (New-Object Drawing.Point 0, $n),
          ([Drawing.Color]::FromArgb(59, 130, 246)), ([Drawing.Color]::FromArgb(29, 78, 216))
    $g.FillPath($bg, (New-RoundPath (8*$f) (8*$f) (240*$f) (240*$f) (52*$f)))
    # 폴더 (탭 + 몸통)
    $white = [Drawing.Brushes]::White
    $g.FillPath($white, (New-RoundPath (48*$f) (70*$f) (72*$f) (40*$f) (10*$f)))
    $g.FillPath($white, (New-RoundPath (48*$f) (88*$f) (160*$f) (110*$f) (14*$f)))
    # 날짜별로 나뉜 칸 3개
    $blue = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(37, 99, 235))
    if ($n -ge 24) {
        foreach ($i in 0..2) { $g.FillPath($blue, (New-RoundPath ((68 + $i * 42)*$f) (118*$f) (34*$f) (56*$f) (6*$f))) }
    } else {
        $g.FillRectangle($blue, [single](70*$f), [single](128*$f), [single](116*$f), [single](36*$f))
    }
    $g.Dispose()
    $ms = New-Object IO.MemoryStream
    $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    , $ms.ToArray()
}

$frames = foreach ($s in $sizes) { , (New-Frame $s) }
$fs = [IO.File]::Create($out); $w = New-Object IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $b = [byte]($(if ($s -ge 256) { 0 } else { $s }))
    $w.Write($b); $w.Write($b); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$frames[$i].Length); $w.Write([uint32]$offset)
    $offset += $frames[$i].Length
}
foreach ($fr in $frames) { $w.Write($fr) }
$w.Dispose()
"icon: $out ($((Get-Item $out).Length) bytes)"
