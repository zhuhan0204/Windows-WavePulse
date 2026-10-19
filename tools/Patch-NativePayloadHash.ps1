param(
    [Parameter(Mandatory = $true)][string]$ExePath,
    [Parameter(Mandatory = $true)][string]$NewPayloadPath
)

$ErrorActionPreference = 'Stop'

if (-not ('WavePulseEmbeddedResourceReader' -as [type])) {
    Add-Type @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

public static class WavePulseEmbeddedResourceReader
{
    private const uint LoadLibraryAsDataFile = 0x00000002;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryExW(string fileName, IntPtr file, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr FindResourceW(IntPtr module, IntPtr name, IntPtr type);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadResource(IntPtr module, IntPtr resource);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LockResource(IntPtr resourceData);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SizeofResource(IntPtr module, IntPtr resource);

    [DllImport("kernel32.dll")]
    private static extern bool FreeLibrary(IntPtr module);

    public static byte[] ReadRcData(string executablePath, int resourceId)
    {
        IntPtr module = LoadLibraryExW(executablePath, IntPtr.Zero, LoadLibraryAsDataFile);
        if (module == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            IntPtr resource = FindResourceW(module, new IntPtr(resourceId), new IntPtr(10));
            if (resource == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            uint size = SizeofResource(module, resource);
            IntPtr loaded = LoadResource(module, resource);
            IntPtr pointer = LockResource(loaded);
            if (size == 0 || pointer == IntPtr.Zero) throw new InvalidOperationException("内置主程序资源为空。");
            byte[] result = new byte[size];
            Marshal.Copy(pointer, result, 0, checked((int)size));
            return result;
        }
        finally
        {
            FreeLibrary(module);
        }
    }
}
'@
}

$resolvedExe = [IO.Path]::GetFullPath($ExePath)
$resolvedPayload = [IO.Path]::GetFullPath($NewPayloadPath)
if (-not (Test-Path -LiteralPath $resolvedExe)) { throw "找不到启动器模板：$resolvedExe" }
if (-not (Test-Path -LiteralPath $resolvedPayload)) { throw "找不到新主程序：$resolvedPayload" }

$oldPayload = [WavePulseEmbeddedResourceReader]::ReadRcData($resolvedExe, 101)
$sha = [Security.Cryptography.SHA256]::Create()
try {
    $oldHash = $sha.ComputeHash($oldPayload)
    $newHash = $sha.ComputeHash([IO.File]::ReadAllBytes($resolvedPayload))
}
finally {
    $sha.Dispose()
}

$binary = [IO.File]::ReadAllBytes($resolvedExe)
$matches = New-Object Collections.Generic.List[int]
for ($offset = 0; $offset -le $binary.Length - $oldHash.Length; $offset++) {
    $same = $true
    for ($index = 0; $index -lt $oldHash.Length; $index++) {
        if ($binary[$offset + $index] -ne $oldHash[$index]) {
            $same = $false
            break
        }
    }
    if ($same) { $matches.Add($offset) }
}
if ($matches.Count -ne 1) {
    throw "启动器中的旧主程序校验值出现 $($matches.Count) 次，拒绝自动替换。"
}

[Array]::Copy($newHash, 0, $binary, $matches[0], $newHash.Length)
[IO.File]::WriteAllBytes($resolvedExe, $binary)
Write-Host "已更新启动器内置主程序校验值：偏移 $($matches[0])"
