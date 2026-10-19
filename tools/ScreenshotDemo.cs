using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Media;

// Documentation-only host. Loads the real WPF views with synthetic devices;
// never calls QueueInitialization or battery/receiver readers.
internal static class ScreenshotDemo
{
    private static Assembly core;
    private static string step = "startup";
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static object New(string name) { return Activator.CreateInstance(core.GetType("BluetoothBatteryMonitor." + name, true), true); }
    private static object Get(object target, string name)
    {
        PropertyInfo property = target.GetType().GetProperty(name, Flags);
        return property != null ? property.GetValue(target, null) : target.GetType().GetField(name, Flags).GetValue(target);
    }
    private static void Set(object target, string name, object value)
    {
        PropertyInfo property = target.GetType().GetProperty(name, Flags);
        if (property != null) property.SetValue(target, value, null);
        else target.GetType().GetField(name, Flags).SetValue(target, value);
    }
    private static object Call(object target, string name, params object[] args)
    {
        return target.GetType().GetMethod(name, Flags).Invoke(target, args);
    }
    private static object EnumValue(string type, string value)
    {
        return Enum.Parse(core.GetType("BluetoothBatteryMonitor." + type, true), value);
    }
    private static IList List(string type)
    {
        return (IList)Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(core.GetType("BluetoothBatteryMonitor." + type, true)));
    }
    private static object Device(string id, string name, string kind, string transport, int? percent, bool connected)
    {
        object item = New("BluetoothDeviceBattery");
        Set(item, "StableId", "demo:" + id); Set(item, "Name", name); Set(item, "DetectedName", name);
        Set(item, "Kind", EnumValue("DeviceKind", kind)); Set(item, "DetectedKind", EnumValue("DeviceKind", kind));
        Set(item, "Transport", EnumValue("DeviceTransport", transport)); Set(item, "IsPaired", true);
        Set(item, "IsConnected", connected); Set(item, "BatteryPercent", percent);
        Set(item, "IsLowEnergy", transport == "Bluetooth");
        Set(item, "BatterySource", "文档示例数据"); Set(item, "LastRefreshedAt", DateTime.Today.AddHours(10).AddMinutes(30));
        return item;
    }
    private static void Render(Window window, string path, int width, int height)
    {
        FrameworkElement content = (FrameworkElement)window.Content;
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
        RenderTargetBitmap bitmap = new RenderTargetBitmap(width * 2, height * 2, 192, 192, PixelFormats.Pbgra32);
        DrawingVisual background = new DrawingVisual();
        using (DrawingContext drawing = background.RenderOpen())
            drawing.DrawRectangle(window.Background ?? Brushes.Transparent, null, new Rect(0, 0, width, height));
        bitmap.Render(background);
        bitmap.Render(content);
        PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (FileStream stream = File.Create(path)) encoder.Save(stream);
    }
    [STAThread]
    public static void Main(string[] args)
    {
        try { Run(args); }
        catch (Exception error)
        {
            Console.Error.WriteLine("Screenshot preview failed at " + step);
            for (Exception item = error; item != null; item = item.InnerException)
                Console.Error.WriteLine(item.GetType().FullName + ": " + item.Message);
            Environment.ExitCode = 1;
            if (Application.Current != null) Application.Current.Shutdown();
        }
    }
    private static void Run(string[] args)
    {
        string directory = AppDomain.CurrentDomain.BaseDirectory;
        Environment.SetEnvironmentVariable("WAVEPULSE_PORTABLE_DIR", Path.Combine(directory, "demo-local"));
        string assemblyPath = File.ReadAllText(Path.Combine(directory, "demo-assembly.txt")).Trim();
        core = Assembly.LoadFrom(assemblyPath);
        Application app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        BitmapImage icon = new BitmapImage(); icon.BeginInit();
        icon.UriSource = new Uri("pack://application:,,,/" + core.GetName().Name + ";component/assets/app-icon.png");
        icon.EndInit(); icon.Freeze();
        core.GetType("BluetoothBatteryMonitor.AppIcon", true).GetField("_image", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, icon);
        step = "construct-main";
        Window main = (Window)New("MainWindow");
        Set(main, "_allowExit", true);
        object settings = Get(main, "_settings");
        IList preferences = (IList)Get(settings, "Devices"); preferences.Clear();
        IList devices = List("BluetoothDeviceBattery");
        devices.Add(Device("mouse", "蓝牙鼠标", "Mouse", "Bluetooth", 85, true));
        object headset = Device("headset", "无线耳机", "Headphones", "Bluetooth", null, true);
        Set(headset, "LeftBatteryPercent", (int?)100); Set(headset, "RightBatteryPercent", (int?)60);
        Set(headset, "EarbudSidesInferred", true); devices.Add(headset);
        devices.Add(Device("gamepad", "无线手柄", "Gamepad", "Bluetooth", null, false));
        devices.Add(Device("m750", "罗技 M750", "Mouse", "Receiver24G", 85, true));
        devices.Add(Device("g98", "迈从 G98 V2", "Keyboard", "Receiver24G", 70, true));
        devices.Add(Device("s9", "前行者 S9 Pro", "Mouse", "Receiver24G", 15, true));
        foreach (object device in devices)
        {
            object preference = New("DevicePreference");
            Set(preference, "StableId", Get(device, "StableId")); Set(preference, "LastKnownName", Get(device, "Name"));
            Set(preference, "LastKnownKind", Get(device, "Kind")); Set(preference, "LastKnownTransport", Get(device, "Transport"));
            Set(preference, "FloatingWidth", 260); Set(preference, "FloatingHeight", 78);
            Set(preference, "IsExpanded", false); Set(preference, "MergedDisplayOrder", preferences.Count);
            preferences.Add(preference);
        }
        step = "render-device-rows";
        Set(main, "_devices", devices); Call(main, "RenderDevices"); Call(main, "UpdateSummaryText", "示例设备 · 10:30");
        app.MainWindow = main;
        bool renderOnly = args.Length == 2 && args[0] == "--render";
        if (!renderOnly) main.Show();
        // Independent preview of the same merged-window class: normal / low / offline.
        step = "construct-merged";
        IList mergedItems = List("MergedFloatingItem");
        Type mergedItem = core.GetType("BluetoothBatteryMonitor.MergedFloatingItem", true);
        foreach (int index in new[] { 0, 5, 2 })
            mergedItems.Add(Activator.CreateInstance(mergedItem, Flags, null, new[] { devices[index], preferences[index] }, null));
        Type mergedType = core.GetType("BluetoothBatteryMonitor.MergedFloatingWindow", true);
        Window merged = (Window)Activator.CreateInstance(mergedType, Flags, null, new object[] { mergedItems, 20, false, false, 100 }, null);
        merged.Title = "WavePulse 截图示例浮窗";
        if (renderOnly)
        {
            string output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
            step = "render-bluetooth";
            Render(main, Path.Combine(output, "bluetooth-devices.png"), 1080, 720);
            Set(main, "_selectedTransport", EnumValue("DeviceTransport", "Receiver24G"));
            Call(main, "UpdateTransportSwitch"); Call(main, "RenderDevices"); Call(main, "UpdateSummaryText", "示例设备 · 10:30");
            step = "render-receiver";
            Render(main, Path.Combine(output, "receiver-devices.png"), 1080, 720);
            Type settingsType = core.GetType("BluetoothBatteryMonitor.SettingsWindow", true);
            step = "construct-settings";
            Window settingsView = (Window)Activator.CreateInstance(settingsType, Flags, null,
                new object[] { null, settings, Get(main, "_settingsStore"), new Action(delegate {}),
                    new Action<bool>(delegate {}), new Action(delegate {}) }, null);
            step = "render-settings";
            Render(settingsView, Path.Combine(output, "settings.png"), 610, 650);
            step = "render-merged";
            Render(merged, Path.Combine(output, "merged-floating.png"), (int)merged.Width, (int)merged.Height);
            main.Close(); app.Shutdown();
            return;
        }
        merged.Owner = main; merged.WindowStartupLocation = WindowStartupLocation.Manual;
        merged.Left = Math.Max(SystemParameters.WorkArea.Left, main.Left + main.Width - merged.Width - 24);
        merged.Top = main.Top + 180; merged.Show();
        main.Activate();
        app.Run();
    }
}
