<#
.SYNOPSIS
  Generates assets\DependenSee.ico from assets\DependenSee.png.
  Produces a 256x256 PNG-compressed .ico (valid on Windows Vista+).
#>
param(
    [string]$Png = "$PSScriptRoot\..\assets\DependenSee.png",
    [string]$Ico = "$PSScriptRoot\..\assets\DependenSee.ico"
)
$ErrorActionPreference = "Stop"
if (-not (Test-Path $Png)) { Write-Host "No PNG at $Png - skipping icon generation."; return }

Add-Type -AssemblyName System.Drawing
$src = [System.Drawing.Image]::FromFile((Resolve-Path $Png))
try {
    $bmp = New-Object System.Drawing.Bitmap 256, 256
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($src, 0, 0, 256, 256)
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngBytes = $ms.ToArray()
    $ms.Dispose(); $bmp.Dispose()

    $out = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($out)
    # ICONDIR
    $bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]1)
    # ICONDIRENTRY (256x256 encoded as 0)
    $bw.Write([Byte]0); $bw.Write([Byte]0); $bw.Write([Byte]0); $bw.Write([Byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$pngBytes.Length); $bw.Write([UInt32]22)
    $bw.Write($pngBytes)
    $bw.Flush()
    [System.IO.File]::WriteAllBytes((Join-Path (Split-Path $Ico -Parent) (Split-Path $Ico -Leaf)), $out.ToArray())
    $out.Dispose()
    Write-Host "Wrote $Ico"
}
finally { $src.Dispose() }
