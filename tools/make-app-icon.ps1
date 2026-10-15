# Convert the supplied image to a Windows icon container; preserve its original appearance.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$assets=Join-Path (Split-Path -Parent $PSScriptRoot) 'src\Checkpoint.App\Assets'
$source=[Drawing.Image]::FromFile((Join-Path $assets 'checkpoint.png'))
$frames=[Collections.Generic.List[byte[]]]::new()
$sizes=@(16,24,32,48,64,128,256)
try {
    foreach ($size in $sizes) {
        $bitmap=[Drawing.Bitmap]::new($size,$size)
        $graphics=[Drawing.Graphics]::FromImage($bitmap)
        $stream=[IO.MemoryStream]::new()
        try {
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $scale=[Math]::Min($size/$source.Width,$size/$source.Height)
            $width=[int][Math]::Round($source.Width*$scale)
            $height=[int][Math]::Round($source.Height*$scale)
            $graphics.DrawImage($source,[Drawing.Rectangle]::new([int](($size-$width)/2),[int](($size-$height)/2),$width,$height))
            $bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
            $frames.Add($stream.ToArray())
        } finally { $stream.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
    }
} finally { $source.Dispose() }
$output=[IO.File]::Create((Join-Path $assets 'checkpoint.ico'))
$writer=[IO.BinaryWriter]::new($output)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Length)
    $offset=6+16*$sizes.Length
    for ($i=0;$i -lt $sizes.Length;$i++) {
        $dimension=if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
        $offset+=$frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write($frame) }
} finally { $writer.Dispose(); $output.Dispose() }
