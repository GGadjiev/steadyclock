param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\src\MyTimeTracker.App\Assets\app.ico')
)

Add-Type -AssemblyName System.Drawing

function New-RoundedPath {
    param([float]$X, [float]$Y, [float]$Width, [float]$Height, [float]$Radius)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $diameter = $Radius * 2
    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-MasterBitmap {
    $bitmap = New-Object System.Drawing.Bitmap 256, 256, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $shadow = New-RoundedPath 12 16 232 232 54
    $shadowBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(38, 21, 52, 47))
    $graphics.FillPath($shadowBrush, $shadow)

    $body = New-RoundedPath 8 8 240 240 54
    $bodyBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 47, 122, 112))
    $graphics.FillPath($bodyBrush, $body)

    $faceBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 247, 251, 250))
    $graphics.FillEllipse($faceBrush, 58, 58, 140, 140)

    $ringPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 233, 244, 241)), 6
    $graphics.DrawEllipse($ringPen, 70, 70, 116, 116)

    $handPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 39, 94, 86)), 12
    $handPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $handPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawLine($handPen, 128, 128, 128, 84)
    $handPen.Width = 10
    $graphics.DrawLine($handPen, 128, 128, 164, 148)

    $centerBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 47, 122, 112))
    $graphics.FillEllipse($centerBrush, 119, 119, 18, 18)

    $accentPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 212, 237, 232)), 7
    $accentPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $accentPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawLine($accentPen, 92, 39, 164, 39)

    $graphics.Dispose()
    $shadow.Dispose(); $shadowBrush.Dispose()
    $body.Dispose(); $bodyBrush.Dispose()
    $faceBrush.Dispose(); $ringPen.Dispose(); $handPen.Dispose()
    $centerBrush.Dispose(); $accentPen.Dispose()
    return $bitmap
}

function Get-PngBytes {
    param([System.Drawing.Bitmap]$Source, [int]$Size)
    $scaled = New-Object System.Drawing.Bitmap $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($scaled)
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.DrawImage($Source, 0, 0, $Size, $Size)
    $graphics.Dispose()
    $stream = New-Object System.IO.MemoryStream
    $scaled.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $stream.ToArray()
    $stream.Dispose()
    $scaled.Dispose()
    return ,$bytes
}

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$master = New-MasterBitmap
$images = New-Object 'System.Collections.Generic.List[byte[]]'
foreach ($size in $sizes) {
    $images.Add([byte[]](Get-PngBytes -Source $master -Size $size))
}
$master.Dispose()

$outputDirectory = Split-Path -Parent $OutputPath
if (-not (Test-Path $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

$file = [System.IO.File]::Create($OutputPath)
$writer = New-Object System.IO.BinaryWriter $file
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$sizes.Count)

$offset = 6 + (16 * $sizes.Count)
for ($index = 0; $index -lt $sizes.Count; $index++) {
    $size = $sizes[$index]
    $bytes = $images[$index]
    $writer.Write([byte]$(if ($size -ge 256) { 0 } else { $size }))
    $writer.Write([byte]$(if ($size -ge 256) { 0 } else { $size }))
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]$bytes.Length)
    $writer.Write([uint32]$offset)
    $offset += $bytes.Length
}

foreach ($bytes in $images) {
    $writer.Write([byte[]]$bytes)
}

$writer.Flush()
$file.Flush($true)
$writer.Dispose()
$file.Dispose()
Write-Output "Icon written to $OutputPath ($($images.Count) sizes)"
