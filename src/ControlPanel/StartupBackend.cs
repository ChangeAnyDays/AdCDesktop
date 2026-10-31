using System;
using System.IO;
using Microsoft.Win32;

namespace SpectrumControl
{
    public interface IStartupStore
    {
        string Read(string name);
        void Write(string name, string command);
        void Delete(string name);
    }

    public sealed class StartupRegistryStore : IStartupStore
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public string Read(string name)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
            {
                if (key == null || key.GetValue(name) == null) return null;
                if (key.GetValueKind(name) != RegistryValueKind.String)
                    throw new InvalidDataException("启动项格式不兼容，请在 Windows 启动应用中检查。");
                return (string)key.GetValue(name);
            }
        }
        public void Write(string name, string command)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
                key.SetValue(name, command, RegistryValueKind.String);
        }
        public void Delete(string name)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                if (key != null) key.DeleteValue(name, false);
        }
    }

    public sealed class StartupState
    {
        public bool Enabled;
        public bool CanChange;
        public string Detail;
    }

    // Each switch owns exactly one HKCU Run value. Never delete unrelated startup entries.
    // The portable Rainmeter executable uses Rainmeter.ini next to itself.
    public sealed class StartupBackend
    {
        public const string SpectrumName = "AdCDesktop.Spectrum";
        public const string NexusName = "AdCDesktop.Nexus";
        private readonly IStartupStore store;
        private readonly bool integration;
        private readonly Func<string, bool> fileExists;
        public StartupBackend(IStartupStore store, bool integration, Func<string, bool> fileExists)
        { this.store = store; this.integration = integration; this.fileExists = fileExists; }
        public StartupBackend(bool integration) : this(new StartupRegistryStore(), integration, File.Exists) { }

        public static string Command(string executable)
        {
            if (String.IsNullOrWhiteSpace(executable) || executable.IndexOfAny(new[] { '"', '\r', '\n', '\0' }) >= 0 || !Path.IsPathRooted(executable))
                throw new ArgumentException("启动程序路径无效，请重新选择安装目录。");
            string command = "\"" + Path.GetFullPath(executable) + "\"";
            if (command.Length > 260) throw new ArgumentException("安装路径过长，Windows 启动项最多支持 260 个字符，请缩短安装路径。");
            return command;
        }
        private static void ValidateName(string name)
        {
            if (name != SpectrumName && name != NexusName) throw new ArgumentException("未知启动组件。");
        }
        public StartupState Read(string name, string executable)
        {
            ValidateName(name);
            if (!integration) return new StartupState { Detail = "测试模式 · 不读取系统启动项" };
            string existing = store.Read(name);
            if (String.IsNullOrWhiteSpace(executable))
                return new StartupState { Enabled = existing != null, CanChange = false, Detail = "请先安装或选择程序路径" };
            string command = Command(executable);
            if (existing != null && !String.Equals(existing, command, StringComparison.OrdinalIgnoreCase))
                return new StartupState { CanChange = false, Detail = "启动项属于其他目录，请从原目录关闭后再设置" };
            bool present = fileExists(executable);
            bool nativeStartup = false;
            try
            {
                string native = store.Read(name == NexusName ? "Nexus" : "Rainmeter");
                nativeStartup = native != null && (String.Equals(native, command, StringComparison.OrdinalIgnoreCase) ||
                    native.StartsWith(command + " ", StringComparison.OrdinalIgnoreCase));
            }
            catch { /* An unrelated native entry must not prevent managing our own entry. */ }
            return new StartupState {
                Enabled = existing != null, CanChange = present || existing != null,
                Detail = !present ? (existing != null ? "程序文件已缺失，可关闭旧启动项" : "未检测到程序，安装或选择路径后可开启") : nativeStartup ? (existing != null ? "已登记 · 另有原程序自启动" : "ADC 已关闭 · 原程序仍设为自启动") : existing != null ? "已登记 · 下次登录启动" : "已关闭 · 需要时手动启动"
            };
        }
        public void Set(string name, string executable, bool enabled)
        {
            if (!integration) throw new InvalidOperationException("测试模式不修改开机启动项。");
            StartupState state = Read(name, executable);
            if (!state.CanChange) throw new InvalidOperationException(state.Detail);
            if (enabled && !fileExists(executable)) throw new FileNotFoundException("未找到组件程序，无法开启自启动。", executable);
            string command = Command(executable);
            string before = store.Read(name);
            // Recheck immediately before mutation: another installation may have changed the entry.
            if (before != null && !String.Equals(before, command, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("启动项刚刚被其他程序修改，请重新读取。");
            if (enabled == (before != null)) return;
            try
            {
                if (enabled) store.Write(name, command); else store.Delete(name);
                string after = store.Read(name);
                if (after != (enabled ? command : null)) throw new IOException("启动项保存后未通过校验。");
            }
            catch (Exception failure)
            {
                try { if (before == null) store.Delete(name); else store.Write(name, before); }
                catch (Exception rollback) { throw new AggregateException("自启动保存失败，回退也未完成，请检查 Windows 启动应用。", failure, rollback); }
                throw;
            }
        }
    }
}
