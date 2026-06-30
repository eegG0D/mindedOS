using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MindedOS.Ai;
using MindedOS.Engine;
using MindedOS.Sensor;
using Path = System.IO.Path;

namespace MindedOS.Shell;

/// <summary>
/// Brain-Machine-Interface mini-game: move a character in 4 directions from live
/// EEG (collecting targets), or replay a recorded EEG CSV in order. With no CSV in
/// the folder it records continuously in rolling 5-minute files until you exit.
/// </summary>
public partial class BmiGameWindow : Window
{
    private const string CsvHeader =
        "t_sec,attention,meditation,blink,signal,delta,theta,lowAlpha,highAlpha,lowBeta,highBeta,lowGamma,midGamma";

    private readonly OsContext _os;
    private readonly BmiConfig _cfg;
    private readonly string _mapPath;
    private readonly string _dir;

    private VehicleMoveMap _map;
    private BmiCharacter _char = new();
    private Ellipse _dot = null!, _target = null!;
    private readonly DispatcherTimer _gameTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly DispatcherTimer _dataTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    private bool _running = true;
    private bool _playback;

    // playback
    private readonly List<double[]> _rows = new();
    private Dictionary<string, int> _cols = new(StringComparer.OrdinalIgnoreCase);
    private int _rowIdx;

    // live rolling recorder
    private readonly List<string> _segRows = new();
    private DateTime _segStart;
    private int _segSecond;

    public BmiGameWindow(OsContext os, BmiConfig cfg)
    {
        _os = os;
        _cfg = cfg;
        _dir = string.IsNullOrWhiteSpace(cfg.OutputDir)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "mindedOS", "bmi")
            : cfg.OutputDir;
        _mapPath = MindedOS.Core.DataFile.Resolve(Path.Combine(AppContext.BaseDirectory, "data", string.IsNullOrWhiteSpace(cfg.MapFile) ? "eeg_map_bmi.csv" : cfg.MapFile));
        InitializeComponent();

        _map = File.Exists(_mapPath) ? VehicleMoveMap.Load(_mapPath) : VehicleMoveMap.Parse("0,default,,,idle");

        Loaded += OnLoaded;
        Closed += (_, _) =>
        {
            _gameTimer.Stop(); _dataTimer.Stop();
            if (!_playback) FlushSegment(); // save the final recording on exit
        };
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        BuildBoard();
        Directory.CreateDirectory(_dir);

        // No CSV → live + record; a CSV exists → play it back in order.
        var csvs = Directory.EnumerateFiles(_dir, "*.csv").OrderBy(f => f).ToList();
        if (csvs.Count > 0 && TryLoadPlayback(csvs[0]))
        {
            _playback = true;
        }
        else
        {
            _playback = false;
            _segRows.Clear();
            _segStart = DateTime.UtcNow;
            _segSecond = 0;
        }

        if (_os.Source.State != LinkState.Streaming) await _os.Source.ConnectAsync();

        _gameTimer.Tick += OnGameTick;
        _dataTimer.Tick += OnDataTick;
        _gameTimer.Start();
        _dataTimer.Start();
    }

    // ===== board ===========================================================
    private void BuildBoard()
    {
        var grid = new SolidColorBrush(Color.FromRgb(0x1E, 0x2A, 0x33));
        for (double x = 0; x <= GameCanvas.Width; x += 60)
            GameCanvas.Children.Add(new Line { X1 = x, Y1 = 0, X2 = x, Y2 = GameCanvas.Height, Stroke = grid, StrokeThickness = 1 });
        for (double y = 0; y <= GameCanvas.Height; y += 60)
            GameCanvas.Children.Add(new Line { X1 = 0, Y1 = y, X2 = GameCanvas.Width, Y2 = y, Stroke = grid, StrokeThickness = 1 });

        _char = new BmiCharacter(GameCanvas.Width, GameCanvas.Height, seed: Environment.TickCount);
        _target = new Ellipse { Width = 22, Height = 22, Fill = Brushes.Gold, Stroke = Brushes.Black, StrokeThickness = 1.5 };
        _dot = new Ellipse { Width = 24, Height = 24, Fill = new SolidColorBrush(Color.FromRgb(0x39, 0xFF, 0x14)), Stroke = Brushes.Black, StrokeThickness = 2 };
        GameCanvas.Children.Add(_target);
        GameCanvas.Children.Add(_dot);
        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        Canvas.SetLeft(_dot, _char.X - 12); Canvas.SetTop(_dot, _char.Y - 12);
        Canvas.SetLeft(_target, _char.TargetX - 11); Canvas.SetTop(_target, _char.TargetY - 11);
    }

    // ===== loops ===========================================================
    private Func<string, double> Source()
    {
        if (_playback && _rows.Count > 0)
        {
            var row = _rows[_rowIdx % _rows.Count];
            return name => _cols.TryGetValue(name, out int i) && i < row.Length ? row[i] : 0;
        }
        return _os.Signals.GetSignal;
    }

    private void OnGameTick(object? sender, EventArgs e)
    {
        string move = _map.Resolve(Source());
        _char.Step(move);
        UpdateVisuals();

        string mode = _playback ? $"PLAYBACK {_rowIdx % Math.Max(1, _rows.Count) + 1}/{_rows.Count}" : "LIVE ● REC";
        Hud.Text = $"{mode,-18} MOVE {move.ToUpperInvariant(),-6} att {_os.Signals.Attention,3} med {_os.Signals.Meditation,3} | SCORE {_char.Score}";
    }

    private void OnDataTick(object? sender, EventArgs e)
    {
        if (_playback) { _rowIdx++; return; }   // advance the recording in order

        // live: append a 1 Hz row; roll to a new file every recordSeconds (5 min)
        var b = _os.Signals.Bands;
        _segRows.Add(string.Create(CultureInfo.InvariantCulture,
            $"{_segSecond},{_os.Signals.Attention},{_os.Signals.Meditation},{_os.Signals.LastBlink},{_os.Signals.SignalNoise},{b.Delta},{b.Theta},{b.LowAlpha},{b.HighAlpha},{b.LowBeta},{b.HighBeta},{b.LowGamma},{b.MidGamma}"));
        _segSecond++;

        if ((DateTime.UtcNow - _segStart).TotalSeconds >= Math.Max(5, _cfg.RecordSeconds))
        {
            FlushSegment();
            _segStart = DateTime.UtcNow;
            _segSecond = 0;
        }
    }

    private void FlushSegment()
    {
        if (_segRows.Count == 0) return;
        try
        {
            Directory.CreateDirectory(_dir);
            var path = Path.Combine(_dir, $"bmi_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            var lines = new List<string> { CsvHeader };
            lines.AddRange(_segRows);
            File.WriteAllLines(path, lines);
        }
        catch { /* ignore */ }
        _segRows.Clear();
    }

    private bool TryLoadPlayback(string path)
    {
        try
        {
            var lines = File.ReadAllLines(path);
            if (lines.Length < 2) return false;
            var header = lines[0].Split(',');
            _cols = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < header.Length; i++) _cols[header[i].Trim()] = i;

            _rows.Clear();
            for (int i = 1; i < lines.Length; i++)
            {
                var c = lines[i].Split(',');
                var row = new double[c.Length];
                for (int k = 0; k < c.Length; k++)
                    double.TryParse(c[k], NumberStyles.Any, CultureInfo.InvariantCulture, out row[k]);
                _rows.Add(row);
            }
            _rowIdx = 0;
            return _rows.Count > 0;
        }
        catch { return false; }
    }

    // ===== controls ========================================================
    private void OnTogglePause(object sender, RoutedEventArgs e)
    {
        _running = !_running;
        if (_running) { _gameTimer.Start(); _dataTimer.Start(); PauseButton.Content = "⏸ PAUSE"; }
        else { _gameTimer.Stop(); _dataTimer.Stop(); PauseButton.Content = "▶ RESUME"; }
    }

    private async void OnAiMap(object sender, RoutedEventArgs e)
    {
        AiMapButton.IsEnabled = false;
        try
        {
            Hud.Text = "Asking LM Studio for a control map…";
            using var client = new LmStudioClient(_cfg.LmStudioUrl);
            var model = string.IsNullOrWhiteSpace(_cfg.Model) ? await client.GetFirstModelAsync() : _cfg.Model;
            if (string.IsNullOrWhiteSpace(model)) { Hud.Text = "LM Studio has no model loaded."; return; }
            var p = BmiMapPromptBuilder.Build();
            var reply = await client.CompleteAsync(model!, p.System, p.User);
            var csv = RewritePromptBuilder.CleanReply(reply);
            var parsed = VehicleMoveMap.Parse(csv);
            if (parsed.Rules.Count >= 2) { File.WriteAllText(_mapPath, csv); _map = parsed; Hud.Text = $"New control map loaded ({parsed.Rules.Count} rules)."; }
            else Hud.Text = "LM Studio returned an unusable map; kept the current one.";
        }
        catch (Exception ex) { Hud.Text = "LM Studio unavailable: " + ex.Message; }
        finally { AiMapButton.IsEnabled = true; }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
    private void OnKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) Close(); }
}
