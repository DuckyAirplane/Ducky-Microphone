using System.Drawing.Drawing2D;
namespace DuckyMicrophone;
internal sealed class DuckyForm : Form
{
    private static readonly Color Background = Color.FromArgb(20, 25, 35), PanelColor = Color.FromArgb(32, 40, 54), Yellow = Color.FromArgb(255, 211, 92), Ink = Color.FromArgb(239, 243, 250);
    private readonly ComboBox kind = Combo(), picker = Combo(), output = Combo();
    private readonly Button add = Button("+ Add source"), refresh = Button("Refresh apps"), start = Button("START MIXING");
    private readonly FlowLayoutPanel rows = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
    private readonly Label status = MakeLabel("Ready. Add an app, a microphone, or both."), count = MakeLabel("0 sources"), hint = MakeLabel(""), masterLabel = MakeLabel("Master · 100%");
    private readonly TrackBar masterVolume = new() { Minimum = 0, Maximum = 100, Value = 100, TickStyle = TickStyle.None, Width = 180 };
    private readonly CheckBox masterMute = new() { Text = "Mute all", AutoSize = true }, robot = new() { Text = "Robot duck", AutoSize = true };
    private readonly LevelMeter masterMeter = new() { Width = 130, Height = 24 };
    private readonly List<SourceRow> sourceRows = [];
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 70 };
    private AudioRouter? router;
    private bool busy, closing, allowClose;
    public DuckyForm()
    {
        Name = "DuckyMicrophone"; Text = "DuckyMicrophone · many sounds, one microphone";
        Font = new Font("Segoe UI", 10); BackColor = Background; ForeColor = Ink; Size = new Size(1050, 860); MinimumSize = new Size(900, 760);
        StartPosition = FormStartPosition.CenterScreen;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 8 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (int height in new[] { 108, 74, 76, 28 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        foreach (int height in new[] { 60, 58, 60 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        Controls.Add(layout);
        var hero = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        hero.Controls.Add(new DuckFace { Width = 90, Height = 85, Margin = new Padding(0, 0, 20, 0) });
        var titles = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, Width = 730, Height = 100 };
        var title = MakeLabel("DuckyMicrophone"); title.Font = new Font("Segoe UI", 27, FontStyle.Bold); title.ForeColor = Yellow;
        titles.Controls.Add(title); titles.Controls.Add(MakeLabel("Many sounds. One microphone. A suspicious number of ducks.")); hero.Controls.Add(titles); layout.Controls.Add(hero);
        var dest = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        dest.Controls.Add(MakeLabel("MIX OUTPUT  →  choose your virtual cable")); output.Width = 570; dest.Controls.Add(output); layout.Controls.Add(dest);
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, Padding = new Padding(0, 8, 0, 0) };
        kind.Items.AddRange(["App audio", "Microphone"]); kind.Width = 145; picker.Width = 365;
        bar.Controls.AddRange([kind, picker, add, refresh]); layout.Controls.Add(bar);
        count.ForeColor = Yellow; layout.Controls.Add(count); layout.Controls.Add(rows);
        var master = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = PanelColor, Padding = new Padding(8), WrapContents = false };
        masterLabel.Width = 120; masterLabel.AutoSize = false;
        master.Controls.AddRange([masterLabel, masterVolume, masterMute, robot, masterMeter]); layout.Controls.Add(master);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0) }; start.Width = 220; start.Height = 40;
        actions.Controls.Add(start); status.AutoSize = false; status.Width = 660; status.Height = 44; actions.Controls.Add(status); layout.Controls.Add(actions);
        hint.AutoSize = false; hint.Dock = DockStyle.Fill; hint.ForeColor = Color.LightSteelBlue; layout.Controls.Add(hint);
        kind.SelectedIndexChanged += (_, _) => RefreshPicker(); kind.SelectedIndex = 0;
        refresh.Click += (_, _) => { RefreshPicker(); if (router == null) RefreshOutputs(); };
        add.Click += async (_, _) => await Guard(AddSelectedAsync);
        start.Click += async (_, _) => await Guard(ToggleAsync);
        masterVolume.ValueChanged += (_, _) => { masterLabel.Text = $"Master · {masterVolume.Value}%"; Configure(); };
        masterMute.CheckedChanged += (_, _) => Configure(); robot.CheckedChanged += (_, _) => Configure();
        output.SelectedIndexChanged += (_, _) => UpdateHint();
        rows.SizeChanged += (_, _) => ResizeRows();
        timer.Tick += (_, _) =>
        {
            foreach (var row in sourceRows) row.UpdateMeter();
            masterMeter.Level = router?.Master.Peak ?? 0; masterMeter.Invalidate();
            if (router?.Clipping == true) status.Text = "Mix is too loud. Lower source or master volumes.";
        };
        timer.Start(); Shown += (_, _) => RefreshOutputs();
        FormClosing += async (_, e) =>
        {
            if (allowClose) return;
            e.Cancel = true; if (closing) return; closing = true;
            while (busy) await Task.Delay(50);
            await StopAsync(); allowClose = true; Close();
        };
    }
    private static ComboBox Combo() => new() { DropDownStyle = ComboBoxStyle.DropDownList, ForeColor = Color.Black, BackColor = Color.White, DropDownWidth = 650, Margin = new Padding(0, 3, 10, 3) };
    internal static Label MakeLabel(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(3, 6, 12, 3) };
    internal static Button Button(string text) => new() { Text = text, AutoSize = false, Width = 135, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Yellow, ForeColor = Background, Cursor = Cursors.Hand, Margin = new Padding(0, 2, 10, 2) };
    private void RefreshPicker()
    {
        try
        {
            var previous = picker.SelectedItem as SourceChoice;
            var options = kind.SelectedIndex == 1 ? DeviceCatalog.Microphones() : DeviceCatalog.Apps();
            picker.Items.Clear(); picker.Items.AddRange(options.Cast<object>().ToArray());
            picker.SelectedItem = options.FirstOrDefault(x => x.Id == previous?.Id) ?? options.FirstOrDefault();
            if (options.Count == 0) status.Text = "Open an app and play a sound, then refresh.";
        }
        catch (Exception ex) { status.Text = "Couldn't list sources: " + ex.Message; }
    }
    private void RefreshOutputs()
    {
        try
        {
            var id = (output.SelectedItem as DeviceChoice)?.Id; var devices = DeviceCatalog.Outputs();
            output.Items.Clear(); output.Items.AddRange(devices.Cast<object>().ToArray());
            output.SelectedItem = devices.FirstOrDefault(x => x.Id == id) ?? devices.FirstOrDefault(x => x.Name.StartsWith("CABLE Input")) ?? devices.FirstOrDefault(x => DeviceCatalog.IsVirtual(x.Name));
            UpdateHint();
        }
        catch (Exception ex) { status.Text = "Couldn't list outputs: " + ex.Message; }
    }
    private void UpdateHint()
    {
        hint.Text = output.SelectedItem is DeviceChoice d && DeviceCatalog.IsVirtual(d.Name)
            ? "In your call, game, or recorder, select the matching cable's recording input (e.g. CABLE Output).\nApp capture includes its child processes; browsers include all tabs. Use headphones to keep your mic from picking up speakers."
            : "Choose a virtual cable to use the mix as a microphone. A regular playback output only plays the mix there.\nApp sources are captured directly. No speaker source selection required.";
    }
    private async Task Guard(Func<Task> action)
    {
        if (busy || closing) return; busy = true; SetBusy(true);
        try { await action(); } catch (Exception ex) { status.Text = ex.Message; }
        finally { busy = false; if (!IsDisposed) SetBusy(false); }
    }
    private void SetBusy(bool value)
    {
        add.Enabled = refresh.Enabled = start.Enabled = !value;
        kind.Enabled = picker.Enabled = !value;
        output.Enabled = !value && router == null;
        foreach (var row in sourceRows) row.Remove.Enabled = !value;
    }
    private async Task AddSelectedAsync()
    {
        if (picker.SelectedItem is not SourceChoice choice) throw new InvalidOperationException("Pick an app or microphone first.");
        if (sourceRows.Any(x => x.State.Choice.Kind == choice.Kind && x.State.Choice.Id == choice.Id)) throw new InvalidOperationException("That source is already in your mix.");
        var state = new SourceState(choice);
        if (router != null) await router.AddAsync(state);
        var row = new SourceRow(state); sourceRows.Add(row); rows.Controls.Add(row);
        row.Remove.Click += async (_, _) => await Guard(async () =>
        {
            if (router != null) await router.RemoveAsync(state);
            sourceRows.Remove(row); rows.Controls.Remove(row); row.Dispose(); UpdateCount();
        });
        UpdateCount(); ResizeRows(); status.Text = "Added " + choice.Name;
    }
    private void UpdateCount() => count.Text = $"{sourceRows.Count} source{(sourceRows.Count == 1 ? "" : "s")} · Add one or many. 100% = original level. Above 100% boosts quiet sources.";
    private void ResizeRows() { foreach (var row in sourceRows) row.Width = Math.Max(760, rows.ClientSize.Width - 26); }
    private void Configure()
    {
        if (router == null) return;
        router.Master.Volume = masterVolume.Value / 100f; router.Master.Muted = masterMute.Checked; router.Robot = robot.Checked;
    }
    private async Task ToggleAsync()
    {
        if (router != null) { await StopAsync(); return; }
        if (sourceRows.Count == 0) throw new InvalidOperationException("Add at least one app or microphone.");
        if (output.SelectedItem is not DeviceChoice target) throw new InvalidOperationException("Choose your mix output.");
        var current = new AudioRouter(); router = current;
        current.Master.Volume = masterVolume.Value / 100f; current.Master.Muted = masterMute.Checked;
        current.Failed += message =>
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke((Action)(async () =>
            {
                while (busy && !closing) await Task.Delay(50);
                if (router != current || closing) return;
                await Guard(async () => { await StopAsync(); status.Text = "Mix stopped: " + message; });
            })); } catch (InvalidOperationException) { }
        };
        status.Text = "Opening your sources…";
        try { await current.StartAsync(target.Id, sourceRows.Select(x => x.State)); Configure(); start.Text = "STOP MIXING"; status.Text = "Live. Your sources are sharing one microphone."; }
        catch { await StopAsync(); throw; }
    }
    private async Task StopAsync()
    {
        var old = router; router = null;
        if (old != null) await old.DisposeAsync();
        start.Text = "START MIXING"; status.Text = "Stopped. Ducky is off duty."; output.Enabled = true;
    }
    internal async Task CheckInterfaceAsync()
    {
        if (output.SelectedItem == null) throw new Exception("Output selection was empty.");
        kind.SelectedIndex = 0; RefreshPicker(); await AddSelectedAsync();
        if (sourceRows.Count != 1) throw new Exception("Single-source selection failed.");
        bool duplicate = false;
        try { await AddSelectedAsync(); } catch (InvalidOperationException) { duplicate = true; }
        if (!duplicate) throw new Exception("Duplicate source was accepted.");
        kind.SelectedIndex = 1; RefreshPicker(); await AddSelectedAsync();
        if (sourceRows.Count != 2) throw new Exception("Multi-source selection failed.");
        using (var bitmap = new Bitmap(Width, Height))
        {
            DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height));
            bitmap.Save(Path.Combine(AppContext.BaseDirectory, "mixer-preview.png"));
        }
        sourceRows[1].Remove.PerformClick();
        if (sourceRows.Count != 1) throw new Exception("Remove control failed.");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "ui-test.txt"), "PASS: selected output, populated app picker, single source, duplicate prevention, app plus microphone, remove control. No audio started.");
    }
    protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
}
internal sealed class SourceRow : Panel
{
    public SourceState State { get; }
    public Button Remove { get; } = DuckyForm.Button("Remove");
    private readonly LevelMeter meter = new() { Width = 95, Height = 20 };
    public SourceRow(SourceState state)
    {
        State = state; Height = 82; BackColor = Color.FromArgb(32, 40, 54); Margin = new Padding(0, 0, 0, 9);
        var name = DuckyForm.MakeLabel(state.Choice.Name); name.AutoSize = false; name.Width = 295; name.Height = 28; name.Location = new Point(10, 4);
        var type = DuckyForm.MakeLabel(state.Choice.Kind == SourceKind.App ? "APP AUDIO" : "MICROPHONE"); type.Font = new Font("Segoe UI", 8); type.ForeColor = Color.LightSteelBlue; type.Location = new Point(12, 43);
        var volume = new TrackBar { Minimum = 0, Maximum = 400, Value = 100, TickStyle = TickStyle.None, Width = 175, Location = new Point(310, 15) };
        var percent = DuckyForm.MakeLabel("100%"); percent.Location = new Point(487, 19);
        var mute = new CheckBox { Text = "Mute", AutoSize = true, Location = new Point(545, 23) };
        meter.Location = new Point(625, 23); Remove.Width = 85; Remove.Location = new Point(740, 16);
        Controls.AddRange([name, type, volume, percent, mute, meter, Remove]);
        volume.ValueChanged += (_, _) => { State.Volume = volume.Value / 100f; percent.Text = $"{volume.Value}%"; };
        mute.CheckedChanged += (_, _) => State.Muted = mute.Checked;
        SizeChanged += (_, _) => { Remove.Left = Width - 95; meter.Left = Width - 205; };
    }
    public void UpdateMeter() { meter.Level = State.Peak; meter.Invalidate(); }
}
internal sealed class LevelMeter : Control
{
    public float Level;
    public LevelMeter() { DoubleBuffered = true; }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Color.FromArgb(48, 58, 75));
        using var brush = new SolidBrush(Level >= 0.98f ? Color.Salmon : Color.FromArgb(255, 211, 92));
        e.Graphics.FillRectangle(brush, 0, 0, Width * Math.Clamp(Level, 0, 1), Height);
    }
}
internal sealed class DuckFace : Control
{
    public DuckFace() { DoubleBuffered = true; }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        using var yellow = new SolidBrush(Color.FromArgb(255, 211, 92));
        g.FillEllipse(yellow, 9, 37, 67, 40); g.FillEllipse(yellow, 31, 8, 46, 47);
        g.FillPolygon(Brushes.Orange, [new Point(69, 30), new Point(89, 38), new Point(68, 43)]);
        g.FillEllipse(Brushes.Black, 59, 22, 7, 9); g.FillEllipse(Brushes.White, 60, 23, 2, 3);
        using var wing = new Pen(Color.Goldenrod, 3); g.DrawArc(wing, 20, 41, 36, 25, 0, 150);
    }
}



