using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace DuckyMicrophone;

internal enum SourceKind { App, Microphone }
internal sealed record SourceChoice(SourceKind Kind, string Id, string Name, long Started = 0)
{
    public override string ToString() => Name;
}
internal sealed class SourceState(SourceChoice choice)
{
    public SourceChoice Choice { get; } = choice;
    public volatile float Volume = 1f;
    public volatile bool Muted;
    public volatile float Peak;
}

// Playback clocks this mixer. Silent or stopped sources contribute silence.
// No fixed source-count limit; changes and reads share one short lock.
internal sealed class FloatMixer : ISampleProvider
{
    private readonly object sync = new();
    private readonly List<ISampleProvider> inputs = [];
    private float[] scratch = [];
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
    public void Add(ISampleProvider input)
    {
        if (input.WaveFormat.SampleRate != 48000 || input.WaveFormat.Channels != 2)
            throw new ArgumentException("Mixer inputs must be 48 kHz stereo.");
        lock (sync) inputs.Add(input);
    }
    public void Remove(ISampleProvider input) { lock (sync) inputs.Remove(input); }
    public int Read(float[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public int Read(Span<float> buffer)
    {
        int count = buffer.Length; buffer.Clear();
        lock (sync)
        {
            if (scratch.Length < count) scratch = new float[count];
            foreach (var input in inputs)
            {
                int read = input.Read(scratch.AsSpan(0, count));
                for (int i = 0; i < read; i++) buffer[i] += scratch[i];
            }
        }
        return count;
    }
}
internal sealed class GainStage(ISampleProvider source, SourceState state, bool limit = false) : ISampleProvider
{
    public WaveFormat WaveFormat => source.WaveFormat;
    public volatile bool Robot;
    public volatile bool Clipping;
    private long frame;
    public int Read(float[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public int Read(Span<float> buffer)
    {
        int read = source.Read(buffer);
        float gain = state.Muted ? 0 : state.Volume, peak = 0;
        bool clipping = false;
        for (int i = 0; i < read; i++)
        {
            float value = buffer[i] * gain;
            if (Robot) value *= (float)Math.Sin(2 * Math.PI * 42 * frame / WaveFormat.SampleRate);
            clipping |= Math.Abs(value) > 1;
            if (limit) value = Math.Clamp(value, -1, 1);
            buffer[i] = value;
            peak = Math.Max(peak, Math.Abs(value));
            if ((i + 1) % WaveFormat.Channels == 0) frame++;
        }
        state.Peak = peak; Clipping = clipping;
        return read;
    }
}
internal sealed class SourceCapture : IAsyncDisposable
{
    public SourceState State { get; }
    public GainStage Samples { get; }
    private readonly WasapiRecorder recorder;
    private readonly MMDevice? device;
    private volatile bool stopping;
    public event Action<string>? Failed;
    private SourceCapture(SourceState state, WasapiRecorder recorder, MMDevice? device)
    {
        State = state; this.recorder = recorder; this.device = device;
        var buffer = new CaptureBuffer(recorder.WaveFormat);
        ISampleProvider samples = buffer.ToSampleProvider();
        if (samples.WaveFormat.Channels == 1) samples = new MonoToStereoSampleProvider(samples);
        if (samples.WaveFormat.Channels != 2) throw new NotSupportedException("Choose a mono or stereo input.");
        if (samples.WaveFormat.SampleRate != 48000) samples = new WdlResamplingSampleProvider(samples, 48000);
        Samples = new GainStage(samples, state);
        recorder.DataAvailable += (data, flags, _, _) =>
        {
            if (stopping) return;
            try
            {
                buffer.Add(data);
            }
            catch (Exception ex) { if (!stopping) Failed?.Invoke(ex.Message); }
        };
        recorder.RecordingStopped += (_, e) => { if (!stopping) Failed?.Invoke(e.Exception?.Message ?? "Capture stopped. Remove and re-add this source."); };
    }
    public static async Task<SourceCapture> CreateAsync(SourceState state)
    {
        MMDevice? device = null;
        WasapiRecorder? recorder = null;
        try
        {
            var builder = new WasapiRecorderBuilder().WithFormat(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)).WithBufferLength(40);
            if (state.Choice.Kind == SourceKind.App)
            {
                if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348)) throw new PlatformNotSupportedException("App capture needs Windows build 20348 or later.");
                using var process = System.Diagnostics.Process.GetProcessById(int.Parse(state.Choice.Id));
                if (state.Choice.Started != 0 && process.StartTime.ToUniversalTime().Ticks != state.Choice.Started)
                    throw new InvalidOperationException("This app restarted. Refresh and add it again.");
                builder.WithProcessLoopback((uint)process.Id, ProcessLoopbackMode.IncludeTargetProcessTree);
            }
            else
            {
                using var enumerator = new MMDeviceEnumerator();
                device = enumerator.GetDevice(state.Choice.Id);
                builder.WithDevice(device);
            }
            recorder = await builder.BuildAsync();
            return new SourceCapture(state, recorder, device);
        }
        catch { if (recorder != null) await recorder.DisposeAsync(); device?.Dispose(); throw; }
    }
    public void Start() => recorder.StartRecording();
    public async ValueTask DisposeAsync()
    {
        stopping = true;
        await recorder.DisposeAsync(); device?.Dispose(); State.Peak = 0;
    }
}
internal sealed class AudioRouter : IAsyncDisposable
{
    private readonly FloatMixer mixer = new();
    private readonly Dictionary<SourceState, SourceCapture> captures = [];
    private WasapiPlayer? player;
    private MMDevice? target;
    private GainStage? masterStage;
    private volatile bool stopping;
    public SourceState Master { get; } = new(new SourceChoice(SourceKind.App, "master", "Master")) { Volume = 1f };
    public bool Clipping => masterStage?.Clipping ?? false;
    public bool Robot { set { if (masterStage != null) masterStage.Robot = value; } }
    public event Action<string>? Failed;
    public async Task StartAsync(string destination, IEnumerable<SourceState> sources)
    {
        stopping = false;
        try
        {
            using var enumerator = new MMDeviceEnumerator(); target = enumerator.GetDevice(destination);
            masterStage = new GainStage(mixer, Master, true);
            ISampleProvider stream = masterStage;
            using var client = target.CreateAudioClient();
            int rate = client.MixFormat.SampleRate;
            if (rate != 48000) stream = new WdlResamplingSampleProvider(stream, rate);
            player = new WasapiPlayerBuilder().WithDevice(target).WithSharedMode().WithLatency(80).Build();
            player.Init(stream.ToWaveProvider());
            player.PlaybackStopped += (_, e) => { if (!stopping) Failed?.Invoke(e.Exception?.Message ?? "Output disconnected."); };
            foreach (var source in sources) await AddAsync(source);
            player.Play();
        }
        catch { await DisposeAsync(); throw; }
    }
    public async Task AddAsync(SourceState source)
    {
        if (captures.Keys.Any(x => x.Choice.Kind == source.Choice.Kind && x.Choice.Id == source.Choice.Id))
            throw new InvalidOperationException("That source is already in your mix.");
        if (source.Choice.Kind == SourceKind.App && source.Choice.Id == Environment.ProcessId.ToString())
            throw new InvalidOperationException("Ducky cannot capture itself.");
        // Never feed a cable's recording side back into its own playback side.
        if (source.Choice.Kind == SourceKind.Microphone && DeviceCatalog.IsVirtual(source.Choice.Name))
            throw new InvalidOperationException("Use a physical microphone here to avoid a virtual-cable feedback loop.");
        var capture = await SourceCapture.CreateAsync(source);
        capture.Failed += error => { if (!stopping) Failed?.Invoke(source.Choice.Name + ": " + error); };
        try { mixer.Add(capture.Samples); capture.Start(); captures.Add(source, capture); }
        catch { mixer.Remove(capture.Samples); await capture.DisposeAsync(); throw; }
    }
    public async Task RemoveAsync(SourceState source)
    {
        if (!captures.Remove(source, out var capture)) return;
        mixer.Remove(capture.Samples); await capture.DisposeAsync();
    }
    public async ValueTask DisposeAsync()
    {
        stopping = true;
        var oldPlayer = player; player = null;
        if (oldPlayer != null) { try { oldPlayer.Stop(); } finally { oldPlayer.Dispose(); } }
        foreach (var source in captures.Keys.ToArray()) await RemoveAsync(source);
        target?.Dispose(); target = null; masterStage = null; Master.Peak = 0;
    }
}



