param([string]$AssemblyPath = (Join-Path $PSScriptRoot '..\bin\net48\Release\WavePulse 无线设备电量助手.Core.exe'))
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$testContainer = Join-Path $projectRoot 'bin\monitor-reconnect-tests'
$testRoot = [IO.Path]::GetFullPath((Join-Path $testContainer ([Guid]::NewGuid().ToString('N'))))
$containerPrefix = [IO.Path]::GetFullPath($testContainer).TrimEnd('\') + '\'
if (-not $testRoot.StartsWith($containerPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid test root.' }
try {
    New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
    $testExe = Join-Path $testRoot 'MonitorReconnectTest.exe'
    $references = @('System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Xaml.dll', 'WPF\WindowsBase.dll',
        'WPF\PresentationCore.dll', 'WPF\PresentationFramework.dll') | ForEach-Object { '/r:' + (Join-Path $framework $_) }
    & (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 ("/out:$testExe") $references (Join-Path $PSScriptRoot 'MonitorReconnectTest.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test harness compilation failed.' }
    & $testExe ([IO.Path]::GetFullPath($AssemblyPath)) $testRoot
    if ($LASTEXITCODE -ne 0) { throw 'Monitor reconnect simulation failed.' }
}
finally {
    $resolvedCleanup = [IO.Path]::GetFullPath($testRoot)
    if (-not $resolvedCleanup.StartsWith($containerPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid cleanup target.' }
    if (Test-Path -LiteralPath $resolvedCleanup) { Remove-Item -LiteralPath $resolvedCleanup -Recurse -Force }
}
