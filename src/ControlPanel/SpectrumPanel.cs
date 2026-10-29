using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Path = System.IO.Path;

[assembly: AssemblyTitle("AdC桌面美化")]
[assembly: AssemblyDescription("用颜色、滑块和预设自定义桌面频谱")]
[assembly: AssemblyVersion("1.0.0.0")]

namespace SpectrumControl
{
    internal static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                bool screenshot = args.Length >= 2 && args[0] == "--screenshot";
                bool smoke = args.Length >= 3 && args[0] == "--smoke-test";
                string root = ResolveRoot(args, screenshot, smoke);
                if (root == null) return 0;
                bool created = true;
                string instanceKey = InstanceKey(root);
                uint activateMessage = Native.RegisterWindowMessage("AdCDesktop.Activate." + instanceKey);
                using (Mutex mutex = new Mutex(true, "Local\\AdCDesktop.SpectrumControl." + instanceKey, out created))
                {
                    if (!created && !screenshot && !smoke)
                    {
                        Native.PostMessage(new IntPtr(0xffff), activateMessage, IntPtr.Zero, IntPtr.Zero);
                        return 0;
                    }
                    Application app = new Application();
                    app.ShutdownMode = ShutdownMode.OnMainWindowClose;
                    Panel panel = new Panel(root, !smoke);
                    app.MainWindow = panel.Window;
                    if (!screenshot && !smoke)
                        panel.Window.SourceInitialized += delegate
                        {
                            HwndSource source = HwndSource.FromHwnd(new WindowInteropHelper(panel.Window).Handle);
                            source.AddHook(delegate(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
                            {
                                if ((uint)message == activateMessage)
                                {
                                    Native.ShowWindow(hwnd, 9);
                                    Native.SetForegroundWindow(hwnd);
                                    handled = true;
                                }
                                return IntPtr.Zero;
                            });
                        };
                    if (screenshot || smoke)
                    {
                        panel.Window.ShowActivated = false;
                        panel.Window.WindowStartupLocation = WindowStartupLocation.Manual;
                        panel.Window.Left = -12000;
                        panel.Window.Top = 0;
                        panel.Window.Width = 1080;
                        panel.Window.Height = 840;
                        panel.Window.MaxHeight = 2000;
                        panel.Window.ContentRendered += delegate
                        {
                            panel.Window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(delegate
                            {
                                try
                                {
                                    if (smoke) panel.SmokeTest(args[2]);
                                    else panel.ExportScreenshot(Path.GetFullPath(args[1]));
                                }
                                catch (Exception ex) { File.WriteAllText(smoke ? args[2] : args[1] + ".error.txt", ex.ToString()); Environment.ExitCode = 1; }
                                finally { panel.Window.Close(); }
                            }));
                        };
                    }
                    app.Run(panel.Window);
                    return Environment.ExitCode;
                }
            }
            catch (Exception ex)
            {
                if (args.Length > 0 && (args[0] == "--screenshot" || args[0] == "--smoke-test"))
                { Console.Error.WriteLine(ex.ToString()); return 1; }
                MessageBox.Show("控制台暂时无法打开：\n" + ex.Message + "\n\n桌面频谱不会因此被关闭。", "AdC桌面美化", MessageBoxButton.OK, MessageBoxImage.Information);
                return 1;
            }
        }

        private static string InstanceKey(string root)
        {
            string normalized = Path.GetFullPath(root).TrimEnd('\\', '/').ToUpperInvariant();
            using (SHA256 hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(normalized))).Replace("-", "");
        }

        private static string ResolveRoot(string[] args, bool screenshot, bool smoke)
        {
            if (smoke) return Path.GetFullPath(args[1]);
            string explicitRoot = null;
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "--root") explicitRoot = Path.GetFullPath(args[i + 1]);
            string parent = Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\')).FullName;
            foreach (string candidate in explicitRoot != null ? new[] { explicitRoot } : new[] { parent, Environment.GetEnvironmentVariable("ADCDESKTOP_HOME") })
                if (!String.IsNullOrWhiteSpace(candidate) && File.Exists(Path.Combine(candidate, "Rainmeter", "Rainmeter.ini")) &&
                    File.Exists(Path.Combine(candidate, "Rainmeter", "Skins", "RetrOcOlOr", "Visualizer", "Visualizer.ini")))
                    return Path.GetFullPath(candidate);
            if (explicitRoot != null || screenshot)
                throw new DirectoryNotFoundException("找不到桌面安装目录。请先运行 Install.ps1，或通过 --root 指定安装目录。");
            using (Forms.FolderBrowserDialog dialog = new Forms.FolderBrowserDialog())
            {
                dialog.Description = "请选择桌面工具的安装目录（包含 Rainmeter 文件夹）。首次使用请先运行项目的 Install.cmd。";
                dialog.ShowNewFolderButton = false;
                if (dialog.ShowDialog() != Forms.DialogResult.OK) return null;
                string selected = Path.GetFullPath(dialog.SelectedPath);
                if (!File.Exists(Path.Combine(selected, "Rainmeter", "Rainmeter.ini")))
                    throw new DirectoryNotFoundException("所选目录不包含 Rainmeter 配置，请先完成安装。");
                return selected;
            }
        }
    }

    public sealed class Panel
    {
        public Window Window { get; private set; }
        private readonly SpectrumBackend backend;
        private readonly string root;
        private SpectrumSettings loaded;
        private SpectrumSettings stagedBase;
        private bool loading;
        private readonly Dictionary<string, string> editors = new Dictionary<string, string>();
        private readonly DispatcherTimer animation;
        private readonly DispatcherTimer statusTimer;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly int screenWidth;
        private readonly int screenHeight;
        private string startColor;
        private string endColor;

        public Panel(string rootPath, bool integration)
        {
            root = rootPath;
            backend = new SpectrumBackend(rootPath, integration);
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SpectrumControl.Panel.xaml"))
                Window = (Window)XamlReader.Load(stream);
            screenWidth = Forms.Screen.PrimaryScreen.Bounds.Width;
            screenHeight = Forms.Screen.PrimaryScreen.Bounds.Height;
            Window.MaxHeight = Math.Max(620, SystemParameters.WorkArea.Height - 35);
            Window.MinHeight = Math.Min(650, Window.MaxHeight);
            Window.Width = Math.Min(1080, SystemParameters.WorkArea.Width - 30);
            Window.MinWidth = Math.Min(940, Window.Width);
            Find<Slider>("WidthSlider").Maximum = Math.Max(screenWidth, 1000);
            WireSlider("WidthSlider", "WidthValue");
            WireSlider("HeightSlider", "HeightValue");
            WireSlider("SensitivitySlider", "SensitivityValue");
            WireSlider("SmoothingSlider", "SmoothingValue");
            WireSlider("OpacitySlider", "OpacityValue");
            Find<Slider>("WidthSlider").ValueChanged += delegate(object sender, RoutedPropertyChangedEventArgs<double> e)
            {
                if (loading) return;
                int x = ParseInteger(Find<TextBox>("XValue").Text, 0);
                if (Math.Abs(x - (screenWidth - e.OldValue) / 2) <= 3)
                    Find<TextBox>("XValue").Text = Math.Max(0, (screenWidth - (int)e.NewValue) / 2).ToString(CultureInfo.InvariantCulture);
            };
            Find<Slider>("HeightSlider").ValueChanged += delegate(object sender, RoutedPropertyChangedEventArgs<double> e)
            {
                if (loading) return;
                int y = ParseInteger(Find<TextBox>("YValue").Text, 0);
                Find<TextBox>("YValue").Text = (y + (int)e.OldValue - (int)e.NewValue).ToString(CultureInfo.InvariantCulture);
            };
            Find<TextBox>("XValue").TextChanged += delegate { MarkDirty(); };
            Find<TextBox>("YValue").TextChanged += delegate { MarkDirty(); };
            foreach (string name in new[] { "VisibleCheck", "ClickThroughCheck" })
            {
                Find<CheckBox>(name).Checked += delegate { MarkDirty(); UpdateHoverAvailability(); DrawPreview(); };
                Find<CheckBox>(name).Unchecked += delegate { MarkDirty(); UpdateHoverAvailability(); DrawPreview(); };
            }
            Find<ComboBox>("HoverCombo").SelectionChanged += delegate { MarkDirty(); };
            foreach (string name in new[] { "PresetOcean", "PresetIce", "PresetSunset", "PresetMint" })
                Find<Button>(name).Click += delegate(object sender, RoutedEventArgs e) { SelectPreset((string)((Button)sender).Tag); };
            Find<Button>("StartColorButton").Click += delegate { PickColor(true); };
            Find<Button>("EndColorButton").Click += delegate { PickColor(false); };
            Find<Button>("CenterButton").Click += delegate { Find<TextBox>("XValue").Text = Math.Max(0, (screenWidth - (int)Find<Slider>("WidthSlider").Value) / 2).ToString(); };
            Find<Button>("BottomButton").Click += delegate
            {
                Find<TextBox>("XValue").Text = Math.Max(0, (screenWidth - (int)Find<Slider>("WidthSlider").Value) / 2).ToString();
                Find<TextBox>("YValue").Text = Math.Max(0, screenHeight - 140 - (int)Find<Slider>("HeightSlider").Value).ToString();
            };
            Find<Button>("ApplyButton").Click += delegate { RunAction(delegate { backend.Apply(ReadEditors()); LoadSettings(backend.Read()); Feedback("已应用到桌面", false); }); };
            Find<Button>("ReloadButton").Click += delegate { RunAction(delegate { LoadSettings(backend.Read()); Feedback("已重新读取桌面设置", false); }); };
            Find<Button>("RestoreButton").Click += delegate { RunAction(delegate { backend.RestoreOriginal(); LoadSettings(backend.Read()); Feedback("已恢复使用控制台前的设置", false); }); };
            Find<Button>("StartButton").Click += delegate { RunAction(delegate { backend.EnsureRunning(); RefreshStatus(); Feedback("组件已启动", false); }); };
            Find<Button>("SavePresetButton").Click += delegate { RunAction(delegate { SaveMyPreset(ReadEditors()); Feedback("已保存你的方案；桌面设置尚未改变", false); }); };
            Find<Button>("LoadPresetButton").Click += delegate { RunAction(delegate { StageSettings(LoadMyPreset()); MarkDirty(); Feedback("已载入你的方案，点击应用即可生效", false); }); };
            Window.SourceInitialized += delegate
            {
                try { int dark = 1; Native.DwmSetWindowAttribute(new WindowInteropHelper(Window).Handle, 20, ref dark, 4); } catch { }
            };
            LoadSettings(backend.Read());
            animation = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            animation.Tick += delegate { DrawPreview(); };
            animation.Start();
            statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            statusTimer.Tick += delegate { RefreshStatus(); };
            statusTimer.Start();
            Window.Closed += delegate { animation.Stop(); statusTimer.Stop(); };
            Find<Canvas>("PreviewCanvas").SizeChanged += delegate { DrawPreview(); };
        }

        private T Find<T>(string name) where T : class
        {
            T value = Window.FindName(name) as T;
            if (value == null) throw new InvalidOperationException("缺少界面组件：" + name);
            return value;
        }
        private void WireSlider(string sliderName, string editorName)
        {
            Slider slider = Find<Slider>(sliderName);
            TextBox editor = Find<TextBox>(editorName);
            editors.Add(sliderName, editorName);
            slider.ValueChanged += delegate { editor.Text = Math.Round(slider.Value).ToString(CultureInfo.InvariantCulture); MarkDirty(); DrawPreview(); };
            editor.LostKeyboardFocus += delegate { CommitEditor(slider, editor); };
            editor.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { CommitEditor(slider, editor); e.Handled = true; } };
        }
        private void CommitEditor(Slider slider, TextBox editor)
        {
            double value;
            if (!double.TryParse(editor.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || double.IsNaN(value) || double.IsInfinity(value))
            { editor.Text = Math.Round(slider.Value).ToString(CultureInfo.InvariantCulture); Feedback("请输入有效数字", true); return; }
            slider.Value = Math.Max(slider.Minimum, Math.Min(slider.Maximum, value));
            editor.Text = Math.Round(slider.Value).ToString(CultureInfo.InvariantCulture);
        }
        private SpectrumSettings ReadEditors()
        {
            foreach (KeyValuePair<string, string> pair in editors)
            {
                double value;
                if (!double.TryParse(Find<TextBox>(pair.Value).Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || double.IsNaN(value) || double.IsInfinity(value))
                    throw new InvalidOperationException("请先检查输入的数字。");
                CommitEditor(Find<Slider>(pair.Key), Find<TextBox>(pair.Value));
            }
            SpectrumSettings valueSettings = (stagedBase ?? loaded).Clone();
            valueSettings.Width = (int)Math.Round(Find<Slider>("WidthSlider").Value);
            valueSettings.Height = (int)Math.Round(Find<Slider>("HeightSlider").Value);
            valueSettings.Sensitivity = Find<Slider>("SensitivitySlider").Value;
            valueSettings.Smoothing = (int)Math.Round(Find<Slider>("SmoothingSlider").Value);
            valueSettings.Opacity = (int)Math.Round(Find<Slider>("OpacitySlider").Value);
            int x, y;
            if (!int.TryParse(Find<TextBox>("XValue").Text, out x) || !int.TryParse(Find<TextBox>("YValue").Text, out y)) throw new InvalidOperationException("位置坐标需要填写整数。");
            valueSettings.X = x; valueSettings.Y = y;
            valueSettings.StartColor = startColor; valueSettings.EndColor = endColor;
            valueSettings.Visible = Find<CheckBox>("VisibleCheck").IsChecked == true;
            valueSettings.ClickThrough = Find<CheckBox>("ClickThroughCheck").IsChecked == true;
            valueSettings.HoverMode = Find<ComboBox>("HoverCombo").SelectedIndex;
            return valueSettings;
        }
        private void StageSettings(SpectrumSettings settings)
        {
            stagedBase = settings.Clone();
            loading = true;
            try
            {
                Find<Slider>("WidthSlider").Maximum = Math.Max(Math.Max(screenWidth, settings.Width), 1000);
                Find<Slider>("HeightSlider").Maximum = Math.Max(300, settings.Height);
                Find<Slider>("WidthSlider").Value = settings.Width;
                Find<Slider>("HeightSlider").Value = settings.Height;
                Find<Slider>("SensitivitySlider").Value = settings.Sensitivity;
                Find<Slider>("SmoothingSlider").Value = settings.Smoothing;
                Find<Slider>("OpacitySlider").Value = settings.Opacity;
                foreach (KeyValuePair<string, string> pair in editors)
                    Find<TextBox>(pair.Value).Text = Math.Round(Find<Slider>(pair.Key).Value).ToString(CultureInfo.InvariantCulture);
                Find<TextBox>("XValue").Text = settings.X.ToString();
                Find<TextBox>("YValue").Text = settings.Y.ToString();
                startColor = settings.StartColor; endColor = settings.EndColor;
                Find<CheckBox>("VisibleCheck").IsChecked = settings.Visible;
                Find<CheckBox>("ClickThroughCheck").IsChecked = settings.ClickThrough;
                Find<ComboBox>("HoverCombo").SelectedIndex = Math.Max(0, Math.Min(3, settings.HoverMode));
                UpdateSwatches(); UpdateHoverAvailability(); DrawPreview();
            }
            finally { loading = false; }
        }
        private void LoadSettings(SpectrumSettings settings)
        {
            loaded = settings.Clone(); StageSettings(settings);
            Find<TextBlock>("DirtyLabel").Text = "已与桌面同步";
            Find<TextBlock>("DirtyLabel").Foreground = Brush("#809BB7");
            Find<Button>("RestoreButton").IsEnabled = backend.HasOriginalBackup;
            Find<Button>("LoadPresetButton").IsEnabled = File.Exists(MyPresetPath);
            RefreshStatus();
        }
        private void MarkDirty()
        {
            if (loading || loaded == null) return;
            Find<TextBlock>("DirtyLabel").Text = "有修改 · 待应用";
            Find<TextBlock>("DirtyLabel").Foreground = Brush("#F4C26A");
            Feedback("面板预览已更新，点击应用即可更新桌面", false);
        }
        private void UpdateHoverAvailability()
        {
            bool clickThrough = Find<CheckBox>("ClickThroughCheck").IsChecked == true;
            Find<ComboBox>("HoverCombo").IsEnabled = !clickThrough;
            Find<ComboBox>("HoverCombo").Opacity = clickThrough ? 0.55 : 1;
            Find<TextBlock>("HoverHint").Text = clickThrough ? "关闭鼠标穿透后，可以使用悬停效果。" : "位置固定，鼠标经过时按所选方式显示。";
        }
        private void SelectPreset(string preset)
        {
            switch (preset)
            {
                case "ice": startColor = "#78E7FF"; endColor = "#4D79FF"; break;
                case "sunset": startColor = "#FFCF70"; endColor = "#FF6A88"; break;
                case "mint": startColor = "#4DE2C6"; endColor = "#85CAFF"; break;
                default: startColor = "#12EDF7"; endColor = "#F50BB9"; break;
            }
            UpdateSwatches(); MarkDirty(); DrawPreview();
        }
        private void PickColor(bool first)
        {
            string current = first ? startColor : endColor;
            Color value = (Color)ColorConverter.ConvertFromString(current);
            using (Forms.ColorDialog dialog = new Forms.ColorDialog())
            {
                dialog.FullOpen = true; dialog.Color = System.Drawing.Color.FromArgb(value.R, value.G, value.B);
                if (dialog.ShowDialog(new DialogOwner(new WindowInteropHelper(Window).Handle)) != Forms.DialogResult.OK) return;
                string hex = string.Format("#{0:X2}{1:X2}{2:X2}", dialog.Color.R, dialog.Color.G, dialog.Color.B);
                if (first) startColor = hex; else endColor = hex;
            }
            UpdateSwatches(); MarkDirty(); DrawPreview();
        }
        private void UpdateSwatches()
        {
            if (startColor == null || endColor == null) return;
            Find<Border>("StartSwatch").Background = Brush(startColor);
            Find<Border>("EndSwatch").Background = Brush(endColor);
            Find<TextBlock>("StartHex").Text = startColor;
            Find<TextBlock>("EndHex").Text = endColor;
            foreach (string name in new[] { "PresetOcean", "PresetIce", "PresetSunset", "PresetMint" }) Find<Button>(name).BorderBrush = Brush("#324B68");
            string active = startColor == "#12EDF7" && endColor == "#F50BB9" ? "PresetOcean" : startColor == "#78E7FF" && endColor == "#4D79FF" ? "PresetIce" : startColor == "#FFCF70" && endColor == "#FF6A88" ? "PresetSunset" : startColor == "#4DE2C6" && endColor == "#85CAFF" ? "PresetMint" : null;
            if (active != null) Find<Button>(active).BorderBrush = Brush("#71DCE5");
        }
        private void RefreshStatus()
        {
            try
            {
                SpectrumStatus status = backend.GetStatus();
                string text = !status.Running ? "组件未运行" : status.SkinActive ? "频谱运行中" : "频谱已隐藏";
                Find<TextBlock>("RuntimeStatus").Text = text;
                Find<Ellipse>("StatusDot").Fill = Brush(status.Running && status.SkinActive ? "#60E2AF" : "#F0B65A");
                Find<Button>("StartButton").Visibility = status.Running ? Visibility.Collapsed : Visibility.Visible;
            }
            catch { Find<TextBlock>("RuntimeStatus").Text = "暂时无法读取状态"; }
        }
        private void Feedback(string message, bool error)
        { Find<TextBlock>("FeedbackText").Text = message; Find<TextBlock>("FeedbackText").Foreground = Brush(error ? "#FFA49A" : "#9CB4CD"); }
        private void RunAction(Action action)
        {
            try { Window.Cursor = Cursors.Wait; action(); }
            catch (Exception ex) { Feedback("操作未完成：" + ex.Message, true); }
            finally { Window.Cursor = null; RefreshStatus(); }
        }
        private string MyPresetPath { get { return Path.Combine(root, "SpectrumControl", "my-preset.json"); } }
        private void SaveMyPreset(SpectrumSettings settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MyPresetPath));
            using (FileStream stream = File.Create(MyPresetPath)) new DataContractJsonSerializer(typeof(SpectrumSettings)).WriteObject(stream, settings);
            Find<Button>("LoadPresetButton").IsEnabled = true;
        }
        private SpectrumSettings LoadMyPreset()
        { using (FileStream stream = File.OpenRead(MyPresetPath)) return (SpectrumSettings)new DataContractJsonSerializer(typeof(SpectrumSettings)).ReadObject(stream); }
        private static int ParseInteger(string value, int fallback) { int parsed; return int.TryParse(value, out parsed) ? parsed : fallback; }
        private static SolidColorBrush Brush(string hex) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }

        private void DrawPreview()
        {
            if (startColor == null || endColor == null) return;
            Canvas canvas = Find<Canvas>("PreviewCanvas");
            double w = canvas.ActualWidth > 5 ? canvas.ActualWidth : 308, h = canvas.ActualHeight > 5 ? canvas.ActualHeight : 196;
            canvas.Children.Clear();
            for (int i = 1; i <= 4; i++) canvas.Children.Add(new Line { X1 = 0, X2 = w, Y1 = h * i / 5, Y2 = h * i / 5, Stroke = Brush("#142D42"), StrokeThickness = 1 });
            for (int i = 1; i <= 5; i++) canvas.Children.Add(new Line { X1 = w * i / 6, X2 = w * i / 6, Y1 = 0, Y2 = h, Stroke = Brush("#102A40"), StrokeThickness = 1 });
            double width = Find<Slider>("WidthSlider").Value, height = Find<Slider>("HeightSlider").Value;
            double amplitude = Math.Min(66, 9 + height * 0.4);
            double waveWidth = (w - 28) * Math.Min(1, Math.Max(0.32, width / 1000));
            double phase = clock.Elapsed.TotalSeconds * 1.5;
            StreamGeometry geometry = new StreamGeometry();
            using (StreamGeometryContext context = geometry.Open())
            {
                for (int i = 0; i <= 100; i++)
                {
                    double t = i / 100.0;
                    double envelope = Math.Pow(Math.Sin(Math.PI * t), 1.4);
                    double y = h / 2 + envelope * amplitude * (0.65 * Math.Sin(t * 30 + phase) + 0.22 * Math.Sin(t * 69 - phase * 1.5));
                    Point point = new Point((w - waveWidth) / 2 + t * waveWidth, y);
                    if (i == 0) context.BeginFigure(point, false, false); else context.LineTo(point, true, false);
                }
            }
            geometry.Freeze();
            LinearGradientBrush gradient = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(startColor), (Color)ColorConverter.ConvertFromString(endColor), new Point(0, 0.5), new Point(1, 0.5));
            double alpha = Find<Slider>("OpacitySlider").Value / 100;
            canvas.Children.Add(new System.Windows.Shapes.Path { Data = geometry, Stroke = gradient, StrokeThickness = 12, Opacity = 0.1 * alpha, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
            canvas.Children.Add(new System.Windows.Shapes.Path { Data = geometry, Stroke = gradient, StrokeThickness = 2.5, Opacity = alpha, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
            Find<Border>("PreviewHidden").Visibility = Find<CheckBox>("VisibleCheck").IsChecked == true ? Visibility.Collapsed : Visibility.Visible;
            Find<TextBlock>("PreviewSize").Text = string.Format("{0:0} × {1:0} 像素 · 不透明度 {2:0}%", width, height, Find<Slider>("OpacitySlider").Value);
        }

        public void ExportScreenshot(string path)
        {
            DrawPreview(); Window.UpdateLayout();
            FrameworkElement visual = (FrameworkElement)Window.Content;
            RenderTargetBitmap image = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth), (int)Math.Ceiling(visual.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            image.Render(visual);
            PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            Directory.CreateDirectory(Path.GetDirectoryName(path)); using (FileStream stream = File.Create(path)) encoder.Save(stream);
        }
        public void SmokeTest(string output)
        {
            SpectrumSettings before = backend.Read();
            if (before.RawAlpha != 178 || before.HoverMode != 2) throw new Exception("Fixture must use Alpha178 and Hover2.");
            Find<Slider>("WidthSlider").Value = 840;
            Find<Slider>("HeightSlider").Value = 95;
            Find<Slider>("SensitivitySlider").Value = 60;
            Find<Slider>("SmoothingSlider").Value = 150;
            Find<Button>("PresetIce").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Find<Button>("ApplyButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            SpectrumSettings after = backend.Read();
            if (after.Width != 840 || after.Height != 95 || Math.Abs(after.Sensitivity - 60) > 0.1 || after.Smoothing != 150 || after.StartColor != "#78E7FF" || after.RawAlpha != 178 || after.HoverMode != 2)
                throw new Exception("UI apply did not round-trip expected settings: " + Find<TextBlock>("FeedbackText").Text);
            Find<Button>("SavePresetButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Find<Button>("PresetSunset").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Find<Button>("LoadPresetButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (startColor != "#78E7FF") throw new Exception("Personal preset did not reload.");
            Find<Button>("RestoreButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            SpectrumSettings restored = backend.Read();
            if (restored.Width != before.Width || restored.Height != before.Height || restored.RawAlpha != before.RawAlpha || restored.HoverMode != before.HoverMode)
                throw new Exception("UI restore failed: " + Find<TextBlock>("FeedbackText").Text);
            File.WriteAllText(output, "{\"success\":true,\"checks\":[\"read\",\"preset staging\",\"sliders\",\"apply\",\"raw alpha 178\",\"hover mode 2\",\"save personal preset\",\"load personal preset\",\"restore original\"]}");
        }
    }

    internal sealed class DialogOwner : Forms.IWin32Window
    {
        public IntPtr Handle { get; private set; }
        public DialogOwner(IntPtr handle) { Handle = handle; }
    }
    internal static class Native
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern uint RegisterWindowMessage(string name);
        [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    }
}
