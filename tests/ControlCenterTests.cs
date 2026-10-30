using System;
using System.Collections.Generic;
using System.IO;
using SpectrumControl;

public static class ControlCenterTests
{
    private static int checks;
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
    private static void Throws(Action action, string message)
    {
        try { action(); } catch { checks++; return; }
        throw new Exception(message);
    }
    private sealed class MemoryStore : INexusStore
    {
        public List<NexusValue> Values = new List<NexusValue> {
            new NexusValue { Key = @"Docks\1", Name = "DockShowLabels", Value = "True" },
            new NexusValue { Key = "", Name = "ShowIconTooltips", Value = "2", Dword = true }
        };
        public int Writes;
        public int FailOnWrite;
        public List<NexusValue> Read() { return Values.ConvertAll(delegate(NexusValue v) { return v.Copy(); }); }
        public void Write(NexusValue value)
        {
            Writes++;
            if (Writes == FailOnWrite) throw new IOException("Injected write failure");
            int index = Values.FindIndex(delegate(NexusValue v) { return v.Key == value.Key && v.Name == value.Name; });
            Values[index] = value.Copy();
        }
    }
    public static int Main(string[] args)
    {
        string temporary = Path.Combine(Path.GetTempPath(), "adc-center-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temporary);
            var store = new MemoryStore();
            string backup = Path.Combine(temporary, "original.json");
            var service = new NexusSettingsService(store, backup);
            service.Apply(false, false);
            Check(store.Values[0].Value == "False" && store.Values[1].Value == "0", "Quiet mode must disable both kinds of text.");
            Check(File.Exists(backup), "Must create an original backup.");
            string original = File.ReadAllText(backup);
            service.Apply(true, true);
            Check(store.Values[0].Value == "True" && store.Values[1].Value == "1", "Re-enabling must use the same value kinds.");
            Check(File.ReadAllText(backup) == original, "Repeated apply must preserve the original backup.");
            service.Restore();
            Check(store.Values[0].Value == "True" && store.Values[1].Value == "2", "Restore must preserve the original tooltip mode, not just a boolean.");
            int writes = store.Writes;
            service.Apply(true, true);
            Check(store.Writes == writes && store.Values[1].Value == "2", "An unchanged enabled tooltip must keep Only Modules.");

            store.FailOnWrite = store.Writes + 2;
            Throws(delegate { service.Apply(false, false); }, "A write failure must not be ignored.");
            Check(store.Values[0].Value == "True" && store.Values[1].Value == "2", "A partial write must roll back.");
            store.FailOnWrite = 0;
            store.Values[0].Value = "unknown";
            writes = store.Writes;
            Throws(delegate { service.Apply(false, false); }, "Unknown values must fail before mutation.");
            Check(store.Writes == writes, "An unsupported format must not write.");
            store.Values.RemoveAt(0);
            Throws(delegate { service.Apply(false, false); }, "A missing Dock must not silently succeed.");
            Check(!NexusSettingsService.IsAllowed(new NexusValue { Key = @"Docks\..\Other", Name = "DockShowLabels" }), "Restore must reject escaping paths.");
            Check(!NexusSettingsService.IsAllowed(new NexusValue { Key = "", Name = "GenAutoRun" }), "Must not allow unrelated settings.");
            var incomplete = new NexusSettingsService(store, Path.Combine(temporary, "unsupported.json"));
            Throws(delegate { incomplete.Apply(false, false); }, "Unsupported profiles must not be backed up as if supported.");
            Check(!incomplete.HasBackup, "No backup should be created from an unsupported profile.");

            // Real Nexus 26.9 layout: numbered labels live directly in Docks.
            var numbered = new MemoryStore();
            numbered.Values = new List<NexusValue> {
                new NexusValue { Key = "Docks", Name = "DockShowLabels1", Value = "True" },
                new NexusValue { Key = "Docks", Name = "DockShowLabels2", Value = "-1", Dword = true },
                new NexusValue { Key = @"Docks\3", Name = "DockShowLabels", Value = "False" },
                new NexusValue { Key = "", Name = "ShowIconTooltips", Value = "3" }
            };
            var numberedService = new NexusSettingsService(numbered, Path.Combine(temporary, "numbered.json"));
            Check(numberedService.Read().Count == 4, "Numbered and legacy labels must be readable together.");
            numberedService.Apply(false, true);
            Check(numbered.Values[0].Value == "False" && numbered.Values[1].Value == "0" && numbered.Values[2].Value == "False",
                "Quiet labels must disable every dock while preserving registry value kinds.");
            Check(numbered.Values[3].Value == "3", "Changing numbered labels must preserve the enabled tooltip mode.");
            numberedService.Restore();
            Check(numbered.Values[0].Value == "True" && numbered.Values[1].Value == "-1" && numbered.Values[1].Dword && numbered.Values[2].Value == "False",
                "Restore must preserve each dock's exact original label value and type.");
            numbered.FailOnWrite = numbered.Writes + 2;
            Throws(delegate { numberedService.Apply(false, false); }, "A numbered multi-dock write failure must be reported.");
            Check(numbered.Values[0].Value == "True" && numbered.Values[1].Value == "-1" && numbered.Values[3].Value == "3",
                "A partial numbered multi-dock write must roll back every changed value.");
            numbered.FailOnWrite = 0;
            numberedService.Apply(false, false);
            Check(numbered.Values[0].Value == "False" && numbered.Values[1].Value == "0" && numbered.Values[3].Value == "0",
                "Quiet mode must disable numbered labels and delayed tooltips together.");
            numberedService.Restore();
            Check(numbered.Values[3].Value == "3", "Restoring numbered labels must also restore the exact tooltip mode.");
            Check(NexusSettingsService.IsAllowed(new NexusValue { Key = "Docks", Name = "DockShowLabels12" }), "Positive numeric dock suffixes must be allowed.");
            foreach (string suffix in new[] { "0", "01", "-1", "+1", "1Other", "1\\Other", "\u0661", "2147483648" })
                Check(!NexusSettingsService.IsAllowed(new NexusValue { Key = "Docks", Name = "DockShowLabels" + suffix }),
                    "An invalid dock suffix must not enter the label write allowlist: " + suffix);
            Check(!NexusSettingsService.IsAllowed(new NexusValue { Key = "", Name = "DockShowLabels1" }), "Numbered labels outside Docks must be rejected.");
            Check(!NexusSettingsService.IsAllowed(new NexusValue { Key = "DocksOther", Name = "DockShowLabels1" }), "A similarly named key must not be treated as Docks.");
            Check(!NexusSettingsService.IsLabelName(null), "A missing value name must be rejected.");
            numbered.Values[0].Value = "2";
            writes = numbered.Writes;
            Throws(delegate { numberedService.Apply(false, false); }, "Numbered labels must use boolean values, not tooltip modes.");
            Check(numbered.Writes == writes, "Invalid numbered label values must fail before any write.");

            var quiet = new SpectrumPreview();
            var loud = new SpectrumPreview();
            double[] low = quiet.Step(1, 0.04, 0, 0, false);
            double[] high = loud.Step(1, 0.04, 100, 0, false);
            double lowSum = 0, highSum = 0;
            for (int i = 0; i < low.Length; i++) { lowSum += low[i]; highSum += high[i]; }
            Check(highSum > lowSum, "Sensitivity must affect the preview.");
            double previous = high[50];
            high = loud.Step(1, 0.04, 100, 1000, true);
            Check(high[50] > 0 && high[50] < previous, "Silence with smoothing must decay rather than freeze or snap.");
            high = loud.Step(1, 0.04, 100, 0, true);
            Check(Array.TrueForAll(high, delegate(double value) { return value == 0; }), "Silence without smoothing must become a baseline.");
            var guarded = new NexusBackend(temporary, false);
            Throws(delegate { guarded.Apply(false, false, false); }, "Fixture mode must not write the registry.");
            Throws(delegate { guarded.Start(false); }, "Fixture mode must not start Nexus.");
            Console.WriteLine("PASS: " + checks + " control center checks");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
    }
}
