using NAudio.CoreAudioApi;
using System.Text.Json;
namespace DuckyMicrophone;
internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Contains("--capture-probe")) { Task.Run(AudioTests.CaptureProbeAsync).GetAwaiter().GetResult(); return; }
        if (args.Contains("--audio-status")) { AudioTests.AudioStatus(); return; }
        if (args.Contains("--self-test"))
        {
            try { AudioTests.Run(); }
            catch (Exception ex) { Environment.ExitCode = 1; File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "self-test.txt"), "FAIL: " + ex.Message); }
            return;
        }
        if (args.Contains("--list-sources"))
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "sources.json"), JsonSerializer.Serialize(new { Apps = DeviceCatalog.Apps(), Microphones = DeviceCatalog.Microphones(), Outputs = DeviceCatalog.Outputs() }, new JsonSerializerOptions { WriteIndented = true })); return;
        }
        if (args.Contains("--integration-test"))
        {
            Task.Run(AudioTests.IntegrationAsync).GetAwaiter().GetResult(); return;
        }
        ApplicationConfiguration.Initialize();
        var form = new DuckyForm();
        if (args.Contains("--preview")) form.Shown += (_, _) => form.BeginInvoke((Action)(() =>
        {
            using var bitmap = new Bitmap(form.Width, form.Height); form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
            bitmap.Save(Path.Combine(AppContext.BaseDirectory, "preview.png")); form.Close();
        }));
        if (args.Contains("--ui-test")) form.Shown += async (_, _) =>
        {
            try { await form.CheckInterfaceAsync(); }
            catch (Exception ex) { Environment.ExitCode = 1; File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "ui-test.txt"), "FAIL: " + ex); }
            finally { form.Close(); }
        };
        Application.Run(form);
    }
}




