using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using Microsoft.Win32;

namespace SpectrumControl
{
    [DataContract]
    public sealed class NexusValue
    {
        [DataMember] public string Key;
        [DataMember] public string Name;
        [DataMember] public string Value;
        [DataMember] public bool Dword;
        public NexusValue Copy() { return (NexusValue)MemberwiseClone(); }
    }

    public interface INexusStore
    {
        List<NexusValue> Read();
        void Write(NexusValue value);
    }

    // Nexus Free 26.9 stores its per-user preferences in the 32-bit registry view.
    // Only existing, recognized values are edited; no Dock or application is created.
    public sealed class NexusRegistryStore : INexusStore
    {
        private const string Profile = @"Software\WinSTEP2000\NeXuS";
        public List<NexusValue> Read()
        {
            var values = new List<NexusValue>();
            using (RegistryKey user = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry32))
            using (RegistryKey profile = user.OpenSubKey(Profile))
            {
                if (profile == null) return values;
                ReadValue(profile, "", "ShowIconTooltips", values);
                using (RegistryKey docks = profile.OpenSubKey("Docks"))
                    if (docks != null) ReadDocks(docks, "Docks", values, 0);
            }
            return values;
        }
        private static void ReadDocks(RegistryKey key, string path, List<NexusValue> values, int depth)
        {
            // Nexus 26.9 stores DockShowLabels1, DockShowLabels2, ... directly
            // under Docks. Older layouts can use DockShowLabels in a dock subkey.
            foreach (string name in key.GetValueNames())
                if (NexusSettingsService.IsLabelName(name)) ReadValue(key, path, name, values);
            if (depth >= 3) return;
            foreach (string child in key.GetSubKeyNames())
                using (RegistryKey sub = key.OpenSubKey(child))
                    if (sub != null) ReadDocks(sub, path + "\\" + child, values, depth + 1);
        }
        private static void ReadValue(RegistryKey key, string path, string name, List<NexusValue> values)
        {
            object value = key.GetValue(name);
            if (value == null) return;
            RegistryValueKind kind = key.GetValueKind(name);
            if (kind != RegistryValueKind.String && kind != RegistryValueKind.DWord)
                throw new InvalidOperationException("Nexus 文字设置格式不兼容，请使用详细设置。\n" + name);
            values.Add(new NexusValue { Key = path, Name = name, Value = Convert.ToString(value, CultureInfo.InvariantCulture), Dword = kind == RegistryValueKind.DWord });
        }
        public void Write(NexusValue value)
        {
            if (!NexusSettingsService.IsAllowed(value)) throw new InvalidDataException("拒绝写入未知 Nexus 设置。");
            using (RegistryKey user = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry32))
            using (RegistryKey key = user.OpenSubKey(Profile + (value.Key.Length == 0 ? "" : "\\" + value.Key), true))
            {
                if (key == null || key.GetValue(value.Name) == null) throw new InvalidOperationException("Nexus 配置已变化，请重新读取。");
                key.SetValue(value.Name, value.Dword ? (object)int.Parse(value.Value, CultureInfo.InvariantCulture) : value.Value,
                    value.Dword ? RegistryValueKind.DWord : RegistryValueKind.String);
            }
        }
    }

    public sealed class NexusSettingsService
    {
        private readonly INexusStore store;
        private readonly string backup;
        public NexusSettingsService(INexusStore store, string backupPath) { this.store = store; backup = backupPath; }
        public bool HasBackup { get { return File.Exists(backup); } }
        public static bool IsLabelName(string name)
        {
            const string prefix = "DockShowLabels";
            if (name == prefix) return true;
            if (name == null || !name.StartsWith(prefix, StringComparison.Ordinal)) return false;
            string suffix = name.Substring(prefix.Length);
            if (suffix.Length == 0 || suffix[0] < '1' || suffix[0] > '9') return false;
            for (int i = 1; i < suffix.Length; i++)
                if (suffix[i] < '0' || suffix[i] > '9') return false;
            int index;
            return Int32.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out index) && index > 0;
        }
        public static bool IsAllowed(NexusValue v)
        {
            if (v == null || v.Key == null || v.Key.Contains("..") || v.Key.Contains("/")) return false;
            return (v.Key.Length == 0 && v.Name == "ShowIconTooltips") ||
                ((v.Key == "Docks" || v.Key.StartsWith("Docks\\", StringComparison.Ordinal)) && IsLabelName(v.Name));
        }
        public static int Decode(NexusValue v)
        {
            if (!IsAllowed(v)) throw new InvalidDataException("未知 Nexus 文字设置。");
            bool boolean;
            int number;
            if (!v.Dword && Boolean.TryParse(v.Value, out boolean)) return boolean ? 1 : 0;
            if (Int32.TryParse(v.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number) &&
                (IsLabelName(v.Name) ? number == 0 || number == 1 || number == -1 : number >= 0 && number <= 3)) return number;
            throw new InvalidDataException("Nexus 文字设置格式不兼容，请通过详细设置修改。");
        }
        public List<NexusValue> Read()
        {
            List<NexusValue> values = store.Read();
            bool labels = false, tips = false;
            foreach (NexusValue value in values)
            {
                Decode(value);
                labels |= IsLabelName(value.Name);
                tips |= value.Name == "ShowIconTooltips";
            }
            if (!labels || !tips) throw new InvalidOperationException("尚未发现兼容的 Nexus 文字配置。请先启动 Nexus 并保存一次设置；也可在详细设置中关闭标签和信息气泡。");
            return values;
        }
        public void Apply(bool labels, bool tips)
        {
            List<NexusValue> before = Read();
            var after = new List<NexusValue>();
            foreach (NexusValue value in before)
            {
                NexusValue changed = value.Copy();
                bool enabled = IsLabelName(value.Name) ? labels : tips;
                if (enabled != (Decode(value) != 0))
                {
                    bool parsed;
                    changed.Value = !value.Dword && Boolean.TryParse(value.Value, out parsed) ? (enabled ? "True" : "False") : (enabled ? "1" : "0");
                }
                after.Add(changed);
            }
            if (!HasBackup)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(backup));
                string temporary = backup + ".tmp";
                try
                {
                    using (FileStream stream = File.Create(temporary)) new DataContractJsonSerializer(typeof(List<NexusValue>)).WriteObject(stream, before);
                    File.Move(temporary, backup);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            WriteWithRollback(before, after);
        }
        public void Restore()
        {
            List<NexusValue> before = Read();
            List<NexusValue> saved;
            using (FileStream stream = File.OpenRead(backup)) saved = (List<NexusValue>)new DataContractJsonSerializer(typeof(List<NexusValue>)).ReadObject(stream);
            var after = new List<NexusValue>();
            foreach (NexusValue current in before)
            {
                NexusValue original = saved.Find(delegate(NexusValue item) { return item.Key == current.Key && item.Name == current.Name; });
                if (original == null) continue;
                Decode(original);
                if (original.Dword != current.Dword) throw new InvalidDataException("配置格式已变化，无法恢复，请使用 Nexus 详细设置。");
                after.Add(original);
            }
            if (after.Count == 0) throw new InvalidOperationException("当前配置与备份不匹配。");
            WriteWithRollback(before, after);
        }
        private void WriteWithRollback(List<NexusValue> before, List<NexusValue> after)
        {
            var changed = new List<NexusValue>();
            try
            {
                foreach (NexusValue value in after)
                {
                    NexusValue original = before.Find(delegate(NexusValue item) { return item.Key == value.Key && item.Name == value.Name; });
                    if (original.Value == value.Value && original.Dword == value.Dword) continue;
                    changed.Add(original);
                    store.Write(value);
                }
                List<NexusValue> verified = store.Read();
                foreach (NexusValue value in after)
                    if (!verified.Exists(delegate(NexusValue item) { return item.Key == value.Key && item.Name == value.Name && item.Value == value.Value && item.Dword == value.Dword; }))
                        throw new IOException("Nexus 设置写入后未通过校验。");
            }
            catch (Exception failure)
            {
                var failures = new List<Exception>(); failures.Add(failure);
                for (int i = changed.Count - 1; i >= 0; i--)
                    try { store.Write(changed[i]); } catch (Exception rollback) { failures.Add(rollback); }
                if (failures.Count > 1) throw new AggregateException("写入失败且部分回退失败；请保留备份并用详细设置检查。", failures);
                throw;
            }
        }
    }

    public sealed class NexusBackend
    {
        private readonly string root;
        private readonly bool integration;
        public readonly NexusSettingsService Settings;
        public readonly NexusAppearanceService Appearance;
        private string PathFile { get { return Path.Combine(root, "SpectrumControl", "nexus-path.txt"); } }
        public NexusBackend(string rootPath, bool integrationEnabled)
        {
            root = rootPath; integration = integrationEnabled;
            Settings = new NexusSettingsService(new NexusRegistryStore(), Path.Combine(root, "SpectrumControl", "Backups", "nexus-text-original.json"));
            Appearance = new NexusAppearanceService(Path.Combine(root, "SpectrumControl", "Backups", "nexus-appearance-original.json"));
        }
        public string StartupExecutable
        {
            get { return SelectedExecutable ?? Executable; }
        }
        private string SelectedExecutable
        {
            get
            {
                // A stale nexus-path.txt must not disable both the path display and
                // the startup switch after the user moves or uninstalls Nexus.
                if (!File.Exists(PathFile)) return null;
                try
                {
                    string path = File.ReadAllText(PathFile).Trim();
                    return !String.IsNullOrWhiteSpace(path) && File.Exists(path) ? path : null;
                }
                catch { return null; }
            }
        }
        public string Executable
        {
            get
            {
                string selected = SelectedExecutable;
                if (selected != null) return selected;
                string local = Path.Combine(root, "Nexus", "Nexus.exe");
                if (File.Exists(local)) return local;
                foreach (Environment.SpecialFolder folder in new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles })
                {
                    string basePath = Environment.GetFolderPath(folder);
                    if (String.IsNullOrEmpty(basePath)) continue;
                    string candidate = Path.Combine(basePath, "Winstep", "Nexus.exe");
                    if (File.Exists(candidate)) return candidate;
                }
                return null;
            }
        }
        public void SelectExecutable(string path)
        {
            path = Path.GetFullPath(path);
            if (!File.Exists(path) || !String.Equals(Path.GetFileName(path), "Nexus.exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("请选择已安装的 Nexus.exe。");
            Directory.CreateDirectory(Path.GetDirectoryName(PathFile));
            File.WriteAllText(PathFile, path);
        }
        public bool Running
        {
            get
            {
                if (!integration) return false;
                Process[] processes = Process.GetProcessesByName("Nexus");
                try { return processes.Length > 0; }
                finally { foreach (Process process in processes) process.Dispose(); }
            }
        }
        public bool CanIntegrate { get { return integration; } }
        public void Start(bool preferences)
        {
            if (!integration) throw new InvalidOperationException("测试模式不启动外部程序。");
            string path = Executable;
            if (path == null) throw new FileNotFoundException("未找到 Nexus，请先安装或选择 Nexus.exe。");
            bool samePathRunning = false;
            foreach (Process process in Process.GetProcessesByName("Nexus"))
                using (process)
                {
                    string runningPath;
                    try { runningPath = process.MainModule.FileName; }
                    catch { throw new InvalidOperationException("无法确认正在运行的 Nexus 路径，请先退出它。"); }
                    if (!String.Equals(Path.GetFullPath(runningPath), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("另一目录的 Nexus 正在运行。请选择该 Nexus.exe，或先退出它。");
                    if (!preferences) return;
                    samePathRunning = true;
                }
            // A second Nexus process cannot open the Preferences form; it only leaves
            // a hidden helper window. Require the native Dock menu instead.
            if (preferences && samePathRunning) return;
            // Nexus Free uses the bare "setup" argument, not Ultimate's "-setup".
            // https://forums.winstep.net/phpBB2/viewtopic.php?f=2&t=9854
            using (Process process = Process.Start(new ProcessStartInfo(path, preferences ? "setup" : "") { WorkingDirectory = Path.GetDirectoryName(path), UseShellExecute = true })) { }
        }
        public void Apply(bool labels, bool tips, bool restore)
        {
            if (!integration) throw new InvalidOperationException("测试模式不修改 Nexus 配置。");
            if (Running) throw new InvalidOperationException("请先从 Nexus 图标右键菜单退出 Nexus，再点击应用，避免正在运行的 Nexus 覆盖修改。应用后点击「启动 Nexus」。");
            if (restore) Settings.Restore(); else Settings.Apply(labels, tips);
        }
        public void ApplyAppearance(NexusAppearancePreset preset)
        {
            if (!integration) throw new InvalidOperationException("测试模式不修改 Nexus 配置。");
            if (preset == null) throw new ArgumentNullException("preset");
            string path = Executable;
            if (path == null) throw new FileNotFoundException("未找到 Nexus，请先安装或选择 Nexus.exe。");
            StopForAppearance(path);
            try { Appearance.Apply(preset); }
            catch
            {
                try { Start(false); } catch { }
                throw;
            }
            Start(false);
        }
        public void RestoreAppearance()
        {
            if (!integration) throw new InvalidOperationException("测试模式不修改 Nexus 配置。");
            string path = Executable;
            if (path == null) throw new FileNotFoundException("未找到 Nexus，请先安装或选择 Nexus.exe。");
            StopForAppearance(path);
            try { Appearance.Restore(); }
            catch
            {
                try { Start(false); } catch { }
                throw;
            }
            Start(false);
        }
        private void StopForAppearance(string executable)
        {
            var same = new List<Process>();
            foreach (Process process in Process.GetProcessesByName("Nexus"))
            {
                string runningPath;
                try { runningPath = process.MainModule.FileName; }
                catch
                {
                    process.Dispose();
                    throw new InvalidOperationException("无法确认正在运行的 Nexus 路径，请先手动退出它。");
                }
                if (!String.Equals(Path.GetFullPath(runningPath), Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase))
                {
                    process.Dispose();
                    throw new InvalidOperationException("另一目录的 Nexus 正在运行，请先退出它。");
                }
                same.Add(process);
            }
            try
            {
                foreach (Process process in same)
                {
                    try
                    {
                        if (!process.CloseMainWindow() || !process.WaitForExit(5000))
                        {
                            process.Kill();
                            process.WaitForExit(3000);
                        }
                    }
                    catch (InvalidOperationException) { }
                }
            }
            finally { foreach (Process process in same) process.Dispose(); }
        }
    }
}
