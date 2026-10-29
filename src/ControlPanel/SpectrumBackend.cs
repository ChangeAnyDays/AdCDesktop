using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace SpectrumControl
{
    public sealed class SpectrumSettings
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public double Sensitivity { get; set; }
        public int Smoothing { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Opacity { get; set; }
        public string StartColor { get; set; }
        public string EndColor { get; set; }
        public bool Visible { get; set; }
        public bool ClickThrough { get; set; }
        public int HoverMode { get; set; }
        public int RawAlpha { get; set; }
        public int OriginalOpacity { get; set; }
        public bool HoverHide
        {
            get { return HoverMode != 0; }
            set { if (value != (HoverMode != 0)) HoverMode = value ? 3 : 0; }
        }

        public SpectrumSettings()
        {
            Width = 700; Height = 60; Sensitivity = 45; Smoothing = 0;
            X = 610; Y = 879; Opacity = 100; RawAlpha = 255; OriginalOpacity = 100;
            StartColor = "#12EDF7"; EndColor = "#F50BB9";
            Visible = true; ClickThrough = true; HoverMode = 0;
        }

        public SpectrumSettings Clone() { return (SpectrumSettings)MemberwiseClone(); }
    }

    public sealed class SpectrumStatus
    {
        public bool Running { get; set; }
        public bool SkinActive { get; set; }
        public bool Visible { get; set; }
        public int ProcessId { get; set; }
        public string Message { get; set; }
    }

    public sealed class SpectrumBackend
    {
        public const string SkinName = "RetrOcOlOr\\Visualizer";
        private readonly object sync = new object();
        private readonly string root;
        private readonly bool integrationEnabled;
        private readonly string executable;
        private readonly string skinFile;
        private readonly string variablesFile;
        private readonly string rainmeterFile;
        private readonly string backupRoot;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public SpectrumBackend(string rootPath) : this(rootPath, true) { }
        public SpectrumBackend(string rootPath, bool integrationEnabled)
        {
            if (String.IsNullOrWhiteSpace(rootPath)) throw new ArgumentException("请提供桌面程序目录。", "rootPath");
            root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar);
            this.integrationEnabled = integrationEnabled;
            executable = Path.Combine(root, "Rainmeter", "Rainmeter.exe");
            rainmeterFile = Path.Combine(root, "Rainmeter", "Rainmeter.ini");
            skinFile = Path.Combine(root, "Rainmeter", "Skins", "RetrOcOlOr", "Visualizer", "Visualizer.ini");
            variablesFile = Path.Combine(root, "Rainmeter", "Skins", "RetrOcOlOr", "@Resources", "Variables.inc");
            backupRoot = Path.Combine(root, "SpectrumControl", "Backups");
        }

        public bool HasOriginalBackup
        {
            get
            {
                string folder = Path.Combine(backupRoot, "original");
                return File.Exists(Path.Combine(folder, "complete.txt")) &&
                    BackupNames.All(delegate(string n) { return File.Exists(Path.Combine(folder, n)); });
            }
        }

        private static readonly string[] BackupNames = { "Visualizer.ini", "Variables.inc", "Rainmeter.ini" };
        private string[] TargetFiles { get { return new string[] { skinFile, variablesFile, rainmeterFile }; } }

        public SpectrumSettings Read()
        {
            lock (sync) { return ReadSettings(IniDocument.Load(skinFile), IniDocument.Load(variablesFile), IniDocument.Load(rainmeterFile)); }
        }

        private static SpectrumSettings ReadSettings(IniDocument skin, IniDocument variables, IniDocument settings)
        {
            SpectrumSettings s = new SpectrumSettings();
            s.Width = Integer(skin.Get("Variables", "BarWidth", "700"), 700);
            s.Height = Integer(skin.Get("Variables", "BarHeight", "60"), 60);
            s.Sensitivity = Number(skin.Get("MeasureAudio", "Sensitivity", "45"), 45);
            s.Smoothing = Integer(skin.Get("MeasureAudio", "FFTDecay", "0"), 0);
            s.StartColor = ColorFromIni(variables.Get("Variables", "Color2", "18,237,247"));
            s.EndColor = ColorFromIni(variables.Get("Variables", "Color3", "245,11,185"));
            int width = SystemInformation.PrimaryMonitorSize.Width;
            int height = SystemInformation.PrimaryMonitorSize.Height;
            s.X = Coordinate(settings.Get(SkinName, "WindowX", "610"), width, 610);
            s.Y = Coordinate(settings.Get(SkinName, "WindowY", "879"), height, 879);
            s.RawAlpha = Math.Max(0, Math.Min(255, Integer(settings.Get(SkinName, "AlphaValue", "255"), 255)));
            s.Opacity = (int)Math.Round(s.RawAlpha * 100.0 / 255.0, MidpointRounding.AwayFromZero);
            s.OriginalOpacity = s.Opacity;
            s.Visible = Integer(settings.Get(SkinName, "Active", "0"), 0) != 0 && Integer(settings.Get(SkinName, "StartHidden", "0"), 0) == 0;
            s.ClickThrough = Integer(settings.Get(SkinName, "ClickThrough", "0"), 0) != 0;
            s.HoverMode = Math.Max(0, Math.Min(3, Integer(settings.Get(SkinName, "OnHover", "0"), 0)));
            return s;
        }

        public SpectrumStatus GetStatus()
        {
            SpectrumStatus status = new SpectrumStatus();
            if (!integrationEnabled)
            {
                status.Message = "配置预览模式";
                return status;
            }
            Process process = FindProcess();
            if (process == null)
            {
                status.Message = "频谱程序未运行";
                return status;
            }
            using (process)
            {
                status.Running = true;
                status.ProcessId = process.Id;
                IntPtr tray = Native.FindWindowForProcess("RainmeterTrayClass", process.Id);
                IntPtr skin = tray == IntPtr.Zero ? IntPtr.Zero : Native.CopyData(tray, 5101, SkinName, false);
                status.SkinActive = skin != IntPtr.Zero;
                status.Visible = status.SkinActive && Native.IsWindowVisible(skin);
                status.Message = status.Visible ? "频谱正在显示" : (status.SkinActive ? "频谱已隐藏" : "频谱未加载");
            }
            return status;
        }

        public void EnsureRunning()
        {
            if (!integrationEnabled) return;
            if (!File.Exists(executable)) throw new FileNotFoundException("找不到频谱程序，请检查桌面程序目录。", executable);
            Process running = FindProcess();
            if (running != null)
            {
                using (running) { if (Native.FindWindowForProcess("DummyRainWClass", running.Id) != IntPtr.Zero) return; }
            }
            else
            {
                foreach (Process other in Process.GetProcessesByName("Rainmeter"))
                {
                    using (other)
                    {
                        if (!other.HasExited)
                            throw new InvalidOperationException("另一套 Rainmeter 正在运行。请先退出它，再启动当前安装的频谱；原有桌面设置会保留。");
                    }
                }
                ProcessStartInfo start = new ProcessStartInfo(executable);
                start.WorkingDirectory = Path.GetDirectoryName(executable);
                start.UseShellExecute = false;
                start.CreateNoWindow = true;
                start.WindowStyle = ProcessWindowStyle.Hidden;
                using (Process process = Process.Start(start))
                {
                    if (process == null) throw new InvalidOperationException("无法启动频谱程序。");
                }
            }
            DateTime until = DateTime.UtcNow.AddSeconds(7);
            while (DateTime.UtcNow < until)
            {
                running = FindProcess();
                if (running != null)
                {
                    using (running)
                    {
                        if (Native.FindWindowForProcess("DummyRainWClass", running.Id) != IntPtr.Zero) return;
                    }
                }
                Thread.Sleep(80);
            }
            throw new InvalidOperationException("频谱程序启动较慢，请稍后再点应用。");
        }

        public void Apply(SpectrumSettings settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            lock (sync)
            {
                SpectrumSettings desired = settings.Clone();
                Validate(desired);
                if (integrationEnabled && desired.Visible) EnsureRunning();
                string[] targets = TargetFiles;
                IniDocument[] documents = targets.Select(IniDocument.Load).ToArray();
                byte[][] originals = documents.Select(delegate(IniDocument d) { return d.OriginalBytes; }).ToArray();
                SpectrumSettings before = ReadSettings(documents[0], documents[1], documents[2]);
                bool hoverChanged = before.HoverMode != desired.HoverMode;
                int alpha = desired.Opacity == desired.OriginalOpacity && desired.RawAlpha >= 0 && desired.RawAlpha <= 255
                    ? desired.RawAlpha : (int)Math.Round(desired.Opacity * 255.0 / 100.0, MidpointRounding.AwayFromZero);
                documents[0].Set("Variables", "BarWidth", desired.Width.ToString(Inv));
                documents[0].Set("Variables", "BarHeight", desired.Height.ToString(Inv));
                documents[0].Set("MeasureAudio", "Sensitivity", desired.Sensitivity.ToString("0.###", Inv));
                documents[0].Set("MeasureAudio", "FFTDecay", desired.Smoothing.ToString(Inv));
                documents[1].Set("Variables", "Color2", ColorToIni(desired.StartColor));
                documents[1].Set("Variables", "Color3", ColorToIni(desired.EndColor));
                documents[2].Set(SkinName, "WindowX", desired.X.ToString(Inv));
                documents[2].Set(SkinName, "WindowY", desired.Y.ToString(Inv));
                documents[2].Set(SkinName, "AnchorX", "0");
                documents[2].Set(SkinName, "AnchorY", "0");
                documents[2].Set(SkinName, "AlphaValue", alpha.ToString(Inv));
                documents[2].Set(SkinName, "ClickThrough", desired.ClickThrough ? "1" : "0");
                documents[2].Set(SkinName, "OnHover", desired.HoverMode.ToString(Inv));
                documents[2].Set(SkinName, "Active", desired.Visible ? "1" : "0");
                documents[2].Set(SkinName, "StartHidden", "0");
                byte[][] updates = documents.Select(delegate(IniDocument d) { return d.GetBytes(); }).ToArray();
                if (!HasOriginalBackup) SaveBackup("original", originals);
                SaveBackup("last", originals);
                try
                {
                    WriteTransaction(targets, updates);
                    if (integrationEnabled) ApplyToRunning(desired, alpha, hoverChanged);
                }
                catch (Exception error)
                {
                    string rollbackError = "";
                    try { WriteTransaction(targets, originals); }
                    catch (Exception restoreError) { rollbackError = " 配置回退失败：" + restoreError.Message; }
                    if (integrationEnabled)
                    {
                        try { ApplyToRunning(before, before.RawAlpha, true); }
                        catch { }
                    }
                    throw new InvalidOperationException("修改未完成，已尝试恢复修改前的配置。" + rollbackError, error);
                }
            }
        }

        public void RestoreOriginal()
        {
            lock (sync)
            {
                if (!HasOriginalBackup) throw new InvalidOperationException("尚未应用过修改，没有需要恢复的原始设置。");
                string folder = Path.Combine(backupRoot, "original");
                SpectrumSettings original = ReadSettings(
                    IniDocument.Load(Path.Combine(folder, BackupNames[0])),
                    IniDocument.Load(Path.Combine(folder, BackupNames[1])),
                    IniDocument.Load(Path.Combine(folder, BackupNames[2])));
                Apply(original);
            }
        }

        private void SaveBackup(string name, byte[][] files)
        {
            string folder = Path.Combine(backupRoot, name);
            Directory.CreateDirectory(folder);
            for (int i = 0; i < files.Length; i++) AtomicWrite(Path.Combine(folder, BackupNames[i]), files[i]);
            AtomicWrite(Path.Combine(folder, "complete.txt"), Encoding.UTF8.GetBytes(DateTime.UtcNow.ToString("o", Inv)));
        }

        private static void WriteTransaction(string[] files, byte[][] content)
        {
            byte[][] previous = files.Select(File.ReadAllBytes).ToArray();
            int completed = 0;
            try
            {
                for (int i = 0; i < files.Length; i++) { AtomicWrite(files[i], content[i]); completed++; }
            }
            catch
            {
                for (int i = completed - 1; i >= 0; i--)
                {
                    try { AtomicWrite(files[i], previous[i]); } catch { }
                }
                throw;
            }
        }

        private static void AtomicWrite(string path, byte[] bytes)
        {
            string temp = path + ".spectrum-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    try
                    {
                        if (!File.Exists(temp))
                        {
                            using (FileStream stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                            { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                        }
                        if (File.Exists(path)) File.Replace(temp, path, null);
                        else File.Move(temp, path);
                        return;
                    }
                    catch (IOException error)
                    {
                        if (!IsTransientFileError(error) || attempt == 7) throw;
                        Thread.Sleep(40 * (attempt + 1));
                    }
                }
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        private static bool IsTransientFileError(IOException error)
        {
            int code = error.HResult & 0xffff;
            // Sharing/locking violations, and atomic-replacement races reported by ReplaceFile.
            return code == 32 || code == 33 || code == 1175 || code == 1176 || code == 1177;
        }

        private void ApplyToRunning(SpectrumSettings desired, int alpha, bool hoverChanged)
        {
            Process process = FindProcess();
            if (process == null)
            {
                if (desired.Visible) throw new InvalidOperationException("频谱程序已退出，请重新应用。");
                return;
            }
            using (process)
            {
                IntPtr control = Native.FindWindowForProcess("DummyRainWClass", process.Id);
                if (control == IntPtr.Zero) throw new InvalidOperationException("频谱程序还未准备好，请稍后重试。");
                string config = Quote(SkinName);
                StringBuilder bang = new StringBuilder();
                if (hoverChanged) bang.Append("[!RefreshApp]");
                if (!desired.Visible) bang.Append("[!DeactivateConfig ").Append(config).Append("]");
                else
                {
                    bang.Append("[!ActivateConfig ").Append(config).Append(" \"Visualizer.ini\"]");
                    bang.Append("[!Refresh ").Append(config).Append("]");
                    bang.Append("[!SetWindowPosition ").Append(desired.X.ToString(Inv)).Append(" ")
                        .Append(desired.Y.ToString(Inv)).Append(" 0 0 ").Append(config).Append("]");
                    bang.Append("[!SetTransparency ").Append(alpha.ToString(Inv)).Append(" ").Append(config).Append("]");
                    bang.Append("[!ClickThrough ").Append(desired.ClickThrough ? "1" : "0").Append(" ").Append(config).Append("]");
                    bang.Append("[!Show ").Append(config).Append("]");
                }
                Native.CopyData(control, 1, bang.ToString(), true);
                // Rainmeter persists these same fields while processing the bangs. Let it
                // settle, and avoid replacing a file it has already saved correctly.
                Dictionary<string, string> expected = new Dictionary<string, string>();
                expected.Add("WindowX", desired.X.ToString(Inv)); expected.Add("WindowY", desired.Y.ToString(Inv));
                expected.Add("AnchorX", "0"); expected.Add("AnchorY", "0");
                expected.Add("AlphaValue", alpha.ToString(Inv));
                expected.Add("ClickThrough", desired.ClickThrough ? "1" : "0");
                expected.Add("OnHover", desired.HoverMode.ToString(Inv));
                expected.Add("Active", desired.Visible ? "1" : "0"); expected.Add("StartHidden", "0");
                int consistentReads = 0;
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    IniDocument current;
                    try { current = IniDocument.Load(rainmeterFile); }
                    catch (IOException error)
                    {
                        if (!IsTransientFileError(error) || attempt == 7) throw;
                        consistentReads = 0; Thread.Sleep(60); continue;
                    }
                    if (MatchesSkinSettings(current, expected))
                    {
                        consistentReads++;
                        if (consistentReads >= 2 || attempt == 7) return;
                    }
                    else
                    {
                        consistentReads = 0;
                        if (attempt == 7)
                        {
                            foreach (KeyValuePair<string, string> item in expected) current.Set(SkinName, item.Key, item.Value);
                            AtomicWrite(rainmeterFile, current.GetBytes());
                            return;
                        }
                    }
                    Thread.Sleep(60);
                }
            }
        }

        private static bool MatchesSkinSettings(IniDocument document, Dictionary<string, string> expected)
        {
            foreach (KeyValuePair<string, string> item in expected)
            {
                string defaultValue = item.Key == "AlphaValue" ? "255" : "0";
                if (!String.Equals(document.Get(SkinName, item.Key, defaultValue), item.Value, StringComparison.Ordinal)) return false;
            }
            return true;
        }

        private Process FindProcess()
        {
            foreach (Process process in Process.GetProcessesByName("Rainmeter"))
            {
                try
                {
                    string path = process.MainModule.FileName;
                    if (String.Equals(Path.GetFullPath(path), executable, StringComparison.OrdinalIgnoreCase)) return process;
                }
                catch { }
                process.Dispose();
            }
            return null;
        }

        private static string Quote(string value) { return "\"" + value.Replace("\"", "") + "\""; }
        private static int Integer(string value, int fallback)
        { int result; return Int32.TryParse(value.Trim(), NumberStyles.Integer, Inv, out result) ? result : fallback; }
        private static double Number(string value, double fallback)
        { double result; return Double.TryParse(value.Trim(), NumberStyles.Float, Inv, out result) && !Double.IsNaN(result) && !Double.IsInfinity(result) ? result : fallback; }
        private static int Coordinate(string value, int dimension, int fallback)
        {
            Match match = Regex.Match(value.Trim(), @"^(?<n>-?\d+(?:\.\d+)?)(?<p>%?)(?<edge>[RB]?)(?:@\d+)?$", RegexOptions.IgnoreCase);
            if (!match.Success) return fallback;
            double number = Number(match.Groups["n"].Value, fallback);
            if (match.Groups["p"].Value == "%") number = number * dimension / 100.0;
            if (match.Groups["edge"].Value.Length != 0) number = dimension - number;
            return (int)Math.Round(number, MidpointRounding.AwayFromZero);
        }

        public static void Validate(SpectrumSettings s)
        {
            if (s == null) throw new ArgumentNullException("s");
            if (s.Width < 100 || s.Width > 4096) throw new ArgumentOutOfRangeException("Width", "宽度应在 100–4096 像素之间。");
            if (s.Height < 10 || s.Height > 600) throw new ArgumentOutOfRangeException("Height", "高度应在 10–600 像素之间。");
            if (Double.IsNaN(s.Sensitivity) || Double.IsInfinity(s.Sensitivity) || s.Sensitivity < 0 || s.Sensitivity > 100) throw new ArgumentOutOfRangeException("Sensitivity", "灵敏度应在 0–100 之间。");
            if (s.Smoothing < 0 || s.Smoothing > 1000) throw new ArgumentOutOfRangeException("Smoothing", "平滑时间应在 0–1000 毫秒之间。");
            if (s.X < -32768 || s.X > 32767 || s.Y < -32768 || s.Y > 32767) throw new ArgumentOutOfRangeException("X", "位置超出了支持的屏幕范围。");
            if (s.Opacity < 0 || s.Opacity > 100) throw new ArgumentOutOfRangeException("Opacity", "不透明度应在 0–100% 之间。");
            if (s.HoverMode < 0 || s.HoverMode > 3) throw new ArgumentOutOfRangeException("HoverMode", "请选择有效的鼠标经过效果。");
            if (!Regex.IsMatch(s.StartColor ?? "", @"^#[0-9A-Fa-f]{6}$") || !Regex.IsMatch(s.EndColor ?? "", @"^#[0-9A-Fa-f]{6}$")) throw new ArgumentException("颜色应为 #RRGGBB，例如 #12EDF7。");
        }

        private static string ColorFromIni(string value)
        {
            string[] parts = value.Split(',');
            if (parts.Length < 3) return "#FFFFFF";
            int red = Math.Max(0, Math.Min(255, Integer(parts[0], 255)));
            int green = Math.Max(0, Math.Min(255, Integer(parts[1], 255)));
            int blue = Math.Max(0, Math.Min(255, Integer(parts[2], 255)));
            return "#" + red.ToString("X2", Inv) + green.ToString("X2", Inv) + blue.ToString("X2", Inv);
        }
        private static string ColorToIni(string value)
        {
            return Int32.Parse(value.Substring(1, 2), NumberStyles.HexNumber, Inv).ToString(Inv) + "," +
                Int32.Parse(value.Substring(3, 2), NumberStyles.HexNumber, Inv).ToString(Inv) + "," +
                Int32.Parse(value.Substring(5, 2), NumberStyles.HexNumber, Inv).ToString(Inv);
        }

        private static class Native
        {
            private delegate bool EnumCallback(IntPtr hwnd, IntPtr parameter);
            [StructLayout(LayoutKind.Sequential)]
            private struct COPYDATASTRUCT { public IntPtr Data; public int Length; public IntPtr Text; }
            [DllImport("user32.dll")]
            private static extern bool EnumWindows(EnumCallback callback, IntPtr parameter);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int count);
            [DllImport("user32.dll")]
            private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
            [DllImport("user32.dll", SetLastError = true)]
            private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, IntPtr wParam, ref COPYDATASTRUCT data, uint flags, uint timeout, out IntPtr result);
            [DllImport("user32.dll")]
            public static extern bool IsWindowVisible(IntPtr hwnd);

            public static IntPtr FindWindowForProcess(string className, int processId)
            {
                IntPtr found = IntPtr.Zero;
                EnumWindows(delegate(IntPtr hwnd, IntPtr parameter)
                {
                    uint owner; GetWindowThreadProcessId(hwnd, out owner);
                    if (owner == (uint)processId)
                    {
                        StringBuilder name = new StringBuilder(256); GetClassName(hwnd, name, name.Capacity);
                        if (String.Equals(name.ToString(), className, StringComparison.Ordinal)) { found = hwnd; return false; }
                    }
                    return true;
                }, IntPtr.Zero);
                return found;
            }

            public static IntPtr CopyData(IntPtr hwnd, int operation, string text, bool throwOnTimeout)
            {
                IntPtr buffer = Marshal.StringToHGlobalUni(text);
                try
                {
                    COPYDATASTRUCT data = new COPYDATASTRUCT();
                    data.Data = new IntPtr(operation); data.Length = (text.Length + 1) * 2; data.Text = buffer;
                    IntPtr result;
                    IntPtr sent = SendMessageTimeout(hwnd, 0x004A, IntPtr.Zero, ref data, 0x0002, 5000, out result);
                    if (sent == IntPtr.Zero && throwOnTimeout) throw new InvalidOperationException("频谱程序暂未响应，请稍后重试。");
                    return sent == IntPtr.Zero ? IntPtr.Zero : result;
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
        }

        private sealed class IniDocument
        {
            private sealed class Line { public string Body; public string Ending; public Line(string body, string ending) { Body = body; Ending = ending; } }
            private readonly List<Line> lines = new List<Line>();
            private readonly Encoding encoding;
            private readonly byte[] preamble;
            private readonly string newline;
            public readonly byte[] OriginalBytes;

            private IniDocument(byte[] bytes)
            {
                OriginalBytes = bytes;
                int skip = 0;
                if (bytes.Length >= 4 && bytes[0] == 0xff && bytes[1] == 0xfe && bytes[2] == 0 && bytes[3] == 0) { encoding = new UTF32Encoding(false, false); skip = 4; }
                else if (bytes.Length >= 4 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0xfe && bytes[3] == 0xff) { encoding = new UTF32Encoding(true, false); skip = 4; }
                else if (bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf) { encoding = new UTF8Encoding(false); skip = 3; }
                else if (bytes.Length >= 2 && bytes[0] == 0xff && bytes[1] == 0xfe) { encoding = new UnicodeEncoding(false, false); skip = 2; }
                else if (bytes.Length >= 2 && bytes[0] == 0xfe && bytes[1] == 0xff) { encoding = new UnicodeEncoding(true, false); skip = 2; }
                else
                {
                    try { new UTF8Encoding(false, true).GetString(bytes); encoding = new UTF8Encoding(false); }
                    catch (DecoderFallbackException) { encoding = Encoding.Default; }
                }
                preamble = bytes.Take(skip).ToArray();
                string content = encoding.GetString(bytes, skip, bytes.Length - skip);
                foreach (Match match in Regex.Matches(content, @"([^\r\n]*)(\r\n|\r|\n|$)"))
                {
                    if (match.Length == 0) continue;
                    lines.Add(new Line(match.Groups[1].Value, match.Groups[2].Value));
                }
                Line firstEnding = lines.FirstOrDefault(delegate(Line line) { return line.Ending.Length != 0; });
                newline = firstEnding == null ? "\r\n" : firstEnding.Ending;
            }

            public static IniDocument Load(string path)
            {
                if (!File.Exists(path)) throw new FileNotFoundException("找不到频谱配置文件。", path);
                return new IniDocument(File.ReadAllBytes(path));
            }

            private static string Section(string line)
            {
                Match match = Regex.Match(line, @"^\s*\[([^\]]+)\]\s*(?:;.*)?$");
                return match.Success ? match.Groups[1].Value.Trim() : null;
            }
            private static bool Key(string line, string key, out string prefix, out string value)
            {
                Match match = Regex.Match(line, @"^(\s*([^;=]+?)\s*=\s*)(.*)$");
                if (match.Success && String.Equals(match.Groups[2].Value.Trim(), key, StringComparison.OrdinalIgnoreCase))
                { prefix = match.Groups[1].Value; value = match.Groups[3].Value; return true; }
                prefix = value = null; return false;
            }
            public string Get(string section, string key, string fallback)
            {
                string current = null;
                foreach (Line line in lines)
                {
                    string heading = Section(line.Body);
                    if (heading != null) { current = heading; continue; }
                    string prefix, value;
                    if (String.Equals(current, section, StringComparison.OrdinalIgnoreCase) && Key(line.Body, key, out prefix, out value)) return value.Trim();
                }
                return fallback;
            }
            public void Set(string section, string key, string value)
            {
                int sectionIndex = -1, endIndex = lines.Count;
                for (int i = 0; i < lines.Count; i++)
                {
                    string heading = Section(lines[i].Body);
                    if (heading != null)
                    {
                        if (sectionIndex >= 0) { endIndex = i; break; }
                        if (String.Equals(heading, section, StringComparison.OrdinalIgnoreCase)) sectionIndex = i;
                    }
                }
                if (sectionIndex < 0)
                {
                    if (lines.Count != 0 && lines[lines.Count - 1].Ending.Length == 0) lines[lines.Count - 1].Ending = newline;
                    if (lines.Count != 0 && lines[lines.Count - 1].Body.Length != 0) lines.Add(new Line("", newline));
                    lines.Add(new Line("[" + section + "]", newline)); lines.Add(new Line(key + "=" + value, newline)); return;
                }
                bool changed = false;
                for (int i = sectionIndex + 1; i < endIndex; i++)
                {
                    string prefix, oldValue;
                    if (Key(lines[i].Body, key, out prefix, out oldValue)) { lines[i].Body = prefix + value; changed = true; }
                }
                if (changed) return;
                int insert = endIndex;
                while (insert > sectionIndex + 1 && String.IsNullOrWhiteSpace(lines[insert - 1].Body)) insert--;
                if (insert > 0 && lines[insert - 1].Ending.Length == 0) lines[insert - 1].Ending = newline;
                lines.Insert(insert, new Line(key + "=" + value, newline));
            }
            public byte[] GetBytes()
            {
                StringBuilder text = new StringBuilder();
                foreach (Line line in lines) text.Append(line.Body).Append(line.Ending);
                byte[] body = encoding.GetBytes(text.ToString());
                byte[] bytes = new byte[preamble.Length + body.Length];
                Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length); Buffer.BlockCopy(body, 0, bytes, preamble.Length, body.Length);
                return bytes;
            }
        }
    }
}
