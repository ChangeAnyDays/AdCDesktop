using System;
using System.IO;
using System.Linq;
using System.Text;
using SpectrumControl;

namespace SpectrumControlTests
{
    internal static class Program
    {
        private static int assertions;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        private static string testOutput;

        public static int Main()
        {
            try
            {
                testOutput = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fixtures", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(testOutput);
                TestReadAndValidation();
                TestApplyAndScopedRestore();
                TestWriteRollback();
                Console.WriteLine("PASS: " + assertions + " backend assertions; all writes limited to " + testOutput);
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("FAIL: " + error);
                return 1;
            }
        }

        private static void TestReadAndValidation()
        {
            Fixture f = CreateFixture("read-validation");
            SpectrumBackend backend = new SpectrumBackend(f.Root, false);
            SpectrumSettings s = backend.Read();
            Equal(700, s.Width, "width read"); Equal(60, s.Height, "height read");
            Equal(45.0, s.Sensitivity, "sensitivity read"); Equal(0, s.Smoothing, "smoothing read");
            Equal(610, s.X, "X read"); Equal(879, s.Y, "Y read");
            Equal(70, s.Opacity, "display opacity rounded"); Equal(178, s.RawAlpha, "raw alpha preserved");
            Equal(70, s.OriginalOpacity, "original display opacity captured");
            Equal(2, s.HoverMode, "exact hover mode read"); True(s.HoverHide, "compatibility hover flag");
            True(s.Visible, "active skin read"); True(s.ClickThrough, "click through read");
            Equal("#12EDF7", s.StartColor, "start color read"); Equal("#F50BB9", s.EndColor, "end color read");
            True(!backend.HasOriginalBackup, "read does not create backup");
            True(!Directory.Exists(Path.Combine(f.Root, "SpectrumControl")), "read is nonmutating");
            SpectrumSettings clone = s.Clone(); clone.Width = 900;
            Equal(700, s.Width, "clone is independent"); Equal(178, clone.RawAlpha, "clone keeps raw alpha"); Equal(2, clone.HoverMode, "clone keeps hover mode");
            byte[][] original = f.Files.Select(File.ReadAllBytes).ToArray();
            s.Width = 900; s.StartColor = "#000000\r\n[Evil]";
            Throws(delegate { backend.Apply(s); }, "invalid color rejected");
            AssertBytes(f.Files, original, "validation is nonmutating");
            True(!backend.HasOriginalBackup, "invalid apply does not make original backup");
            Throws(delegate { backend.RestoreOriginal(); }, "restore without backup reports error");
            AssertBytes(f.Files, original, "missing restore backup does not write");
            SpectrumSettings percent = backend.Read();
            percent.HoverHide = true; Equal(2, percent.HoverMode, "same compatibility flag does not change mode");
            percent.HoverHide = false; Equal(0, percent.HoverMode, "compatibility off sets no hover action");
        }

        private static void TestApplyAndScopedRestore()
        {
            Fixture f = CreateFixture("apply-restore");
            SpectrumBackend backend = new SpectrumBackend(f.Root, false);
            SpectrumSettings s = backend.Read();
            s.Width = 900; s.Height = 100; s.Sensitivity = 57.5; s.Smoothing = 180;
            s.X = -220; s.Y = 700; s.StartColor = "#00C8FF"; s.EndColor = "#B450FF";
            s.Visible = false; s.ClickThrough = false;
            backend.Apply(s);
            True(backend.HasOriginalBackup, "first apply creates original backup");
            SpectrumSettings applied = backend.Read();
            Equal(900, applied.Width, "width applied"); Equal(100, applied.Height, "height applied");
            Equal(57.5, applied.Sensitivity, "fractional sensitivity applied"); Equal(180, applied.Smoothing, "smoothing applied");
            Equal(-220, applied.X, "negative multi-monitor X applied"); Equal(700, applied.Y, "Y applied");
            Equal(178, applied.RawAlpha, "only changing width does not alter raw alpha");
            Equal(2, applied.HoverMode, "only changing size does not alter hover mode");
            True(!applied.Visible, "visibility applied"); True(!applied.ClickThrough, "click through applied");
            Equal("#00C8FF", applied.StartColor, "start color applied"); Equal("#B450FF", applied.EndColor, "end color applied");
            byte[] skinBytes = File.ReadAllBytes(f.Skin);
            True(skinBytes[0] == 0xff && skinBytes[1] == 0xfe, "UTF16 BOM preserved");
            string skinText = File.ReadAllText(f.Skin, Encoding.Unicode);
            True(skinText.Contains("; 频谱原始备注\r\nKeepSkin=untouched\n"), "mixed newline unrelated skin block preserved");
            True(skinText.Contains("Bands=(#BandCount#+1)\nPort=Output\r\n"), "audio endpoint settings preserved");
            byte[] colorBytes = File.ReadAllBytes(f.Variables);
            True(colorBytes.Take(3).SequenceEqual(new byte[] { 0xef, 0xbb, 0xbf }), "UTF8 BOM preserved");
            string variablesText = File.ReadAllText(f.Variables, Encoding.UTF8);
            True(variablesText.Contains("Color1=255,255,255\nUserName=现有用户名\r\n"), "unrelated color and user variable preserved");
            string settingsText = File.ReadAllText(f.Settings, Encoding.UTF8);
            True(settingsText.Contains("ConfigEditor=C:\\自定义编辑器.exe\nDisableDragging=1\r\n"), "global settings preserved byte-for-byte text");
            True(settingsText.Contains("[Welfare\\Clock]\r\nActive=1\nWindowY=76%\r\n"), "clock section preserved");
            True(File.Exists(Path.Combine(f.Root, "SpectrumControl", "Backups", "last", "complete.txt")), "last backup present");

            // Simulate independent edits made after the panel's original snapshot.
            settingsText = settingsText.Replace("DisableVersionCheck=0", "DisableVersionCheck=1").Replace("WindowY=76%", "WindowY=77%");
            settingsText += "\r\n[IndependentSkin]\r\nActive=1\r\nWindowX=99\r\n";
            File.WriteAllText(f.Settings, settingsText, Utf8);
            applied.Opacity = 90; applied.HoverMode = 3; backend.Apply(applied);
            SpectrumSettings changed = backend.Read();
            Equal(230, changed.RawAlpha, "changed percentage maps to alpha"); Equal(3, changed.HoverMode, "hover mode applied exactly");
            backend.RestoreOriginal();
            SpectrumSettings restored = backend.Read();
            Equal(700, restored.Width, "original width restored"); Equal(60, restored.Height, "original height restored");
            Equal(45.0, restored.Sensitivity, "original sensitivity restored"); Equal(0, restored.Smoothing, "original smoothing restored");
            Equal(610, restored.X, "original X restored"); Equal(879, restored.Y, "original Y restored");
            Equal(178, restored.RawAlpha, "original exact alpha restored"); Equal(2, restored.HoverMode, "original exact hover restored");
            True(restored.Visible && restored.ClickThrough, "original skin visibility and input restored");
            settingsText = File.ReadAllText(f.Settings, Encoding.UTF8);
            True(settingsText.Contains("DisableVersionCheck=1"), "restore preserves later global changes");
            True(settingsText.Contains("WindowY=77%"), "restore preserves later clock changes");
            True(settingsText.Contains("[IndependentSkin]\r\nActive=1\r\nWindowX=99\r\n"), "restore preserves later independent skin");
            string originalBackup = Path.Combine(f.Root, "SpectrumControl", "Backups", "original", "Rainmeter.ini");
            True(File.ReadAllText(originalBackup, Encoding.UTF8).Contains("DisableVersionCheck=0"), "original backup remains first snapshot");
        }

        private static void TestWriteRollback()
        {
            Fixture f = CreateFixture("rollback");
            SpectrumBackend backend = new SpectrumBackend(f.Root, false);
            SpectrumSettings s = backend.Read(); s.Width = 1000; s.StartColor = "#0000FF";
            byte[][] original = f.Files.Select(File.ReadAllBytes).ToArray();
            File.SetAttributes(f.Variables, File.GetAttributes(f.Variables) | FileAttributes.ReadOnly);
            try
            {
                Throws(delegate { backend.Apply(s); }, "readonly destination produces apply error");
                AssertBytes(f.Files, original, "failed multi-file transaction rolls back all file bytes");
            }
            finally { File.SetAttributes(f.Variables, FileAttributes.Normal); }
            True(!Directory.GetFiles(Path.GetDirectoryName(f.Skin), "*.tmp").Any(), "temporary update files cleaned");
        }

        private static Fixture CreateFixture(string name)
        {
            Fixture f = new Fixture();
            f.Root = Path.Combine(testOutput, name);
            f.Skin = Path.Combine(f.Root, "Rainmeter", "Skins", "RetrOcOlOr", "Visualizer", "Visualizer.ini");
            f.Variables = Path.Combine(f.Root, "Rainmeter", "Skins", "RetrOcOlOr", "@Resources", "Variables.inc");
            f.Settings = Path.Combine(f.Root, "Rainmeter", "Rainmeter.ini");
            Directory.CreateDirectory(Path.GetDirectoryName(f.Skin)); Directory.CreateDirectory(Path.GetDirectoryName(f.Variables));
            File.WriteAllText(f.Skin, "[Rainmeter]\r\n; 频谱原始备注\r\nKeepSkin=untouched\n[Variables]\r\nBarWidth=700\nBarHeight=60\r\nOther=unchanged\n[MeasureAudio]\r\nSensitivity=45\nFFTDecay=0\r\nBands=(#BandCount#+1)\nPort=Output\r\n", new UnicodeEncoding(false, true));
            File.WriteAllText(f.Variables, "[Variables]\r\nColor2=18,237,247\nColor3=245,11,185\r\nColor1=255,255,255\nUserName=现有用户名\r\n", new UTF8Encoding(true));
            File.WriteAllText(f.Settings, "[Rainmeter]\r\nDisableVersionCheck=0\r\nConfigEditor=C:\\自定义编辑器.exe\nDisableDragging=1\r\n\r\n[Welfare\\Clock]\r\nActive=1\nWindowY=76%\r\n\r\n[RetrOcOlOr\\Visualizer]\r\nActive=1\nWindowX=610\r\nWindowY=879\nAnchorX=0\r\nAnchorY=0\nAlphaValue=178\r\nOnHover=2\nClickThrough=1\r\nKeepOnScreen=1\n", Utf8);
            f.Files = new string[] { f.Skin, f.Variables, f.Settings };
            return f;
        }

        private sealed class Fixture { public string Root, Skin, Variables, Settings; public string[] Files; }
        private static void True(bool condition, string label) { assertions++; if (!condition) throw new Exception(label); }
        private static void Equal<T>(T expected, T actual, string label) { True(Object.Equals(expected, actual), label + ": expected " + expected + ", actual " + actual); }
        private static void Throws(Action action, string label) { bool threw = false; try { action(); } catch { threw = true; } True(threw, label); }
        private static void AssertBytes(string[] files, byte[][] original, string label)
        { for (int i = 0; i < files.Length; i++) True(File.ReadAllBytes(files[i]).SequenceEqual(original[i]), label + " " + Path.GetFileName(files[i])); }
    }
}
