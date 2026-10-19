# 构建说明

公开版本只维护 .NET Framework 4.8：`src/` 是 WPF 应用，`native/RuntimeLauncher.c` 是不依赖 .NET 的环境检测入口，`net48/` 是 SDK 风格工程。

构建使用 .NET SDK，不代表运行需要 .NET 8。SDK、参考程序集与 TinyCC 不打进发布包。

## 准备工具

在 Windows x64 环境准备：

1. .NET SDK。本项目当前使用 .NET 8 SDK 构建 NET48 工程，通过 `dotnet --info` 检查。
2. NuGet 访问能力或预先下载的缓存。依赖 `Microsoft.NETFramework.ReferenceAssemblies.net48` 1.0.3 和 `Microsoft.Windows.SDK.Contracts` 10.0.19041.1。
3. 64 位 TinyCC，放到 `.tools/tcc/`，需要 `tcc.exe`、`include/`、`lib/` 及配套文件。获取入口：[Tiny C Compiler](https://bellard.org/tcc/)。请保留分发包的许可证及配套文件。

脚本优先使用 `.tools/dotnet/dotnet.exe`，否则使用 PATH 中的 `dotnet`。本地工具与缓存不会提交到仓库。

## 执行构建

在 PowerShell 中进入项目根目录：

```powershell
# 本地 NuGet 源可以为空，但目录必须存在。
New-Item -ItemType Directory -Path .tools/local-feed -Force | Out-Null
.\build.ps1
```

脚本恢复依赖、构建 NET48、生成原生入口、嵌入图标 / 清单 / 主程序，然后生成 EXE 和 ZIP。默认输出到 `release/WavePulse-无线设备电量助手-v0.1/` 及同名 ZIP。

只检查托管程序编译可运行：

```powershell
dotnet restore .\net48\BluetoothBatteryMonitor.Net48.csproj --configfile .\NuGet.local.Config
dotnet build .\net48\BluetoothBatteryMonitor.Net48.csproj --configuration Release --no-restore
```

本地源不存在时先执行上面的 `New-Item`。`build-net48.ps1` 是默认构建的兼容入口，构建输出均被 `.gitignore` 排除。

## 受限执行环境

受限环境可能无法启动 TinyCC。维护者确认原生源码未改、目标发布目录已有可信启动器时，可用 `-ReuseExistingLauncher` 复用原生壳，重新写入校验值与资源。这不是从零构建的默认方式，不应使用未知来源的启动器。

## 测试与发布

`tools/` 包含多屏定位、屏幕断开 / 重连、后台启动等验证。部分脚本会显示窗口或枚举设备，先阅读参数和隔离目录规则。模拟测试不替代硬件实测。

README 截图使用示例数据，不是设备兼容性的验证证据；截图工具不应加入用户发布包。

如需重新生成离屏预览，先构建托管程序，然后运行：

```powershell
.\tools\Build-ScreenshotDemo.ps1
.\bin\docs-preview\ScreenshotDemo.exe --render .\docs\images
```

这只加载真实 WPF 界面和示例数据，不启动设备扫描，不操作其他桌面窗口。生成的四张 PNG 分别对应蓝牙列表、2.4G 列表、设置和合并浮窗。

首版为 v0.1，后续使用新版本号。发布前核对工程、应用内关于信息、原生资源、文档、Git 标签与文件名。公开仓库不包含个人配置、日志、缓存、个人耳机地址映射或厂商驱动分析物，流程见 [PUBLISHING.md](PUBLISHING.md)。
