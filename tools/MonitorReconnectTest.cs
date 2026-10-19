using System;
using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

// Isolated integration test: no battery scan, no registry/startup changes and no
// physical display configuration changes. Absence/reappearance is simulated by
// making the saved target identity unavailable/available on the current desktop.
internal static class MonitorReconnectTest
{
    private static Assembly core;
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static object New(string name) { return Activator.CreateInstance(core.GetType("BluetoothBatteryMonitor." + name, true), true); }
    private static object Get(object target, string name)
    {
        PropertyInfo property = target.GetType().GetProperty(name, Instance);
        return property != null ? property.GetValue(target, null) : target.GetType().GetField(name, Instance).GetValue(target);
    }
    private static void Set(object target, string name, object value)
    {
        PropertyInfo property = target.GetType().GetProperty(name, Instance);
        if (property != null) property.SetValue(target, value, null);
        else target.GetType().GetField(name, Instance).SetValue(target, value);
    }
    private static object Call(object target, string name, params object[] args)
    {
        return target.GetType().GetMethod(name, Instance).Invoke(target, args);
    }
    private static object Placement(string name, params object[] args)
    {
        return core.GetType("BluetoothBatteryMonitor.FloatingWindowPlacement", true).GetMethod(name, Static).Invoke(null, args);
    }
    private static void Assert(bool condition, string scenario)
    {
        if (!condition) throw new Exception("FAIL: " + scenario);
        Console.WriteLine("PASS: " + scenario);
    }
    private static void Pump() { Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(delegate { })); }
    private static void Bind(object preference, object monitor)
    {
        Set(preference, "MonitorDeviceName", Get(monitor, "DeviceName"));
        Set(preference, "MonitorIdentity", Get(monitor, "MonitorIdentity"));
        Set(preference, "MonitorWorkWidth", Get(monitor, "WorkWidth"));
        Set(preference, "MonitorWorkHeight", Get(monitor, "WorkHeight"));
        Set(preference, "MonitorCenterXRatio", 0.7);
        Set(preference, "MonitorCenterYRatio", 0.2);
    }
    private static string Saved(object item, string prefix)
    {
        string result = "";
        foreach (string name in new[] { "MonitorDeviceName", "MonitorIdentity", "MonitorWorkWidth", "MonitorWorkHeight",
            "MonitorCenterXRatio", "MonitorCenterYRatio", "Left", "Top" })
            result += Get(item, prefix + name) + "|";
        return result;
    }
    private static Window Single(object main)
    {
        foreach (DictionaryEntry entry in (IDictionary)Get(main, "_floatingWindows")) return (Window)entry.Value;
        return null;
    }
    private static void Stable(object main)
    {
        Call(main, "OnDisplayMonitorTick", null, EventArgs.Empty);
        Call(main, "OnDisplayMonitorTick", null, EventArgs.Empty);
        Pump();
    }
    private static object Record(string name, string identity, int width, int height)
    {
        Type recordType = core.GetType("BluetoothBatteryMonitor.FloatingWindowPlacement+MonitorRecord", true);
        object record = Activator.CreateInstance(recordType, true);
        Set(record, "DeviceName", name); Set(record, "Identity", identity);
        Set(record, "WorkArea", new System.Drawing.Rectangle(0, 0, width, height));
        return record;
    }
    private static IList Records(params object[] records)
    {
        Type recordType = core.GetType("BluetoothBatteryMonitor.FloatingWindowPlacement+MonitorRecord", true);
        IList list = (IList)Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(recordType));
        foreach (object record in records) list.Add(record);
        return list;
    }
    private static void MatchingTests()
    {
        object primary = Record("DISPLAY2", "primary", 1920, 1040);
        object secondary = Record("DISPLAY20", "secondary", 3840, 2120);
        Assert(Placement("FindTargetMonitor", Records(primary), "DISPLAY2", 1920, 1040, "secondary") == null,
            "boot with secondary absent: reused name and identical resolution cannot select primary");
        Assert(Placement("FindTargetMonitor", Records(primary, Record("DISPLAY3", "other", 1920, 1040)),
            "DISPLAY2", 1920, 1040, "secondary") == null, "another similar display is not a substitute");
        Assert(Object.ReferenceEquals(Placement("FindTargetMonitor", Records(primary, secondary),
            "DISPLAY2", 1920, 1040, "secondary"), secondary), "reconnect with renamed DISPLAY and changed resolution finds same identity");
        Assert(Placement("FindTargetMonitor", Records(primary, secondary), "DISPLAY999", 3840, 2120, "") == null,
            "legacy missing display is not guessed by resolution");
    }

    [STAThread]
    public static int Main(string[] args)
    {
        object main = null;
        Application app = null;
        try
        {
            Environment.SetEnvironmentVariable("WAVEPULSE_PORTABLE_DIR", args[1]);
            core = Assembly.LoadFrom(args[0]);
            app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            // The test executable is the entry assembly, so seed the icon using
            // an assembly-qualified pack URI without changing production assets.
            System.Windows.Media.Imaging.BitmapImage icon = new System.Windows.Media.Imaging.BitmapImage();
            icon.BeginInit();
            icon.UriSource = new Uri("pack://application:,,,/" + core.GetName().Name + ";component/assets/app-icon.png");
            icon.EndInit(); icon.Freeze();
            core.GetType("BluetoothBatteryMonitor.AppIcon", true).GetField("_image", Static).SetValue(null, icon);
            MatchingTests();
            IList monitors = (IList)Placement("GetAvailableMonitors");
            Assert(monitors.Count > 0, "active display enumerated");
            object monitor = monitors[0];
            string identity = (string)Get(monitor, "MonitorIdentity");
            Assert(!String.IsNullOrWhiteSpace(identity), "physical display interface identity available");
            main = New("MainWindow");
            Set(main, "_initializationQueued", true); // Do not queue battery I/O.
            object settings = Get(main, "_settings");
            object preference = New("DevicePreference");
            Set(preference, "StableId", "isolated-monitor-test");
            Set(preference, "LastKnownName", "Monitor Test");
            Set(preference, "IsFloating", true);
            Bind(preference, monitor);
            ((IList)Get(settings, "Devices")).Add(preference);
            Set(settings, "MergeFloatingWindows", true);
            Set(settings, "MergedMonitorDeviceName", Get(monitor, "DeviceName"));
            Set(settings, "MergedMonitorIdentity", identity);
            Set(settings, "MergedMonitorWorkWidth", Get(monitor, "WorkWidth"));
            Set(settings, "MergedMonitorWorkHeight", Get(monitor, "WorkHeight"));
            Set(settings, "MergedMonitorCenterXRatio", 0.6);
            Set(settings, "MergedMonitorCenterYRatio", 0.3);
            Call(main, "RestoreAndUpdateFloatingWindows"); Call(main, "UpdateMergedFloatingWindow"); Pump();
            Window single = Single(main);
            Window merged = (Window)Get(main, "_mergedFloatingWindow");
            Assert(single != null && single.IsVisible && single.Opacity > 0 && merged != null && merged.Opacity > 0,
                "single and merged floats visible on available target");
            string savedSingle = Saved(preference, ""), savedMerged = Saved(settings, "Merged");
            single.Left += 80; merged.Left += 100; Pump();
            Assert(Saved(preference, "") == savedSingle && Saved(settings, "Merged") == savedMerged,
                "programmatic/OS relocation never overwrites saved positions");

            // Simulate removal before the managed SystemEvents notification. The
            // native guard must suppress visibility during WINDOWPOSCHANGING.
            Set(preference, "MonitorIdentity", "missing-screen");
            Set(settings, "MergedMonitorIdentity", "missing-screen");
            savedSingle = Saved(preference, ""); savedMerged = Saved(settings, "Merged");
            IntPtr handle = new WindowInteropHelper(single).Handle;
            SetWindowPos(handle, IntPtr.Zero, 10, 10, 0, 0, 0x15);
            Assert(!IsWindowVisible(handle), "removed target hidden before native move becomes visible on primary");
            handle = new WindowInteropHelper(merged).Handle;
            SetWindowPos(handle, IntPtr.Zero, 20, 20, 0, 0, 0x15);
            Assert(!IsWindowVisible(handle), "merged float also suppressed during native relocation");
            Pump(); Stable(main);
            Assert(Single(main) == null && Get(main, "_mergedFloatingWindow") == null,
                "both floats remain absent while target disconnected");
            Assert(Saved(preference, "") == savedSingle && Saved(settings, "Merged") == savedMerged,
                "disconnect preserves target identity and normalized positions");
            Assert((bool)Get(preference, "IsFloating") && (bool)Get(settings, "MergeFloatingWindows"),
                "disconnect does not turn off floating preferences");

            Set(preference, "MonitorIdentity", identity); Set(settings, "MergedMonitorIdentity", identity);
            Call(main, "OnDisplaySettingsChanged", null, EventArgs.Empty); Pump(); Stable(main);
            single = Single(main); merged = (Window)Get(main, "_mergedFloatingWindow");
            Assert(single != null && merged != null && single.Opacity > 0 && merged.Opacity > 0,
                "reconnect restores both floats without battery refresh");
            object captured = Placement("Capture", single);
            Assert(Math.Abs((double)Get(captured, "CenterXRatio") - 0.7) < 0.01 &&
                Math.Abs((double)Get(captured, "CenterYRatio") - 0.2) < 0.01, "single float restored at saved relative position");
            captured = Placement("Capture", merged);
            Assert(Math.Abs((double)Get(captured, "CenterXRatio") - 0.6) < 0.01 &&
                Math.Abs((double)Get(captured, "CenterYRatio") - 0.3) < 0.01, "merged float restored at saved relative position");
            object reloaded = Call(Get(main, "_settingsStore"), "Load");
            Assert((string)Get(((IList)Get(reloaded, "Devices"))[0], "MonitorIdentity") == identity &&
                (string)Get(reloaded, "MergedMonitorIdentity") == identity, "display identities persist in local settings");

            // A saved but absent screen at boot must create no HWND on primary.
            Set(preference, "MonitorIdentity", "missing-at-boot"); Set(settings, "MergedMonitorIdentity", "missing-at-boot");
            Call(main, "SuspendFloatingForDisplayChange"); Stable(main);
            Assert(Single(main) == null && Get(main, "_mergedFloatingWindow") == null,
                "startup target missing: no single or merged float on primary");
            Set(preference, "MonitorIdentity", identity);
            Call(main, "RestoreRememberedFloatingWindows");
            Set(preference, "MonitorIdentity", "removed-during-startup");
            Pump(); Stable(main);
            Assert(Single(main) == null, "display removed between HWND creation and first layout never shows fallback");
            Console.WriteLine("Monitor reconnect simulation passed. Real multi-monitor hot-plug still requires hardware validation.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            if (main != null) { Set(main, "_allowExit", true); ((Window)main).Close(); }
            if (app != null) app.Shutdown();
        }
    }
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
