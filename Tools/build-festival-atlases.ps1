# Normalize preserved generated sheets to three 512-square frames with matching
# foot/pole baselines. No animation pose is synthesized or painted by this step.
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$textureDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) 'WolfNET.Examples/wwwroot/textures/raceway'

foreach ($asset in @('flags', 'crowd')) {
    $source = [System.Drawing.Bitmap]::new((Join-Path $textureDirectory "festival-$asset-source.png"))
    $atlas = [System.Drawing.Bitmap]::new(1536, 512, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($atlas)
    try {
        if ($source.Width % 3 -ne 0) { throw 'Source must contain three equal horizontal frames.' }
        $frameWidth = [int]($source.Width / 3)
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $scale = 480.0 / $frameWidth
        $baselines = @()
        for ($frame = 0; $frame -lt 3; $frame++) {
            $baseline = -1
            # Ignore faint alpha specks beneath the prop when aligning feet.
            for ($y = $source.Height - 1; $y -ge 0 -and $baseline -lt 0; $y--) {
                for ($x = $frame * $frameWidth; $x -lt ($frame + 1) * $frameWidth; $x++) {
                    if ($source.GetPixel($x, $y).A -ge 32) { $baseline = $y; break }
                }
            }
            if ($baseline -lt 0) { throw "Empty $asset animation frame $frame" }
            $baselines += $baseline
            $destination = [System.Drawing.Rectangle]::new(
                $frame * 512 + 16, [int][Math]::Round(496 - $baseline * $scale),
                480, [int][Math]::Round($source.Height * $scale))
            $graphics.DrawImage($source, $destination,
                $frame * $frameWidth, 0,
                $frameWidth, $source.Height, [System.Drawing.GraphicsUnit]::Pixel)
        }
        $graphics.Dispose()
        $graphics = $null
        $atlas.Save((Join-Path $textureDirectory "festival-$asset-atlas.png"), [System.Drawing.Imaging.ImageFormat]::Png)
        [ordered]@{
            texture = "textures/raceway/festival-$asset-atlas.png"
            width = 1536; height = 512; baselineRow = 496
            frames = @(0, 1, 2 | ForEach-Object { [ordered]@{ x = $_ * 512; y = 0; width = 512; height = 512 } })
            source = "festival-$asset-source.png"; sourceFrameWidth = $frameWidth
            sourceBaselines = $baselines; scale = $scale
        } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $textureDirectory "festival-$asset-atlas.layout.json") -Encoding utf8
        Write-Output "Built festival-$asset-atlas.png: 1536x512, baselines $baselines -> 496"
    }
    finally {
        if ($graphics) { $graphics.Dispose() }
        $atlas.Dispose()
        $source.Dispose()
    }
}
