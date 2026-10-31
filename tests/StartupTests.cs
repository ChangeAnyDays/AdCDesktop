using System;
using System.Collections.Generic;
using System.IO;
using SpectrumControl;

public static class StartupTests
{
    private static int checks;
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
    private static void Throws(Action action, string message)
    { try { action(); } catch { checks++; return; } throw new Exception(message); }
    private sealed class Store : IStartupStore
    {
        public Dictionary<string, string> Values = new Dictionary<string, string>();
        public int Reads, Writes;
        public bool CorruptNextWrite;
        public bool FailNextDelete;
        public string Read(string name) { Reads++; string value; return Values.TryGetValue(name, out value) ? value : null; }
        public void Write(string name, string command)
        { Writes++; Values[name] = CorruptNextWrite ? "corrupted" : command; CorruptNextWrite = false; }
        public void Delete(string name)
        { Writes++; if (FailNextDelete) { FailNextDelete = false; throw new IOException("injected"); } Values.Remove(name); }
    }
    public static int Main()
    {
        try
        {
            var store = new Store();
            bool exists = true;
            var service = new StartupBackend(store, true, delegate(string path) { return exists; });
            string root = Path.Combine(Path.GetTempPath(), "ADC Startup 测试");
            string spectrum = Path.Combine(root, "Rainmeter.exe"), nexus = Path.Combine(root, "Nexus.exe");
            Check(!service.Read(StartupBackend.SpectrumName, spectrum).Enabled, "Opening must default to existing state, not enable startup.");
            Check(store.Writes == 0, "Reading state must not create startup entries.");
            service.Set(StartupBackend.SpectrumName, spectrum, true);
            Check(store.Values[StartupBackend.SpectrumName] == "\"" + spectrum + "\"", "Paths with spaces and Chinese characters must be quoted.");
            Check(service.Read(StartupBackend.SpectrumName, spectrum).Enabled, "Enabled state must read back.");
            Check(!service.Read(StartupBackend.NexusName, nexus).Enabled, "Spectrum must not enable Nexus.");
            service.Set(StartupBackend.NexusName, nexus, true);
            service.Set(StartupBackend.SpectrumName, spectrum, false);
            Check(!store.Values.ContainsKey(StartupBackend.SpectrumName) && store.Values.ContainsKey(StartupBackend.NexusName), "Disabling one component must not affect the other.");
            store.Values["Nexus"] = StartupBackend.Command(nexus);
            Check(service.Read(StartupBackend.NexusName, nexus).Detail.Contains("原程序自启动"), "Native duplicate startup must be surfaced.");
            store.Values["Unrelated"] = "keep";
            service.Set(StartupBackend.NexusName, nexus, false);
            Check(store.Values["Unrelated"] == "keep", "Unrelated startup entries must be preserved.");
            Check(store.Values["Nexus"] == StartupBackend.Command(nexus) && service.Read(StartupBackend.NexusName, nexus).Detail.Contains("原程序仍设为自启动"), "Disabling ADC startup must preserve and report native startup.");
            exists = false;
            Throws(delegate { service.Set(StartupBackend.SpectrumName, spectrum, true); }, "Missing component must not be enabled.");
            Check(!store.Values.ContainsKey(StartupBackend.SpectrumName), "Missing component must not leave an entry.");
            store.Values[StartupBackend.SpectrumName] = StartupBackend.Command(spectrum);
            Check(service.Read(StartupBackend.SpectrumName, spectrum).CanChange, "A stale owned entry must still be removable.");
            service.Set(StartupBackend.SpectrumName, spectrum, false);
            Check(!store.Values.ContainsKey(StartupBackend.SpectrumName), "Missing executable must not block disabling its owned entry.");
            exists = true;
            store.Values[StartupBackend.SpectrumName] = "\"another executable\"";
            Throws(delegate { service.Set(StartupBackend.SpectrumName, spectrum, true); }, "Conflicting installation must not be overwritten.");
            Throws(delegate { service.Set(StartupBackend.SpectrumName, spectrum, false); }, "Conflicting installation must not be deleted.");
            Check(store.Values[StartupBackend.SpectrumName] == "\"another executable\"", "Foreign entry changed.");
            store.Values.Remove(StartupBackend.SpectrumName);
            store.CorruptNextWrite = true;
            Throws(delegate { service.Set(StartupBackend.SpectrumName, spectrum, true); }, "Failed verification must be reported.");
            Check(!store.Values.ContainsKey(StartupBackend.SpectrumName), "Failed enabling must roll back.");
            service.Set(StartupBackend.SpectrumName, spectrum, true);
            store.FailNextDelete = true;
            Throws(delegate { service.Set(StartupBackend.SpectrumName, spectrum, false); }, "Failed deletion must be reported.");
            Check(service.Read(StartupBackend.SpectrumName, spectrum).Enabled, "Failed deletion must preserve the previous state.");
            int writes = store.Writes;
            service.Set(StartupBackend.SpectrumName, spectrum, true);
            Check(store.Writes == writes, "No-op must not rewrite the entry.");
            Throws(delegate { service.Set("OtherApp", spectrum, true); }, "Only the two owned names may be used.");
            Throws(delegate { StartupBackend.Command("relative.exe"); }, "Relative paths must be refused.");
            Throws(delegate { StartupBackend.Command(spectrum + "\" -bad"); }, "Quote injection must be refused.");
            Throws(delegate { StartupBackend.Command(Path.Combine(root, new string('a', 270))); }, "Oversized Run commands must be refused.");
            var fixture = new StartupBackend(store, false, delegate(string path) { return true; });
            int reads = store.Reads;
            Check(!fixture.Read(StartupBackend.NexusName, nexus).CanChange && store.Reads == reads, "Fixture mode must not read live state.");
            Throws(delegate { fixture.Set(StartupBackend.NexusName, nexus, true); }, "Fixture mode must not write startup entries.");

            // A clean ControlPanel install has no Nexus and no startup entry. The
            // panel must say the program is not installed, not imply a stale entry
            // exists that the user can close.
            var cleanStore = new Store();
            var cleanService = new StartupBackend(cleanStore, true, delegate(string path) { return false; });
            StartupState cleanNexus = cleanService.Read(StartupBackend.NexusName, nexus);
            Check(!cleanNexus.CanChange, "A missing Nexus must not be enabled.");
            Check(cleanNexus.Detail.Contains("未检测到程序"), "A clean profile must name the missing program.");
            Check(!cleanNexus.Detail.Contains("可关闭旧启动项"), "A clean profile must not imply a stale startup entry.");
            Console.WriteLine("PASS: " + checks + " startup checks"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
