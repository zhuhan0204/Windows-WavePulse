param(
    [string]$SourceRoot = (Join-Path $PSScriptRoot '..\artifacts\github-source-v0.1'),
    [string]$ReleaseRoot = (Join-Path $PSScriptRoot '..\artifacts\releases\v0.1')
)
$ErrorActionPreference = 'Stop'
$source = [IO.Path]::GetFullPath($SourceRoot)
$release = [IO.Path]::GetFullPath($ReleaseRoot)
Add-Type -AssemblyName System.IO.Compression.FileSystem

$privateSource = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\src\EarbudSampleInference.cs'))
$privateId = [regex]::Match($privateSource, 'ConfirmedLeftRightDeviceId = "([^"]*)";').Groups[1].Value
$privateAddress = $privateId -replace '^address:', ''
$files = @(Get-ChildItem -LiteralPath $source -File -Recurse)
foreach ($file in $files) {
    $relative = $file.FullName.Substring($source.Length + 1)
    if ($relative -match '(^|\\)(bin|obj|data|\.tools|\.test|\.git|artifacts|release|\.wavepulse-runtime)(\\|$)') {
        throw "公开快照包含不应发布的路径：$relative"
    }
    if ($file.Extension -notin @('.png','.ico')) {
        $content = [IO.File]::ReadAllText($file.FullName)
        if ($privateAddress -and $content.IndexOf($privateAddress, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            throw "公开文件包含个人地址：$relative"
        }
    }
    if ($file.Extension -eq '.ps1') {
        $tokens = $null; $errors = $null
        [Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$errors) | Out-Null
        if ($errors.Count) { throw "脚本语法错误：$relative" }
    }
    if ($file.Extension -eq '.md') {
        $markdown = [IO.File]::ReadAllText($file.FullName)
        foreach ($match in [regex]::Matches($markdown, '\]\(([^)]+)\)|src="([^"]+)"')) {
            $target = if ($match.Groups[1].Success) { $match.Groups[1].Value } else { $match.Groups[2].Value }
            if ($target -match '^(https?://|#|mailto:)') { continue }
            $target = [Uri]::UnescapeDataString(($target -split '#',2)[0])
            if ($target -and -not (Test-Path -LiteralPath (Join-Path $file.DirectoryName $target))) {
                throw "Markdown 相对链接不存在：$relative -> $target"
            }
        }
    }
}
foreach ($line in Get-Content -LiteralPath (Join-Path $release 'SHA256SUMS.txt')) {
    if ($line -notmatch '^([a-f0-9]{64})  (.+)$') { throw 'SHA256SUMS 格式错误。' }
    $expected = $Matches[1]; $name = $Matches[2]
    if ((Get-FileHash -LiteralPath (Join-Path $release $name) -Algorithm SHA256).Hash -ne $expected) { throw "校验失败：$name" }
}
$zip = [IO.Compression.ZipFile]::OpenRead((Join-Path $release 'WavePulse-v0.1-win-x64.zip'))
try {
    $names = @($zip.Entries | ForEach-Object FullName)
    if ($names.Count -ne 2 -or $names -notcontains 'WavePulse-v0.1-win-x64.exe' -or $names -notcontains '使用说明.md') {
        throw ('ZIP 内容不符合公开发布约定：' + ($names -join ', '))
    }
}
finally { $zip.Dispose() }

if (-not ('PublicPayloadReader' -as [type])) {
Add-Type @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
public static class PublicPayloadReader {
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("kernel32.dll", SetLastError=true)] static extern IntPtr FindResourceW(IntPtr module, IntPtr name, IntPtr type);
    [DllImport("kernel32.dll")] static extern uint SizeofResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")] static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")] static extern IntPtr LockResource(IntPtr resource);
    [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
    public static byte[] Read(string path) {
        IntPtr module=LoadLibraryEx(path, IntPtr.Zero, 2);
        if(module==IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try {
            IntPtr resource=FindResourceW(module, new IntPtr(101), new IntPtr(10));
            if(resource==IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            byte[] data=new byte[checked((int)SizeofResource(module,resource))];
            Marshal.Copy(LockResource(LoadResource(module,resource)), data, 0, data.Length);
            return data;
        } finally {FreeLibrary(module);}
    }
}
'@
}
$exe = Join-Path $release 'WavePulse-v0.1-win-x64.exe'
$payload = [PublicPayloadReader]::Read($exe)
foreach ($encoding in @([Text.Encoding]::UTF8,[Text.Encoding]::Unicode)) {
    if ($privateAddress -and $encoding.GetString([IO.File]::ReadAllBytes($exe)).Contains($privateAddress)) {
        throw '公开 EXE 包含个人设备地址。'
    }
}
$assembly = [Reflection.Assembly]::Load($payload)
$inference = $assembly.GetType('BluetoothBatteryMonitor.EarbudSampleInference', $true)
$constant = $inference.GetField('ConfirmedLeftRightDeviceId', [Reflection.BindingFlags]'Static,NonPublic').GetRawConstantValue()
if ($constant -ne '') { throw '公开程序仍然启用了个人耳机地址映射。' }
$device = [Activator]::CreateInstance($assembly.GetType('BluetoothBatteryMonitor.BluetoothDeviceBattery'), $true)
$preference = [Activator]::CreateInstance($assembly.GetType('BluetoothBatteryMonitor.DevicePreference'), $true)
$device.StableId = 'demo:public-headset'; $device.BatteryPercent = 100
$samples = [Collections.Generic.List[int]]::new(); $samples.Add(100); $samples.Add(60)
$inference.GetMethod('Apply').Invoke($null, @($device,$preference,$samples,[DateTime]::UtcNow)) | Out-Null
if ($device.EarbudSidesConfirmed -or -not $device.EarbudSidesInferred -or $device.LeftBatteryPercent -ne 100 -or $device.RightBatteryPercent -ne 60) {
    throw '公开版通用耳机采样行为不正确。'
}
[PSCustomObject]@{
    Version = (Get-Content -LiteralPath (Join-Path $source 'VERSION') -Raw).Trim()
    PublicSourceFiles = $files.Count
    MarkdownLocalLinks = 'Passed'
    PowerShellSyntax = 'Passed'
    PrivateAddressRemoved = $true
    EmbeddedPublicPayloadVerified = $true
    NeutralEarbudLabels = $true
    ReleaseHashes = 'Passed'
    CleanZip = $true
    UploadedToGitHub = $false
} | Format-List
