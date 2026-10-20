$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$source=[Drawing.Image]::FromFile((Join-Path $PSScriptRoot 'assets\patch.png'))
$frames=[Collections.Generic.List[byte[]]]::new()
$sizes=16,24,32,48,64,128,256
try {
    foreach($size in $sizes) {
        $bitmap=[Drawing.Bitmap]::new($size,$size,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics=[Drawing.Graphics]::FromImage($bitmap)
        $graphics.CompositingMode=[Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode=[Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.DrawImage($source,0,0,$size,$size)
        $stream=[IO.MemoryStream]::new()
        $bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
        $frames.Add($stream.ToArray())
        $stream.Dispose();$graphics.Dispose();$bitmap.Dispose()
    }
    $out=[IO.File]::Create((Join-Path $PSScriptRoot 'assets\patch.ico'))
    $writer=[IO.BinaryWriter]::new($out)
    try {
        $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$frames.Count)
        $offset=6+16*$frames.Count
        for($i=0;$i -lt $frames.Count;$i++) {
            $dimension=if($sizes[$i] -eq 256){0}else{$sizes[$i]}
            $writer.Write([byte]$dimension);$writer.Write([byte]$dimension);$writer.Write([byte]0);$writer.Write([byte]0)
            $writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$frames[$i].Length);$writer.Write([uint32]$offset)
            $offset+=$frames[$i].Length
        }
        foreach($frame in $frames){$writer.Write($frame)}
    } finally {$writer.Dispose();$out.Dispose()}
} finally {$source.Dispose()}
