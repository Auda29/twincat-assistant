param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
# Reuse the rounded TC mark from product-logo.tsx as a Windows application tile.
# Regenerates the checked-in SVG, PNG and multi-resolution ICO without extra tools.
$directory = Join-Path $PSScriptRoot 'installer/assets'
[IO.Directory]::CreateDirectory($directory) | Out-Null
$svg = @'
<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 64 64">
  <rect x="5" y="5" width="54" height="54" rx="12" fill="#1d5fc2"/>
  <path d="M14 21h18M23 21v22M49 23a11 11 0 1 0 0 18" fill="none" stroke="#fff" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"/>
</svg>
'@
[IO.File]::WriteAllText((Join-Path $directory 'setup.svg'), $svg + "`n", [Text.UTF8Encoding]::new($false))
$frames = @()
foreach ($size in @(16,20,24,32,48,64,128,256)) {
    $large = [Drawing.Bitmap]::new($size * 4, $size * 4)
    $graphics = [Drawing.Graphics]::FromImage($large)
    $tile = [Drawing.Drawing2D.GraphicsPath]::new()
    $brush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#1d5fc2'))
    $pen = [Drawing.Pen]::new([Drawing.Color]::White, 3)
    try {
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.ScaleTransform($size / 16.0, $size / 16.0)
        $tile.AddArc(5,5,24,24,180,90); $tile.AddArc(35,5,24,24,270,90)
        $tile.AddArc(35,35,24,24,0,90); $tile.AddArc(5,35,24,24,90,90); $tile.CloseFigure()
        $graphics.FillPath($brush, $tile)
        $pen.StartCap = $pen.EndCap = [Drawing.Drawing2D.LineCap]::Round
        $graphics.DrawLine($pen,14,21,32,21); $graphics.DrawLine($pen,23,21,23,43)
        # Circle center and arc angles reproduce the SVG's large counterclockwise arc.
        $offset = [Math]::Sqrt(40)
        $angle = [Math]::Atan2(9,$offset) * 180 / [Math]::PI
        $graphics.DrawArc($pen, [single](49-$offset-11), [single]21, [single]22, [single]22, [single]$angle, [single](360-2*$angle))
    } finally { $graphics.Dispose(); $tile.Dispose(); $brush.Dispose(); $pen.Dispose() }
    $bitmap = [Drawing.Bitmap]::new($size, $size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $stream = [IO.MemoryStream]::new()
    try {
        $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.DrawImage($large, [Drawing.Rectangle]::new(0,0,$size,$size))
        $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
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
Write-Host 'Generated setup.svg, setup.png and setup.ico (16/20/24/32/48/64/128/256 px).'
