using System.IO;
using MindedOS.Baseline;
using MindedOS.Core;
using MindedOS.Engine;
using MindedOS.Sensor;

namespace MindedOS.Shell;

/// <summary>
/// Composition root: owns the single shared EEG source and every service the
/// shell and sub-programs depend on. Built once at startup.
/// </summary>
public sealed class OsContext
{
    public RawLexicon Lexicon { get; } = new();
    public ActionRegistry Actions { get; } = new();
    public IEegSource Source { get; private set; } = null!;
    public SignalHub Signals { get; private set; } = null!;
    public ActionExecutor Executor { get; private set; } = null!;
    public TriggerEngine Triggers { get; private set; } = null!;
    public SequenceEngine Sequences { get; private set; } = null!;
    public SequenceLoader SequenceLoader { get; private set; } = null!;
    public IReadOnlyList<SubProgram> Programs { get; private set; } = Array.Empty<SubProgram>();

    public string DataDir { get; }
    public string ProgramsDir { get; }
    public string IconsDir { get; }
    public string SequencesDir { get; }
    public string? TemplatesDir { get; }

    /// <summary>Image files in icons/, sorted by filename — assigned to programs by order.</summary>
    public IReadOnlyList<string> IconFiles { get; private set; } = Array.Empty<string>();

    public BaselineResult? Baseline { get; set; }

    public OsContext()
    {
        var baseDir = AppContext.BaseDirectory;
        DataDir = Path.Combine(baseDir, "data");
        ProgramsDir = Path.Combine(baseDir, "programs");
        IconsDir = Path.Combine(baseDir, "icons");
        SequencesDir = Path.Combine(baseDir, "sequences");

        // The 5600 AHK templates live outside the build; use them if present.
        var candidate = @"C:\Users\vinny\Downloads\5600 ahk programs\New folder - Copy";
        TemplatesDir = Directory.Exists(candidate) ? candidate : null;
    }

    public void Initialize()
    {
        // The bundled CSVs ship encrypted (built from *.encrypted.csv); every
        // loader decrypts in-memory via FileCrypto with the key embedded in
        // MindedOS.dll. eeg_map.csv on disk is EEG1 ciphertext, not plaintext.
        var lexPath = MindedOS.Core.DataFile.Resolve(Path.Combine(DataDir, "eeg_map.csv"));
        if (File.Exists(lexPath)) Lexicon.Load(lexPath);

        var actionsPath = Path.Combine(DataDir, "actions.txt");
        if (File.Exists(actionsPath)) Actions.Load(actionsPath);

        var loader = new SubProgramLoader(TemplatesDir);
        Programs = loader.LoadDirectory(ProgramsDir);

        // App icons load from the icons/ folder, ordered by filename; the Nth
        // program gets the Nth image (rendered 48x48 on the desktop).
        if (Directory.Exists(IconsDir))
        {
            string[] exts = { ".png", ".ico", ".jpg", ".jpeg", ".bmp", ".gif" };
            IconFiles = Directory.EnumerateFiles(IconsDir)
                .Where(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        // The one EEG source: OpenBCI via BrainFlow. Defaults to simulate (the
        // synthetic board); set MINDEDOS_EEG_BOARD/MINDEDOS_EEG_SERIAL to drive a
        // real headset such as the 16-channel Cyton+Daisy over its USB dongle.
        SequenceLoader = new SequenceLoader(SequencesDir);

        Source = OpenBciLink.FromEnvironment();
        Signals = new SignalHub(Source, Lexicon);
        Executor = new ActionExecutor(Actions) { SafeMode = true };
        // Only simulate is exempt from the skin-contact gate; a real headset must
        // have contact for actions to fire. Reads Source/Signals live.
        Executor.ContactGate = () => Source is OpenBciLink { IsSimulated: true } || Signals.Contact;
        Triggers = new TriggerEngine(Signals, Executor);
        Sequences = new SequenceEngine(Source, Lexicon, Executor);

        Baseline = BaselineRecorder.Load();
    }

    /// <summary>
    /// Swap the live EEG source (e.g. simulate ↔ real headset chosen from the
    /// Connect dialog), rewiring the hub, triggers and sequences, then connect.
    /// </summary>
    public async Task<bool> UseSourceAsync(IEegSource source)
    {
        await Source.DisconnectAsync();
        Source = source;
        Signals = new SignalHub(Source, Lexicon);
        Triggers.Dispose();
        Triggers = new TriggerEngine(Signals, Executor);
        Sequences.Dispose();
        Sequences = new SequenceEngine(Source, Lexicon, Executor);
        return await Source.ConnectAsync();
    }
}
