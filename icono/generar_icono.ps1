# Genera icono.ico (16 a 256 px) para Selector de Fotos.
# Diseño: cuadrado redondeado con degradado azul → violeta, una foto (montañas y sol) y el ✓ verde de favorita.
#   powershell -ExecutionPolicy Bypass -File icono\generar_icono.ps1
param([string]$Salida = (Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "icono.ico"),
      [string]$Vista = "")

Add-Type -AssemblyName System.Drawing

function RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function C([string]$hex) { return [System.Drawing.ColorTranslator]::FromHtml($hex) }

function Dibujar([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $k = $size / 256.0
    $g.ScaleTransform($k, $k)

    # Fondo: cuadrado redondeado con degradado
    $bg = RoundRect 8 8 240 240 56
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 8, 8), (New-Object System.Drawing.PointF 248, 248), (C "#3B82F6"), (C "#7C3AED")
    $g.FillPath($grad, $bg)

    # Foto: marco blanco con cielo, montañas y sol
    $card = RoundRect 44 58 168 132 18
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)), $card)
    $inner = RoundRect 58 72 140 104 8
    $g.SetClip($inner)
    $g.FillRectangle((New-Object System.Drawing.SolidBrush (C "#DBEAFE")), 58, 72, 140, 104)
    $g.FillEllipse((New-Object System.Drawing.SolidBrush (C "#FBBF24")), 150, 84, 30, 30)
    $back = [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF 104, 176), (New-Object System.Drawing.PointF 150, 112), (New-Object System.Drawing.PointF 210, 176))
    $g.FillPolygon((New-Object System.Drawing.SolidBrush (C "#60A5FA")), $back)
    $front = [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF 50, 176), (New-Object System.Drawing.PointF 100, 104), (New-Object System.Drawing.PointF 150, 176))
    $g.FillPolygon((New-Object System.Drawing.SolidBrush (C "#1E40AF")), $front)
    $g.ResetClip()

    # Marca de favorita: círculo verde con ✓ blanco
    $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)), 150, 150, 92, 92)
    $g.FillEllipse((New-Object System.Drawing.SolidBrush (C "#22C55E")), 158, 158, 76, 76)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), 13
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
    $check = [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF 178, 197), (New-Object System.Drawing.PointF 192, 211), (New-Object System.Drawing.PointF 216, 182))
    $g.DrawLines($pen, $check)

    $g.Dispose()
    return $bmp
}

$sizes = 256, 128, 64, 48, 32, 24, 16
$pngs = @()
foreach ($s in $sizes) {
    $bmp = Dibujar $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    if ($Vista -and $s -eq 256) { $bmp.Save($Vista, [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
    $pngs += ,($ms.ToArray())
}

# Contenedor ICO con imágenes PNG (compatible con Windows Vista y posteriores)
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$pngs[$i].Length); $w.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Flush()
[System.IO.File]::WriteAllBytes($Salida, $out.ToArray())
Write-Output "Icono generado: $Salida"
