param(
    [string]$LauncherPath = (Join-Path $PSScriptRoot '..\release\WavePulse-无线设备电量助手-v0.1-双屏定位修正版\WavePulse 无线设备电量助手.exe')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$testContainer = [IO.Path]::GetFullPath((Join-Path $projectRoot 'bin\background-startup-tests'))
$testRoot = [IO.Path]::GetFullPath((Join-Path $testContainer ([Guid]::NewGuid().ToString('N'))))
$containerPrefix = $testContainer.TrimEnd('\') + '\'
if (-not $testRoot.StartsWith($containerPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "拒绝使用测试目录以外的路径：$testRoot"
}

$launcher = $null
$core = $null
try {
    New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
    $testLauncher = Join-Path $testRoot 'WavePulse 无线设备电量助手.exe'
    Copy-Item -LiteralPath ([IO.Path]::GetFullPath($LauncherPath)) -Destination $testLauncher

    $launcher = Start-Process -FilePath $testLauncher -ArgumentList '--background' -WindowStyle Hidden -PassThru
    if (-not $launcher.WaitForExit(10000)) {
        throw '后台启动时原生启动器没有正常完成交接。'
    }

    $runtimeCore = [IO.Path]::GetFullPath((Join-Path $testRoot '.wavepulse-runtime\Runtime\v0.1\WavePulse 无线设备电量助手.exe'))
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    while ([DateTime]::UtcNow -lt $deadline -and $null -eq $core) {
        $core = Get-Process -ErrorAction SilentlyContinue | Where-Object {
            try { return [string]::Equals($_.Path, $runtimeCore, [StringComparison]::OrdinalIgnoreCase) } catch { return $false }
        } | Select-Object -First 1
        if ($null -eq $core) { Start-Sleep -Milliseconds 250 }
    }
    if ($null -eq $core) { throw '没有找到后台运行的 WavePulse 核心进程。' }

    Start-Sleep -Seconds 3
    $condition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $core.Id)),
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, 'WavePulse 无线设备电量助手')))
    $visibleMain = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        $condition)
    if ($null -ne $visibleMain) { throw '后台启动后主应用窗口仍然可见。' }

    $logPath = Join-Path $testRoot 'data\diagnostic.log'
    $refreshLogged = (Test-Path -LiteralPath $logPath) -and
        ((Get-Content -LiteralPath $logPath -Raw).Contains('开机自启：立即刷新一次电量，不等待设备的定时刷新周期。'))
    if (-not $refreshLogged) { throw '诊断日志没有记录开机自启的独立即时刷新。' }

    [PSCustomObject]@{
        BackgroundCoreRunning = -not $core.HasExited
        MainApplicationWindowHidden = $true
        NativeArgumentForwarded = $true
        StartupImmediateRefreshLogged = $refreshLogged
        ProcessId = $core.Id
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
