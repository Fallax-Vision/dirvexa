param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$assetsDirectory = [System.IO.Path]::GetFullPath((Join-Path $projectRoot 'src\Dirvexa.App\Assets'))
if (-not $assetsDirectory.StartsWith($projectRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
  throw "Unsafe icon output directory: $assetsDirectory"
}

New-Item -ItemType Directory -Force -Path $assetsDirectory | Out-Null
$previewPath = Join-Path $assetsDirectory 'DirvexaIcon.png'
$iconPath = Join-Path $assetsDirectory 'Dirvexa.ico'

function New-RoundedRectanglePath {
  param(
    [float]$X,
    [float]$Y,
    [float]$Width,
    [float]$Height,
    [float]$Radius
  )

  $diameter = $Radius * 2
  $path = New-Object System.Drawing.Drawing2D.GraphicsPath
  $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
  $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
  $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
  $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
  $path.CloseFigure()
  return $path
}

function New-DirvexaIconPng {
  param([ValidateRange(16, 512)][int]$Size)

  $supersampling = if ($Size -le 64) { 8 } else { 4 }
  $renderSize = $Size * $supersampling
  $scale = $renderSize / 512.0
  $bitmap = New-Object System.Drawing.Bitmap(
    $renderSize,
    $renderSize,
    [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $graphics = [System.Drawing.Graphics]::FromImage($bitmap)

  try {
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

    $green = [System.Drawing.ColorTranslator]::FromHtml('#12e442')
    $ink = [System.Drawing.ColorTranslator]::FromHtml('#07110a')
    $backgroundBrush = New-Object System.Drawing.SolidBrush($green)
    $inkBrush = New-Object System.Drawing.SolidBrush($ink)
    $backgroundPath = New-RoundedRectanglePath `
      -X (16 * $scale) -Y (16 * $scale) `
      -Width (480 * $scale) -Height (480 * $scale) -Radius (108 * $scale)

    try {
      $graphics.FillPath($backgroundBrush, $backgroundPath)
      $pen = New-Object System.Drawing.Pen($ink, (36 * $scale))
      $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
      $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
      $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

      try {
        $brackets = @(
          @(@(208, 142), @(142, 142), @(142, 208)),
          @(@(304, 142), @(370, 142), @(370, 208)),
          @(@(142, 304), @(142, 370), @(208, 370)),
          @(@(370, 304), @(370, 370), @(304, 370))
        )
        foreach ($bracket in $brackets) {
          $points = [System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new($bracket[0][0] * $scale, $bracket[0][1] * $scale),
            [System.Drawing.PointF]::new($bracket[1][0] * $scale, $bracket[1][1] * $scale),
            [System.Drawing.PointF]::new($bracket[2][0] * $scale, $bracket[2][1] * $scale)
          )
          $graphics.DrawLines($pen, $points)
        }

        foreach ($x in @(214, 266)) {
          foreach ($y in @(214, 266)) {
            $tile = New-RoundedRectanglePath `
              -X ($x * $scale) -Y ($y * $scale) `
              -Width (32 * $scale) -Height (32 * $scale) -Radius (8 * $scale)
            try { $graphics.FillPath($inkBrush, $tile) } finally { $tile.Dispose() }
          }
        }
      }
      finally {
        $pen.Dispose()
      }
    }
    finally {
      $backgroundPath.Dispose()
      $backgroundBrush.Dispose()
      $inkBrush.Dispose()
    }

    $result = New-Object System.Drawing.Bitmap(
      $Size,
      $Size,
      [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $resultGraphics = [System.Drawing.Graphics]::FromImage($result)
    try {
      $resultGraphics.Clear([System.Drawing.Color]::Transparent)
      $resultGraphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
      $resultGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
      $resultGraphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
      $resultGraphics.DrawImage($bitmap, 0, 0, $Size, $Size)
    }
    finally {
      $resultGraphics.Dispose()
    }

    $stream = New-Object System.IO.MemoryStream
    try {
      $result.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
      return $stream.ToArray()
    }
    finally {
      $stream.Dispose()
      $result.Dispose()
    }
  }
  finally {
    $graphics.Dispose()
    $bitmap.Dispose()
  }
}

$previewBytes = New-DirvexaIconPng -Size 512
[System.IO.File]::WriteAllBytes($previewPath, $previewBytes)

$iconSizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$images = foreach ($size in $iconSizes) {
  [pscustomobject]@{ Size = $size; Bytes = New-DirvexaIconPng -Size $size }
}

$iconStream = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($iconStream)
try {
  $writer.Write([uint16]0)
  $writer.Write([uint16]1)
  $writer.Write([uint16]$images.Count)
  $offset = 6 + (16 * $images.Count)

  foreach ($image in $images) {
    $dimension = if ($image.Size -eq 256) { [byte]0 } else { [byte]$image.Size }
    $writer.Write($dimension)
    $writer.Write($dimension)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]$image.Bytes.Length)
    $writer.Write([uint32]$offset)
    $offset += $image.Bytes.Length
  }

  foreach ($image in $images) {
    $writer.Write([byte[]]$image.Bytes)
  }
  $writer.Flush()
  [System.IO.File]::WriteAllBytes($iconPath, $iconStream.ToArray())
}
finally {
  $writer.Dispose()
  $iconStream.Dispose()
}

Write-Output "Generated $previewPath"
Write-Output "Generated $iconPath with $($iconSizes.Count) resolutions"
