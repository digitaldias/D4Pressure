using Avalonia.Controls;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using D4Pressure.Models;
using D4Pressure.Services;
using System;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using MouseButton = D4Pressure.Services.MouseButton;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace D4Pressure.ViewModels;

public record CharacterProfile(string Name, string Class)
{
    public string DisplayName => $"{Name} ({Class})";
    public string Key         => (Name + "_" + Class).Replace(" ", "_");
}

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private double _windowOpacity = 0.92;
    [ObservableProperty] private KeyRowViewModel? _capturingRow;
    [ObservableProperty] private string _scanStatus = string.Empty;
    [ObservableProperty] private CharacterProfile? _selectedCharacter;
    [ObservableProperty] private bool _isAddingCharacter;
    [ObservableProperty] private string _newCharacterName = string.Empty;
    [ObservableProperty] private string _newCharacterClass = "Necromancer";
    [ObservableProperty] private bool _isCapturingToggleKey;
    [ObservableProperty] private int _toggleVk;
    [ObservableProperty] private string _toggleKeyName = string.Empty;

    public ObservableCollection<KeyRowViewModel>   Rows       { get; } = new();
    public ObservableCollection<CharacterProfile>  Characters { get; } = new();

    public static IReadOnlyList<KeyMode> KeyModes  { get; } = (KeyMode[])Enum.GetValues(typeof(KeyMode));
    public static IReadOnlyList<string>  D4Classes { get; } = ["Barbarian", "Druid", "Necromancer", "Paladin", "Rogue", "Sorcerer", "Spiritborn"];

    // IsCapturing covers both row binding and toggle-key binding
    public bool IsCapturing => CapturingRow is not null || IsCapturingToggleKey;

    public string ToggleKeyDisplay => string.IsNullOrEmpty(ToggleKeyName) ? "— bind —" : ToggleKeyName;

    public string ToggleLabel => IsRunning ? "■   STOP   (F2)" : "▶   START   (F2)";
    public string ToggleColor => IsRunning ? "#dc2626" : "#16a34a";
    public string StatusText  => IsRunning ? "RUNNING" : "IDLE";
    public string StatusColor => IsRunning ? "#22c55e" : "#ef4444";

    partial void OnCapturingRowChanged(KeyRowViewModel? value)    => OnPropertyChanged(nameof(IsCapturing));
    partial void OnIsCapturingToggleKeyChanged(bool value)        => OnPropertyChanged(nameof(IsCapturing));
    partial void OnToggleKeyNameChanged(string value)             => OnPropertyChanged(nameof(ToggleKeyDisplay));

    partial void OnIsRunningChanged(bool value)
    {
        foreach (var row in Rows) row.IsSystemRunning = value;
        OnPropertyChanged(nameof(ToggleLabel));
        OnPropertyChanged(nameof(ToggleColor));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusColor));
    }

    private CancellationTokenSource? _cts;
    private readonly List<(ushort scanCode, bool isMouse, MouseButton mouseBtn)> _heldInputs = new();
    private bool _suppressCharacterChange;
    private CharacterProfile? _previousCharacter;

    public MainViewModel()
    {
        EnsureMinimumRows();
    }

    // ── Character management ──────────────────────────────────────────────────

    partial void OnSelectedCharacterChanging(CharacterProfile? value)
    {
        _previousCharacter = SelectedCharacter;
    }

    partial void OnSelectedCharacterChanged(CharacterProfile? value)
    {
        if (_suppressCharacterChange) return;
        if (IsRunning) StopInternal();
        if (_previousCharacter != null) SaveCharacter(_previousCharacter);
        if (value != null) _ = LoadCharacterAsync(value);
        SaveSettings();
    }

    [RelayCommand]
    private void AddCharacter() => IsAddingCharacter = true;

    [RelayCommand]
    private void CancelAddCharacter()
    {
        IsAddingCharacter = false;
        NewCharacterName = string.Empty;
    }

    [RelayCommand]
    private void ConfirmAddCharacter()
    {
        var name = NewCharacterName.Trim();
        if (string.IsNullOrEmpty(name)) return;

        var profile = new CharacterProfile(name, NewCharacterClass);
        SaveCharacter(profile);

        _suppressCharacterChange = true;
        Characters.Add(profile);
        SelectedCharacter = profile;
        _suppressCharacterChange = false;

        SaveSettings();
        IsAddingCharacter = false;
        NewCharacterName = string.Empty;
    }

    [RelayCommand]
    private void DeleteCharacter()
    {
        var toDelete = SelectedCharacter;
        if (toDelete is null) return;

        var idx = Characters.IndexOf(toDelete);
        try { File.Delete(CharacterFilePath(toDelete)); } catch { }

        _suppressCharacterChange = true;
        Characters.Remove(toDelete);
        _suppressCharacterChange = false;

        if (Characters.Count > 0)
            SelectedCharacter = Characters[Math.Min(idx, Characters.Count - 1)];
        else
        {
            SelectedCharacter = null;
            Rows.Clear();
            EnsureMinimumRows();
        }

        SaveSettings();
    }

    // ── Toggle key binding ────────────────────────────────────────────────────

    [RelayCommand]
    private void StartToggleKeyCapture() => IsCapturingToggleKey = true;

    public void ApplyToggleKey(Key key, int vk)
    {
        ToggleVk      = vk;
        ToggleKeyName = FormatKeyDisplay(key);
        IsCapturingToggleKey = false;
        SaveSettings();
    }

    public void ClearToggleKey()
    {
        ToggleVk             = 0;
        ToggleKeyName        = string.Empty;
        IsCapturingToggleKey = false;
        SaveSettings();
    }

    private static string FormatKeyDisplay(Key key)
    {
        var s = key.ToString();
        if (s.Length == 2 && s[0] == 'D' && char.IsDigit(s[1])) return s[1..];
        return s;
    }

    // ── Toggle / input loop ───────────────────────────────────────────────────

    [RelayCommand]
    private void Toggle()
    {
        if (IsRunning) StopInternal();
        else StartInternal();
    }

    private void StartInternal()
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _heldInputs.Clear();

        foreach (var row in Rows.Where(r => r.IsEnabled && (r.ScanCode > 0 || r.IsMouseInput)))
        {
            if (row.Mode == KeyMode.Hold)
            {
                if (row.IsMouseInput) InputSender.MouseDown(row.MouseButtonIndex);
                else                  InputSender.KeyDown(row.ScanCode);
                _heldInputs.Add((row.ScanCode, row.IsMouseInput, row.MouseButtonIndex));
            }
            else
            {
                var isMouseInput = row.IsMouseInput;
                var mouseBtn     = row.MouseButtonIndex;
                var scanCode     = row.ScanCode;
                var baseDelay    = Math.Max(row.DelayMs, 10);

                Task.Run(async () =>
                {
                    while (!ct.IsCancellationRequested)
                    {
                        if (isMouseInput) InputSender.MouseTap(mouseBtn);
                        else              InputSender.Tap(scanCode);
                        var lo = (int)(baseDelay * 0.8);
                        var hi = (int)(baseDelay * 1.2) + 1;
                        try { await Task.Delay(Random.Shared.Next(lo, hi), ct); }
                        catch (OperationCanceledException) { break; }
                    }
                }, ct);
            }
        }

        IsRunning = true;
        ToggleCommand.NotifyCanExecuteChanged();
    }

    private void StopInternal()
    {
        _cts?.Cancel();
        foreach (var (sc, isMouse, mouseBtn) in _heldInputs)
        {
            if (isMouse) InputSender.MouseUp(mouseBtn);
            else         InputSender.KeyUp(sc);
        }
        _heldInputs.Clear();
        IsRunning = false;
        ToggleCommand.NotifyCanExecuteChanged();
    }

    public void Toggle_Hotkey()
    {
        if (IsRunning) StopInternal();
        else StartInternal();
    }

    // ── Row management ────────────────────────────────────────────────────────

    [RelayCommand]
    private void AddRow()
    {
        var row = new KeyRowViewModel { IsSystemRunning = IsRunning };
        WireRow(row);
        Rows.Add(row);
    }

    private void WireRow(KeyRowViewModel row)
    {
        row.RemoveRequested  += r => Rows.Remove(r);
        row.CaptureRequested += r => CapturingRow = r;
    }

    private void EnsureMinimumRows()
    {
        while (Rows.Count < 6) AddRow();
        if (Rows.Count < 7)
        {
            var space = CreateSpaceRow();
            Rows.Add(space);
        }
    }

    private KeyRowViewModel CreateSpaceRow()
    {
        var row = new KeyRowViewModel
        {
            IsMouseInput    = false,
            Key             = Key.Space,
            ScanCode        = InputSender.VkToScan(0x20),
            Mode            = KeyMode.Tap,
            DelayMs         = 300,
            IsEnabled       = true,
            IsSystemRunning = IsRunning,
            Icon            = CreateSpacebarIcon()
        };
        WireRow(row);
        return row;
    }

    private static Avalonia.Media.Imaging.Bitmap CreateSpacebarIcon()
    {
        const int S = 64;
        using var bmp = new Bitmap(S, S);
        using var g   = System.Drawing.Graphics.FromImage(bmp);
        g.SmoothingMode     = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        g.Clear(Color.FromArgb(12, 12, 28));

        using var glow = new System.Drawing.Drawing2D.LinearGradientBrush(
            new System.Drawing.Point(0, 0), new System.Drawing.Point(0, S),
            Color.FromArgb(30, 80, 120, 200), Color.FromArgb(5, 40, 60, 120));
        g.FillRectangle(glow, 0, 0, S, S);

        var keyRect = new RectangleF(5, 8, S - 10, S - 18);
        using var keyBrush = new SolidBrush(Color.FromArgb(35, 40, 72));
        SpacebarFillRound(g, keyBrush, keyRect, 7);
        using var keyPen = new System.Drawing.Pen(Color.FromArgb(65, 85, 145), 1.5f);
        SpacebarDrawRound(g, keyPen, keyRect, 7);

        var highlightRect = new RectangleF(8, 10, S - 16, 8);
        using var hlBrush = new SolidBrush(Color.FromArgb(30, 120, 160, 255));
        SpacebarFillRound(g, hlBrush, highlightRect, 3);

        var barRect = new RectangleF(8, S - 18, S - 16, 9);
        using var barBrush = new SolidBrush(Color.FromArgb(255, 102, 0));
        SpacebarFillRound(g, barBrush, barRect, 3);
        using var barGlow = new System.Drawing.Pen(Color.FromArgb(180, 255, 140, 30), 1f);
        SpacebarDrawRound(g, barGlow, barRect, 3);

        using var font = new Font("Arial", 9.5f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var sf   = new StringFormat
        {
            Alignment     = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        using var textBrush = new SolidBrush(Color.FromArgb(210, 225, 255));
        var textRect = new RectangleF(5, 14, S - 10, S - 36);
        g.DrawString("SPACE", font, textBrush, textRect, sf);

        using var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        ms.Seek(0, SeekOrigin.Begin);
        return new Avalonia.Media.Imaging.Bitmap(ms);
    }

    private static void SpacebarFillRound(System.Drawing.Graphics g, System.Drawing.Brush b, RectangleF r, float radius)
    {
        using var path = SpacebarRoundPath(r, radius);
        g.FillPath(b, path);
    }

    private static void SpacebarDrawRound(System.Drawing.Graphics g, System.Drawing.Pen p, RectangleF r, float radius)
    {
        using var path = SpacebarRoundPath(r, radius);
        g.DrawPath(p, path);
    }

    private static GraphicsPath SpacebarRoundPath(RectangleF r, float radius)
    {
        float d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X,         r.Y,          d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y,          d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d,   0, 90);
        path.AddArc(r.X,         r.Bottom - d, d, d,  90, 90);
        path.CloseFigure();
        return path;
    }

    // ── Screen scan ───────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task ScanScreen()
    {
        ScanStatus = "Scanning...";
        try
        {
            var icons = await Task.Run(ScreenCapture.CaptureSkillIcons);
            for (int i = 0; i < Math.Min(icons.Length, Rows.Count); i++)
            {
                if (icons[i] is not null)
                    Rows[i].Icon = icons[i];
            }
            ScanStatus = $"Scanned {icons.Count(x => x is not null)} icons";
        }
        catch
        {
            ScanStatus = "Scan failed";
        }
        await Task.Delay(2500);
        ScanStatus = string.Empty;
    }

    // ── Storage paths ─────────────────────────────────────────────────────────

    private static readonly string AppDataDir    = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D4Pressure");
    private static readonly string CharactersDir = Path.Combine(AppDataDir, "characters");
    private static readonly string SettingsPath  = Path.Combine(AppDataDir, "settings.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string CharacterFilePath(CharacterProfile p) =>
        Path.Combine(CharactersDir, p.Key + ".json");

    // ── Serialization helpers ─────────────────────────────────────────────────

    private static string? IconToBase64(Avalonia.Media.Imaging.Bitmap? bmp)
    {
        if (bmp is null) return null;
        try
        {
            using var ms = new System.IO.MemoryStream();
            bmp.Save(ms);
            return Convert.ToBase64String(ms.ToArray());
        }
        catch { return null; }
    }

    private static Avalonia.Media.Imaging.Bitmap? Base64ToIcon(string? b64)
    {
        if (string.IsNullOrEmpty(b64)) return null;
        try
        {
            var bytes = Convert.FromBase64String(b64);
            return new Avalonia.Media.Imaging.Bitmap(new System.IO.MemoryStream(bytes));
        }
        catch { return null; }
    }

    private ConfigModel BuildConfigModel() => new(Rows.Select(r => new KeyRowConfig(
        r.Key.ToString(), r.ScanCode, r.Mode.ToString(),
        r.ActionLabel, r.DelayMs, r.IsEnabled,
        r.IsMouseInput, (int)r.MouseButtonIndex,
        IconToBase64(r.Icon))).ToList());

    // ── Settings (character registry + app-level prefs) ───────────────────────

    // ── Window position (not observable — UI doesn't bind these) ─────────────
    public int?  MainWindowX     { get; set; }
    public int?  MainWindowY     { get; set; }
    public int?  OverlayX        { get; set; }
    public int?  OverlayY        { get; set; }
    public int?  OverlayW        { get; set; }
    public int?  OverlayH        { get; set; }
    public int?  OverlayScreenIdx { get; set; }
    public bool  OverlayVisible   { get; set; }

    public void SaveOverlayGeometry(int x, int y, int w, int h, int screenIdx)
    {
        OverlayX         = x;
        OverlayY         = y;
        OverlayW         = w;
        OverlayH         = h;
        OverlayScreenIdx = screenIdx;
        SaveSettings();
    }

    private record AppSettings(
        List<CharacterEntry>? Characters,
        string? LastCharacterKey,
        int ToggleVk = 0,
        string? ToggleKeyName = null,
        int? MainWindowX     = null,
        int? MainWindowY     = null,
        int? OverlayX        = null,
        int? OverlayY        = null,
        int? OverlayW        = null,
        int? OverlayH        = null,
        int? OverlayScreenIdx = null,
        bool OverlayVisible  = false,
        double OverlayOpacity = 0.92);

    private record CharacterEntry(string Name, string Class);

    private AppSettings? LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return null;
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath));
        }
        catch { return null; }
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(AppDataDir);
            var s = new AppSettings(
                Characters.Select(c => new CharacterEntry(c.Name, c.Class)).ToList(),
                SelectedCharacter?.Key,
                ToggleVk,
                ToggleKeyName,
                MainWindowX,
                MainWindowY,
                OverlayX,
                OverlayY,
                OverlayW,
                OverlayH,
                OverlayScreenIdx,
                OverlayVisible,
                WindowOpacity);
            File.WriteAllText(SettingsPath,
                JsonSerializer.Serialize(s, JsonOptions));
        }
        catch { }
    }

    private void SaveCharacter(CharacterProfile profile)
    {
        try
        {
            Directory.CreateDirectory(CharactersDir);
            File.WriteAllText(CharacterFilePath(profile),
                JsonSerializer.Serialize(BuildConfigModel(), JsonOptions));
        }
        catch { }
    }

    private async Task LoadCharacterAsync(CharacterProfile profile)
    {
        var path = CharacterFilePath(profile);
        if (!File.Exists(path))
        {
            Rows.Clear();
            EnsureMinimumRows();
            return;
        }
        try
        {
            await using var fs = File.OpenRead(path);
            var config = await JsonSerializer.DeserializeAsync<ConfigModel>(fs);
            if (config is null) { EnsureMinimumRows(); return; }
            Rows.Clear();
            foreach (var rc in config.Rows)
            {
                var row = new KeyRowViewModel
                {
                    ActionLabel      = rc.ActionLabel,
                    Mode             = Enum.Parse<KeyMode>(rc.Mode),
                    DelayMs          = rc.DelayMs,
                    IsEnabled        = rc.IsEnabled,
                    ScanCode         = rc.ScanCode,
                    IsMouseInput     = rc.IsMouseInput,
                    MouseButtonIndex = (MouseButton)rc.MouseButtonIndex,
                    IsSystemRunning  = IsRunning,
                    Icon             = Base64ToIcon(rc.IconBase64)
                };
                if (Enum.TryParse<Key>(rc.KeyName, out var k)) row.Key = k;
                WireRow(row);
                Rows.Add(row);
            }
            EnsureMinimumRows();
        }
        catch { EnsureMinimumRows(); }
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public async void AutoLoad()
    {
        var settings = LoadSettings();
        Characters.Clear();
        if (settings?.Characters is { } list)
            foreach (var e in list)
                Characters.Add(new CharacterProfile(e.Name, e.Class));

        // Restore app-level prefs
        ToggleVk      = settings?.ToggleVk ?? 0;
        ToggleKeyName = settings?.ToggleKeyName ?? string.Empty;

        // Restore window positions — signal windows via PropertyChanged
        MainWindowX = settings?.MainWindowX;
        MainWindowY = settings?.MainWindowY;
        OverlayX         = settings?.OverlayX;
        OverlayY         = settings?.OverlayY;
        OverlayW         = settings?.OverlayW;
        OverlayH         = settings?.OverlayH;
        OverlayScreenIdx = settings?.OverlayScreenIdx;
        OverlayVisible   = settings?.OverlayVisible ?? false;
        WindowOpacity    = settings?.OverlayOpacity ?? 0.92;
        OnPropertyChanged(nameof(MainWindowX));
        OnPropertyChanged(nameof(OverlayVisible));

        var lastKey = settings?.LastCharacterKey;
        var last = Characters.FirstOrDefault(c => c.Key == lastKey) ?? Characters.FirstOrDefault();

        _suppressCharacterChange = true;
        SelectedCharacter = last;
        _suppressCharacterChange = false;

        if (last != null)
        {
            Rows.Clear();
            await LoadCharacterAsync(last);
        }
    }

    /// <summary>Called synchronously from OnClosed — must not be async.</summary>
    public void SaveOnExit()
    {
        try
        {
            if (SelectedCharacter != null) SaveCharacter(SelectedCharacter);
            SaveSettings();
        }
        catch { }
    }
}
