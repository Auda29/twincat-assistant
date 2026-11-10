param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
# Shared code/conversation mark, matching product-logo.tsx.
# Regenerates the checked-in SVG, PNG and multi-resolution ICO without extra tools.
$directory = Join-Path $PSScriptRoot 'installer/assets'
[IO.Directory]::CreateDirectory($directory) | Out-Null
$svg = @'
<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 64 64">
  <rect x="5" y="6" width="54" height="44" rx="12" fill="#1d5fc2"/>
  <path d="M14 46v13l20-12" fill="#1d5fc2"/>
  <path d="m23 21-8 8 8 8m18-16 8 8-8 8" fill="none" stroke="#fff" stroke-width="3.5" stroke-linecap="round" stroke-linejoin="round"/>
  <path d="m35 20-6 18" fill="none" stroke="#66e3d3" stroke-width="3.5" stroke-linecap="round"/>
</svg>
'@
[IO.File]::WriteAllText((Join-Path $directory 'setup.svg'), $svg + "`n", [Text.UTF8Encoding]::new($false))
$frames = @()
foreach ($size in @(16,20,24,32,48,64,128,256)) {
    $large = [Drawing.Bitmap]::new($size * 4, $size * 4)
    $graphics = [Drawing.Graphics]::FromImage($large)
    $tile = [Drawing.Drawing2D.GraphicsPath]::new()
    $tail = [Drawing.Drawing2D.GraphicsPath]::new()
    $brush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#1d5fc2'))
    $pen = [Drawing.Pen]::new([Drawing.Color]::White, 3.5)
    $accent = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#66e3d3'), 3.5)
    try {
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.ScaleTransform($size / 16.0, $size / 16.0)
        $tile.AddArc(5,6,24,24,180,90); $tile.AddArc(35,6,24,24,270,90)
        $tile.AddArc(35,26,24,24,0,90); $tile.AddArc(5,26,24,24,90,90); $tile.CloseFigure()
        $graphics.FillPath($brush, $tile)
        $tail.AddPolygon([Drawing.PointF[]]@([Drawing.PointF]::new(14,46),[Drawing.PointF]::new(14,59),[Drawing.PointF]::new(34,47)))
        $graphics.FillPath($brush, $tail)
        $pen.StartCap = $pen.EndCap = [Drawing.Drawing2D.LineCap]::Round
        $pen.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
        $graphics.DrawLines($pen,[Drawing.PointF[]]@([Drawing.PointF]::new(23,21),[Drawing.PointF]::new(15,29),[Drawing.PointF]::new(23,37)))
        $graphics.DrawLines($pen,[Drawing.PointF[]]@([Drawing.PointF]::new(41,21),[Drawing.PointF]::new(49,29),[Drawing.PointF]::new(41,37)))
        $accent.StartCap = $accent.EndCap = [Drawing.Drawing2D.LineCap]::Round
        $graphics.DrawLine($accent,35,20,29,38)
    } finally { $graphics.Dispose(); $tile.Dispose(); $tail.Dispose(); $brush.Dispose(); $pen.Dispose(); $accent.Dispose() }
    $bitmap = [Drawing.Bitmap]::new($size, $size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $stream = [IO.MemoryStream]::new()
    try {
        $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.DrawImage($large, [Drawing.Rectangle]::new(0,0,$size,$size))
        $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
        if ($size -eq 16) { [IO.File]::WriteAllBytes((Join-Path $directory 'menu.png'), $stream.ToArray()) }
        if ($size -eq 256) {
            [IO.File]::WriteAllBytes((Join-Path $directory 'setup.png'), $stream.ToArray())
            $frames += @{size=$size; bytes=$stream.ToArray()}
        } else {
            # DIB frames keep small icons compatible with .NET Framework's Icon
            # decoder. Only the 256px Explorer frame uses PNG compression.
            $dib = [IO.MemoryStream]::new(); $data = [IO.BinaryWriter]::new($dib)
            try {
                $maskStride = [int]([Math]::Ceiling($size / 32.0) * 4)
                $data.Write([uint32]40); $data.Write([int]$size); $data.Write([int]($size*2))
                $data.Write([uint16]1); $data.Write([uint16]32); $data.Write([uint32]0)
                $data.Write([uint32]($size*$size*4+$maskStride*$size))
                1..4 | ForEach-Object { $data.Write([uint32]0) }
                for ($y=$size-1; $y -ge 0; $y--) {
                    for ($x=0; $x -lt $size; $x++) {
                        $color = $bitmap.GetPixel($x,$y)
                        $data.Write([byte]$color.B); $data.Write([byte]$color.G)
                        $data.Write([byte]$color.R); $data.Write([byte]$color.A)
                    }
                }
                for ($y=$size-1; $y -ge 0; $y--) {
                    $mask = New-Object byte[] $maskStride
                    for ($x=0; $x -lt $size; $x++) {
                        if ($bitmap.GetPixel($x,$y).A -eq 0) { $index=[int][Math]::Floor($x/8.0); $mask[$index] = $mask[$index] -bor (128 -shr ($x%8)) }
                    }
                    $data.Write([byte[]]$mask)
                }
                $frames += @{size=$size; bytes=$dib.ToArray()}
            } finally { $data.Dispose() }
        }
    } finally { $graphics.Dispose(); $bitmap.Dispose(); $large.Dispose(); $stream.Dispose() }
}
$stream = [IO.File]::Create((Join-Path $directory 'setup.ico'))
$writer = [IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = $frame.size % 256
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.bytes.Length); $writer.Write([uint32]$offset)
        $offset += $frame.bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.bytes) }
} finally { $writer.Dispose() }
Write-Host 'Generated setup.svg, setup.png, menu.png and setup.ico (16/20/24/32/48/64/128/256 px).'
