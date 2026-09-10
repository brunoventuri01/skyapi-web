param([string]$Out)
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

# Símbolo Skynova (Assets/skynova.svg). Os dois triângulos mais escuros são clareados
# para continuarem visíveis sobre o azulejo azul-marinho do ícone.
$shapes = @(
  @{ c = '#5D86DE'; d = 'M37.89,30.53l8.74-15.94,11.01,20.07h-17.31c-2.12,0-3.46-2.27-2.44-4.13Z' },
  @{ c = '#4E86E0'; d = 'M68.65,14.58 L57.64,34.65 L46.64,14.58 Z' },
  @{ c = '#2E9AE8'; d = 'M57.57,34.65l11.01-20.07,8.74,15.94c1.02,1.86-.33,4.13-2.44,4.13h-17.31Z' },
  @{ c = '#6E8FE0'; d = 'M23.42,14.58l-11.01,20.07L3.67,18.71c-1.02-1.86.33-4.13,2.44-4.13h17.31Z' },
  @{ c = '#00BAEB'; d = 'M66.14,58.85l-8.74,15.94-11.01-20.07h17.31c2.12,0,3.46,2.27,2.44,4.13Z' },
  @{ c = '#009DDB'; d = 'M35.38,74.8 L46.39,54.73 L57.39,74.8 Z' },
  @{ c = '#0074BC'; d = 'M46.46,54.73l-11.01,20.07-8.74-15.94c-1.02-1.86.33-4.13,2.44-4.13h17.31Z' },
  @{ c = '#00BAEB'; d = 'M79.98,74.8l11.01-20.07,8.74,15.94c1.02,1.86-.33,4.13-2.44,4.13h-17.31Z' }
)

$group = New-Object System.Windows.Media.DrawingGroup
foreach ($s in $shapes) {
  $geo = [System.Windows.Media.Geometry]::Parse($s.d)
  $brush = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.ColorConverter]::ConvertFromString($s.c))
  $group.Children.Add((New-Object System.Windows.Media.GeometryDrawing $brush, $null, $geo))
}
$b = $group.Bounds

function Render-Bitmap([int]$size) {
  $visual = New-Object System.Windows.Media.DrawingVisual
  $dc = $visual.RenderOpen()

  $lg = New-Object System.Windows.Media.LinearGradientBrush
  $lg.StartPoint = New-Object System.Windows.Point 0, 0
  $lg.EndPoint = New-Object System.Windows.Point 1, 1
  $lg.GradientStops.Add((New-Object System.Windows.Media.GradientStop ([System.Windows.Media.ColorConverter]::ConvertFromString('#222F63')), 0))
  $lg.GradientStops.Add((New-Object System.Windows.Media.GradientStop ([System.Windows.Media.ColorConverter]::ConvertFromString('#0B5FA5')), 1))
  $r = New-Object System.Windows.Rect 0, 0, $size, $size
  $dc.DrawRoundedRectangle($lg, $null, $r, $size * 0.22, $size * 0.22)

  $pad = [double]$size * 0.14
  $inner = [double]$size - 2 * $pad
  $k = [Math]::Min($inner / $b.Width, $inner / $b.Height)
  $tx = ($size - $b.Width * $k) / 2 - $b.X * $k
  $ty = ($size - $b.Height * $k) / 2 - $b.Y * $k
  $tg = New-Object System.Windows.Media.TransformGroup
  $tg.Children.Add((New-Object System.Windows.Media.ScaleTransform $k, $k))
  $tg.Children.Add((New-Object System.Windows.Media.TranslateTransform $tx, $ty))
  $dc.PushTransform($tg); $dc.DrawDrawing($group); $dc.Pop(); $dc.Close()

  $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
  $rtb.Render($visual)
  $rtb
}

function Render-Png([int]$size) {
  $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
  $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create((Render-Bitmap $size)))
  $ms = New-Object System.IO.MemoryStream
  $enc.Save($ms)
  , $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = @{}
foreach ($s in $sizes) { $pngs[$s] = Render-Png $s }

$fs = [System.IO.File]::Create($Out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
foreach ($s in $sizes) {
  $dim = [byte]$(if ($s -ge 256) { 0 } else { $s })
  $bw.Write($dim); $bw.Write($dim); $bw.Write([byte]0); $bw.Write([byte]0)
  $bw.Write([uint16]1); $bw.Write([uint16]32)
  $bw.Write([uint32]$pngs[$s].Length); $bw.Write([uint32]$offset)
  $offset += $pngs[$s].Length
}
foreach ($s in $sizes) { $bw.Write($pngs[$s]) }
$bw.Flush(); $fs.Close()
Write-Host ("wrote {0} ({1} bytes)" -f $Out, (Get-Item $Out).Length)
