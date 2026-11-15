$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetDirectory = Join-Path $PSScriptRoot 'modern\Assets'
New-Item -ItemType Directory -Path $assetDirectory -Force | Out-Null
$imageStreams = @()
foreach ($dimension in @(32, 48, 64, 128, 256)) {
    $bitmap = New-Object System.Drawing.Bitmap($dimension, $dimension)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform(($dimension / 256.0), ($dimension / 256.0))
    $tile = New-Object System.Drawing.Drawing2D.GraphicsPath
    $tile.AddArc(4, 4, 72, 72, 180, 90)
    $tile.AddArc(180, 4, 72, 72, 270, 90)
    $tile.AddArc(180, 180, 72, 72, 0, 90)
    $tile.AddArc(4, 180, 72, 72, 90, 90)
    $tile.CloseFigure()
    $accent = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(84, 89, 232))
    $graphics.FillPath($accent, $tile)
    $windowPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(235, 241, 255), 11)
    $windowPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $graphics.DrawRectangle($windowPen, 56, 61, 136, 112)
    $graphics.DrawLine($windowPen, 56, 88, 192, 88)
    $pointer = [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF(123, 122)), (New-Object System.Drawing.PointF(128, 211)), (New-Object System.Drawing.PointF(151, 186)), (New-Object System.Drawing.PointF(179, 215)), (New-Object System.Drawing.PointF(197, 196)), (New-Object System.Drawing.PointF(167, 169)), (New-Object System.Drawing.PointF(201, 160)))
    $graphics.FillPolygon([System.Drawing.Brushes]::White, $pointer)
    $memory = New-Object System.IO.MemoryStream
    $bitmap.Save($memory, [System.Drawing.Imaging.ImageFormat]::Png)
    $imageStreams += ,@($dimension, $memory.ToArray())
    $memory.Dispose(); $windowPen.Dispose(); $accent.Dispose(); $tile.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
$destination = Join-Path $assetDirectory 'Glide.ico'
$file = [System.IO.File]::Open($destination, [System.IO.FileMode]::Create)
$writer = New-Object System.IO.BinaryWriter($file)
$writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]$imageStreams.Count)
$offset = 6 + 16 * $imageStreams.Count
foreach ($item in $imageStreams) {
    $writer.Write([byte]($item[0] % 256)); $writer.Write([byte]($item[0] % 256))
    $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([UInt16]1); $writer.Write([UInt16]32)
    $writer.Write([UInt32]$item[1].Length); $writer.Write([UInt32]$offset)
    $offset += $item[1].Length
}
foreach ($item in $imageStreams) { $writer.Write([byte[]]$item[1]) }
$writer.Dispose(); $file.Dispose()
