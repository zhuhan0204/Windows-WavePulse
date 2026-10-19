param(
    [string]$AssemblyPath = '.\bin\net48\Release\WavePulse 无线设备电量助手.Core.exe'
)

$ErrorActionPreference = 'Stop'
$resolvedAssembly = [IO.Path]::GetFullPath((Join-Path (Get-Location) $AssemblyPath))
if (-not (Test-Path -LiteralPath $resolvedAssembly)) {
    throw "未找到待测试程序：$resolvedAssembly"
}

$assembly = [Reflection.Assembly]::LoadFrom($resolvedAssembly)
$placementType = $assembly.GetType('BluetoothBatteryMonitor.FloatingWindowPlacement', $true)
$method = $placementType.GetMethod(
    'CalculateTargetBounds',
    [Reflection.BindingFlags]'Static,NonPublic')
if ($null -eq $method) {
    throw '未找到浮窗定位计算方法。'
}

function Assert-InWorkArea {
    param(
        [Drawing.Rectangle]$WorkArea,
        [int]$WindowWidth,
        [int]$WindowHeight,
        [double]$CenterXRatio,
        [double]$CenterYRatio,
        [string]$Scenario
    )

    $arguments = @($WorkArea, $WindowWidth, $WindowHeight, $CenterXRatio, $CenterYRatio)
    $result = [Drawing.Rectangle]$method.Invoke($null, $arguments)
    if ($result.Left -lt $WorkArea.Left -or
        $result.Top -lt $WorkArea.Top -or
        $result.Right -gt $WorkArea.Right -or
        $result.Bottom -gt $WorkArea.Bottom) {
        throw "$Scenario 越界：WorkArea=$WorkArea Result=$result"
    }
    [PSCustomObject]@{
        Scenario = $Scenario
        WorkArea = $WorkArea.ToString()
        Result = $result.ToString()
    }
}

$results = @(
    Assert-InWorkArea -WorkArea ([Drawing.Rectangle]::FromLTRB(-1920, 0, 0, 1040)) `
        -WindowWidth 392 -WindowHeight 132 -CenterXRatio 0.82 -CenterYRatio 0.20 `
        -Scenario '1080p 副屏位于主屏左侧（负坐标）'
    Assert-InWorkArea -WorkArea ([Drawing.Rectangle]::FromLTRB(1920, 0, 5760, 2120)) `
        -WindowWidth 588 -WindowHeight 198 -CenterXRatio 0.25 -CenterYRatio 0.85 `
        -Scenario '4K 屏位于主屏右侧（不同 DPI 尺寸）'
    Assert-InWorkArea -WorkArea ([Drawing.Rectangle]::FromLTRB(0, -2160, 3840, 0)) `
        -WindowWidth 588 -WindowHeight 198 -CenterXRatio 1 -CenterYRatio 0 `
        -Scenario '4K 屏位于主屏上方（边缘吸附）'
)

$results | Format-Table -AutoSize
Write-Host '浮窗多屏定位计算测试通过。'
