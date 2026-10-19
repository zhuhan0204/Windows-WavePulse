param(
    [string]$LauncherSeed = '',
    [switch]$Offline
)

# Local preparation only: no git, gh, network publishing, tags or commits.
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$version = (Get-Content -LiteralPath (Join-Path $projectRoot 'VERSION') -Raw).Trim()
if ($version -ne '0.1') { throw '此准备脚本当前仅验证 v0.1；更新版本前请同步工程、原生资源、关于信息与文档。' }
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts'))
$sourceRoot = Join-Path $artifactRoot "github-source-v$version"
$releaseRoot = Join-Path $artifactRoot "releases\v$version"
$buildArchive = Join-Path $artifactRoot "public-build-v$version"
$artifactPrefix = $artifactRoot.TrimEnd('\') + '\'
foreach ($target in @($sourceRoot, $releaseRoot, $buildArchive)) {
    $absoluteTarget = [IO.Path]::GetFullPath($target)
    if (-not $absoluteTarget.StartsWith($artifactPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw '输出路径超出 artifacts。' }
    # Never overwrite a snapshot that may already have been inspected or committed.
    if (Test-Path -LiteralPath $absoluteTarget) { throw "输出已存在，未覆盖：$absoluteTarget。请先单独保留或移动旧快照。" }
}
if ([string]::IsNullOrWhiteSpace($LauncherSeed)) {
    foreach ($relativeSeed in @('release\WavePulse-无线设备电量助手-v0.1-双屏定位修正版\WavePulse 无线设备电量助手.exe',
        'release\WavePulse-无线设备电量助手-v0.1\WavePulse 无线设备电量助手.exe')) {
        $candidateSeed = Join-Path $projectRoot $relativeSeed
        if (Test-Path -LiteralPath $candidateSeed -PathType Leaf) { $LauncherSeed = $candidateSeed; break }
    }
}
if (-not $LauncherSeed -or -not (Test-Path -LiteralPath $LauncherSeed -PathType Leaf)) { throw '没有找到可信的现有原生启动器。' }
foreach ($imageName in @('bluetooth-devices.png','receiver-devices.png','settings.png','merged-floating.png')) {
    if (-not (Test-Path -LiteralPath (Join-Path $projectRoot "docs\images\$imageName"))) { throw "缺少文档图片：$imageName" }
}

$privateSource = Get-Content -LiteralPath (Join-Path $projectRoot 'src\EarbudSampleInference.cs') -Raw
$identityMatch = [regex]::Match($privateSource, 'private const string ConfirmedLeftRightDeviceId = "([^"]*)";')
if (-not $identityMatch.Success) { throw '个人耳机映射结构已变化，停止导出，需重新检查脱敏规则。' }
$privateIdentity = $identityMatch.Groups[1].Value
$privateAddress = $privateIdentity -replace '^address:', ''
$textEncoding = [Text.UTF8Encoding]::new($false)
New-Item -ItemType Directory -Path $sourceRoot,$releaseRoot -Force | Out-Null

$rootFiles = @('README.md','CHANGELOG.md','VERSION','.gitignore','app.manifest','build.ps1','build-net48.ps1','NuGet.local.Config')
$relativeFiles = [Collections.Generic.List[string]]::new()
foreach ($relative in $rootFiles) { $relativeFiles.Add($relative) }
foreach ($folder in @('src','native','docs','.github')) {
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $projectRoot $folder) -File -Recurse) {
        $relativeFiles.Add($file.FullName.Substring($projectRoot.Length + 1))
    }
}
foreach ($relative in @('assets\app-icon.ico','assets\app-icon.png','net48\BluetoothBatteryMonitor.Net48.csproj',
    'net48\App.config','net48\使用说明.md')) { $relativeFiles.Add($relative) }
foreach ($name in @('Set-NativeResources.ps1','Patch-NativePayloadHash.ps1','Test-MonitorPlacement.ps1',
    'Test-MonitorReconnect.ps1','MonitorReconnectTest.cs','Test-MonitorSelection.ps1',
    'Test-DisconnectedMonitor.ps1','Test-BackgroundStartup.ps1','Build-ScreenshotDemo.ps1','ScreenshotDemo.cs',
    'Prepare-PublicRelease.ps1','Test-PublicRelease.ps1')) {
    $relativeFiles.Add('tools\' + $name)
}

foreach ($relative in $relativeFiles) {
    $inputFile = Join-Path $projectRoot $relative
    if (-not (Test-Path -LiteralPath $inputFile -PathType Leaf)) { throw "公开源码缺少必需文件：$relative" }
    $outputFile = Join-Path $sourceRoot $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $outputFile) -Force | Out-Null
    Copy-Item -LiteralPath $inputFile -Destination $outputFile
    if ([IO.Path]::GetExtension($relative) -in @('.cs','.ps1','.md','.config','.csproj','.c','.manifest') -or $relative -eq 'README.md') {
        # Mechanical sanitization applies only to the generated public copy.
        $content = [IO.File]::ReadAllText($outputFile)
        if ($privateAddress) { $content = $content.Replace($privateAddress, '00:11:22:33:44:55') }
        if ($relative -eq 'src\EarbudSampleInference.cs') {
            $content = [regex]::Replace($content, 'private const string ConfirmedLeftRightDeviceId = "[^"]*";',
                'private const string ConfirmedLeftRightDeviceId = "";')
            $content = $content.Replace('// 该设备的用户已通过实际佩戴确认：样本 A 为左耳，样本 B 为右耳。',
                '// 公开版本不内置任何个人设备地址或专属左右耳映射。')
        }
        [IO.File]::WriteAllText($outputFile, $content, $textEncoding)
    }
}

# Fail closed if personal identity remains anywhere in exported text files.
foreach ($file in Get-ChildItem -LiteralPath $sourceRoot -File -Recurse) {
    if ($file.Extension -in @('.png','.ico')) { continue }
    $text = [IO.File]::ReadAllText($file.FullName)
    if ($privateAddress -and $text.IndexOf($privateAddress, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        throw "公开源码仍含个人设备地址：$($file.Name)"
    }
}

$dotnet = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { $dotnet = (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\dotnet-home'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.tools\nuget-packages'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$publicProject = Join-Path $sourceRoot 'net48\BluetoothBatteryMonitor.Net48.csproj'
$restoreArguments = @('restore', $publicProject, '--configfile', (Join-Path $projectRoot 'NuGet.local.Config'),
    '--ignore-failed-sources', '--nologo', '-p:NuGetAudit=false')
if ($Offline) { $restoreArguments += @('--source', (Join-Path $projectRoot '.tools\local-feed')) }
& $dotnet $restoreArguments
if ($LASTEXITCODE -ne 0) { throw '公开源码依赖恢复失败。' }
& $dotnet build $publicProject --configuration Release --no-restore --nologo
if ($LASTEXITCODE -ne 0) { throw '公开源码编译失败。' }

# Native source is unchanged: reuse only its trusted shell and replace payload,
# embedded SHA-256, manifest, icon and config with the public build.
$publicCore = Join-Path $sourceRoot 'bin\net48\Release\WavePulse 无线设备电量助手.Core.exe'
$publicExe = Join-Path $releaseRoot "WavePulse-v$version-win-x64.exe"
Copy-Item -LiteralPath ([IO.Path]::GetFullPath($LauncherSeed)) -Destination $publicExe
& (Join-Path $projectRoot 'tools\Patch-NativePayloadHash.ps1') -ExePath $publicExe -NewPayloadPath $publicCore
& (Join-Path $projectRoot 'tools\Set-NativeResources.ps1') -ExePath $publicExe `
    -IconPath (Join-Path $sourceRoot 'assets\app-icon.ico') -ManifestPath (Join-Path $sourceRoot 'native\RuntimeLauncher.manifest') `
    -PayloadPath $publicCore -PayloadConfigPath ($publicCore + '.config')

if ($privateAddress) {
    foreach ($binaryFile in @($publicCore, $publicExe)) {
        $bytes = [IO.File]::ReadAllBytes($binaryFile)
        foreach ($encoding in @([Text.Encoding]::UTF8, [Text.Encoding]::Unicode)) {
            if ($encoding.GetString($bytes).IndexOf($privateAddress, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                throw "公开二进制仍含个人设备地址：$binaryFile"
            }
        }
    }
}
$usage = Join-Path $releaseRoot '使用说明.md'
Copy-Item -LiteralPath (Join-Path $sourceRoot 'net48\使用说明.md') -Destination $usage
$zip = Join-Path $releaseRoot "WavePulse-v$version-win-x64.zip"
Compress-Archive -LiteralPath $publicExe,$usage -DestinationPath $zip -CompressionLevel Optimal
$hashLines = foreach ($file in @($publicExe,$zip)) {
    (Get-FileHash -Algorithm SHA256 -LiteralPath $file).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($file)
}
[IO.File]::WriteAllLines((Join-Path $releaseRoot 'SHA256SUMS.txt'), $hashLines, $textEncoding)
Copy-Item -LiteralPath (Join-Path $projectRoot "docs\releases\v$version.md") -Destination (Join-Path $releaseRoot 'RELEASE-NOTES.md')
# Keep even a manual folder upload clean: move generated bin/obj out of the
# public source tree after packaging, retaining them privately for diagnostics.
New-Item -ItemType Directory -Path $buildArchive -Force | Out-Null
foreach ($generatedDirectory in @((Join-Path $sourceRoot 'bin'), (Join-Path $sourceRoot 'net48\obj'))) {
    $resolvedGenerated = [IO.Path]::GetFullPath($generatedDirectory)
    if (-not $resolvedGenerated.StartsWith($sourceRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -or
        -not $buildArchive.StartsWith($artifactPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw '生成文件移动范围无效。' }
    if (Test-Path -LiteralPath $resolvedGenerated) { Move-Item -LiteralPath $resolvedGenerated -Destination $buildArchive }
}
Write-Output "公开源码（未上传）：$sourceRoot"
Write-Output "公开附件（未上传）：$releaseRoot"
Write-Output '未执行任何 Git / GitHub 写入；本地自用源码和配置保持不变。'
