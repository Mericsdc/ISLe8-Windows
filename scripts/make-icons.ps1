# Windows'un istediği .ico dosyalarını üretir (Assets klasörüne):
#   AppIcon.ico     ISLe8.exe'nin ve Ayarlar penceresinin simgesi, mevcut logo PNG'lerinden
#   TrayOnDark.ico  bildirim alanı simgesi, koyu görev çubuğu için (beyaz kapsül)
#   TrayOnLight.ico aynısı, açık görev çubuğu için (siyah kapsül)
# Kapsül, Mac'teki menü çubuğu simgesinin (SF Symbol "capsule.fill") karşılığı.
#   powershell -ExecutionPolicy Bypass -File scripts\make-icons.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assets = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\ISLe8.App\Assets'

function New-Png([System.Drawing.Bitmap]$bitmap) {
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    return ,$stream.ToArray()
}

# An .ico whose images are PNGs (Windows Vista and later read them).
function Write-Ico([string]$path, [object[]]$images) {
    $stream = [System.IO.File]::Create($path)
    $writer = New-Object System.IO.BinaryWriter $stream
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
    $offset = 6 + 16 * $images.Count
    foreach ($image in $images) {
        $size = $image.Size
        $writer.Write([byte]($(if ($size -ge 256) { 0 } else { $size })))
        $writer.Write([byte]($(if ($size -ge 256) { 0 } else { $size })))
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$image.Png.Length); $writer.Write([uint32]$offset)
        $offset += $image.Png.Length
    }
    foreach ($image in $images) { $writer.Write($image.Png) }
    $writer.Close()
}

function Resize([string]$source, [int]$size) {
    $original = [System.Drawing.Image]::FromFile($source)
    $bitmap = New-Object System.Drawing.Bitmap $size, $size
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.DrawImage($original, 0, 0, $size, $size)
    $graphics.Dispose(); $original.Dispose()
    $png = New-Png $bitmap
    $bitmap.Dispose()
    return @{ Size = $size; Png = $png }
}

function Capsule([int]$size, [System.Drawing.Color]$color) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $width = $size * 0.875; $height = $size * 0.44
    $x = ($size - $width) / 2; $y = ($size - $height) / 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($x, $y, $height, $height, 90, 180)
    $path.AddArc($x + $width - $height, $y, $height, $height, 270, 180)
    $path.CloseFigure()
    $brush = New-Object System.Drawing.SolidBrush $color
    $graphics.FillPath($brush, $path)
    $brush.Dispose(); $path.Dispose(); $graphics.Dispose()
    $png = New-Png $bitmap
    $bitmap.Dispose()
    return @{ Size = $size; Png = $png }
}

# Small sizes from the 88 px logo, large ones from the 300 px one, so each stays sharp.
$small = Join-Path $assets 'Square44x44Logo.scale-200.png'
$large = Join-Path $assets 'Square150x150Logo.scale-200.png'
Write-Ico (Join-Path $assets 'AppIcon.ico') @(
    (Resize $small 16), (Resize $small 20), (Resize $small 24), (Resize $small 32),
    (Resize $small 40), (Resize $small 48), (Resize $large 64), (Resize $large 256))

# The notification area uses 16 px at 100 % up to 32 px at 200 %.
$traySizes = 16, 20, 24, 32, 40, 48
Write-Ico (Join-Path $assets 'TrayOnDark.ico') @($traySizes | ForEach-Object { Capsule $_ ([System.Drawing.Color]::White) })
Write-Ico (Join-Path $assets 'TrayOnLight.ico') @($traySizes | ForEach-Object { Capsule $_ ([System.Drawing.Color]::Black) })

Get-ChildItem $assets -Filter *.ico | ForEach-Object { '{0}  {1:N0} bytes' -f $_.Name, $_.Length }
