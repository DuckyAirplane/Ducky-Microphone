using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
namespace DuckyMicrophone;
internal sealed record DeviceChoice(string Id, string Name)
{
    public override string ToString() => Name;
}
internal static class DeviceCatalog
{
    public static bool IsVirtual(string name) => new[] { "cable", "voicemeeter", "virtual" }.Any(x => name.Contains(x, StringComparison.OrdinalIgnoreCase));
    public static List<DeviceChoice> Outputs()
    {
        using var e = new MMDeviceEnumerator(); var list = new List<DeviceChoice>();
        foreach (var d in e.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            using (d) list.Add(new(d.ID, d.FriendlyName));
        return list;
    }
    public static List<SourceChoice> Microphones()
    {
        using var e = new MMDeviceEnumerator(); var list = new List<SourceChoice>();
        foreach (var d in e.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            using (d) if (!IsVirtual(d.FriendlyName)) list.Add(new(SourceKind.Microphone, d.ID, d.FriendlyName));
        return list;
    }
    public static List<SourceChoice> Apps()
    {
        var parents = Parents();
        var processes = new Dictionary<int, (string Name, string Title, long Started, bool Window)>();
        foreach (var p in Process.GetProcesses())
            using (p) try { processes[p.Id] = (p.ProcessName, p.MainWindowTitle, p.StartTime.ToUniversalTime().Ticks, p.MainWindowHandle != IntPtr.Zero); } catch { }
        var audible = new HashSet<int>();
        using (var e = new MMDeviceEnumerator())
            foreach (var d in e.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                using (d) try
                {
                    var sessions = d.AudioSessionManager.Sessions;
                    for (int i = 0; i < sessions.Count; i++) using (var s = sessions[i]) audible.Add((int)s.GetProcessID);
                }
                catch { }
        var roots = new HashSet<int>();
        foreach (var pair in processes.Where(x => x.Value.Window || audible.Contains(x.Key)))
        {
            int id = pair.Key; var seen = new HashSet<int>();
            while (seen.Add(id) && parents.TryGetValue(id, out int parent) && processes.TryGetValue(parent, out var ancestor) && ancestor.Name == pair.Value.Name) id = parent;
            if (id != Environment.ProcessId && id != 0) roots.Add(id);
        }
        return roots.Where(processes.ContainsKey).Select(id => new SourceChoice(SourceKind.App, id.ToString(), $"{processes[id].Name} · PID {id}", processes[id].Started)).OrderBy(x => x.Name).ToList();
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size, Usage, Id; public IntPtr Heap; public uint Module, Threads, Parent; public int Priority; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name;
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint id);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32FirstW(IntPtr handle, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32NextW(IntPtr handle, ref ProcessEntry entry);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    private static Dictionary<int, int> Parents()
    {
        var result = new Dictionary<int, int>(); IntPtr handle = CreateToolhelp32Snapshot(2, 0);
        if (handle == new IntPtr(-1)) return result;
        try
        {
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>(), Name = "" };
            if (Process32FirstW(handle, ref entry)) do { result[(int)entry.Id] = (int)entry.Parent; } while (Process32NextW(handle, ref entry));
        }
        finally { CloseHandle(handle); }
        return result;
    }
}
