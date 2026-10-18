using NAudio.Wave;
namespace DuckyMicrophone;

// Keep a small reserve between independent capture/playback clocks. Never discard
// the entire queue after a scheduling delay, and flush short soundboard clips too.
internal sealed class CaptureBuffer : IWaveProvider
{
    private readonly object sync = new();
    private readonly BufferedWaveProvider buffer;
    private readonly Func<long> clock;
    private readonly byte[] discard;
    private bool primed;
    private long firstPacket = -1;
    public WaveFormat WaveFormat => buffer.WaveFormat;
    public CaptureBuffer(WaveFormat format, Func<long>? clock = null)
    {
        buffer = new BufferedWaveProvider(format, TimeSpan.FromMilliseconds(600)) { ReadFully = true, DiscardOnBufferOverflow = true };
        this.clock = clock ?? (() => Environment.TickCount64);
        discard = new byte[format.AverageBytesPerSecond];
    }
    public void Add(ReadOnlySpan<byte> data)
    {
        lock (sync)
        {
            if (data.IsEmpty) return;
            if (firstPacket < 0) firstPacket = clock();
            int max = Bytes(300);
            if (data.Length > max) data = data[^max..];
            int surplus = buffer.BufferedBytes + data.Length - max;
            if (surplus > 0)
            {
                int drop = Math.Min(buffer.BufferedBytes, surplus + Bytes(80));
                drop -= drop % WaveFormat.BlockAlign;
                buffer.Read(discard.AsSpan(0, drop));
            }
            buffer.AddSamples(data);
        }
    }
    private int Bytes(int milliseconds) => WaveFormat.AverageBytesPerSecond * milliseconds / 1000 / WaveFormat.BlockAlign * WaveFormat.BlockAlign;
    public int Read(Span<byte> destination)
    {
        lock (sync)
        {
            int available = buffer.BufferedBytes;
            if (available == 0)
            {
                primed = false; firstPacket = -1; destination.Clear(); return destination.Length;
            }
            // The timeout ensures a clip shorter than the reserve still plays.
            if (!primed && available < destination.Length + Bytes(40) && clock() - firstPacket < 100)
            {
                destination.Clear(); return destination.Length;
            }
            primed = true;
            buffer.Read(destination);
            if (available < destination.Length) { primed = false; firstPacket = -1; }
            return destination.Length;
        }
    }
}
