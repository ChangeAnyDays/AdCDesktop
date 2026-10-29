using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

public static class FixtureUiChecks
{
    private static object backend;
    private static Type backendType;
    private static Window window;
    private static int count;

    private static object Read() { return backendType.GetMethod("Read").Invoke(backend, null); }
    private static int Integer(object value, string name) { return (int)value.GetType().GetProperty(name).GetValue(value, null); }
    private static bool Boolean(object value, string name) { return (bool)value.GetType().GetProperty(name).GetValue(value, null); }
    private static T Find<T>(string name) where T : class { return (T)window.FindName(name); }
    private static void Click(string name) { Find<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); count++; }

    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length != 3) throw new Exception("Expected application path, isolated fixture root, output JSON.");
            string fixture = Path.GetFullPath(args[1]);
            string safeRoot = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!fixture.StartsWith(safeRoot, StringComparison.OrdinalIgnoreCase)) throw new Exception("Fixture must remain under the isolated test output directory.");
            Application app = new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Assembly assembly = Assembly.LoadFrom(args[0]);
            backendType = assembly.GetType("SpectrumControl.SpectrumBackend", true);
            backend = Activator.CreateInstance(backendType, new object[] { fixture, false });
            object panel = Activator.CreateInstance(assembly.GetType("SpectrumControl.Panel", true), new object[] { fixture, false });
            window = (Window)panel.GetType().GetProperty("Window").GetValue(panel, null);

            Check(Integer(Read(), "RawAlpha") == 178, "Initial alpha was not 178.");
            Check(Find<ComboBox>("HoverCombo").SelectedIndex == 2, "Hover mode did not read back as fade in.");
            Check(!Find<ComboBox>("HoverCombo").IsEnabled, "Hover selector must be disabled while click-through is on.");
            Find<TextBox>("XValue").Text = "123";
            Find<TextBox>("YValue").Text = "456";
            Click("ApplyButton");
            object moved = Read();
            Check(Integer(moved, "X") == 123 && Integer(moved, "Y") == 456, "X/Y did not round-trip.");
            Check(Integer(moved, "RawAlpha") == 178 && Integer(moved, "HoverMode") == 2, "Moving changed alpha or hover mode.");

            Find<CheckBox>("ClickThroughCheck").IsChecked = false;
            Check(Find<ComboBox>("HoverCombo").IsEnabled, "Hover selector did not enable after click-through was disabled.");
            Find<ComboBox>("HoverCombo").SelectedIndex = 3;
            Click("ApplyButton");
            Check(!Boolean(Read(), "ClickThrough") && Integer(Read(), "HoverMode") == 3, "Hover settings did not round-trip.");
            Find<CheckBox>("VisibleCheck").IsChecked = false;
            Click("ApplyButton");
            Check(!Boolean(Read(), "Visible"), "Hide did not round-trip.");
            Find<CheckBox>("VisibleCheck").IsChecked = true;
            Click("ApplyButton");
            Check(Boolean(Read(), "Visible"), "Show did not round-trip.");

            Click("RestoreButton");
            Check(Integer(Read(), "RawAlpha") == 178, "Restore did not return alpha to 178.");
            Click("SavePresetButton");
            Find<Slider>("OpacitySlider").Value = 50;
            Click("ApplyButton");
            Check(Integer(Read(), "RawAlpha") == 128, "50% opacity did not produce alpha 128.");
            Click("LoadPresetButton");
            Click("ApplyButton");
            int loadedAlpha = Integer(Read(), "RawAlpha");
            Check(loadedAlpha == 178, "Saved personal preset lost its exact alpha: expected 178, got " + loadedAlpha + ".");
            window.Close();
            app.Shutdown();
            File.WriteAllText(args[2], "{\"success\":true,\"checks\":" + count + "}");
            Console.WriteLine("PASS " + count);
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(args[2], "{\"success\":false,\"checksBeforeFailure\":" + count + ",\"error\":\"" + ex.GetBaseException().Message.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"}");
            Console.WriteLine(ex.ToString());
            return 1;
        }
    }
}
