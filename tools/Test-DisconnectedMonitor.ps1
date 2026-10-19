param(
    [string]$LauncherPath = (Join-Path $PSScriptRoot '..\release\WavePulse-无线设备电量助手-v0.1-双屏定位修正版\WavePulse 无线设备电量助手.exe')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$testContainer = [IO.Path]::GetFullPath((Join-Path $projectRoot 'bin\disconnected-monitor-tests'))
$testRoot = [IO.Path]::GetFullPath((Join-Path $testContainer ([Guid]::NewGuid().ToString('N'))))
$containerPrefix = $testContainer.TrimEnd('\') + '\'
if (-not $testRoot.StartsWith($containerPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "拒绝使用测试目录以外的路径：$testRoot"
}

$launcher = $null
$core = $null
try {
    New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
    $sourceLauncher = [IO.Path]::GetFullPath($LauncherPath)
    $sourceRoot = Split-Path -Parent $sourceLauncher
    $testLauncher = Join-Path $testRoot 'WavePulse 无线设备电量助手.exe'
    Copy-Item -LiteralPath $sourceLauncher -Destination $testLauncher
    Copy-Item -LiteralPath (Join-Path $sourceRoot 'data') -Destination $testRoot -Recurse

    $settingsPath = Join-Path $testRoot 'data\settings.json'
    $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
    $settings.MergeFloatingWindows = $false
    foreach ($preference in $settings.Devices) { $preference.IsFloating = $false }
    $target = $settings.Devices | Select-Object -First 1
    if ($null -eq $target) { throw '测试设置中没有设备。' }
    $target.IsFloating = $true
    $target.KeepFloatingWhenDisconnected = $true
    $target.MonitorDeviceName = '\\.\DISPLAY999'
    $target.MonitorWorkWidth = 1920
    $target.MonitorWorkHeight = 1040
    $target.MonitorCenterXRatio = 0.5
    $target.MonitorCenterYRatio = 0.5
    $settings | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $settingsPath -Encoding UTF8

    $settingsBytes = [IO.File]::ReadAllBytes($settingsPath)
    $integrityKey = [byte[]](
        0x42,0x6C,0x75,0x65,0x50,0x75,0x6C,0x73,
        0x65,0x2D,0x79,0x75,0x62,0x61,0x6E,0x2D,
        0x32,0x30,0x32,0x36,0x2D,0x49,0x6E,0x74,
        0x65,0x67,0x72,0x69,0x74,0x79,0x21,0x5A)
    $hmac = [Security.Cryptography.HMACSHA256]::new($integrityKey)
    try {
        $signature = -join ($hmac.ComputeHash($settingsBytes) | ForEach-Object { $_.ToString('X2') })
        [IO.File]::WriteAllText((Join-Path $testRoot 'data\settings.sig'), $signature, [Text.UTF8Encoding]::new($false))
    }
    finally { $hmac.Dispose() }

    $launcher = Start-Process -FilePath $testLauncher -ArgumentList '--background' -WindowStyle Hidden -PassThru
    if (-not $launcher.WaitForExit(10000)) { throw '原生启动器没有正常完成交接。' }
    $runtimeCore = [IO.Path]::GetFullPath((Join-Path $testRoot '.wavepulse-runtime\Runtime\v0.1\WavePulse 无线设备电量助手.exe'))
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    while ([DateTime]::UtcNow -lt $deadline -and $null -eq $core) {
        $core = Get-Process -ErrorAction SilentlyContinue | Where-Object {
            try { return [string]::Equals($_.Path, $runtimeCore, [StringComparison]::OrdinalIgnoreCase) } catch { return $false }
        } | Select-Object -First 1
        if ($null -eq $core) { Start-Sleep -Milliseconds 250 }
    }
    if ($null -eq $core) { throw '没有找到后台运行的 WavePulse 核心进程。' }

    Start-Sleep -Seconds 4
    $windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
        [System.Windows.Automation.TreeScope]::Children,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
            $core.Id)))
    $floatingWindows = @($windows | Where-Object {
        $_.Current.Name -eq '' -and
        $_.Current.BoundingRectangle.Width -ge 150 -and
        $_.Current.BoundingRectangle.Width -lt 1000 -and
        $_.Current.BoundingRectangle.Height -ge 50 -and
        $_.Current.BoundingRectangle.Height -lt 1000
    })
    if ($floatingWindows.Count -ne 0) {
        throw '目标副屏不存在时仍然显示了浮窗。'
    }

    [PSCustomObject]@{
        CoreRunning = -not $core.HasExited
        MissingMonitor = '\\.\DISPLAY999'
        VisibleFloatingWindowCount = $floatingWindows.Count
        HiddenUntilMonitorReconnects = $true
    } | Format-List
}
finally {
    if ($null -ne $core -and -not $core.HasExited) { Stop-Process -Id $core.Id -Force }
    if ($null -ne $launcher -and -not $launcher.HasExited) { Stop-Process -Id $launcher.Id -Force }
    Start-Sleep -Milliseconds 300
    if (Test-Path -LiteralPath $testRoot) {
        $resolvedCleanup = [IO.Path]::GetFullPath($testRoot)
        if (-not $resolvedCleanup.StartsWith($containerPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "拒绝清理测试目录以外的路径：$resolvedCleanup"
        }
        Remove-Item -LiteralPath $resolvedCleanup -Recurse -Force
    }
}
