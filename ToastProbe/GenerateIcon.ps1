Add-Type -AssemblyName System.Drawing

function New-Logo([int]$size, [string]$path) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $margin = [int]($size * 0.06)
    $background = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(19, 54, 74))
    $radius = [int]($size * 0.18)
    $rounded = New-Object System.Drawing.Drawing2D.GraphicsPath
    $rounded.AddArc($margin, $margin, $radius, $radius, 180, 90)
    $rounded.AddArc($size - $margin - $radius, $margin, $radius, $radius, 270, 90)
    $rounded.AddArc($size - $margin - $radius, $size - $margin - $radius, $radius, $radius, 0, 90)
    $rounded.AddArc($margin, $size - $margin - $radius, $radius, $radius, 90, 90)
    $rounded.CloseFigure()
    $graphics.FillPath($background, $rounded)

    $bell = New-Object System.Drawing.Drawing2D.GraphicsPath
    $bell.AddArc([int]($size * 0.30), [int]($size * 0.22), [int]($size * 0.40), [int]($size * 0.42), 180, 180)
    $bell.AddLine([int]($size * 0.30), [int]($size * 0.43), [int]($size * 0.24), [int]($size * 0.67))
    $bell.AddLine([int]($size * 0.24), [int]($size * 0.67), [int]($size * 0.76), [int]($size * 0.67))
    $bell.AddLine([int]($size * 0.76), [int]($size * 0.67), [int]($size * 0.70), [int]($size * 0.43))
    $bell.CloseFigure()
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $graphics.FillPath($white, $bell)
    $graphics.FillEllipse($white, [int]($size * 0.44), [int]($size * 0.67), [int]($size * 0.12), [int]($size * 0.12))

    $green = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(72, 196, 145))
    $graphics.FillEllipse($green, [int]($size * 0.61), [int]($size * 0.57), [int]($size * 0.24), [int]($size * 0.24))
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, [Math]::Max(2, [int]($size * 0.035)))
    $graphics.DrawLine($pen, [int]($size * 0.67), [int]($size * 0.69), [int]($size * 0.72), [int]($size * 0.75))
    $graphics.DrawLine($pen, [int]($size * 0.72), [int]($size * 0.75), [int]($size * 0.80), [int]($size * 0.65))

    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $bitmap.Dispose()
    $background.Dispose()
    $bell.Dispose()
    $white.Dispose()
    $green.Dispose()
    $pen.Dispose()
    $rounded.Dispose()
}

$assetPath = Join-Path $PSScriptRoot 'Assets'
New-Logo 44 (Join-Path $assetPath 'Square44x44Logo.png')
New-Logo 150 (Join-Path $assetPath 'Square150x150Logo.png')
New-Logo 256 (Join-Path $assetPath 'StoreLogo.png')

$iconSource = Join-Path $assetPath 'StoreLogo.png'
$bitmap = [System.Drawing.Bitmap]::FromFile($iconSource)
$icon = [System.Drawing.Icon]::FromHandle($bitmap.GetHicon())
$stream = [System.IO.File]::Create((Join-Path $PSScriptRoot 'Assets\CodexToastMonitor.ico'))
$icon.Save($stream)
$stream.Dispose()
$icon.Dispose()
$bitmap.Dispose()
