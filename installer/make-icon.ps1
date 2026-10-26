# Generates app.ico with proper BMP/DIB entries (max toolchain compatibility).
Add-Type -AssemblyName System.Drawing

function New-IconBitmap([int]$size) {
  $bmp = New-Object System.Drawing.Bitmap($size, $size)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'
  $g.Clear([System.Drawing.Color]::Transparent)

  $r = [Math]::Max(2, [int]($size * 0.22))
  $path = New-Object System.Drawing.Drawing2D.GraphicsPath
  $path.AddArc(0,0,$r,$r,180,90)
  $path.AddArc($size-$r,0,$r,$r,270,90)
  $path.AddArc($size-$r,$size-$r,$r,$r,0,90)
  $path.AddArc(0,$size-$r,$r,$r,90,90)
  $path.CloseFigure()

  $rect = New-Object System.Drawing.Rectangle(0,0,$size,$size)
  $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    (New-Object System.Drawing.Point(0,0)), (New-Object System.Drawing.Point($size,$size)),
    [System.Drawing.Color]::FromArgb(255,124,92,252),
    [System.Drawing.Color]::FromArgb(255,56,182,255))
  $g.FillPath($grad, $path)

  $m = [int]($size * 0.16)
  $ir = [Math]::Max(2, [int]($size * 0.12))
  $p = New-Object System.Drawing.Drawing2D.GraphicsPath
  $p.AddArc($m,$m,$ir,$ir,180,90)
  $p.AddArc($size-$m-$ir,$m,$ir,$ir,270,90)
  $p.AddArc($size-$m-$ir,$size-$m-$ir,$ir,$ir,0,90)
  $p.AddArc($m,$size-$m-$ir,$ir,$ir,90,90)
  $p.CloseFigure()
  $g.FillPath((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255,22,22,28))), $p)

  $penW = [Math]::Max(1, $size/11)
  $brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
  for ($i = 0; $i -lt 3; $i++) {
    $x = $size * (0.28 + 0.17 * $i)
    $h = $size * (0.34 - 0.07 * $i)
    $y = ($size - $h) / 2
    $pt = New-Object System.Drawing.Drawing2D.GraphicsPath
    $w = $penW
    $pt.AddPolygon(@(
      (New-Object System.Drawing.PointF($x, $y)),
      (New-Object System.Drawing.PointF(($x + $w*0.9), ($y + 0.14*$h))),
      (New-Object System.Drawing.PointF(($x + $w*0.9), ($y + $h))),
      (New-Object System.Drawing.PointF($x, ($y + 0.86*$h)))
    ))
    $g.FillPath($brush, $pt)
  }

  $g.Dispose()
  return $bmp
}

function Get-Dib([System.Drawing.Bitmap]$bmp) {
  $w = $bmp.Width; $h = $bmp.Height
  # BITMAPINFOHEADER (40 bytes), 32bpp
  $pixelStride = $w * 4
  $pad = (4 - ($pixelStride % 4)) % 4
  $xorSize = ($pixelStride + $pad) * $h
  $andStride = [Math]::Ceiling($w / 8)
  $andStride = 4 * [Math]::Ceiling($andStride / 4)
  $andSize = $andStride * $h

  $ms = New-Object System.IO.MemoryStream
  $bw = New-Object System.IO.BinaryWriter($ms)
  # BITMAPINFOHEADER
  $bw.Write([UInt32]40); $bw.Write([Int32]$w); $bw.Write([Int32]($h * 2))
  $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]0)
  $bw.Write([UInt32]$xorSize); $bw.Write([Int32]2835); $bw.Write([Int32]2835)
  $bw.Write([UInt32]0); $bw.Write([UInt32]0)
  # XOR data bottom-up BGRA
  $rect = New-Object System.Drawing.Rectangle(0, 0, $w, $h)
  $data = $bmp.LockBits($rect, 'ReadOnly', 'Format32bppArgb')
  $ptr = $data.Scan0
  $stride = $data.Stride
  $bytes = New-Object byte[] ($h * $stride)
  [System.Runtime.InteropServices.Marshal]::Copy($ptr, $bytes, 0, $bytes.Length)
  $bmp.UnlockBits($data)
  for ($y = $h - 1; $y -ge 0; $y--) {
    $rowStart = $y * $stride
    for ($x = 0; $x -lt $w; $x++) {
      $idx = $rowStart + $x * 4        # ARGB little-endian: BGRA in memory
      $bw.Write([Byte]$bytes[$idx])    # B
      $bw.Write([Byte]$bytes[$idx+1])  # G
      $bw.Write([Byte]$bytes[$idx+2])  # R
      $bw.Write([Byte]$bytes[$idx+3])  # A
    }
    # row padding
    for ($p = 0; $p -lt $pad; $p++) { $bw.Write([Byte]0) }
  }
  # AND mask (all zeros: alpha channel provides transparency)
  $zeros = New-Object byte[] $andSize
  $bw.Write($zeros)
  $bw.Flush()
  return $ms.ToArray()
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$images = @()
foreach ($s in $sizes) {
  $bmp = New-IconBitmap $s
  $images += ,@{ Size = $s; Data = (Get-Dib $bmp) }
  $bmp.Dispose()
}

$fs = [System.IO.File]::Create('app.ico')
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($img in $images) {
  $s = $img.Size
  $dim = $s; if ($dim -ge 256) { $dim = 0 }
  $bw.Write([Byte]$dim)   # width
  $bw.Write([Byte]$dim)   # height
  $bw.Write([Byte]0)                                      # color count
  $bw.Write([Byte]0)                                      # reserved
  $bw.Write([UInt16]1); $bw.Write([UInt16]32)             # planes, bpp
  $bw.Write([UInt32]$img.Data.Length)
  $bw.Write([UInt32]$offset)
  $offset += $img.Data.Length
}
foreach ($img in $images) { $bw.Write([byte[]]$img.Data) }
$bw.Dispose(); $fs.Dispose()

$big = New-IconBitmap 256
$big.Save('installer\icon256.png', 'Png')
$big.Dispose()
Write-Host 'app.ico regenerated with DIB entries.'
