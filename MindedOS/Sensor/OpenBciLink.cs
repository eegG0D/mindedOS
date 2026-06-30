using brainflow;
using brainflow.math;
using MindedOS.Core;

namespace MindedOS.Sensor;

/// <summary>
/// EEG source backed by BrainFlow. Runs either the OpenBCI "simulate" option —
/// the synthetic board (<see cref="BoardIds.SYNTHETIC_BOARD"/>, no hardware) —
/// or a real OpenBCI headset such as the 16-channel Cyton+Daisy over a serial
/// dongle. The board is chosen at construction; <see cref="FromEnvironment"/>
/// reads the choice from environment variables so simulate stays the default.
///
/// Surfaces the same <see cref="EegEvent"/>s the old TGAM path did so nothing
/// downstream (SignalHub, triggers, baseline, sub-programs) needs to change:
/// attention/meditation come from BrainFlow's MLModel metrics, the spectrum
/// from <see cref="DataFilter.get_avg_band_powers"/>, raw waves from every EEG
/// channel, and contact from per-channel railing (synthetic is always clean).
/// </summary>
public sealed class OpenBciLink : IEegSource
{
    /// <summary>Cosmetic factor mapping small PSD band powers into the
    /// hundreds-of-thousands range <see cref="BandInterpreter"/>'s tiers assume.
    /// Affects only the band-summary text, not triggers or baseline.</summary>
    private const double BandScale = 1_000_000;

    private const double BlinkThresholdUv = 120; // µV excursion that reads as a blink

    private readonly int _boardId;
    private readonly string? _serialPort;
    private readonly string? _ipAddress;
    private readonly int _ipPort;

    private BoardShim? _board;
    private MLModel? _mindfulness;
    private MLModel? _restfulness;
    private int _rate;
    private int[] _eegChannels = Array.Empty<int>();

    private CancellationTokenSource? _cts;
    private Task? _loop;

    /// <param name="board">BrainFlow board id. Default is the synthetic
    /// (simulate) board; pass <see cref="BoardIds.CYTON_DAISY_BOARD"/> for a
    /// real 16-channel OpenBCI headset.</param>
    /// <param name="serialPort">USB dongle COM port for a serial board (e.g. "COM3").</param>
    /// <param name="ipAddress">IP address for a WiFi-shield board.</param>
    /// <param name="ipPort">IP port for a WiFi-shield board.</param>
    public OpenBciLink(
        BoardIds board = BoardIds.SYNTHETIC_BOARD,
        string? serialPort = null,
        string? ipAddress = null,
        int ipPort = 0)
    {
        _boardId = (int)board;
        _serialPort = serialPort;
        _ipAddress = ipAddress;
        _ipPort = ipPort;
    }

    /// <summary>
    /// Build a source from environment variables, defaulting to simulate:
    /// <c>MINDEDOS_EEG_BOARD</c> (synthetic | cyton | cyton_daisy |
    /// cyton_daisy_wifi | ganglion), <c>MINDEDOS_EEG_SERIAL</c> (COM port),
    /// <c>MINDEDOS_EEG_IP</c> / <c>MINDEDOS_EEG_PORT</c> (WiFi).
    /// </summary>
    public static OpenBciLink FromEnvironment()
    {
        string board = (Environment.GetEnvironmentVariable("MINDEDOS_EEG_BOARD") ?? "")
            .Trim().ToLowerInvariant();
        string? serial = Environment.GetEnvironmentVariable("MINDEDOS_EEG_SERIAL");
        string? ip = Environment.GetEnvironmentVariable("MINDEDOS_EEG_IP");
        int.TryParse(Environment.GetEnvironmentVariable("MINDEDOS_EEG_PORT"), out int ipPort);

        BoardIds id = board switch
        {
            "cyton" => BoardIds.CYTON_BOARD,
            "cyton_daisy" or "daisy" or "16" => BoardIds.CYTON_DAISY_BOARD,
            "cyton_daisy_wifi" => BoardIds.CYTON_DAISY_WIFI_BOARD,
            "ganglion" => BoardIds.GANGLION_BOARD,
            _ => BoardIds.SYNTHETIC_BOARD,
        };
        return new OpenBciLink(id, serial, ip, ipPort);
    }

    /// <summary>Simulate: BrainFlow's synthetic board, no hardware.</summary>
    public static OpenBciLink Simulated() => new(BoardIds.SYNTHETIC_BOARD);

    /// <summary>Real 16-channel OpenBCI Cyton+Daisy over a USB dongle.</summary>
    public static OpenBciLink CytonDaisy(string serialPort) =>
        new(BoardIds.CYTON_DAISY_BOARD, serialPort);

    /// <summary>Real 8-channel OpenBCI Cyton over a USB dongle.</summary>
    public static OpenBciLink Cyton(string serialPort) =>
        new(BoardIds.CYTON_BOARD, serialPort);

    /// <summary>Real 4-channel OpenBCI Ganglion over a USB dongle.</summary>
    public static OpenBciLink Ganglion(string serialPort) =>
        new(BoardIds.GANGLION_BOARD, serialPort);

    /// <summary>True when running the synthetic board (simulate). Real headsets
    /// report false, so the shell can keep the skin-contact gate active.</summary>
    public bool IsSimulated => _boardId == (int)BoardIds.SYNTHETIC_BOARD;

    public LinkState State { get; private set; } = LinkState.Idle;

    public string SourceName
    {
        get
        {
            if (IsSimulated) return "OpenBCI Synthetic (simulate)";
            string name = ((BoardIds)_boardId).ToString();
            string where = _serialPort ?? _ipAddress ?? "—";
            return $"OpenBCI {name} @ {where}";
        }
    }

    public event Action<EegEvent>? Event;
    public event Action<LinkState>? StateChanged;

    private void SetState(LinkState s)
    {
        State = s;
        StateChanged?.Invoke(s);
    }

    public Task<bool> ConnectAsync(CancellationToken ct = default)
    {
        SetState(LinkState.Searching);
        try
        {
            var input = new BrainFlowInputParams();
            if (!string.IsNullOrWhiteSpace(_serialPort)) input.serial_port = _serialPort;
            if (!string.IsNullOrWhiteSpace(_ipAddress)) input.ip_address = _ipAddress;
            if (_ipPort > 0) input.ip_port = _ipPort;
            _board = new BoardShim(_boardId, input);
            _board.prepare_session();
            _board.start_stream();

            _rate = BoardShim.get_sampling_rate(_boardId);
            _eegChannels = BoardShim.get_eeg_channels(_boardId);

            _mindfulness = new MLModel(new BrainFlowModelParams(
                (int)BrainFlowMetrics.MINDFULNESS, (int)BrainFlowClassifiers.DEFAULT_CLASSIFIER));
            _mindfulness.prepare();
            _restfulness = new MLModel(new BrainFlowModelParams(
                (int)BrainFlowMetrics.RESTFULNESS, (int)BrainFlowClassifiers.DEFAULT_CLASSIFIER));
            _restfulness.prepare();
        }
        catch (Exception)
        {
            SetState(LinkState.NotReachable);
            ReleaseBoard();
            return Task.FromResult(false);
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        SetState(LinkState.Streaming);
        _loop = Task.Run(() => RunAsync(_cts.Token));
        return Task.FromResult(true);
    }

    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            Event?.Invoke(new SignalEvent(0)); // assume clean until the first window
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(1000, token);

                double[,] data = _board!.get_current_board_data(2 * _rate);
                if (data.GetLength(1) < _rate) continue; // not enough samples yet

                Event?.Invoke(new SignalEvent(ContactNoise(data)));

                Tuple<double[], double[]> bands =
                    DataFilter.get_avg_band_powers(data, _eegChannels, _rate, true);
                double[] vec = bands.Item1; // [delta, theta, alpha, beta, gamma]

                EmitMetrics(vec);
                EmitSpectrum(vec);
                EmitRaw(data);
            }
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
        catch (Exception) when (!token.IsCancellationRequested)
        {
            SetState(LinkState.Dropped);
        }
    }

    private void EmitMetrics(double[] vec)
    {
        int focus = Clamp0_100(_mindfulness!.predict(vec)[0] * 100);
        int calm = Clamp0_100(_restfulness!.predict(vec)[0] * 100);
        Event?.Invoke(new AttentionEvent(focus));
        Event?.Invoke(new MeditationEvent(calm));
    }

    private void EmitSpectrum(double[] vec)
    {
        // BrainFlow gives 5 bands; the TGAM model wants 8. Split alpha/beta/gamma
        // 60/40 into low/high and scale into BandInterpreter's expected range.
        int Band(double v) => (int)(v * BandScale);
        double delta = vec[0], theta = vec[1], alpha = vec[2], beta = vec[3], gamma = vec[4];

        Event?.Invoke(new SpectrumEvent(new BandPowers(
            Delta: Band(delta),
            Theta: Band(theta),
            LowAlpha: Band(alpha * 0.6), HighAlpha: Band(alpha * 0.4),
            LowBeta: Band(beta * 0.6), HighBeta: Band(beta * 0.4),
            LowGamma: Band(gamma * 0.6), MidGamma: Band(gamma * 0.4))));
    }

    private void EmitRaw(double[,] data)
    {
        int channels = _eegChannels.Length;
        if (channels == 0) return;
        int n = data.GetLength(1);
        if (n == 0) return;

        // Pull each EEG channel's row once (µV samples).
        var rows = new double[channels][];
        for (int c = 0; c < channels; c++) rows[c] = data.GetRow(_eegChannels[c]);

        double peakUv = 0;
        // Downsample to ~32 evenly spaced samples to keep the UI light.
        int step = Math.Max(1, n / 32);
        for (int i = 0; i < n; i += step)
        {
            var frame = new int[channels];
            for (int c = 0; c < channels; c++)
            {
                double uv = rows[c][i];
                // Inverse of BandInterpreter.RawToMicrovolts (amp -> µV uses ~0.21973).
                frame[c] = (int)Math.Clamp(uv / 0.21973, -2048, 2047);
            }

            double primaryUv = rows[0][i];
            if (Math.Abs(primaryUv) > peakUv) peakUv = Math.Abs(primaryUv);

            // Primary channel keeps driving word mapping / triggers; the frame
            // carries all 16 channels for the per-channel display.
            Event?.Invoke(new RawEvent(frame[0]));
            Event?.Invoke(new RawFrameEvent(frame));
        }

        if (peakUv > BlinkThresholdUv)
        {
            int strength = (int)Math.Clamp(peakUv - BlinkThresholdUv, 0, 255);
            Event?.Invoke(new BlinkEvent(strength));
        }
    }

    /// <summary>
    /// Map electrode contact to the 0 (clean) … 200 (no contact) scale SignalHub
    /// expects. The synthetic board is always clean; a real headset is scored by
    /// the average per-channel railed percentage (a railed channel = electrode
    /// off-head or saturated).
    /// </summary>
    private int ContactNoise(double[,] data)
    {
        if (IsSimulated) return 0;
        if (_eegChannels.Length == 0) return 200;

        double sumRailed = 0;
        int counted = 0;
        foreach (int ch in _eegChannels)
        {
            double[] row = data.GetRow(ch);
            if (row.Length == 0) continue;
            sumRailed += DataFilter.get_railed_percentage(row, row.Length); // 0..100
            counted++;
        }
        if (counted == 0) return 200;

        double avgRailed = sumRailed / counted;             // 0 clean … 100 fully railed
        return (int)Math.Clamp(avgRailed * 2, 0, 200);      // → 0 … 200 noise scale
    }

    private static int Clamp0_100(double v) => (int)Math.Clamp(Math.Round(v), 0, 100);

    public async Task DisconnectAsync()
    {
        _cts?.Cancel();
        if (_loop is not null)
        {
            try { await _loop; } catch { /* ignore shutdown races */ }
        }
        ReleaseBoard();
        SetState(LinkState.Idle);
    }

    private void ReleaseBoard()
    {
        try { _board?.stop_stream(); } catch { /* ignore */ }
        try { _board?.release_session(); } catch { /* ignore */ }
        try { _mindfulness?.release(); } catch { /* ignore */ }
        try { _restfulness?.release(); } catch { /* ignore */ }
        _board = null;
        _mindfulness = null;
        _restfulness = null;
    }
}
