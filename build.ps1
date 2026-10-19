param(
    [switch]$NoZip,
    [string]$ReleaseSuffix = '',
    [switch]$ReuseExistingLauncher
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $MyInvocation.MyCommand.Path))
$localDotnet = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$projectFile = Join-Path $projectRoot 'net48\BluetoothBatteryMonitor.Net48.csproj'
$buildRoot = Join-Path $projectRoot 'bin\net48\Release'
$nativeBuildRoot = Join-Path $projectRoot 'bin\native-launcher'
$releaseRoot = Join-Path $projectRoot 'release'
$releaseName = 'WavePulse-无线设备电量助手-v0.1' + $ReleaseSuffix
$portableRoot = Join-Path $releaseRoot $releaseName
$currentPortableRoot = Join-Path $releaseRoot 'WavePulse-无线设备电量助手-v0.1'
$legacyPortableRoot = Join-Path $releaseRoot 'BluePulse-蓝牙电量助手-v0.1'
$zipPath = Join-Path $releaseRoot ($releaseName + '.zip')
$nugetConfig = Join-Path $projectRoot 'NuGet.local.Config'
$tccRoot = Join-Path $projectRoot '.tools\tcc'
$tcc = Join-Path $tccRoot 'tcc.exe'
$projectPrefix = $projectRoot.TrimEnd('\') + '\'
$preservedData = Join-Path $nativeBuildRoot 'preserved-release-data'

foreach ($targetPath in @($buildRoot, $nativeBuildRoot, $portableRoot, $zipPath)) {
    $fullTarget = [IO.Path]::GetFullPath($targetPath)
    if (-not $fullTarget.StartsWith($projectPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝处理项目目录以外的路径：$fullTarget"
    }
}

if (Test-Path -LiteralPath $localDotnet) {
    $dotnet = $localDotnet
} else {
    $systemDotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $systemDotnet) {
        throw '未找到 .NET SDK。构建 NET48 版本需要 .NET SDK。'
    }
    $dotnet = $systemDotnet.Source
}
if (-not (Test-Path -LiteralPath $tcc)) {
    throw '未找到 .tools\tcc\tcc.exe，无法生成不依赖 .NET 的环境检测启动器。'
}

$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\dotnet-home'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.tools\nuget-packages'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'

if (-not (Test-Path -LiteralPath (Join-Path $projectRoot 'net48\obj\project.assets.json'))) {
    & $dotnet restore $projectFile --configfile $nugetConfig --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "NET48 依赖恢复失败，退出代码：$LASTEXITCODE"
    }
}
& $dotnet build $projectFile --configuration Release --no-restore --nologo
if ($LASTEXITCODE -ne 0) {
    throw "NET48 编译失败，退出代码：$LASTEXITCODE"
}

if (Test-Path -LiteralPath $nativeBuildRoot) {
    Remove-Item -LiteralPath $nativeBuildRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $nativeBuildRoot -Force | Out-Null
$launcherPath = Join-Path $nativeBuildRoot 'WavePulse 无线设备电量助手.exe'
$managedPayload = Join-Path $buildRoot 'WavePulse 无线设备电量助手.Core.exe'
$managedConfig = Join-Path $buildRoot 'WavePulse 无线设备电量助手.Core.exe.config'
$payloadHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $managedPayload).Hash
$hashBytes = for ($index = 0; $index -lt $payloadHash.Length; $index += 2) {
    '0x' + $payloadHash.Substring($index, 2)
}
$hashHeader = Join-Path $nativeBuildRoot 'PayloadHash.h'
$hashHeaderText = "static const unsigned char EXPECTED_CORE_SHA256[32] = { " + ($hashBytes -join ', ') + " };`r`n"
[IO.File]::WriteAllText($hashHeader, $hashHeaderText, [Text.UTF8Encoding]::new($false))
$launcherCompiled = $false
try {
    & $tcc `
        -m64 `
        -mwindows `
        -I (Join-Path $tccRoot 'include') `
        -I $nativeBuildRoot `
        -L (Join-Path $tccRoot 'lib') `
        -o $launcherPath `
        (Join-Path $projectRoot 'native\RuntimeLauncher.c') `
        -ladvapi32 -lgdi32 -lshell32 -luser32
    $launcherCompiled = $LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath $launcherPath)
}
catch {
    Write-Warning "原生启动器编译器无法运行：$($_.Exception.Message)"
}
if (-not $launcherCompiled) {
    if (-not $ReuseExistingLauncher) {
        throw '原生环境检测启动器编译失败；如确认 native\RuntimeLauncher.c 未修改，可使用 -ReuseExistingLauncher。'
    }
    $existingLauncher = Join-Path $portableRoot 'WavePulse 无线设备电量助手.exe'
    if (-not (Test-Path -LiteralPath $existingLauncher)) {
        throw "没有可复用的原生启动器：$existingLauncher"
    }
    Copy-Item -LiteralPath $existingLauncher -Destination $launcherPath -Force
    & (Join-Path $projectRoot 'tools\Patch-NativePayloadHash.ps1') `
        -ExePath $launcherPath `
        -NewPayloadPath $managedPayload
}
& (Join-Path $projectRoot 'tools\Set-NativeResources.ps1') `
    -ExePath $launcherPath `
    -IconPath (Join-Path $projectRoot 'assets\app-icon.ico') `
    -ManifestPath (Join-Path $projectRoot 'native\RuntimeLauncher.manifest') `
    -PayloadPath $managedPayload `
    -PayloadConfigPath $managedConfig

$existingData = Join-Path $portableRoot 'data'
if (-not (Test-Path -LiteralPath $existingData)) {
    $currentData = Join-Path $currentPortableRoot 'data'
    if (Test-Path -LiteralPath $currentData) {
        $existingData = $currentData
    } else {
        $legacyData = Join-Path $legacyPortableRoot 'data'
        if (Test-Path -LiteralPath $legacyData) {
            $existingData = $legacyData
        }
    }
}
if (Test-Path -LiteralPath $existingData) {
    Copy-Item -LiteralPath $existingData -Destination $preservedData -Recurse -Force
}
try {
    if (Test-Path -LiteralPath $portableRoot) {
        Remove-Item -LiteralPath $portableRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Path $portableRoot -Force | Out-Null
    Copy-Item -LiteralPath $launcherPath -Destination $portableRoot

    if (-not $NoZip) {
        if (Test-Path -LiteralPath $zipPath) {
            Remove-Item -LiteralPath $zipPath -Force
        }
        Compress-Archive -LiteralPath $portableRoot -DestinationPath $zipPath -CompressionLevel Optimal
    }
}
finally {
    if (Test-Path -LiteralPath $preservedData) {
        $restoredData = Join-Path $portableRoot 'data'
        New-Item -ItemType Directory -Path $restoredData -Force | Out-Null
        foreach ($preservedItem in Get-ChildItem -LiteralPath $preservedData -Force) {
            Copy-Item -LiteralPath $preservedItem.FullName -Destination $restoredData -Recurse -Force
        }
        Remove-Item -LiteralPath $preservedData -Recurse -Force
    }
}

Write-Host "WavePulse v0.1 NET48 发布版已生成：$portableRoot"
