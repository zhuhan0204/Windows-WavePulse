param([string]$AssemblyPath = (Join-Path $PSScriptRoot '..\bin\net48\Release\WavePulse 无线设备电量助手.Core.exe'))
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = Join-Path $projectRoot 'bin\docs-preview'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$references = @('System.dll', 'System.Core.dll', 'System.Xaml.dll', 'WPF\WindowsBase.dll',
    'WPF\PresentationCore.dll', 'WPF\PresentationFramework.dll') | ForEach-Object { '/r:' + (Join-Path $framework $_) }
& (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 ("/out:" + (Join-Path $output 'ScreenshotDemo.exe')) $references (Join-Path $PSScriptRoot 'ScreenshotDemo.cs')
if ($LASTEXITCODE -ne 0) { throw '截图示例工具编译失败。' }
[IO.File]::WriteAllText((Join-Path $output 'demo-assembly.txt'), [IO.Path]::GetFullPath($AssemblyPath), [Text.UTF8Encoding]::new($false))
Write-Output (Join-Path $output 'ScreenshotDemo.exe')
