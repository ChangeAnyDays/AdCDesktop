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
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Path = System.IO.Path;

[assembly: AssemblyTitle("ADC 总控台")]
[assembly: AssemblyDescription("统一管理桌面频谱与 Nexus 启动栏")]
[assembly: AssemblyVersion("1.3.0.0")]

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
                    Panel panel = new Panel(root, !smoke && !screenshot);
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
                MessageBox.Show("控制台暂时无法打开：\n" + ex.Message + "\n\n桌面频谱不会因此被关闭。", "ADC 总控台", MessageBoxButton.OK, MessageBoxImage.Information);
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
        private readonly SpectrumPreview preview = new SpectrumPreview();
        private double previewSeconds;
        private double lastPreviewTick;
        private bool previewPaused;
        private bool nexusPage;
        private readonly NexusBackend nexus;
        private bool nexusLoading;
        private bool nexusDirty;
        private readonly StartupBackend startup;
        private bool refreshingStartup;
        private double previewCanvasWidth, previewCanvasHeight;
        private System.Windows.Shapes.Path previewLine, previewGlow;
        private string previewGradientKey;

        public Panel(string rootPath, bool integration)
        {
            root = rootPath;
            backend = new SpectrumBackend(rootPath, integration);
            nexus = new NexusBackend(rootPath, integration);
            startup = new StartupBackend(integration);
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SpectrumControl.Panel.xaml"))
                Window = (Window)XamlReader.Load(stream);
            using (Stream logoStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SpectrumControl.Logo.png"))
            {
                if (logoStream != null)
                {
                    BitmapImage logo = new BitmapImage();
                    logo.BeginInit();
                    logo.CacheOption = BitmapCacheOption.OnLoad;
                    logo.StreamSource = logoStream;
                    logo.EndInit();
                    logo.Freeze();
                    Find<Image>("BrandLogo").Source = logo;
                }
            }
            screenWidth = Forms.Screen.PrimaryScreen.Bounds.Width;
            screenHeight = Forms.Screen.PrimaryScreen.Bounds.Height;
            Window.MaxHeight = Math.Max(620, SystemParameters.WorkArea.Height - 35);
            Window.Height = Math.Min(960, Window.MaxHeight);
            Window.MinHeight = Math.Min(650, Window.MaxHeight);
            Window.Width = Math.Min(1160, SystemParameters.WorkArea.Width - 30);
            Window.MinWidth = Math.Min(940, Window.Width);
            Find<Slider>("WidthSlider").Maximum = Math.Min(4096, Math.Max(screenWidth, 1000));
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
            Find<Button>("RestoreDesktopButton").Click += delegate { RestoreDesktop(); };
            Find<Button>("StartButton").Click += delegate { RunAction(delegate { backend.EnsureRunning(); RefreshStatus(); Feedback("组件已启动", false); }); };
            Find<Button>("SavePresetButton").Click += delegate { RunAction(delegate { SaveMyPreset(ReadEditors()); Feedback("已保存你的方案；桌面设置尚未改变", false); }); };
            Find<Button>("LoadPresetButton").Click += delegate { RunAction(delegate { StageSettings(LoadMyPreset()); MarkDirty(); Feedback("已载入你的方案，点击应用即可生效", false); }); };
            Window.SourceInitialized += delegate { ApplyLightTitleBar(); };
            Window.Loaded += delegate { ApplyLightTitleBar(); };
            Window.Activated += delegate { ApplyLightTitleBar(); };
            Find<Button>("HelpButton").IsEnabled = integration;
            Find<Button>("HelpButton").Click += delegate
            {
                Action openHelp = delegate
                {
                    string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "QuickStart.html");
                    if (!File.Exists(path)) throw new FileNotFoundException("未找到教程，请将随包的 QuickStart.html 放在程序旁边。");
                    using (Process process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })) { }
                };
                if (nexusPage) NexusAction(openHelp); else RunAction(openHelp);
            };
            WireNexus();
            WireStartup();
            Find<Button>("WindowsStartupButton").IsEnabled = integration;
            Find<Button>("WindowsStartupButton").Click += delegate
            {
                try { using (Process process = Process.Start(new ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true })) { } }
                catch (Exception ex) { Find<TextBlock>("StartupFeedback").Text = "系统启动项未能打开：" + ex.Message; }
            };
            Window.Loaded += delegate { WireMotion(Window.Content as DependencyObject); };
            Window.Activated += delegate { RefreshStartup(); };
            Find<Button>("SpectrumTab").Click += delegate { SelectPage(false); };
            Find<Button>("NexusTab").Click += delegate { SelectPage(true); };
            Find<Button>("PausePreviewButton").Click += delegate
            {
                previewPaused = !previewPaused;
                Find<Button>("PausePreviewButton").Content = previewPaused ? "继续动画" : "暂停动画";
                UpdateAnimation();
            };
            Find<CheckBox>("PreviewSilenceCheck").Checked += delegate { DrawPreview(); };
            Find<CheckBox>("PreviewSilenceCheck").Unchecked += delegate { DrawPreview(); };
            LoadSettings(backend.Read());
            animation = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            animation.Tick += delegate
            {
                double now = clock.Elapsed.TotalSeconds;
                previewSeconds += Math.Min(0.1, Math.Max(0, now - lastPreviewTick));
                lastPreviewTick = now;
                DrawPreview();
            };
            animation.Start();
            statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            statusTimer.Tick += delegate { RefreshStatus(); RefreshNexusStatus(); RefreshStartup(); };
            statusTimer.Start();
            Window.Closed += delegate { animation.Stop(); statusTimer.Stop(); };
            Window.StateChanged += delegate { UpdateAnimation(); };
            Window.IsVisibleChanged += delegate { UpdateAnimation(); };
            SelectPage(false);
            Find<Canvas>("PreviewCanvas").SizeChanged += delegate { DrawPreview(); };
        }

        private string SpectrumExecutable { get { return Path.Combine(root, "Rainmeter", "Rainmeter.exe"); } }
        private string NexusStartupExecutable { get { return nexus.StartupExecutable ?? Path.Combine(root, "Nexus", "Nexus.exe"); } }
        private void WireStartup()
        {
            foreach (bool isNexus in new[] { false, true })
            {
                bool component = isNexus;
                CheckBox toggle = Find<CheckBox>(component ? "NexusStartupToggle" : "SpectrumStartupToggle");
                toggle.Checked += delegate { ChangeStartup(component); };
                toggle.Unchecked += delegate { ChangeStartup(component); };
            }
            RefreshStartup();
        }
        private void RefreshStartup()
        {
            if (refreshingStartup) return;
            refreshingStartup = true;
            try
            {
                foreach (bool isNexus in new[] { false, true })
                {
                    CheckBox toggle = Find<CheckBox>(isNexus ? "NexusStartupToggle" : "SpectrumStartupToggle");
                    TextBlock label = Find<TextBlock>(isNexus ? "NexusStartupStatus" : "SpectrumStartupStatus");
                    try
                    {
                        StartupState state = startup.Read(isNexus ? StartupBackend.NexusName : StartupBackend.SpectrumName,
                            isNexus ? NexusStartupExecutable : SpectrumExecutable);
                        if (toggle.IsChecked != state.Enabled)
                        {
                            if (toggle.Template != null)
                            {
                                Ellipse knob = toggle.Template.FindName("SwitchKnob", toggle) as Ellipse;
                                if (knob != null) knob.ClearValue(UIElement.RenderTransformProperty);
                            }
                            toggle.IsChecked = state.Enabled;
                        }
                        toggle.IsEnabled = state.CanChange;
                        label.Text = state.Detail;
                        label.Foreground = Brush(state.Enabled ? "#4F6B5B" : "#6E6A63");
                    }
                    catch (Exception ex)
                    {
                        toggle.IsChecked = false; toggle.IsEnabled = false;
                        label.Text = "无法读取：" + ex.Message;
                        label.Foreground = Brush("#A33A34");
                    }
                }
            }
            finally { refreshingStartup = false; }
        }
        private void ChangeStartup(bool isNexus)
        {
            if (refreshingStartup) return;
            CheckBox toggle = Find<CheckBox>(isNexus ? "NexusStartupToggle" : "SpectrumStartupToggle");
            bool enabled = toggle.IsChecked == true;
            TextBlock message = Find<TextBlock>("StartupFeedback");
            try
            {
                startup.Set(isNexus ? StartupBackend.NexusName : StartupBackend.SpectrumName,
                    isNexus ? NexusStartupExecutable : SpectrumExecutable, enabled);
                message.Text = (isNexus ? "Nexus" : "频谱") + (enabled ? "自启动已保存，下次登录生效。" : "的 ADC 自启动已关闭，当前程序继续运行。") + " 原程序另外设置的自启动需在原程序中关闭。";
                message.Foreground = Brush("#4F6B5B");
                RefreshStartup();
                AnimateSwitch(toggle, enabled);
            }
            catch (Exception ex)
            {
                RefreshStartup();
                message.Text = "未能保存：" + ex.Message;
                message.Foreground = Brush("#A33A34");
            }
        }
        private static void AnimateSwitch(CheckBox toggle, bool enabled)
        {
            if (!SystemParameters.ClientAreaAnimation || toggle.Template == null) return;
            Ellipse knob = toggle.Template.FindName("SwitchKnob", toggle) as Ellipse;
            if (knob == null) return;
            var transform = new TranslateTransform(enabled ? 22 : 0, 0);
            knob.RenderTransform = transform;
            transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(enabled ? 0 : 22, enabled ? 22 : 0,
                TimeSpan.FromMilliseconds(160)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
        }
        private void WireMotion(DependencyObject element)
        {
            if (element == null) return;
            Button button = element as Button;
            if (button != null)
            {
                button.RenderTransformOrigin = new Point(0.5, 0.5);
                button.RenderTransform = new ScaleTransform(1, 1);
                button.MouseEnter += delegate { AnimateButton(button, 1.015); };
                button.MouseLeave += delegate { AnimateButton(button, 1); };
                button.PreviewMouseLeftButtonDown += delegate { AnimateButton(button, 0.98); };
                button.PreviewMouseLeftButtonUp += delegate { AnimateButton(button, button.IsMouseOver ? 1.015 : 1); };
            }
            foreach (object child in LogicalTreeHelper.GetChildren(element)) WireMotion(child as DependencyObject);
        }
        private static void AnimateButton(Button button, double scale)
        {
            if (!SystemParameters.ClientAreaAnimation || !button.IsEnabled) return;
            ScaleTransform transform = button.RenderTransform as ScaleTransform;
            if (transform == null) return;
            var animation = new DoubleAnimation(scale, TimeSpan.FromMilliseconds(110)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            transform.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
            transform.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        }
        private void AnimatePage(FrameworkElement page)
        {
            page.BeginAnimation(UIElement.OpacityProperty, null);
            page.Opacity = 1;
            page.RenderTransform = new TranslateTransform();
            if (!Window.IsVisible || !SystemParameters.ClientAreaAnimation) return;
            var duration = TimeSpan.FromMilliseconds(180);
            page.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.55, 1, duration) { FillBehavior = FillBehavior.Stop });
            ((TranslateTransform)page.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(7, 0, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
        }

        private void WireNexus()
        {
            Find<Button>("NexusStartButton").Click += delegate { NexusAction(delegate { nexus.Start(false); NexusFeedback("已请求启动 Nexus。", false); }); };
            Find<Button>("NexusSettingsButton").Click += delegate { NexusAction(delegate
            {
                if (nexus.Running)
                {
                    NexusFeedback("Nexus 已在运行。请右击 Dock 上的 Nexus 控制图标，选择「首选项 / Preferences」；这里不会另开隐藏实例。", false);
                    return;
                }
                nexus.Start(true);
                NexusFeedback("已请求打开原生设置；如未弹出，可点击 Nexus 控制图标或按默认快捷键 Ctrl+F11。", false);
            }); };
            Find<Button>("NexusLocateButton").Click += delegate
            {
                using (Forms.OpenFileDialog dialog = new Forms.OpenFileDialog())
                {
                    dialog.Title = "选择 Nexus.exe"; dialog.Filter = "Nexus 程序|Nexus.exe"; dialog.CheckFileExists = true;
                    if (dialog.ShowDialog(new DialogOwner(new WindowInteropHelper(Window).Handle)) == Forms.DialogResult.OK)
                        NexusAction(delegate { nexus.SelectExecutable(dialog.FileName); LoadNexusSettings(); });
                }
            };
            Find<Button>("NexusReloadButton").Click += delegate { LoadNexusSettings(); };
            Find<Button>("NexusApplyButton").Click += delegate { ApplyNexus(false); };
            Find<Button>("NexusRestoreButton").Click += delegate { ApplyNexus(true); };
            Find<Button>("NexusQuietButton").Click += delegate
            {
                Find<CheckBox>("NexusLabelsCheck").IsChecked = false;
                Find<CheckBox>("NexusTooltipsCheck").IsChecked = false;
                ApplyNexus(false);
            };
            foreach (string name in new[] { "NexusLabelsCheck", "NexusTooltipsCheck" })
            {
                Find<CheckBox>(name).Checked += delegate { MarkNexusDirty(); };
                Find<CheckBox>(name).Unchecked += delegate { MarkNexusDirty(); };
                Find<CheckBox>(name).Indeterminate += delegate { MarkNexusDirty(); };
            }
            RefreshNexusStatus();
            LoadNexusSettings();
        }
        private void MarkNexusDirty()
        {
            if (nexusLoading) return;
            nexusDirty = true;
            NexusFeedback("文字设置待应用。请先退出 Nexus，应用后重新启动。", false);
        }
        private void NexusFeedback(string message, bool error)
        {
            Find<TextBlock>("NexusFeedback").Text = message;
            Find<TextBlock>("NexusFeedback").Foreground = Brush(error ? "#A33A34" : "#6E6A63");
        }
        private void NexusAction(Action action)
        {
            try { action(); }
            catch (Exception ex) { NexusFeedback("操作未完成：" + ex.Message, true); }
            finally { RefreshNexusStatus(); }
        }
        private void ApplyNexus(bool restore)
        {
            NexusAction(delegate
            {
                if (!restore && (Find<CheckBox>("NexusLabelsCheck").IsChecked == null || Find<CheckBox>("NexusTooltipsCheck").IsChecked == null))
                    throw new InvalidOperationException("不同 Dock 的设置不一致，请先明确选择是否显示文字。");
                nexus.Apply(Find<CheckBox>("NexusLabelsCheck").IsChecked == true, Find<CheckBox>("NexusTooltipsCheck").IsChecked == true, restore);
                LoadNexusSettings();
                NexusFeedback(restore ? "已恢复原文字设置。点击「启动 Nexus」生效。" : "文字设置已保存并校验。点击「启动 Nexus」生效。", false);
            });
        }
        private void LoadNexusSettings()
        {
            nexusLoading = true;
            bool supported = false;
            try
            {
                if (!nexus.CanIntegrate) throw new InvalidOperationException("测试模式：不会读取或修改本机 Nexus 设置。");
                List<NexusValue> values = nexus.Settings.Read();
                bool? labels = null, tips = null;
                bool firstLabel = true;
                bool mixed = false;
                foreach (NexusValue value in values)
                    if (NexusSettingsService.IsLabelName(value.Name))
                    {
                        bool enabled = NexusSettingsService.Decode(value) != 0;
                        if (!firstLabel && labels != enabled) mixed = true;
                        labels = enabled; firstLabel = false;
                    }
                    else tips = NexusSettingsService.Decode(value) != 0;
                Find<CheckBox>("NexusLabelsCheck").IsChecked = mixed ? (bool?)null : labels;
                Find<CheckBox>("NexusTooltipsCheck").IsChecked = tips;
                nexusDirty = false;
                supported = true;
                NexusFeedback(mixed ? "多个 Dock 的标签设置不同。选择后将统一修改文字显示。" : "已读取文字设置。修改前请先退出 Nexus；首次应用会备份原值。", false);
            }
            catch (Exception ex)
            {
                Find<CheckBox>("NexusLabelsCheck").IsChecked = null;
                Find<CheckBox>("NexusTooltipsCheck").IsChecked = null;
                NexusFeedback(ex.Message + "\n手动关闭：外观 → 取消鼠标经过时显示图标标签；行为 → 更多选项 → 气泡提示选择从不。", false);
            }
            finally
            {
                nexusLoading = false;
                foreach (string name in new[] { "NexusQuietButton", "NexusApplyButton" }) Find<Button>(name).IsEnabled = supported;
                foreach (string name in new[] { "NexusLabelsCheck", "NexusTooltipsCheck" }) Find<CheckBox>(name).IsEnabled = supported;
                Find<Button>("NexusRestoreButton").IsEnabled = supported && nexus.Settings.HasBackup;
            }
        }
        private void RefreshNexusStatus()
        {
            try
            {
                string path = nexus.Executable;
                Find<TextBlock>("NexusStatus").Text = nexus.Running ? "Nexus 正在运行" : path == null ? "尚未找到 Nexus" : "Nexus 已安装 · 未运行";
                Find<TextBlock>("NexusPath").Text = path ?? "仅安装频谱也可使用总控台；如已安装 Nexus，请选择程序路径。";
                Find<Button>("NexusStartButton").IsEnabled = path != null && nexus.CanIntegrate;
                Find<Button>("NexusSettingsButton").IsEnabled = path != null && nexus.CanIntegrate;
                Find<Button>("NexusLocateButton").IsEnabled = nexus.CanIntegrate;
            }
            catch (Exception ex) { Find<TextBlock>("NexusStatus").Text = "无法读取 Nexus 状态：" + ex.Message; }
        }

        private void SelectPage(bool showNexus)
        {
            bool changedPage = nexusPage != showNexus;
            nexusPage = showNexus;
            Find<Grid>("SpectrumPage").Visibility = showNexus ? Visibility.Collapsed : Visibility.Visible;
            Find<Grid>("SpectrumFooter").Visibility = showNexus ? Visibility.Collapsed : Visibility.Visible;
            Find<ScrollViewer>("NexusPage").Visibility = showNexus ? Visibility.Visible : Visibility.Collapsed;
            Find<Button>("SpectrumTab").BorderBrush = Brush(showNexus ? "#D8D0C4" : "#1D1D1D");
            Find<Button>("NexusTab").BorderBrush = Brush(showNexus ? "#1D1D1D" : "#D8D0C4");
            Find<Button>("SpectrumTab").Background = Brush(showNexus ? "#FBF9F4" : "#1D1D1D");
            Find<Button>("NexusTab").Background = Brush(showNexus ? "#1D1D1D" : "#FBF9F4");
            Find<Button>("SpectrumTab").Foreground = Brush(showNexus ? "#3A3A36" : "#FAF8F4");
            Find<Button>("NexusTab").Foreground = Brush(showNexus ? "#FAF8F4" : "#3A3A36");
            if (showNexus) { RefreshNexusStatus(); if (!nexusDirty) LoadNexusSettings(); }
            if (changedPage) AnimatePage(showNexus ? (FrameworkElement)Find<ScrollViewer>("NexusPage") : Find<Grid>("SpectrumPage"));
            UpdateAnimation();
        }
        private void UpdateAnimation()
        {
            if (animation == null) return;
            lastPreviewTick = clock.Elapsed.TotalSeconds;
            if (previewPaused || nexusPage || !Window.IsVisible || Window.WindowState == WindowState.Minimized) animation.Stop();
            else animation.Start();
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
                Find<Slider>("WidthSlider").Maximum = Math.Min(4096, Math.Max(Math.Max(screenWidth, settings.Width), 1000));
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
            // A reload (or an apply that rereads the desktop) ends any preset staging.
            stagedBase = null;
            Find<TextBlock>("DirtyLabel").Text = "已与桌面同步";
            Find<TextBlock>("DirtyLabel").Foreground = Brush("#8C867C");
            Find<Button>("RestoreButton").IsEnabled = backend.HasOriginalBackup;
            Find<Button>("LoadPresetButton").IsEnabled = File.Exists(MyPresetPath);
            RefreshStatus();
        }
        private void MarkDirty()
        {
            if (loading || loaded == null) return;
            Find<TextBlock>("DirtyLabel").Text = "有修改 · 待应用";
            Find<TextBlock>("DirtyLabel").Foreground = Brush("#B07D3E");
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
            foreach (string name in new[] { "PresetOcean", "PresetIce", "PresetSunset", "PresetMint" }) Find<Button>(name).BorderBrush = Brush("#D8D0C4");
            string active = startColor == "#12EDF7" && endColor == "#F50BB9" ? "PresetOcean" : startColor == "#78E7FF" && endColor == "#4D79FF" ? "PresetIce" : startColor == "#FFCF70" && endColor == "#FF6A88" ? "PresetSunset" : startColor == "#4DE2C6" && endColor == "#85CAFF" ? "PresetMint" : null;
            if (active != null) Find<Button>(active).BorderBrush = Brush("#6F8F7C");
        }
        private void ApplyLightTitleBar()
        {
            try
            {
                IntPtr handle = new WindowInteropHelper(Window).Handle;
                if (handle == IntPtr.Zero) return;
                int light = 0; Native.DwmSetWindowAttribute(handle, 20, ref light, 4);
                // DWM resets some of these when the window activates; apply again.
                int caption = 0x00EBF1F4; Native.DwmSetWindowAttribute(handle, 35, ref caption, 4);
                int captionText = 0x001B1B1B; Native.DwmSetWindowAttribute(handle, 36, ref captionText, 4);
                int border = 0x00C4D0D8; Native.DwmSetWindowAttribute(handle, 34, ref border, 4);
            }
            catch { }
        }
        private void RestoreDesktop()
        {
            MessageBoxResult answer = MessageBox.Show(Window,
                "恢复原桌面会停止 Nexus 和频谱，并恢复安装前的桌面图标、任务栏和壁纸设置。\n\n是否继续？",
                "恢复原桌面", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;
            string script = Path.Combine(root, "Restore-Desktop.ps1");
            Button button = Find<Button>("RestoreDesktopButton");
            if (!File.Exists(script))
            {
                Feedback("恢复脚本缺失，请重新安装完整包。", true);
                return;
            }
            try
            {
                bool keepPanel = false;
                try { keepPanel = File.ReadAllText(script).IndexOf("KeepPanel", StringComparison.OrdinalIgnoreCase) >= 0; } catch { }
                ProcessStartInfo start = new ProcessStartInfo("powershell.exe",
                    "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + script + "\" -InstallDir \"" + root + "\"" + (keepPanel ? " -KeepPanel" : ""));
                start.UseShellExecute = false;
                start.CreateNoWindow = true;
                Process process = Process.Start(start);
                if (process == null) throw new InvalidOperationException("无法启动恢复脚本。");
                button.IsEnabled = false;
                if (!keepPanel)
                {
                    // Older installs do not know -KeepPanel; their restore script will close this window.
                    Window.Close();
                    return;
                }
                Feedback("正在恢复原桌面，请稍候…", false);
                process.EnableRaisingEvents = true;
                process.Exited += delegate
                {
                    Window.Dispatcher.BeginInvoke(new Action(delegate
                    {
                        try
                        {
                            button.IsEnabled = true;
                            if (process.ExitCode == 0)
                            {
                                Feedback("已恢复原桌面；Nexus 和频谱已停止", false);
                                RefreshStatus();
                                if (nexusPage) RefreshNexusStatus();
                            }
                            else Feedback("恢复原桌面未完成，退出代码 " + process.ExitCode, true);
                        }
                        finally { process.Dispose(); }
                    }));
                };
            }
            catch (Exception ex)
            {
                button.IsEnabled = true;
                Feedback("无法恢复原桌面：" + ex.Message, true);
            }
        }
        private void RefreshStatus()
        {
            try
            {
                SpectrumStatus status = backend.GetStatus();
                string text = !status.Running ? "频谱未运行" : !status.SkinActive ? "频谱未加载" : status.Visible ? "频谱运行中" : "频谱已隐藏";
                Find<TextBlock>("RuntimeStatus").Text = text;
                Find<Ellipse>("StatusDot").Fill = Brush(status.Visible ? "#5B7A69" : "#B58A4A");
                Find<Button>("StartButton").Visibility = status.Running ? Visibility.Collapsed : Visibility.Visible;
            }
            catch { Find<TextBlock>("RuntimeStatus").Text = "暂时无法读取状态"; }
        }
        private void Feedback(string message, bool error)
        { Find<TextBlock>("FeedbackText").Text = message; Find<TextBlock>("FeedbackText").Foreground = Brush(error ? "#A33A34" : "#6E6A63"); }
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
            if (previewLine == null || previewCanvasWidth != w || previewCanvasHeight != h)
            {
                canvas.Children.Clear();
                previewCanvasWidth = w; previewCanvasHeight = h;
                for (int i = 1; i <= 4; i++) canvas.Children.Add(new Line { X1 = 0, X2 = w, Y1 = h * i / 5, Y2 = h * i / 5, Stroke = Brush("#142D42"), StrokeThickness = 1 });
                for (int i = 1; i <= 5; i++) canvas.Children.Add(new Line { X1 = w * i / 6, X2 = w * i / 6, Y1 = 0, Y2 = h, Stroke = Brush("#102A40"), StrokeThickness = 1 });
                previewGlow = new System.Windows.Shapes.Path { StrokeThickness = 12, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
                previewLine = new System.Windows.Shapes.Path { StrokeThickness = 2.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
                canvas.Children.Add(previewGlow); canvas.Children.Add(previewLine);
                previewGradientKey = null;
            }
            double width = Find<Slider>("WidthSlider").Value, height = Find<Slider>("HeightSlider").Value;
            double amplitude = Math.Min(h * 0.6, height * 0.45);
            double waveWidth = (w - 28) * Math.Min(1, Math.Max(0.32, width / 1000));
            double[] levels = preview.Step(previewSeconds, previewPaused ? 0 : 0.04,
                Find<Slider>("SensitivitySlider").Value, Find<Slider>("SmoothingSlider").Value,
                Find<CheckBox>("PreviewSilenceCheck").IsChecked == true);
            StreamGeometry geometry = new StreamGeometry();
            using (StreamGeometryContext context = geometry.Open())
            {
                for (int i = 0; i <= 100; i++)
                {
                    double t = i / 100.0;
                    double y = h * 0.72 - levels[i] * amplitude;
                    Point point = new Point((w - waveWidth) / 2 + t * waveWidth, y);
                    if (i == 0) context.BeginFigure(point, false, false); else context.LineTo(point, true, false);
                }
            }
            geometry.Freeze();
            if (previewGradientKey != startColor + endColor)
            {
                var gradient = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(startColor), (Color)ColorConverter.ConvertFromString(endColor), new Point(0, 0.5), new Point(1, 0.5));
                gradient.Freeze();
                previewLine.Stroke = gradient; previewGlow.Stroke = gradient;
                previewGradientKey = startColor + endColor;
            }
            double alpha = Find<Slider>("OpacitySlider").Value / 100;
            previewLine.Data = geometry; previewGlow.Data = geometry;
            previewLine.Opacity = alpha; previewGlow.Opacity = alpha * 0.1;
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
