using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization.Json;
using Microsoft.Win32;

namespace SpectrumControl
{
    public sealed class NexusAppearancePreset
    {
        public string Id;
        public string Name;
        public string Description;
        public Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public static class NexusAppearancePresets
    {
        public static readonly NexusAppearancePreset Minimal = Make("minimal", "极简底部",
            "42px 图标、轻反射、小间距；适合不想让 Dock 抢画面的人。",
            new Dictionary<string, string> { { "DockIconSize1", "42" }, { "DockIconSpacing1", "8" }, { "DockIconVSpacing1", "8" }, { "DockReflectionSize1", "24" }, { "DockMagAmplitude1", "4" }, { "DockMagPixels1", "55" }, { "DockIconBounce1", "12" }, { "DockRainDrops1", "False" }, { "DockWaterRipple1", "False" } });
        public static readonly NexusAppearancePreset Balanced = Make("balanced", "均衡默认",
            "49px 图标、中等反射与放大；保留轻雨滴，接近 Nexus 默认的稳重效果。",
            new Dictionary<string, string> { { "DockIconSize1", "49" }, { "DockIconSpacing1", "8" }, { "DockIconVSpacing1", "8" }, { "DockReflectionSize1", "70" }, { "DockMagAmplitude1", "5" }, { "DockMagPixels1", "65" }, { "DockIconBounce1", "20" }, { "DockRainDrops1", "True" }, { "DockWaterRipple1", "True" } });
        public static readonly NexusAppearancePreset Showcase = Make("showcase", "大图标展示",
            "60px 大图标、强反射、明显放大，并保留雨滴和水波；适合展示和演示。",
            new Dictionary<string, string> { { "DockIconSize1", "60" }, { "DockIconSpacing1", "10" }, { "DockIconVSpacing1", "10" }, { "DockReflectionSize1", "90" }, { "DockMagAmplitude1", "7" }, { "DockMagPixels1", "88" }, { "DockIconBounce1", "30" }, { "DockRainDrops1", "True" }, { "DockWaterRipple1", "True" } });
        public static readonly NexusAppearancePreset Compact = Make("compact", "紧凑悬浮",
            "36px 小图标、无反射、无雨滴、小间距；适合屏幕空间紧张或多窗口。",
            new Dictionary<string, string> { { "DockIconSize1", "36" }, { "DockIconSpacing1", "5" }, { "DockIconVSpacing1", "5" }, { "DockReflectionSize1", "0" }, { "DockMagAmplitude1", "2" }, { "DockMagPixels1", "42" }, { "DockIconBounce1", "8" }, { "DockRainDrops1", "False" }, { "DockWaterRipple1", "False" } });
        public static readonly NexusAppearancePreset RainGlass = Make("rainglass", "雨滴玻璃",
            "40px 图标、明显反射、雨滴和水波；最有氛围的一套。",
            new Dictionary<string, string> { { "DockIconSize1", "40" }, { "DockIconSpacing1", "9" }, { "DockIconVSpacing1", "9" }, { "DockReflectionSize1", "82" }, { "DockMagAmplitude1", "6" }, { "DockMagPixels1", "76" }, { "DockIconBounce1", "24" }, { "DockRainDrops1", "True" }, { "DockWaterRipple1", "True" } });

        public static readonly NexusAppearancePreset[] All = new[] { Minimal, Balanced, Showcase, Compact, RainGlass };

        public static NexusAppearancePreset Find(string id)
        {
            foreach (NexusAppearancePreset preset in All)
                if (String.Equals(preset.Id, id, StringComparison.OrdinalIgnoreCase)) return preset;
            throw new ArgumentException("未知的 Nexus 外观预设。");
        }

        private static NexusAppearancePreset Make(string id, string name, string description, Dictionary<string, string> values)
        {
            return new NexusAppearancePreset { Id = id, Name = name, Description = description, Values = values };
        }
    }

    // Nexus Free keeps Dock appearance under the 32-bit HKCU profile. Only the
    // existing, whitelisted geometry values are changed; icons and layout stay put.
    public sealed class NexusAppearanceService
    {
        private const string Profile = @"Software\WinSTEP2000\NeXuS";
        private const string DockKey = @"Docks";
        private static readonly HashSet<string> Allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "DockIconSize1", "DockIconSpacing1", "DockIconVSpacing1",
            "DockReflectionSize1", "DockMagAmplitude1", "DockMagPixels1", "DockIconBounce1",
            "DockRainDrops1", "DockWaterRipple1"
        };
        private readonly string backup;
        public NexusAppearanceService(string backupPath) { backup = backupPath; }
        public bool HasBackup { get { return File.Exists(backup); } }

        public void Apply(NexusAppearancePreset preset)
        {
            if (preset == null) throw new ArgumentNullException("preset");
            using (RegistryKey user = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry32))
            using (RegistryKey key = user.OpenSubKey(Profile + "\\" + DockKey, true))
            {
                if (key == null) throw new InvalidOperationException("未找到 Nexus Dock 配置，请先启动一次 Nexus 并保存设置。");
                string[] existing = key.GetValueNames();
                var before = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, string> pair in preset.Values)
                {
                    if (!Allowed.Contains(pair.Key)) throw new InvalidDataException("拒绝写入未识别的 Nexus 外观设置。");
                    if (Array.IndexOf(existing, pair.Key) < 0 || key.GetValueKind(pair.Key) != RegistryValueKind.String)
                        throw new InvalidOperationException("Nexus 缺少外观字段：" + pair.Key + "。请先在 Nexus 中保存一次设置。");
                    before[pair.Key] = Convert.ToString(key.GetValue(pair.Key), CultureInfo.InvariantCulture);
                }
                if (!HasBackup) SaveBackup(before);
                var changed = new List<KeyValuePair<string, string>>();
                try
                {
                    foreach (KeyValuePair<string, string> pair in preset.Values)
                    {
                        key.SetValue(pair.Key, pair.Value, RegistryValueKind.String);
                        changed.Add(new KeyValuePair<string, string>(pair.Key, before[pair.Key]));
                    }
                    Verify(key, preset.Values);
                }
                catch
                {
                    foreach (KeyValuePair<string, string> pair in changed)
                        try { key.SetValue(pair.Key, pair.Value, RegistryValueKind.String); } catch { }
                    throw;
                }
            }
        }

        public void Restore()
        {
            if (!HasBackup) throw new InvalidOperationException("没有可恢复的 Nexus 外观备份。");
            Dictionary<string, string> saved;
            using (FileStream stream = File.OpenRead(backup))
                saved = (Dictionary<string, string>)new DataContractJsonSerializer(typeof(Dictionary<string, string>)).ReadObject(stream);
            using (RegistryKey user = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry32))
            using (RegistryKey key = user.OpenSubKey(Profile + "\\" + DockKey, true))
            {
                if (key == null) throw new InvalidOperationException("未找到 Nexus Dock 配置。");
                var before = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, string> pair in saved)
                {
                    if (!Allowed.Contains(pair.Key)) continue;
                    if (Array.IndexOf(key.GetValueNames(), pair.Key) < 0) continue;
                    before[pair.Key] = Convert.ToString(key.GetValue(pair.Key), CultureInfo.InvariantCulture);
                    key.SetValue(pair.Key, pair.Value, RegistryValueKind.String);
                }
                Verify(key, saved);
            }
        }

        private static void Verify(RegistryKey key, Dictionary<string, string> expected)
        {
            foreach (KeyValuePair<string, string> pair in expected)
                if (!String.Equals(Convert.ToString(key.GetValue(pair.Key), CultureInfo.InvariantCulture), pair.Value, StringComparison.Ordinal))
                    throw new IOException("Nexus 外观写入后未通过校验：" + pair.Key);
        }

        private void SaveBackup(Dictionary<string, string> values)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(backup));
            string temporary = backup + ".tmp";
            try
            {
                using (FileStream stream = File.Create(temporary))
                    new DataContractJsonSerializer(typeof(Dictionary<string, string>)).WriteObject(stream, values);
                File.Move(temporary, backup);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
