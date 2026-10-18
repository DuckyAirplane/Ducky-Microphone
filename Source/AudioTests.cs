using NAudio.Wave;
namespace DuckyMicrophone;
internal static class AudioTests
{
    private sealed class Constant(float value) : ISampleProvider
    {
        public WaveFormat WaveFormat => WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        public int Read(Span<float> b) { b.Fill(value); return b.Length; }
    }
    private static void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); }
    public static void AudioStatus()
    {
        var lines = new List<string>();
        using var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
        foreach (var flow in new[] { NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.DataFlow.Capture })
            foreach (var device in enumerator.EnumerateAudioEndPoints(flow, NAudio.CoreAudioApi.DeviceState.Active))
                using (device)
                {
                    lines.Add($"{flow}: {device.FriendlyName}; level={device.AudioEndpointVolume.MasterVolumeLevelScalar:P0}; mute={device.AudioEndpointVolume.Mute}");
                    if (flow != NAudio.CoreAudioApi.DataFlow.Render) continue;
                    var sessions = device.AudioSessionManager.Sessions;
                    for (int i = 0; i < sessions.Count; i++)
                        using (var session = sessions[i])
                            try
                            {
                                using var process = System.Diagnostics.Process.GetProcessById((int)session.GetProcessID);
                                if (process.ProcessName.Contains("opera", StringComparison.OrdinalIgnoreCase) || process.ProcessName == "DuckyMicrophone")
                                    lines.Add($"  {process.ProcessName} PID {process.Id}; session level={session.SimpleAudioVolume.Volume:P0}; mute={session.SimpleAudioVolume.Mute}");
                            }
                            catch { }
                }
        lines.AddRange(DeviceCatalog.Apps().Where(x => x.Name.Contains("opera", StringComparison.OrdinalIgnoreCase)).Select(x => "App picker: " + x.Name));
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "audio-status.txt"), lines);
    }
    private static void RegressionTests()
    {
        var state = new SourceState(new(SourceKind.App, "test", "Test"));
        var gain = new GainStage(new Constant(0.1f), state);
        var values = new float[128]; gain.Read(values);
        Check(Math.Abs(values[0] - 0.1f) < 0.00001f, "default source has unity gain");
        state.Volume = 4; gain.Read(values);
        Check(Math.Abs(values[0] - 0.4f) < 0.00001f, "400% boost preserves waveform below ceiling");
        state.Muted = true; gain.Read(values); Check(values.All(x => x == 0), "boost still respects mute");
        var route = new AudioRouter(); Check(route.Master.Volume == 1, "default master has unity gain");
        long now = 0;
        var format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        byte[] Packet(int ms, float value) => System.Runtime.InteropServices.MemoryMarshal.AsBytes(Enumerable.Repeat(value, 96 * ms).ToArray().AsSpan()).ToArray();
        float[] Samples(byte[] bytes) => System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(bytes).ToArray();
        var shortClip = new CaptureBuffer(format, () => now); var block = new byte[format.AverageBytesPerSecond * 40 / 1000];
        shortClip.Add(Packet(10, 0.25f)); shortClip.Read(block);
        Check(Samples(block).All(x => x == 0), "initial capture reserve");
        now = 101; shortClip.Read(block);
        Check(Samples(block).Take(960).All(x => x == 0.25f), "short soundboard clip flushed after bounded wait");
        Check(Samples(block).Skip(960).All(x => x == 0), "short clip tail padded once");
        shortClip.Read(block); Check(Samples(block).All(x => x == 0), "no repeated clip after silence");
        var jitter = new CaptureBuffer(format, () => now); jitter.Add(Packet(120, 0.25f));
        for (int i = 0; i < 100; i++)
        {
            jitter.Read(block); Check(Samples(block).All(x => x == 0.25f), "bursty capture stays continuous");
            now += 40; if (i % 2 == 1) jitter.Add(Packet(80, 0.25f));
        }
        jitter.Add(Packet(300, 0.5f)); jitter.Add(Packet(100, 0.5f)); jitter.Read(block);
        Check(Samples(block).All(x => x == 0.5f), "backlog trims oldest audio without clearing entire queue");
    }
    public static async Task CaptureProbeAsync()
    {
        var lines = new List<string>();
        foreach (var app in DeviceCatalog.Apps().Where(x => x.Name.StartsWith("opera") || x.Name.StartsWith("chrome") || x.Name.StartsWith("msedge") || x.Name.StartsWith("firefox")))
        {
            var state = new SourceState(app); await using var capture = await SourceCapture.CreateAsync(state);
            string? error = null; capture.Failed += e => error = e;
            capture.Start(); var samples = new float[3840]; float peak = 0; int audible = 0;
            for (int i = 0; i < 100; i++)
            {
                await Task.Delay(40); capture.Samples.Read(samples);
                float p = samples.Max(x => Math.Abs(x)); peak = Math.Max(peak, p); if (p > 0.0001f) audible++;
            }
            lines.Add($"{app.Name}: peak={peak:F5}, non-silent blocks={audible}/100, error={error ?? "none"}. No output playback or audio file created.");
        }
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "capture-probe.txt"), lines);
    }
    public static void Run()
    {
        RegressionTests();
        var mix = new FloatMixer(); var a = new SourceState(new(SourceKind.App, "1", "a")) { Volume = 1 }; var b = new SourceState(new(SourceKind.Microphone, "2", "b")) { Volume = 0.5f };
        var ga = new GainStage(new Constant(0.2f), a); var gb = new GainStage(new Constant(0.4f), b);
        mix.Add(ga); mix.Add(gb); var data = new float[100]; mix.Read(data, 1, 96);
        Check(Math.Abs(data[1] - 0.4f) < 0.0001f && data[0] == 0 && data[97] == 0, "summation and boundaries");
        b.Muted = true; mix.Read(data, 0, 96); Check(Math.Abs(data[0] - 0.2f) < 0.0001f, "independent mute");
        mix.Remove(ga); mix.Read(data, 0, 96); Check(data.Take(96).All(x => x == 0), "live removal");
        b.Muted = false; mix.Read(data, 0, 96); Check(Math.Abs(data[0] - 0.2f) < 0.0001f, "remaining source");
        var master = new SourceState(new(SourceKind.App, "master", "master")) { Volume = 1 };
        var limiter = new GainStage(new Constant(3), master, true); limiter.Read(data, 0, 96); Check(data[0] == 1 && limiter.Clipping, "output clipping guard");
        master.Muted = true; limiter.Read(data, 0, 96); Check(data.Take(96).All(x => x == 0), "master mute");
        var many = new FloatMixer(); for (int i = 0; i < 1500; i++) many.Add(new Constant(0.0001f)); many.Read(data, 0, 96); Check(Math.Abs(data[0] - 0.15f) < 0.001f, "1500 sources, no hard limit");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "self-test.txt"), "PASS: summation, buffer boundaries, source gain/mute, live removal, remaining source, clipping guard, master mute, 1500-source mix, default unity gain, 400% boost, jitter reserve, short clips, silence recovery, bounded backlog.");
    }
    public static async Task IntegrationAsync()
    {
        var lines = new List<string>();
        try
        {
            var target = DeviceCatalog.Outputs().First(x => x.Name.StartsWith("CABLE Input"));
            var apps = DeviceCatalog.Apps();
            await using var route = new AudioRouter(); route.Master.Muted = true;
            string? failure = null; route.Failed += e => failure = e;
            var first = apps.FirstOrDefault(x => x.Name.StartsWith("opera") || x.Name.StartsWith("msedge") || x.Name.StartsWith("chrome")) ?? apps.First();
            var state = new SourceState(first);
            await route.StartAsync(target.Id, [state]); await Task.Delay(400);
            lines.Add("PASS: process loopback opened for " + first.Name);
            var mic = DeviceCatalog.Microphones().FirstOrDefault();
            if (mic != null)
            {
                var microphone = new SourceState(mic); await route.AddAsync(microphone); await Task.Delay(500);
                lines.Add("PASS: microphone and app mixed concurrently (master muted): " + mic.Name);
                await route.RemoveAsync(microphone); lines.Add("PASS: live microphone removal");
            }
            var second = apps.FirstOrDefault(x => x.Id != first.Id);
            if (second != null) { var other = new SourceState(second); await route.AddAsync(other); await Task.Delay(300); await route.RemoveAsync(other); lines.Add("PASS: second app added and removed while running"); }
            bool duplicate = false; try { await route.AddAsync(state); } catch (InvalidOperationException) { duplicate = true; }
            Check(duplicate, "duplicate guard"); lines.Add("PASS: duplicate guard");
            await route.DisposeAsync();
            Check(failure == null, failure ?? "capture events"); lines.Add("PASS: clean stop");
        }
        catch (Exception ex) { Environment.ExitCode = 1; lines.Add("FAIL: " + ex); }
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "integration-test.txt"), lines);
    }
}




