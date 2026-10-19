param(
    [Parameter(Mandatory = $true)][string]$ExePath,
    [Parameter(Mandatory = $true)][string]$IconPath,
    [Parameter(Mandatory = $true)][string]$ManifestPath,
    [Parameter(Mandatory = $true)][string]$PayloadPath,
    [Parameter(Mandatory = $true)][string]$PayloadConfigPath
)

$ErrorActionPreference = 'Stop'

if (-not ('BluePulseNativeResourceWriter' -as [type])) {
    Add-Type @'
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

public static class BluePulseNativeResourceWriter
{
    private const int RtIcon = 3;
    private const int RtGroupIcon = 14;
    private const int RtManifest = 24;
    private const int RtRcData = 10;
    private const int RtVersion = 16;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr BeginUpdateResourceW(string fileName, bool deleteExistingResources);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UpdateResourceW(
        IntPtr update,
        IntPtr type,
        IntPtr name,
        ushort language,
        byte[] data,
        uint dataSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool EndUpdateResourceW(IntPtr update, bool discard);

    private static IntPtr ResourceId(int value)
    {
        return new IntPtr(value);
    }

    private static void Update(IntPtr handle, int type, int id, byte[] data)
    {
        if (!UpdateResourceW(handle, ResourceId(type), ResourceId(id), 0, data, (uint)data.Length))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    private static void Align4(BinaryWriter writer)
    {
        while ((writer.BaseStream.Position & 3) != 0) writer.Write((byte)0);
    }

    private static long BeginVersionBlock(BinaryWriter writer, ushort valueLength, ushort type, string key)
    {
        long start = writer.BaseStream.Position;
        writer.Write((ushort)0);
        writer.Write(valueLength);
        writer.Write(type);
        foreach (char character in key) writer.Write((ushort)character);
        writer.Write((ushort)0);
        Align4(writer);
        return start;
    }

    private static void EndVersionBlock(BinaryWriter writer, long start)
    {
        long end = writer.BaseStream.Position;
        writer.BaseStream.Position = start;
        writer.Write(checked((ushort)(end - start)));
        writer.BaseStream.Position = end;
    }

    private static void WriteVersionString(BinaryWriter writer, string key, string value)
    {
        long block = BeginVersionBlock(writer, checked((ushort)(value.Length + 1)), 1, key);
        foreach (char character in value) writer.Write((ushort)character);
        writer.Write((ushort)0);
        Align4(writer);
        EndVersionBlock(writer, block);
    }

    private static byte[] CreateVersionInfo()
    {
        using (MemoryStream stream = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            long root = BeginVersionBlock(writer, 52, 0, "VS_VERSION_INFO");
            writer.Write(0xFEEF04BDu);
            writer.Write(0x00010000u);
            writer.Write(0x00000001u);
            writer.Write(0x00000000u);
            writer.Write(0x00000001u);
            writer.Write(0x00000000u);
            writer.Write(0x0000003Fu);
            writer.Write(0u);
            writer.Write(0x00040004u);
            writer.Write(1u);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);
            Align4(writer);

            long stringFileInfo = BeginVersionBlock(writer, 0, 1, "StringFileInfo");
            long stringTable = BeginVersionBlock(writer, 0, 1, "080404B0");
            WriteVersionString(writer, "CompanyName", "yuban");
            WriteVersionString(writer, "FileDescription", "WavePulse 无线设备电量助手");
            WriteVersionString(writer, "FileVersion", "0.1.0.0");
            WriteVersionString(writer, "InternalName", "WavePulse 无线设备电量助手");
            WriteVersionString(writer, "LegalCopyright", "Copyright © 2026 yuban. All rights reserved.");
            WriteVersionString(writer, "OriginalFilename", "WavePulse 无线设备电量助手.exe");
            WriteVersionString(writer, "ProductName", "WavePulse 无线设备电量助手");
            WriteVersionString(writer, "ProductVersion", "0.1.0");
            EndVersionBlock(writer, stringTable);
            EndVersionBlock(writer, stringFileInfo);

            long varFileInfo = BeginVersionBlock(writer, 0, 1, "VarFileInfo");
            long translation = BeginVersionBlock(writer, 4, 0, "Translation");
            writer.Write((ushort)0x0804);
            writer.Write((ushort)0x04B0);
            Align4(writer);
            EndVersionBlock(writer, translation);
            EndVersionBlock(writer, varFileInfo);
            EndVersionBlock(writer, root);
            return stream.ToArray();
        }
    }

    public static void Apply(
        string executablePath,
        string iconPath,
        string manifestPath,
        string payloadPath,
        string payloadConfigPath)
    {
        byte[] iconFile = File.ReadAllBytes(iconPath);
        byte[] manifest = File.ReadAllBytes(manifestPath);
        byte[] payload = File.ReadAllBytes(payloadPath);
        byte[] payloadConfig = File.ReadAllBytes(payloadConfigPath);
        IntPtr handle = BeginUpdateResourceW(executablePath, false);
        if (handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());

        bool completed = false;
        try
        {
            using (MemoryStream iconStream = new MemoryStream(iconFile, false))
            using (BinaryReader reader = new BinaryReader(iconStream))
            {
                ushort reserved = reader.ReadUInt16();
                ushort type = reader.ReadUInt16();
                ushort count = reader.ReadUInt16();
                if (reserved != 0 || type != 1 || count == 0) throw new InvalidDataException("ICO 文件格式无效。");

                byte[] widths = new byte[count];
                byte[] heights = new byte[count];
                byte[] colorCounts = new byte[count];
                byte[] entryReserved = new byte[count];
                ushort[] planes = new ushort[count];
                ushort[] bitCounts = new ushort[count];
                uint[] imageSizes = new uint[count];
                uint[] imageOffsets = new uint[count];

                for (int index = 0; index < count; index++)
                {
                    widths[index] = reader.ReadByte();
                    heights[index] = reader.ReadByte();
                    colorCounts[index] = reader.ReadByte();
                    entryReserved[index] = reader.ReadByte();
                    planes[index] = reader.ReadUInt16();
                    bitCounts[index] = reader.ReadUInt16();
                    imageSizes[index] = reader.ReadUInt32();
                    imageOffsets[index] = reader.ReadUInt32();
                }

                using (MemoryStream groupStream = new MemoryStream())
                using (BinaryWriter writer = new BinaryWriter(groupStream))
                {
                    writer.Write((ushort)0);
                    writer.Write((ushort)1);
                    writer.Write(count);
                    for (int index = 0; index < count; index++)
                    {
                        iconStream.Position = imageOffsets[index];
                        byte[] image = reader.ReadBytes(checked((int)imageSizes[index]));
                        if (image.Length != imageSizes[index]) throw new EndOfStreamException("ICO 图像数据不完整。");
                        Update(handle, RtIcon, index + 1, image);

                        writer.Write(widths[index]);
                        writer.Write(heights[index]);
                        writer.Write(colorCounts[index]);
                        writer.Write(entryReserved[index]);
                        writer.Write(planes[index]);
                        writer.Write(bitCounts[index]);
                        writer.Write(imageSizes[index]);
                        writer.Write((ushort)(index + 1));
                    }
                    writer.Flush();
                    Update(handle, RtGroupIcon, 1, groupStream.ToArray());
                }
            }

            Update(handle, RtManifest, 1, manifest);
            Update(handle, RtVersion, 1, CreateVersionInfo());
            Update(handle, RtRcData, 101, payload);
            Update(handle, RtRcData, 102, payloadConfig);
            if (!EndUpdateResourceW(handle, false)) throw new Win32Exception(Marshal.GetLastWin32Error());
            completed = true;
        }
        finally
        {
            if (!completed) EndUpdateResourceW(handle, true);
        }
    }
}
'@
}

$resolvedExe = [IO.Path]::GetFullPath($ExePath)
$resolvedIcon = [IO.Path]::GetFullPath($IconPath)
$resolvedManifest = [IO.Path]::GetFullPath($ManifestPath)
$resolvedPayload = [IO.Path]::GetFullPath($PayloadPath)
$resolvedPayloadConfig = [IO.Path]::GetFullPath($PayloadConfigPath)
[BluePulseNativeResourceWriter]::Apply(
    $resolvedExe,
    $resolvedIcon,
    $resolvedManifest,
    $resolvedPayload,
    $resolvedPayloadConfig)
