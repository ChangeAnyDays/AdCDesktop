using System;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

public static class FixtureUiChecks
{
    private static object backend;
    private static Type backendType;
    private static Window window;
    private static int count;
    private static int layoutScenarios;
    private static readonly List<string> layoutFailures = new List<string>();

    private static object Read() { return backendType.GetMethod("Read").Invoke(backend, null); }
    private static int Integer(object value, string name) { return (int)value.GetType().GetProperty(name).GetValue(value, null); }
    private static bool Boolean(object value, string name) { return (bool)value.GetType().GetProperty(name).GetValue(value, null); }
    private static T Find<T>(string name) where T : class { return (T)window.FindName(name); }
    private static bool IsInsideWindow(string name)
    {
        FrameworkElement element = Find<FrameworkElement>(name);
        Rect bounds = element.TransformToAncestor(window).TransformBounds(new Rect(element.RenderSize));
        return element.IsVisible && new Rect(0, 0, window.ActualWidth, window.ActualHeight).Contains(bounds);
    }
    private static void Click(string name) { Find<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); count++; }

    private static void DrainLayout()
    {
        window.UpdateLayout();
        DispatcherFrame frame = new DispatcherFrame();
        DispatcherTimer timer = new DispatcherTimer(DispatcherPriority.Background);
        timer.Interval = TimeSpan.FromMilliseconds(230);
        timer.Tick += delegate { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
        window.UpdateLayout();
    }

    private static void LayoutCheck(bool ok, string message)
    {
        count++;
        if (!ok) layoutFailures.Add(message);
    }

    private static void CheckReachable(string name, string scenario)
    {
        FrameworkElement element = Find<FrameworkElement>(name);
        element.BringIntoView();
        window.UpdateLayout();
        window.Dispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(delegate { }));
        Rect bounds = element.TransformToAncestor(window).TransformBounds(new Rect(element.RenderSize));
        Rect visible = new Rect(0, 0, window.ActualWidth, window.ActualHeight);
        DependencyObject ancestor = VisualTreeHelper.GetParent(element);
        while (ancestor != null && ancestor != window)
        {
            FrameworkElement container = ancestor as FrameworkElement;
            if (container != null && (container.ClipToBounds || container is ScrollContentPresenter))
            {
                Rect clip = container.TransformToAncestor(window).TransformBounds(new Rect(container.RenderSize));
                visible.Intersect(clip);
            }
            ancestor = VisualTreeHelper.GetParent(ancestor);
        }
        visible.Inflate(1.5, 1.5);
        LayoutCheck(element.IsVisible && bounds.Width > 1 && bounds.Height > 1 && visible.Contains(bounds),
            scenario + ": " + name + " is clipped or unreachable; bounds=" + bounds.ToString(CultureInfo.InvariantCulture) +
            "; viewport=" + visible.ToString(CultureInfo.InvariantCulture));
    }

    private static void Capture(string path)
    {
        RenderTargetBitmap bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),
            (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        PngBitmapEncoder encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (FileStream output = File.Create(path)) encoder.Save(output);
    }

    private static void ResetScrolls(DependencyObject element)
    {
        ScrollViewer scroll = element as ScrollViewer;
        if (scroll != null) { scroll.ScrollToTop(); scroll.ScrollToLeftEnd(); }
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            ResetScrolls(VisualTreeHelper.GetChild(element, i));
    }

    private static void CheckLayouts(string resultPath)
    {
        string screenshots = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(resultPath)), "ui-layout");
        Directory.CreateDirectory(screenshots);
        string failurePath = Path.Combine(screenshots, "layout-failures.txt");
        if (File.Exists(failurePath)) File.Delete(failurePath);
        FrameworkElement content = (FrameworkElement)window.Content;
        window.MaxHeight = 3000;
        // These are content ScaleTransform simulations at fixed window sizes. They
        // exercise WPF layout, but do not impersonate actual system DPI changes.
        foreach (int[] size in new[] { new[] { 1080, 840 }, new[] { 940, 650 } })
        foreach (double scale in new[] { 1.0, 1.25, 1.5, 2.0 })
        foreach (bool nexus in new[] { false, true })
        {
            window.Width = size[0]; window.Height = size[1];
            content.LayoutTransform = new ScaleTransform(scale, scale);
            Click(nexus ? "NexusTab" : "SpectrumTab");
            DrainLayout();
            ResetScrolls(content);
            DrainLayout();
            string scenario = size[0] + "x" + size[1] + "-scale" + (int)(scale * 100) + "-" + (nexus ? "nexus" : "spectrum");
            layoutScenarios++;
            Capture(Path.Combine(screenshots, scenario + ".png"));
            foreach (string name in new[] { "SpectrumStartupToggle", "NexusStartupToggle", "SpectrumTab", "NexusTab", "HelpButton", "WindowsStartupButton" })
                CheckReachable(name, scenario);
            string[] controls = nexus
                ? new[] { "NexusStartButton", "NexusSettingsButton", "NexusLocateButton", "NexusLabelsCheck", "NexusTooltipsCheck", "NexusQuietButton", "NexusApplyButton", "NexusReloadButton", "NexusRestoreButton" }
                : new[] { "PausePreviewButton", "PreviewSilenceCheck", "PresetIce", "SavePresetButton", "LoadPresetButton", "WidthSlider", "OpacitySlider", "XValue", "YValue", "BottomButton", "ClickThroughCheck", "HoverCombo", "RestoreButton", "ReloadButton", "ApplyButton" };
            foreach (string name in controls) CheckReachable(name, scenario);
            Capture(Path.Combine(screenshots, scenario + "-scrolled.png"));
        }
        content.LayoutTransform = Transform.Identity;
        if (layoutFailures.Count != 0)
        {
            File.WriteAllLines(failurePath, layoutFailures);
            throw new Exception(layoutFailures.Count + " layout checks failed. See ui-layout/layout-failures.txt.");
        }
    }

    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length != 4) throw new Exception("Expected application path, isolated fixture root, output JSON, ownership token.");
            string fixture = Path.GetFullPath(args[1]);
            DirectoryInfo owned = Directory.GetParent(fixture);
            string temporary = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
            if (!String.Equals(Path.GetFileName(fixture), "root", StringComparison.Ordinal) ||
                !String.Equals(owned.Parent.FullName, temporary, StringComparison.OrdinalIgnoreCase) ||
                !owned.Name.StartsWith("adc-ui-", StringComparison.Ordinal) || args[3].Length != 32 ||
                (owned.Attributes & FileAttributes.ReparsePoint) != 0 ||
                File.ReadAllText(Path.Combine(owned.FullName, ".adc-test-owner")) != args[3])
                throw new Exception("Fixture must belong to this invocation's unique temporary test workspace.");
            Application app = new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Assembly assembly = Assembly.LoadFrom(args[0]);
            backendType = assembly.GetType("SpectrumControl.SpectrumBackend", true);
            backend = Activator.CreateInstance(backendType, new object[] { fixture, false });
            object panel = Activator.CreateInstance(assembly.GetType("SpectrumControl.Panel", true), new object[] { fixture, false });
            window = (Window)panel.GetType().GetProperty("Window").GetValue(panel, null);
            window.ShowActivated = false;
            window.ShowInTaskbar = false;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -12000; window.Top = 0;
            window.Show();
            DrainLayout();
            Check(window.IsLoaded && PresentationSource.FromVisual(window) != null,
                "Fixture must create and render a real WPF window.");
            foreach (string name in new[] { "RestoreButton", "ReloadButton", "ApplyButton" })
                Check(IsInsideWindow(name), name + " must be visible without scrolling at the default window size.");

            Check(!Find<CheckBox>("SpectrumStartupToggle").IsEnabled && !Find<CheckBox>("NexusStartupToggle").IsEnabled,
                "Fixture mode must disable both startup switches.");
            Check(Find<TextBlock>("SpectrumStartupStatus").Text.Contains("测试模式"), "Fixture startup state must be explicit.");
            Check(window.Title == "AdCat 桌面", "Control center title was not updated.");
            Find<TextBox>("XValue").Text = "321";
            Click("NexusTab");
            Check(Find<ScrollViewer>("NexusPage").Visibility == Visibility.Visible, "Nexus tab did not open.");
            Check(Find<Grid>("SpectrumFooter").Visibility == Visibility.Collapsed, "Spectrum apply controls leaked into the Nexus page.");
            Check(!Find<Button>("NexusApplyButton").IsEnabled && !Find<Button>("NexusStartButton").IsEnabled, "Fixtures must not expose live Nexus writes/launches.");
            Click("SpectrumTab");
            Check(Find<TextBox>("XValue").Text == "321", "Switching pages discarded staged spectrum changes.");
            Click("PausePreviewButton");
            Check((string)Find<Button>("PausePreviewButton").Content == "继续动画", "Preview pause did not toggle.");
            Click("PausePreviewButton");
            Check((string)Find<Button>("PausePreviewButton").Content == "暂停动画", "Preview resume did not toggle.");
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
            CheckLayouts(args[2]);
            window.Close();
            app.Shutdown();
            File.WriteAllText(args[2], "{\"success\":true,\"checks\":" + count + ",\"layoutScenarios\":" + layoutScenarios + ",\"scaleMethod\":\"content ScaleTransform; not system DPI\"}");
            Console.WriteLine("PASS " + count);
            return 0;
        }
        catch (Exception ex)
        {
            if (args.Length >= 3) File.WriteAllText(args[2], "{\"success\":false,\"checksBeforeFailure\":" + count + ",\"layoutScenarios\":" + layoutScenarios + ",\"scaleMethod\":\"content ScaleTransform; not system DPI\",\"error\":\"" + ex.GetBaseException().Message.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"}");
            Console.WriteLine(ex.ToString());
            return 1;
        }
        finally
        {
            if (window != null) window.Close();
            if (Application.Current != null) Application.Current.Shutdown();
        }
    }
}
