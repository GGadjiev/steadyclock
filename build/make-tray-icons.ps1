param(
    [string]$SourcePath = (Join-Path $PSScriptRoot '..\src\SteadyClock.Desktop\Assets\app.png'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\src\SteadyClock.Desktop\Assets\Tray')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-TrayIcon {
    param(
        [System.Drawing.Image]$Source,
        [string]$Path,
        [System.Drawing.Color]$BadgeColor
    )

    $size = 64
    $bitmap = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.DrawImage($Source, 2, 2, 56, 56)

    $borderBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $badgeBrush = New-Object System.Drawing.SolidBrush $BadgeColor
    $graphics.FillEllipse($borderBrush, 40, 40, 23, 23)
    $graphics.FillEllipse($badgeBrush, 43, 43, 17, 17)

    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)

    $badgeBrush.Dispose()
    $borderBrush.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$source = [System.Drawing.Image]::FromFile((Resolve-Path $SourcePath))
try {
    New-TrayIcon $source (Join-Path $OutputDirectory 'tray-stopped.png') ([System.Drawing.Color]::FromArgb(255, 120, 132, 128))
    New-TrayIcon $source (Join-Path $OutputDirectory 'tray-running.png') ([System.Drawing.Color]::FromArgb(255, 46, 160, 110))
    New-TrayIcon $source (Join-Path $OutputDirectory 'tray-paused.png') ([System.Drawing.Color]::FromArgb(255, 214, 158, 46))
}
finally {
    $source.Dispose()
}

Get-ChildItem $OutputDirectory -File |
    Select-Object Name, Length
