# Rebuild the two-artwork directional buffer atlas from its preserved generated sources.
# Run from any directory: pwsh -File Tools/build-buffer-atlas.ps1
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if (-not ('BufferAtlasAlpha' -as [type])) {
    $drawingReferences = @(
        [System.Drawing.Bitmap].Assembly.Location,
        [System.Drawing.Rectangle].Assembly.Location
    )
    Add-Type -ReferencedAssemblies $drawingReferences -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class BufferAtlasAlpha
{
    public static Rectangle Bounds(Bitmap image, byte threshold)
    {
        var area = new Rectangle(0, 0, image.Width, image.Height);
        var pixels = image.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var stride = Math.Abs(pixels.Stride);
        var bytes = new byte[stride * image.Height];
        Marshal.Copy(pixels.Scan0, bytes, 0, bytes.Length);
        image.UnlockBits(pixels);
        int left = image.Width, top = image.Height, right = -1, bottom = -1;
        for (int y = 0; y < image.Height; y++)
        for (int x = 0; x < image.Width; x++)
        {
            if (bytes[y * stride + x * 4 + 3] < threshold) continue;
            left = Math.Min(left, x);
            top = Math.Min(top, y);
            right = Math.Max(right, x);
            bottom = Math.Max(bottom, y);
        }
        return right < 0 ? Rectangle.Empty : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
    }
}
'@
}

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$textureDirectory = Join-Path $repositoryRoot 'WolfNET.Game/wwwroot/textures/raceway'
$frontSource = [System.Drawing.Bitmap]::new((Join-Path $textureDirectory 'buffer.png'))
$sideSource = [System.Drawing.Bitmap]::new((Join-Path $textureDirectory 'buffer-side.png'))
$front = $null
$atlas = $null
$graphics = $null

try {
    # Alpha below eight includes invisible generation specks far from the real silhouette.
    $frontBounds = [BufferAtlasAlpha]::Bounds($frontSource, 8)
    $sideBounds = [BufferAtlasAlpha]::Bounds($sideSource, 8)
    if ($frontBounds.IsEmpty -or $sideBounds.IsEmpty) {
        throw 'Both source images must contain visible artwork.'
    }
    $frameHeight = 141
    $frontWidth = 512
    $sideWidth = 96
    if ($frontBounds.Width -ne $frontWidth -or $frontBounds.Height -ne $frameHeight) {
        throw "Expected the preserved front artwork to be 512 by 141; got $frontBounds."
    }

    $atlas = [System.Drawing.Bitmap]::new(
        $frontWidth + $sideWidth, $frameHeight,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb
    )
    $graphics = [System.Drawing.Graphics]::FromImage($atlas)
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

    # Copy the long face without resampling. Resize the cropped narrow end to the
    # matching frame height; its authored width represents a 0.28125-unit end view.
    $front = $frontSource.Clone($frontBounds, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics.DrawImageUnscaled($front, 0, 0)
    $sideDestination = [System.Drawing.Rectangle]::new($frontWidth, 0, $sideWidth, $frameHeight)
    $graphics.DrawImage(
        $sideSource, $sideDestination,
        $sideBounds.X, $sideBounds.Y, $sideBounds.Width, $sideBounds.Height,
        [System.Drawing.GraphicsUnit]::Pixel
    )
    $graphics.Dispose()
    $graphics = $null

    $atlasPath = Join-Path $textureDirectory 'buffer-directional-atlas-v1.png'
    $atlas.Save($atlasPath, [System.Drawing.Imaging.ImageFormat]::Png)

    $frontFrame = [ordered]@{ x = 0; y = 0; width = $frontWidth; height = $frameHeight }
    $sideFrame = [ordered]@{ x = $frontWidth; y = 0; width = $sideWidth; height = $frameHeight }
    $physicalHeight = 1.5 * $frameHeight / $frontWidth
    $layout = [ordered]@{
        texture = 'textures/raceway/buffer-directional-atlas-v1.png'
        width = $atlas.Width
        height = $atlas.Height
        directions = 4
        frames = @($frontFrame, $sideFrame, $frontFrame, $sideFrame)
        sources = [ordered]@{
            front = [ordered]@{
                file = 'buffer.png'
                crop = @($frontBounds.X, $frontBounds.Y, $frontBounds.Width, $frontBounds.Height)
            }
            side = [ordered]@{
                file = 'buffer-side.png'
                crop = @($sideBounds.X, $sideBounds.Y, $sideBounds.Width, $sideBounds.Height)
            }
        }
        textureLayers = @(110, 111, 112, 113)
        facingCamera = $false
        rotations = @(0, 90)
        groupScale = $physicalHeight
        groupElevation = $physicalHeight / 2 - 0.5
        physicalFrontWidth = 1.5
        physicalSideWidth = $physicalHeight * $sideWidth / $frameHeight
        physicalHeight = $physicalHeight
        baselineRow = $frameHeight - 1
    }
    $layoutPath = Join-Path $textureDirectory 'buffer-directional-atlas-v1.layout.json'
    $layout | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $layoutPath -Encoding utf8
    $layout | ConvertTo-Json -Depth 8
}
finally {
    if ($graphics) { $graphics.Dispose() }
    if ($front) { $front.Dispose() }
    if ($atlas) { $atlas.Dispose() }
    $frontSource.Dispose()
    $sideSource.Dispose()
}

