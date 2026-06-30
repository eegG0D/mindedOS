using System.Globalization;
using System.IO;
using System.Text;
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
/// A top-down, Atari-style city driving game where the car is steered by live EEG
/// through eeg_map_vehicle.csv, with traffic lights and raw-EEG recording to CSV.
/// </summary>
public partial class VehicleGameWindow : Window
{
    private readonly OsContext _os;
    private readonly VehicleConfig _cfg;
    private readonly string _mapPath;

    private VehicleMoveMap _map;
    private VehicleGame _game = new();
    private Polygon _car = null!;
    private readonly List<(Ellipse dot, double x, double y, bool red)> _lights = new();
    private readonly DispatcherTimer _gameTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly DispatcherTimer _lightTimer = new() { Interval = TimeSpan.FromSeconds(3.5) };

    private bool _driving;
    private bool _recording;
    private readonly List<string> _csv = new();
    private long _startMs;

    public VehicleGameWindow(OsContext os, VehicleConfig cfg)
    {
        _os = os;
        _cfg = cfg;
        _mapPath = MindedOS.Core.DataFile.Resolve(Path.Combine(AppContext.BaseDirectory, "data", string.IsNullOrWhiteSpace(cfg.MapFile) ? "eeg_map_vehicle.csv" : cfg.MapFile));
        InitializeComponent();

        _map = File.Exists(_mapPath) ? VehicleMoveMap.Load(_mapPath)
                                     : VehicleMoveMap.Parse("0,default,,,go");

        Loaded += (_, _) =>
        {
            BuildCity();
            _gameTimer.Tick += OnGameTick;
            _lightTimer.Tick += OnLightTick;
            _lightTimer.Start();
        };
        Closed += (_, _) => { _gameTimer.Stop(); _lightTimer.Stop(); };
    }

    // ===== city / car rendering ============================================
    private static readonly double[] RoadsX = { 150, 470, 790 };
    private static readonly double[] RoadsY = { 120, 300, 480 };
    private const double RoadW = 64;

    private void BuildCity()
    {
        var road = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
        foreach (var y in RoadsY)
            GameCanvas.Children.Add(new Rectangle { Width = GameCanvas.Width, Height = RoadW, Fill = road, RenderTransform = new TranslateTransform(0, y - RoadW / 2) });
        foreach (var x in RoadsX)
            GameCanvas.Children.Add(new Rectangle { Width = RoadW, Height = GameCanvas.Height, Fill = road, RenderTransform = new TranslateTransform(x - RoadW / 2, 0) });

        // lane dashes on the horizontal roads
        foreach (var y in RoadsY)
            for (double x = 0; x < GameCanvas.Width; x += 40)
                GameCanvas.Children.Add(new Rectangle { Width = 18, Height = 3, Fill = Brushes.Yellow, RenderTransform = new TranslateTransform(x, y - 1.5) });

        // traffic lights at intersections (alternating start state)
        int i = 0;
        foreach (var x in RoadsX)
            foreach (var y in RoadsY)
            {
                bool red = (i++ % 2) == 0;
                var dot = new Ellipse { Width = 16, Height = 16, Stroke = Brushes.Black, StrokeThickness = 2, Fill = red ? Brushes.Red : Brushes.LimeGreen };
                Canvas.SetLeft(dot, x - 8 + RoadW / 2);
                Canvas.SetTop(dot, y - 8 - RoadW / 2);
                GameCanvas.Children.Add(dot);
                _lights.Add((dot, x, y, red));
            }

        _game = new VehicleGame(GameCanvas.Width, GameCanvas.Height, 150, 300);
        _car = new Polygon
        {
            Points = new PointCollection { new Point(0, 0), new Point(28, 8), new Point(0, 16), new Point(7, 8) },
            Fill = new SolidColorBrush(Color.FromRgb(0x39, 0xFF, 0x14)),
            Stroke = Brushes.Black,
            StrokeThickness = 1.5,
        };
        GameCanvas.Children.Add(_car);
        UpdateCarVisual();
    }

    private void UpdateCarVisual()
    {
        Canvas.SetLeft(_car, _game.X - 14);
        Canvas.SetTop(_car, _game.Y - 8);
        _car.RenderTransform = new RotateTransform(_game.Heading * 180 / Math.PI, 14, 8);
    }

    // ===== game loop =======================================================
    private void OnGameTick(object? sender, EventArgs e)
    {
        string move = _map.Resolve(_os.Signals.GetSignal);

        // stop for a nearby red light
        string lightState = "—";
        foreach (var l in _lights)
        {
            double d = Math.Sqrt(Math.Pow(_game.X - (l.x + RoadW / 2), 2) + Math.Pow(_game.Y - (l.y - RoadW / 2), 2));
            if (d < 52) { lightState = l.red ? "RED" : "GREEN"; if (l.red) move = "stop"; }
        }

        _game.Step(move);
        UpdateCarVisual();

        Hud.Text = $"MOVE {move.ToUpperInvariant(),-8} att {_os.Signals.Attention,3} med {_os.Signals.Meditation,3} " +
                   $"blink {_os.Signals.LastBlink,3} | light {lightState,-5} | spd {_game.Speed,5:0.0} dist {_game.Distance,6:0} " +
                   (_recording ? "| ● REC" : "");

        if (_recording) RecordRow(move);
    }

    private void OnLightTick(object? sender, EventArgs e)
    {
        for (int i = 0; i < _lights.Count; i++)
        {
            var l = _lights[i];
            bool red = !l.red;
            l.dot.Fill = red ? Brushes.Red : Brushes.LimeGreen;
            _lights[i] = (l.dot, l.x, l.y, red);
        }
    }

    private void RecordRow(string move)
    {
        long t = Environment.TickCount64 - _startMs;
        _csv.Add(string.Create(CultureInfo.InvariantCulture,
            $"{t},{_os.Signals.LastRaw},{_os.Signals.Attention},{_os.Signals.Meditation},{_os.Signals.LastBlink},{_os.Signals.SignalNoise},{move},{_game.X:0.0},{_game.Y:0.0},{_game.Speed:0.00},{_game.Heading:0.000}"));
    }

    // ===== controls ========================================================
    private async void OnToggleDrive(object sender, RoutedEventArgs e)
    {
        if (_driving) { _gameTimer.Stop(); _driving = false; DriveButton.Content = "▶ DRIVE (EEG)"; return; }

        if (_os.Source.State != LinkState.Streaming)
        {
            Hud.Text = "Connecting EEG…";
            await _os.Source.ConnectAsync();
        }
        _startMs = Environment.TickCount64;
        _gameTimer.Start();
        _driving = true;
        DriveButton.Content = "⏸ STOP";
    }

    private void OnToggleRecord(object sender, RoutedEventArgs e)
    {
        if (!_recording)
        {
            _csv.Clear();
            _csv.Add("t_ms,raw,attention,meditation,blink,signal,move,x,y,speed,heading");
            _recording = true;
            RecButton.Content = "■ STOP REC";
            return;
        }

        _recording = false;
        RecButton.Content = "● REC RAW EEG";
        try
        {
            var dir = string.IsNullOrWhiteSpace(_cfg.OutputDir)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "mindedOS", "vehicle")
                : _cfg.OutputDir;
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"raw_eeg_drive_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            File.WriteAllLines(path, _csv);
            Hud.Text = $"Saved {Path.GetFileName(path)} ({_csv.Count - 1} rows).";
        }
        catch (Exception ex) { Hud.Text = "Save failed: " + ex.Message; }
    }

    private async void OnAiMap(object sender, RoutedEventArgs e)
    {
        AiMapButton.IsEnabled = false;
        try
        {
            Hud.Text = "Asking LM Studio for a driving map…";
            using var client = new LmStudioClient(_cfg.LmStudioUrl);
            var model = string.IsNullOrWhiteSpace(_cfg.Model) ? await client.GetFirstModelAsync() : _cfg.Model;
            if (string.IsNullOrWhiteSpace(model)) { Hud.Text = "LM Studio has no model loaded."; return; }

            var p = VehicleMapPromptBuilder.Build();
            var reply = await client.CompleteAsync(model!, p.System, p.User);
            var csv = RewritePromptBuilder.CleanReply(reply);
            var parsed = VehicleMoveMap.Parse(csv);
            if (parsed.Rules.Count >= 2)
            {
                File.WriteAllText(_mapPath, csv);
                _map = parsed;
                Hud.Text = $"New driving map loaded ({parsed.Rules.Count} rules).";
            }
            else Hud.Text = "LM Studio returned an unusable map; kept the current one.";
        }
        catch (Exception ex) { Hud.Text = "LM Studio unavailable: " + ex.Message; }
        finally { AiMapButton.IsEnabled = true; }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }
}
