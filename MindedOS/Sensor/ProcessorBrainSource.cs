using System.Diagnostics;
using MindedOS.Core;

namespace MindedOS.Sensor;

/// <summary>
/// An "artificial brain": an <see cref="IEegSource"/> that synthesizes an EEG-like
/// stream from the computer's own processor activity instead of a person. CPU load
/// drives attention, idleness drives meditation, garbage-collection events act as
/// "blinks", and the spectrum is deliberately beta/gamma-dominant with sharp,
/// deterministic, square-ish raw waves — so it reads very differently from organic
/// human EEG (which is alpha/theta-rich and sinusoidal).
/// </summary>
public sealed class ProcessorBrainSource : IEegSource
{
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private long _benchBaseline;
    private double _benchSink;

    public LinkState State { get; private set; } = LinkState.Idle;
    public string SourceName => "Artificial Brain (CPU)";

    public event Action<EegEvent>? Event;
    public event Action<LinkState>? StateChanged;

    private void SetState(LinkState s) { State = s; StateChanged?.Invoke(s); }

    public Task<bool> ConnectAsync(CancellationToken ct = default)
    {
        SetState(LinkState.Searching);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        SetState(LinkState.Streaming);
        _loop = Task.Run(() => RunAsync(_cts.Token));
        return Task.FromResult(true);
    }

    private async Task RunAsync(CancellationToken token)
    {
        var proc = Process.GetCurrentProcess();
        var sw = Stopwatch.StartNew();
        var lastCpu = proc.TotalProcessorTime;
        var lastWall = sw.Elapsed;
        int lastGc0 = GC.CollectionCount(0);
        long phase = 0;

        try
        {
            Event?.Invoke(new SignalEvent(0)); // perfect "contact" — it IS the machine
            while (!token.IsCancellationRequested)
            {
                // --- raw burst: a digital, square-ish waveform with timer-bit spikes ---
                for (int i = 0; i < 32; i++)
                {
                    phase++;
                    long ts = Stopwatch.GetTimestamp();
                    int square = (phase & 8) == 0 ? 650 : -650;             // hard plateaus
                    int jag = (int)((ts & 0x3F) << 4) - 512;                // jagged digital edge
                    Event?.Invoke(new RawEvent(Math.Clamp(square + jag, -2048, 2047)));
                    await Task.Delay(2, token);
                }

                // --- processor activity → attention/meditation ---
                proc.Refresh();
                var cpuNow = proc.TotalProcessorTime;
                var wallNow = sw.Elapsed;
                double dCpu = (cpuNow - lastCpu).TotalSeconds;
                double dWall = (wallNow - lastWall).TotalSeconds;
                lastCpu = cpuNow; lastWall = wallNow;

                double cores = Math.Max(1, Environment.ProcessorCount);
                double cpuPct = dWall > 0 ? Math.Clamp(dCpu / dWall / cores * 100.0, 0, 100) : 0;
                double activity = Math.Clamp(cpuPct * 0.7 + MicroBench() * 0.3, 0, 100);

                Event?.Invoke(new AttentionEvent((int)Math.Round(activity)));
                Event?.Invoke(new MeditationEvent((int)Math.Round(100 - activity)));

                // --- garbage collection acts as a "blink" ---
                int gc0 = GC.CollectionCount(0);
                bool gcSpike = gc0 != lastGc0;
                if (gcSpike)
                {
                    Event?.Invoke(new BlinkEvent(Math.Min(255, 80 + (gc0 - lastGc0) * 40)));
                    lastGc0 = gc0;
                }

                Event?.Invoke(new SpectrumEvent(MachineBands(activity, gcSpike)));
            }
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
    }

    /// <summary>Beta/gamma-dominant, alpha/theta-suppressed — the opposite of resting human EEG.</summary>
    private static BandPowers MachineBands(double activity, bool gcSpike) => new(
        Delta: gcSpike ? 2_000_000 : 45_000,
        Theta: 35_000,
        LowAlpha: 8_000,
        HighAlpha: 10_000,
        LowBeta: (int)(180_000 + activity * 6_000),
        HighBeta: (int)(220_000 + activity * 6_000),
        LowGamma: (int)(160_000 + activity * 3_000),
        MidGamma: (int)(130_000 + activity * 3_000));

    /// <summary>Tiny fixed workload; returns 0..100 contention vs the fastest seen run.</summary>
    private double MicroBench()
    {
        long start = Stopwatch.GetTimestamp();
        double s = 0;
        for (int i = 0; i < 50_000; i++) s += Math.Sqrt(i * 0.5 + 1);
        _benchSink = s;
        long elapsed = Stopwatch.GetTimestamp() - start;

        if (_benchBaseline == 0 || elapsed < _benchBaseline) { _benchBaseline = elapsed; return 0; }
        double ratio = (double)elapsed / _benchBaseline; // 1 = uncontended, >1 = slower
        return Math.Clamp((ratio - 1.0) * 100.0, 0, 100);
    }

    public async Task DisconnectAsync()
    {
        _cts?.Cancel();
        if (_loop is not null)
        {
            try { await _loop; } catch { /* ignore */ }
        }
        SetState(LinkState.Idle);
    }
}
