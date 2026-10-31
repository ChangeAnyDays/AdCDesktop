using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("ADC 桌面美化安装程序")]
[assembly: AssemblyDescription("ADC 桌面美化一键安装程序")]
[assembly: AssemblyProduct("ADC 桌面美化")]
[assembly: AssemblyVersion("1.3.0.0")]
[assembly: AssemblyFileVersion("1.3.0.0")]

internal static class SetupLauncher
{
    [STAThread]
    private static int Main(string[] args)
    {
        string temp = Path.Combine(Path.GetTempPath(), "ADCSetup-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        bool success = false;
        try
        {
            Directory.CreateDirectory(temp);
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ADC.Setup.zip"))
            {
                if (stream == null) throw new InvalidOperationException("安装包资源缺失，请重新下载。");
                using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read))
                    archive.ExtractToDirectory(temp);
            }

            string installCmd = Path.Combine(temp, "Install.cmd");
            if (!File.Exists(installCmd)) throw new InvalidOperationException("安装包不完整：缺少 Install.cmd。");

            if (args.Length > 0 && args[0] == "--extract-only")
            {
                MessageBox.Show(temp, "ADC 安装包已解压", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }

            ProcessStartInfo start = new ProcessStartInfo("cmd.exe", "/c \"\"" + installCmd + "\"\"");
            start.WorkingDirectory = temp;
            start.UseShellExecute = true;
            using (Process process = Process.Start(start))
            {
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    MessageBox.Show("安装未完成，退出代码：" + process.ExitCode + "\n\n临时文件保留在：\n" + temp,
                        "ADC 安装程序", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    try { Process.Start("explorer.exe", "\"" + temp + "\""); } catch { }
                    return process.ExitCode;
                }
            }

            success = true;
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show("安装程序无法启动：\n" + ex.Message + "\n\n临时目录：\n" + temp,
                "ADC 安装程序", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
        finally
        {
            if (success)
            {
                try { Directory.Delete(temp, true); } catch { }
            }
        }
    }
}
