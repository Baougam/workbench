# Zeichnet das Workbench-Symbol (zwei Ebenen übereinander) in allen Größen und schreibt eine .ico-Datei.
param([Parameter(Mandatory)][string]$Ziel)
Add-Type -AssemblyName System.Drawing

function Rund([System.Drawing.Graphics]$g, [System.Drawing.Brush]$b, [float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
  $p = New-Object System.Drawing.Drawing2D.GraphicsPath
  $d = $r * 2
  $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
  $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
  $p.CloseFigure(); $g.FillPath($b, $p); $p.Dispose()
}

$groessen = 16, 20, 24, 32, 40, 48, 64, 128, 256
$bilder = foreach ($s in $groessen) {
  $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'; $g.Clear([System.Drawing.Color]::Transparent)
  $k = $s / 24.0
  Rund $g (New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 20, 22, 28))) 0 0 $s $s (5.5 * $k)
  Rund $g (New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(120, 244, 184, 96))) (8 * $k) (4.5 * $k) (12 * $k) (9.5 * $k) (2.6 * $k)
  Rund $g (New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 244, 184, 96))) (4 * $k) (10 * $k) (12 * $k) (9.5 * $k) (2.6 * $k)
  $g.Dispose()
  $ms = New-Object System.IO.MemoryStream
  $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  ,@($s, $ms.ToArray())
}

$fs = [System.IO.File]::Create($Ziel)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$bilder.Count)
$off = 6 + 16 * $bilder.Count
foreach ($b in $bilder) {
  $s = $b[0]; $d = $b[1]
  $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s }))); $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
  $w.Write([byte]0); $w.Write([byte]0); $w.Write([uint16]1); $w.Write([uint16]32)
  $w.Write([uint32]$d.Length); $w.Write([uint32]$off); $off += $d.Length
}
foreach ($b in $bilder) { $w.Write([byte[]]$b[1]) }
$w.Close()
