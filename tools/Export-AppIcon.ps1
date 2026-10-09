<#
.SYNOPSIS
    Renders the WinGnome logo (src/WinGnome/Theme/Logo.xaml) to the app icon and a 256 px PNG.

.DESCRIPTION
    Uses WPF from Windows PowerShell 5.1 (no extra tools or packages): parses Logo.xaml, renders the
    WinGnomeLogo DrawingImage with RenderTargetBitmap at each icon size, and packs the PNGs into a
    PNG-compressed .ico. Run it after changing the logo, and commit both outputs:
      src/WinGnome/Assets/wingnome.ico   (16, 20, 24, 32, 40, 48, 64 and 256 px)
      assets/logo/wingnome-256.png

.EXAMPLE
    powershell -NoProfile -File tools\Export-AppIcon.ps1
#>
[CmdletBinding()]
param(
    [string]$RepoRoot
)

$ErrorActionPreference = 'Stop'

# $PSScriptRoot is empty in param() defaults under Windows PowerShell 5.1, so resolve the default here.
if (-not $RepoRoot) {
    $RepoRoot = Split-Path -Parent $PSScriptRoot
}

if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
    throw 'WPF rendering needs an STA thread. Run this script with Windows PowerShell (powershell.exe), not with -MTA.'
}

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$logoPath = Join-Path $RepoRoot 'src\WinGnome\Theme\Logo.xaml'
$icoPath = Join-Path $RepoRoot 'src\WinGnome\Assets\wingnome.ico'
$pngPath = Join-Path $RepoRoot 'assets\logo\wingnome-256.png'

$resources = [Windows.Markup.XamlReader]::Parse([IO.File]::ReadAllText($logoPath))
$logo = $resources['WinGnomeLogo']
if ($null -eq $logo) {
    throw "No WinGnomeLogo resource in $logoPath"
}

function ConvertTo-Png([int]$size) {
    $visual = New-Object Windows.Media.DrawingVisual
    $context = $visual.RenderOpen()
    $context.DrawImage($logo, (New-Object Windows.Rect 0, 0, $size, $size))
    $context.Close()

    $bitmap = New-Object Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)

    $encoder = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = New-Object IO.MemoryStream
    try {
        $encoder.Save($stream)
        return , $stream.ToArray()
    }
    finally {
        $stream.Dispose()
    }
}

$images = @{}
foreach ($size in $sizes) {
    $images[$size] = ConvertTo-Png $size
}

# ICO layout: a 6-byte ICONDIR, one 16-byte ICONDIRENTRY per image, then the PNG data. A width or height
# byte of 0 means 256.
$stream = New-Object IO.MemoryStream
$writer = New-Object IO.BinaryWriter $stream
try {
    $writer.Write([UInt16]0)              # reserved
    $writer.Write([UInt16]1)              # type: icon
    $writer.Write([UInt16]$sizes.Count)

    $offset = 6 + (16 * $sizes.Count)
    foreach ($size in $sizes) {
        $dimension = if ($size -ge 256) { 0 } else { $size }
        $writer.Write([byte]$dimension)   # width
        $writer.Write([byte]$dimension)   # height
        $writer.Write([byte]0)            # palette colours
        $writer.Write([byte]0)            # reserved
        $writer.Write([UInt16]1)          # colour planes
        $writer.Write([UInt16]32)         # bits per pixel
        $writer.Write([UInt32]$images[$size].Length)
        $writer.Write([UInt32]$offset)
        $offset += $images[$size].Length
    }

    foreach ($size in $sizes) {
        $writer.Write($images[$size])
    }

    $writer.Flush()
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $icoPath) | Out-Null
    [IO.File]::WriteAllBytes($icoPath, $stream.ToArray())
}
finally {
    $writer.Dispose()
}

[IO.File]::WriteAllBytes($pngPath, $images[256])
Write-Host "Wrote $icoPath ($($sizes -join ', ') px) and $pngPath"
