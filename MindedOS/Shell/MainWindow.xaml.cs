using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using MindedOS.Baseline;
using MindedOS.Core;
using MindedOS.Engine;
using MindedOS.Sensor;

namespace MindedOS.Shell;

public partial class MainWindow : Window
{
    private const int IconsPerPage = 12; // 4 x 3

    private OsContext Os => App.Os;
    private int _page;
    private readonly Dictionary<string, Border> _iconByProgram = new();
    private CancellationTokenSource? _baselineCts;
    private bool _isFullscreen;

    private static readonly Brush _cardBorder = new SolidColorBrush(Color.FromRgb(0xE0, 0xE3, 0xE8));
    private static readonly Brush _cardHover = new SolidColorBrush(Color.FromRgb(0xE9, 0xEC, 0xF1));

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        BuildDesktop();
        UpdateProfileText();
        Os.Signals.Updated += OnSignalUpdated;
        Os.Executor.Logged += OnActionLogged;
        Os.Source.StateChanged += OnStateChanged;

        // Diagnostic: --shot <path> renders the desktop; --shotprog <i> <path> a sub-program.
        var args = Environment.GetCommandLineArgs();
        int idx = Array.IndexOf(args, "--shot");
        if (idx >= 0 && idx + 1 < args.Length)
            _ = ShotAndExitAsync(args[idx + 1], -1);
        int pidx = Array.IndexOf(args, "--shotprog");
        if (pidx >= 0 && pidx + 2 < args.Length && int.TryParse(args[pidx + 1], out int progIndex))
            _ = ShotAndExitAsync(args[pidx + 2], progIndex);
        int gidx = Array.IndexOf(args, "--shotgame");
        if (gidx >= 0 && gidx + 1 < args.Length)
            _ = ShotWindowAsync(new VehicleGameWindow(Os, new MindedOS.Engine.VehicleConfig()) { Owner = this }, args[gidx + 1]);
        int bidx = Array.IndexOf(args, "--shotbmi");
        if (bidx >= 0 && bidx + 1 < args.Length)
            _ = ShotWindowAsync(new BmiGameWindow(Os, new MindedOS.Engine.BmiConfig()) { Owner = this }, args[bidx + 1]);
    }

    private async Task ShotWindowAsync(Window game, string path)
    {
        await Os.Source.ConnectAsync();
        game.Show();
        await Task.Delay(1500); // let the scene render
        try
        {
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                (int)game.ActualWidth, (int)game.ActualHeight, 96, 96,
                System.Windows.Media.PixelFormats.Pbgra32);
            rtb.Render(game);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
            using var fs = System.IO.File.Create(path);
            enc.Save(fs);
        }
        finally { Application.Current.Shutdown(); }
    }

    private async Task ShotAndExitAsync(string path, int programIndex)
    {
        await Os.Source.ConnectAsync();
        AttachSourceHandlers();
        await Task.Delay(2500); // let the simulator populate live values
        try
        {
            Window target = this;
            if (programIndex >= 0 && programIndex < Os.Programs.Count)
            {
                var win = new SubProgramWindow(Os, Os.Programs[programIndex]) { Owner = this };
                win.Show();
                await Task.Delay(2800); // give EEG sequences a chance to match/fire
                target = win;
            }

            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                (int)target.ActualWidth, (int)target.ActualHeight, 96, 96,
                System.Windows.Media.PixelFormats.Pbgra32);
            rtb.Render(target);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
            using var fs = System.IO.File.Create(path);
            enc.Save(fs);
        }
        finally
        {
            Application.Current.Shutdown();
        }
    }

    // ===== Desktop / paging =================================================

    private int PageCount =>
        Math.Max(1, (int)Math.Ceiling(Os.Programs.Count / (double)IconsPerPage));

    private void BuildDesktop()
    {
        IconHost.Items.Clear();
        _iconByProgram.Clear();

        int start = _page * IconsPerPage;
        for (int i = start; i < Math.Min(start + IconsPerPage, Os.Programs.Count); i++)
        {
            var program = Os.Programs[i];
            var icon = BuildIcon(program, i);
            _iconByProgram[program.Name] = icon;
            IconHost.Items.Add(icon);
        }

        PrevBtn.Visibility = _page > 0 ? Visibility.Visible : Visibility.Hidden;
        NextBtn.Visibility = _page < PageCount - 1 ? Visibility.Visible : Visibility.Hidden;
        BuildPageDots();
        ApplyProfileHighlight();
    }

    private Border BuildIcon(SubProgram program, int index)
    {
        // App icon comes from the icons/ folder (by filename order, 48x48); if
        // there is no image for this slot, fall back to the JSON Fluent glyph.
        FrameworkElement iconElement = (FrameworkElement?)LoadIconImage(index) ?? new TextBlock
        {
            Text = GlyphFromHex(program.Icon),
            FontFamily = (FontFamily)FindResource("FluentIcons"),
            FontSize = 46,
            Foreground = (Brush)FindResource("TextBright"),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var label = new TextBlock
        {
            Text = program.Name,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
            MaxWidth = 150,
        };
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(iconElement);
        stack.Children.Add(label);

        var border = new Border
        {
            Background = (Brush)FindResource("Panel"),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            BorderBrush = _cardBorder,
            Margin = new Thickness(12),
            Padding = new Thickness(8, 18, 8, 18),
            Cursor = Cursors.Hand,
            Child = stack,
            ToolTip = program.Description ?? string.Join(", ", program.Profiles),
            Tag = program,
        };

        // double-click launches the sub-program
        border.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) Launch(program);
        };
        border.MouseEnter += (_, _) => border.Background = _cardHover;
        border.MouseLeave += (_, _) => border.Background = (Brush)FindResource("Panel");
        return border;
    }

    private void BuildPageDots()
    {
        PageDots.Items.Clear();
        for (int i = 0; i < PageCount; i++)
        {
            PageDots.Items.Add(new Ellipse
            {
                Width = 9, Height = 9, Margin = new Thickness(4, 0, 4, 0),
                Fill = i == _page ? (Brush)FindResource("Teal") : (Brush)FindResource("TextMuted"),
            });
        }
    }

    private void ApplyProfileHighlight()
    {
        var profile = Os.Baseline?.Profile.ToString();
        foreach (var (name, border) in _iconByProgram)
        {
            var program = (SubProgram)border.Tag;
            bool matches = profile is not null &&
                           program.Profiles.Any(p => string.Equals(p, profile, StringComparison.OrdinalIgnoreCase));
            border.BorderBrush = matches ? (Brush)FindResource("Teal") : _cardBorder;
            border.BorderThickness = new Thickness(matches ? 2 : 1);
        }
    }

    private void OnPrevPage(object sender, RoutedEventArgs e)
    {
        if (_page > 0) { _page--; BuildDesktop(); }
    }

    private void OnNextPage(object sender, RoutedEventArgs e)
    {
        if (_page < PageCount - 1) { _page++; BuildDesktop(); }
    }

    /// <summary>Load the index-th icon image from icons/ as a 48x48 element, or null.</summary>
    private Image? LoadIconImage(int index)
    {
        if (index < 0 || index >= Os.IconFiles.Count) return null;
        try
        {
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(Os.IconFiles[index]);
            bmp.DecodePixelWidth = 48;
            bmp.DecodePixelHeight = 48;
            bmp.EndInit();
            bmp.Freeze();
            return new Image
            {
                Source = bmp,
                Width = 48,
                Height = 48,
                HorizontalAlignment = HorizontalAlignment.Center,
                Stretch = System.Windows.Media.Stretch.Uniform,
            };
        }
        catch
        {
            return null;
        }
    }

    private static string GlyphFromHex(string hex)
    {
        if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int code))
            return char.ConvertFromUtf32(code);
        return "";
    }

    // ===== Launching sub-programs ===========================================

    private void Launch(SubProgram program)
    {
        var window = new SubProgramWindow(Os, program) { Owner = this };
        if (program.Fullscreen)
        {
            window.WindowState = WindowState.Maximized;
            window.WindowStyle = WindowStyle.None;
        }
        window.Show();
    }

    // ===== Connection + baseline ============================================

    private async void OnConnect(object sender, RoutedEventArgs e)
    {
        // Let the user pick simulate or a real OpenBCI headset + COM port.
        var chosen = SourcePicker.Choose(this);
        if (chosen is null) return; // cancelled

        ConnectBtn.IsEnabled = false;

        DetachSourceHandlers();
        bool ok = await Os.UseSourceAsync(chosen);
        AttachSourceHandlers();
        // ConnectAsync fires the Streaming transition while handlers are detached,
        // so refresh the state label with the source's current state.
        OnStateChanged(Os.Source.State);

        ConnectBtn.IsEnabled = true;
        if (!ok)
        {
            LogText.Text = $"Could not start {Os.Source.SourceName}.";
            return;
        }
        ConnectBtn.Content = "Connected";

        // First ever connect with no stored baseline -> record 5 minutes.
        if (Os.Baseline is null)
            await RunBaselineAsync();
    }

    private void AttachSourceHandlers()
    {
        Os.Signals.Updated += OnSignalUpdated;
        Os.Source.StateChanged += OnStateChanged;
    }

    private void DetachSourceHandlers()
    {
        Os.Signals.Updated -= OnSignalUpdated;
        Os.Source.StateChanged -= OnStateChanged;
    }

    private async void OnRunBaseline(object sender, RoutedEventArgs e) => await RunBaselineAsync();

    private async Task RunBaselineAsync()
    {
        if (Os.Source.State != LinkState.Streaming)
        {
            await Os.Source.ConnectAsync();
            AttachSourceHandlers();
        }

        _baselineCts = new CancellationTokenSource();
        BaselineOverlay.Visibility = Visibility.Visible;
        BaselineProgress.Value = 0;

        var recorder = new BaselineRecorder(Os.Source);
        recorder.Progress += frac => Dispatcher.Invoke(() =>
        {
            BaselineProgress.Value = frac;
            var remaining = BaselineRecorder.DefaultDuration * (1 - frac);
            BaselineTime.Text = $"{remaining:mm\\:ss} remaining";
        });

        try
        {
            var result = await recorder.RecordAsync(_baselineCts.Token);
            Os.Baseline = result;
            BaselineRecorder.Save(result);
            BaselineOverlay.Visibility = Visibility.Collapsed;
            UpdateProfileText();
            ApplyProfileHighlight();
            AutoLaunchForProfile(result.Profile);
        }
        catch (OperationCanceledException)
        {
            BaselineOverlay.Visibility = Visibility.Collapsed;
            LogText.Text = "Baseline cancelled.";
        }
    }

    private void OnCancelBaseline(object sender, RoutedEventArgs e) => _baselineCts?.Cancel();

    private void AutoLaunchForProfile(MentalProfile profile)
    {
        var name = profile.ToString();
        var matches = Os.Programs.Where(p =>
            p.Profiles.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase))).ToList();

        LogText.Text = $"Profile {name}: auto-launching {matches.Count} program(s).";
        foreach (var program in matches)
            Launch(program);
    }

    // ===== Status bar =======================================================

    private void OnSignalUpdated(string signal)
    {
        Dispatcher.BeginInvoke(() =>
        {
            AttentionText.Text = $"Focus: {Os.Signals.Attention} ({Os.Signals.FocusWord})";
            MeditationText.Text = $"Calm: {Os.Signals.Meditation} ({Os.Signals.CalmWord})";
            SignalText.Text = $"Signal: {Os.Signals.SignalQuality} ({Os.Signals.SignalNoise})";
            BlinkText.Text = $"Blink: {Os.Signals.LastBlink}";
            RawText.Text = $"Raw: {Os.Signals.LastRaw} ({Os.Signals.Microvolts:0.0}µV)";
            BandText.Text = Os.Signals.BandSummary();
            RawChannelsText.Text = $"Raw ({Os.Signals.LastRawChannels.Length}ch): {Os.Signals.RawChannelSummary()}";
            ChannelWordsText.Text = $"Words ({Os.Signals.LastChannelWords.Length}ch): {Os.Signals.ChannelWordSummary()}";
            WordText.Text = $"word: {Os.Signals.CurrentWord}";
        });
    }

    private void OnStateChanged(LinkState state)
    {
        Dispatcher.BeginInvoke(() =>
        {
            StateText.Text = $"{Os.Source.SourceName} — {state}";
            StateDot.Fill = state switch
            {
                LinkState.Streaming => (Brush)FindResource("Teal"),
                LinkState.Searching => (Brush)FindResource("Focus"),
                LinkState.NotReachable or LinkState.Dropped => (Brush)FindResource("Alert"),
                _ => (Brush)FindResource("TextMuted"),
            };
        });
    }

    private void OnActionLogged(string message, bool executed) =>
        Dispatcher.BeginInvoke(() => LogText.Text = message);

    private void UpdateProfileText()
    {
        ProfileText.Text = Os.Baseline is { } b
            ? $"Profile: {b.Profile}  (focus {b.AvgAttention:0}, calm {b.AvgMeditation:0})"
            : "Profile: not yet recorded";
    }

    // ===== Safe mode / fullscreen ===========================================

    private void OnToggleSafeMode(object sender, RoutedEventArgs e)
    {
        Os.Executor.SafeMode = !Os.Executor.SafeMode;
        SafeModeBtn.Content = $"Safe Mode: {(Os.Executor.SafeMode ? "ON" : "OFF")}";
        SafeModeBtn.Foreground = Os.Executor.SafeMode
            ? (Brush)FindResource("TextBright") : (Brush)FindResource("Alert");
    }

    private void OnToggleFullscreen(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void ToggleFullscreen()
    {
        _isFullscreen = !_isFullscreen;
        if (_isFullscreen)
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = WindowState.Normal;
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.F11: ToggleFullscreen(); break;
            case Key.Escape when _isFullscreen: ToggleFullscreen(); break;
            case Key.Left: OnPrevPage(sender, e); break;
            case Key.Right: OnNextPage(sender, e); break;
        }
    }
}
