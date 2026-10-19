param(
    [string]$AssemblyPath = (Join-Path $PSScriptRoot '..\bin\net48\Release\WavePulse 无线设备电量助手.Core.exe'),
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\test-monitor-selection.png')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName WindowsBase

$assembly = [Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($AssemblyPath))
$optionType = $assembly.GetType('BluetoothBatteryMonitor.FloatingMonitorOption', $true)
$windowType = $assembly.GetType('BluetoothBatteryMonitor.MonitorSelectionWindow', $true)
$listType = [Collections.Generic.List``1].MakeGenericType($optionType)
$options = [Activator]::CreateInstance($listType)

function Add-MonitorOption {
    param(
        [string]$DeviceName,
        [string]$DisplayName,
        [int]$Width,
        [int]$Height,
        [int]$Left,
        [bool]$Primary,
        [string]$Position
    )
    $option = [Activator]::CreateInstance($optionType, $true)
    $option.DeviceName = $DeviceName
    $option.DisplayName = $DisplayName
    $option.ResolutionWidth = $Width
    $option.ResolutionHeight = $Height
    $option.WorkLeft = $Left
    $option.WorkTop = 0
    $option.WorkWidth = $Width
    $option.WorkHeight = $Height - 80
    $option.IsPrimary = $Primary
    $option.PositionDescription = $Position
    $options.Add($option)
}

Add-MonitorOption -DeviceName '\\.\DISPLAY1' -DisplayName '主屏幕' -Width 3840 -Height 2160 -Left 0 -Primary $true -Position 'Windows 主屏'
Add-MonitorOption -DeviceName '\\.\DISPLAY20' -DisplayName '显示器 1' -Width 1920 -Height 1080 -Left 3840 -Primary $false -Position '位于主屏右侧'

$flags = [Reflection.BindingFlags]::Instance -bor [Reflection.BindingFlags]::Public -bor [Reflection.BindingFlags]::NonPublic
$constructor = $windowType.GetConstructors($flags) | Where-Object { $_.GetParameters().Count -eq 3 } | Select-Object -First 1
if ($null -eq $constructor) { throw '没有找到屏幕选择窗口构造函数。' }
$window = $constructor.Invoke([object[]]@($null, $options, 'Logi M750'))

try {
    $window.Show()
    $window.Dispatcher.Invoke([Action]{}, [Windows.Threading.DispatcherPriority]::ApplicationIdle)
    $window.UpdateLayout()

    $scale = [Windows.Media.VisualTreeHelper]::GetDpi($window)
    $pixelWidth = [Math]::Max(1, [int][Math]::Ceiling($window.ActualWidth * $scale.DpiScaleX))
    $pixelHeight = [Math]::Max(1, [int][Math]::Ceiling($window.ActualHeight * $scale.DpiScaleY))
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new(
        $pixelWidth, $pixelHeight, $scale.PixelsPerInchX, $scale.PixelsPerInchY,
        [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($window)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
    $stream = [IO.File]::Open($resolvedOutput, [IO.FileMode]::Create, [IO.FileAccess]::Write)
    try { $encoder.Save($stream) } finally { $stream.Dispose() }

    [PSCustomObject]@{
        MonitorSelectionRendered = $true
        SyntheticMonitorCount = $options.Count
        Width = [int]$window.ActualWidth
        Height = [int]$window.ActualHeight
        Output = $resolvedOutput
    } | Format-List
}
finally {
    $window.Close()
}
