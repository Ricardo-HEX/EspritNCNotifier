Add-Type -AssemblyName System.Drawing

$resourceFolder = Join-Path $PSScriptRoot 'Resources'
$nativeIcon = Join-Path $resourceFolder 'LogoNC.png'
$nativeIcon16 = Join-Path $resourceFolder 'LogoNC16.png'
$icon16 = Join-Path $resourceFolder 'NcSendIcon16.png'
$icon32 = Join-Path $resourceFolder 'NcSendIcon32.png'

$source = [System.Drawing.Bitmap]::FromFile($nativeIcon)
function New-IconImage([int]$canvasSize, [int]$width, [int]$height, [int]$offsetX, [string]$outputPath) {
    $bitmap = [System.Drawing.Bitmap]::new($canvasSize, $canvasSize, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
        $graphics.DrawImage($source, $offsetX, 0, $width, $height)
        $bitmap.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $graphics.Dispose(); $bitmap.Dispose() }
}

try {
    if (Test-Path -LiteralPath $nativeIcon16) {
        $smallIcon = [System.Drawing.Bitmap]::FromFile($nativeIcon16)
        try {
            if ($smallIcon.Width -ne 16 -or $smallIcon.Height -ne 16) {
                throw "LogoNC16.png debe medir exactamente 16x16 píxeles."
            }
        }
        finally { $smallIcon.Dispose() }
        Copy-Item -LiteralPath $nativeIcon16 -Destination $icon16 -Force
    }
    else {
        New-IconImage 16 15 16 0 $icon16
    }
    New-IconImage 32 30 32 1 $icon32
}
finally { $source.Dispose() }

Copy-Item -LiteralPath $icon32 -Destination (Join-Path $resourceFolder 'NcSendIcon.png') -Force

$images = @([System.IO.File]::ReadAllBytes($icon16), [System.IO.File]::ReadAllBytes($icon32))
$dimensions = @(16, 32)
$stream = [System.IO.File]::Create((Join-Path $resourceFolder 'NcSendIcon.ico'))
$writer = [System.IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
    $offset = 6 + (16 * $images.Count)
    for ($index = 0; $index -lt $images.Count; $index++) {
        $dimension = $dimensions[$index]
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$images[$index].Length); $writer.Write([uint32]$offset)
        $offset += $images[$index].Length
    }
    foreach ($image in $images) { $writer.Write($image) }
}
finally { $writer.Dispose(); $stream.Dispose() }
