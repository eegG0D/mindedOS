using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MindedOS.Ai;
using MindedOS.Engine;
using MindedOS.Sensor;

namespace MindedOS.Shell;

/// <summary>
/// Renders a sub-program's JSON/template controls into a live WPF form. Buttons
/// run their bound action; controls with a trigger register against the shared
/// TriggerEngine; ProgressBars and {token} text reflect live EEG signals.
/// </summary>
public partial class SubProgramWindow : Window
{
    private readonly OsContext _os;
    private readonly SubProgram _program;
    private readonly List<(ProgressBar bar, string signal, double max)> _liveBars = new();
    private readonly List<(TextBlock tb, string template)> _liveTexts = new();

    public SubProgramWindow(OsContext os, SubProgram program)
    {
        _os = os;
        _program = program;
        InitializeComponent();

        Title = program.Name;
        HeaderName.Text = program.Name;
        HeaderGlyph.Text = GlyphFromHex(program.Icon);

        // Match the desktop: show this program's icons/ image (by its order index).
        int index = IndexOf(os, program);
        if (index >= 0 && index < os.IconFiles.Count)
        {
            try
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(os.IconFiles[index]);
                bmp.DecodePixelWidth = 40;
                bmp.DecodePixelHeight = 40;
                bmp.EndInit();
                bmp.Freeze();
                HeaderImage.Source = bmp;
                HeaderImage.Visibility = System.Windows.Visibility.Visible;
                HeaderGlyph.Visibility = System.Windows.Visibility.Collapsed;
            }
            catch { /* keep glyph */ }
        }
        HeaderProfiles.Text = program.Profiles.Count > 0
            ? "Profiles: " + string.Join(", ", program.Profiles)
            : "Always available";

        BuildForm();
        RegisterTriggers();
        RegisterSequences();
        SetupAiApp();
        SetupArtLife();
        SetupNetwork();
        SetupNoosphere();
        if (_program.Vehicle is not null) VehiclePanel.Visibility = Visibility.Visible;
        if (_program.Bmi is not null) BmiPanel.Visibility = Visibility.Visible;
        SetupBigData();
        SetupBlackBox();
        SetupChatbot();
        SetupChoices();
        SetupDataIngest();
        SetupDecision();
        SetupAssist();
        SetupIot();
        SetupMemory();

        _os.Signals.Updated += OnSignalUpdated;
        _os.Sequences.Matched += OnSequenceMatched;
        _os.Sequences.Completed += OnSequenceCompleted;
        Closed += (_, _) =>
        {
            _os.Signals.Updated -= OnSignalUpdated;
            _os.Sequences.Matched -= OnSequenceMatched;
            _os.Sequences.Completed -= OnSequenceCompleted;
            _os.Triggers.UnregisterProgram(_program.Name);
            _os.Sequences.UnregisterProgram(_program.Name);
            _ai?.Cancel();
        };
    }

    // ===== EEG-matched move sequences =======================================

    private readonly HashSet<EegSequence> _mySequences = new();

    private void RegisterSequences()
    {
        foreach (var name in _program.Sequences)
        {
            try
            {
                var seq = _os.SequenceLoader.Resolve(name);
                if (seq is not null)
                {
                    _os.Sequences.Register(_program.Name, seq);
                    _mySequences.Add(seq);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Sequence '{name}' failed to load: {ex.Message}");
            }
        }
        if (_mySequences.Count > 0)
            SeqStatus.Text = $"{_mySequences.Count} EEG sequence(s) armed";
    }

    private void OnSequenceMatched(EegSequence seq)
    {
        if (!_mySequences.Contains(seq)) return;
        Dispatcher.BeginInvoke(() => SeqStatus.Text = $"▶ {seq.Name} — running {seq.Moves.Count} moves");
    }

    private void OnSequenceCompleted(EegSequence seq)
    {
        if (!_mySequences.Contains(seq)) return;
        Dispatcher.BeginInvoke(() => SeqStatus.Text = $"✓ {seq.Name} done");
    }

    // ===== AI Application (LM Studio code generation) ========================

    private AiAppGenerator? _ai;

    private void SetupAiApp()
    {
        if (_program.AiApp is null) return;

        _ai = new AiAppGenerator(_os.Source, _os.Lexicon, _program.AiApp);
        _ai.Progress += p => Dispatcher.BeginInvoke(() => AiProgress.Value = p);
        _ai.Status += s => Dispatcher.BeginInvoke(() => AiStatus.Text = s);
        _ai.Completed += path => Dispatcher.BeginInvoke(() =>
        {
            AiStatus.Text = $"✓ Generated {Path.GetFileName(path)}";
            AiStartButton.IsEnabled = true;
        });
        _ai.Failed += _ => Dispatcher.BeginInvoke(() => AiStartButton.IsEnabled = true);
        _ai.Result += text => Dispatcher.BeginInvoke(() =>
        {
            AiResult.Text = text;
            AiResult.Visibility = Visibility.Visible;
        });

        AiAddress.Text = _ai.OutputDirectory;       // prefill with the resolved default
        AiAddressLabel.Text = _program.AiApp.AddressLabel + ":";

        int min = _program.AiApp.AccumulateSeconds / 60;
        switch (_program.AiApp.Kind.ToLowerInvariant())
        {
            case "famitracker":
                AiStartButton.Content = "▶ BEGIN 3-MIN EEG → COMPOSE SONG";
                AiStatus.Text = $"Records {min} min of EEG, then deterministically composes an army-march NES chiptune saved as a FamiTracker-importable .txt (no LM Studio needed).";
                break;
            case "rewrite":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → REWRITE";
                AiStatus.Text = $"Scans your brain for {min} min, decodes EEG → English words (eeg_map), then LM Studio rewrites them into meaningful text.";
                break;
            case "article":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → WRITE ARTICLE";
                AiStatus.Text = $"Scans your brain for {min} min, decodes EEG → English words (eeg_map), then LM Studio writes a research article saved as a formatted {_program.AiApp.Font} .docx.";
                break;
            case "advice":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → SELF-IMPROVEMENT PDF";
                AiStatus.Text = $"Reads your EEG condition + decoded words for {min} min, then LM Studio writes a personalized self-improvement article saved as a formatted {_program.AiApp.Font} .pdf.";
                break;
            case "algorithm":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → WRITE ALGORITHM";
                AiStatus.Text = $"Reads your EEG condition + decoded words for {min} min, then LM Studio writes a ~500-line best-practices Python algorithm whose features reflect your brain state (saved as .py).";
                break;
            case "slides":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → BUILD SLIDES";
                AiStatus.Text = $"Reads your EEG condition + decoded words for {min} min, then LM Studio explains your Ambient User Experience as a 6-slide {_program.AiApp.Font} PowerPoint (.pptx).";
                break;
            case "analysis":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → ANALYZE";
                AiStatus.Text = $"Measures every EEG band + your decoded words for {min} min, then LM Studio writes an advanced, accurate brain analysis grounded in your numbers. The study previews above and saves as .md.";
                break;
            case "blender":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → CITY PROMPT";
                AiStatus.Text = $"Reads your EEG condition + decoded words for {min} min, then LM Studio crafts a futuristic-city {_program.AiApp.Subject} prompt for {_program.AiApp.Tool}, accurate to your brain. Copy it from the preview; saved as .txt.";
                break;
            case "questvr":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → QUEST 3S APPS";
                AiStatus.Text = $"Reads your EEG condition + decoded words for {min} min, then LM Studio writes 3 advanced, innovative Meta Quest 3S (AR/MR) application prompts seeded by your brain. Copy them from the preview; saved as .txt.";
                break;
            case "workforce":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → DEPLOY 200 AGENTS";
                AiStatus.Text = $"Reads your EEG for {min} min, then builds an Augmented Workforce .md — a diagram, a Claude Code deploy prompt, and a roster of 200 agents (seeded by your brain). LM Studio writes the elaboration.";
                break;
            case "chip":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → CHIP PROMPT";
                AiStatus.Text = $"Reads your EEG condition + decoded words for {min} min, then LM Studio writes a very detailed KiCad chip design prompt (spec, block diagram, BOM, schematic nets, KiCad steps + a Claude Code dev prompt). Copy it from the preview; saved as .txt.";
                break;
            case "cloudeval":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → EVALUATE";
                AiStatus.Text = $"Reads {min} min of your EEG (decoded to words), then LM Studio acts as a cloud-computing research scientist: it judges whether your brain can reason about cloud computing, hunts the smallest flaws, and gives a SCIENTIFIC SCORE (0–100). Saved as .md.";
                break;
            case "cognition":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → COGNITION SCORE";
                AiStatus.Text = $"Reads {min} min of your EEG and scores how calculative, powerful and analytical your brain is as a COGNITION SCORE (1–200%). LM Studio validates and explains it. Saved as .md.";
                break;
            case "gpo":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → 35 GPO RULES";
                AiStatus.Text = $"Reads {min} min of your EEG, then builds a coherent 35-rule Windows Group Policy hardening baseline (rules that work together) seeded by your brain; LM Studio writes the threat-model. Saved as .md.";
                break;
            case "deeplearning":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → DL MODEL";
                AiStatus.Text = $"Reads {min} min of your EEG (decoded to words). Your brain decides which deep learning model to create — LM Studio returns a TITLE and a 3-paragraph description (what it is, its architecture, how to build & train it). Saved as .md.";
                break;
            case "ecommerce":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → eCOMMERCE STORE";
                AiStatus.Text = $"Reads {min} min of your EEG (decoded to words). Your brain designs an eCommerce store — LM Studio returns a TITLE, an 8-paragraph description & details, then a full list of every feature/SKU as a niche. Saved as .md.";
                break;
            case "emergent":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → BEHAVIOR SKEW";
                AiStatus.Text = $"Reads {min} min of your EEG (decoded to words). LM Studio detects the emergent BEHAVIOR SKEW that arises from the interplay of your signals and writes one paragraph about it. Saved as .md.";
                break;
            case "emotional":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → EMOTIONAL REPORT";
                AiStatus.Text = $"Reads {min} min of your EEG (decoded to words). LM Studio assesses your feelings and writes a one-page emotional report that explains your emotions. Saved as a PDF.";
                break;
            case "ethics":
                AiStartButton.Content = "▶ BEGIN 10-MIN ETHICS SCAN → 10 SLIDES";
                AiStatus.Text = $"Records {min} min of your EEG and computes how ethical your brain can be (0–100%) from calm, reflection and restraint. LM Studio then explains WHY across a 10-slide PowerPoint. Saved as a .pptx.";
                break;
            case "face":
                AiStartButton.Content = "▶ SCAN FACE + EEG → MATCH REPORT";
                AiStatus.Text = $"Put a .png of your face in the folder above. Reads {min} min of your EEG, then LM Studio's vision model detects your face and compares it to your EEG — how you think vs. how you look — with an EEG–Face match % and whether it's a hidden mismatch. Saved as .md.";
                break;
            case "gear":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → ENGINEERING PDF";
                AiStatus.Text = $"Records {min} min of your EEG, then LM Studio turns it into an engineering concept and writes a PDF article that explains it — with ready-to-paste Claude Code and Blender prompts to build the project. Saved as a {_program.AiApp.Font} .pdf.";
                break;
            case "healthcare":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → DRUG COMBINATION";
                AiStatus.Text = $"Records {min} min of your EEG, which deterministically SELECTS a drug combination from the catalog CSV (eeg_map_drugs.csv). LM Studio then SPECULATES on the illness/symptoms that combination could solve. Experimental — not medical advice. Saved as .md.";
                break;
            case "humanvsai":
                AiStartButton.Content = "▶ DUEL: RECORD HUMAN EEG, THEN AI EEG";
                AiStatus.Text = $"Dual-generates two EEG lists — first your HUMAN EEG ({min} min), then an AI EEG synthesized from this computer's processor ({min} min). Each is scored for scientific quality; LM Studio judges, on the subject of SCIENCE, whose EEG is the most scientific and who wins. Saved as .md.";
                break;
            case "humanoid":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → HUMANOID %";
                AiStatus.Text = $"Reads {min} min of your EEG and measures how much your brain matches a HUMANOID (0–100%) across six human traits — with the exact brain edits to reach 100% and the words that make you more humanoid. LM Studio explains it in detail. Saved as a {_program.AiApp.Font} .pdf.";
                break;
            case "interaction":
                AiStartButton.Content = "▶ RECORD 3-MIN EEG → TALK TO AI";
                AiStatus.Text = $"Records {min} min of your EEG as a list of words (eeg_map.csv), saved to a CSV. Each word becomes a line you say to the AI: LM Studio expands it into a sentence and the AI replies — one You line, one AI line, until your EEG list ends. Saved as a chat .txt.";
                break;
            case "mltheory":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → ML THEORY";
                AiStatus.Text = $"Records {min} min of your EEG, decodes it to English words (eeg_map.csv) and uses them as the prompt. LM Studio picks the machine-learning theory your brain points to and writes what you need to know about it. Saved as .md.";
                break;
            case "artificial":
                AiStartButton.Content = "▶ GENERATE & STUDY ARTIFICIAL BRAIN";
                AiStatus.Text = $"Synthesizes a {min}-min EEG stream from this computer's PROCESSOR (CPU load → attention, GC → blinks, beta/gamma-dominant), then LM Studio explains how this machine-generated EEG differs from human EEG. Saved as .md.";
                break;
            case "aitheory":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → AI THEORY";
                AiStatus.Text = $"Reads your EEG for {min} min — your brain is the source. LM Studio builds an original Artificial Intelligence theory from your decoded words, tuned to your state, saved as a formatted {_program.AiApp.Font} .pdf.";
                break;
            case "multimodal":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → LEARNING PACKAGE";
                AiStatus.Text = $"Scans your brain for {min} min, decodes EEG → English concepts (eeg_map), then LM Studio builds a personalized multimodal learning package — a {_program.AiApp.Font} Word report, a 10-slide deck, a curriculum and a knowledge graph, plus your learning profile and history. Lands in generated_learning.";
                break;
            case "nlp":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → NLP PACKAGE";
                AiStatus.Text = $"Scans your brain for {min} min, decodes EEG → English words (eeg_map), then LM Studio runs NLP — tokens, topics, sentiment, entities, a semantic report, a research paper, slides, 100 questions, a chat log and a knowledge graph. Lands in generated_nlp.";
                break;
            case "pattern":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → FIND PATTERNS";
                AiStatus.Text = $"Scans your brain for {min} min, decodes EEG → English words (eeg_map), then LM Studio finds patterns — recurring words and topics, a cognitive signature, brain states, hidden relationships, cross-session trends and clusters, and a research report. Lands in generated_patterns.";
                break;
            case "perception":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → PERCEPTION MODEL";
                AiStatus.Text = $"Scans your brain for {min} min, decodes EEG → English words (eeg_map), then LM Studio models perception — awareness, attention, cognitive perception, curiosity, mental models, visual imagination, image comparison and a research report. Lands in generated_perception.";
                break;
            case "planning":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → BUILD PLANS";
                AiStatus.Text = $"Scans your brain for {min} min, decodes EEG → English words (eeg_map), then LM Studio builds plans — goals, priorities, timelines, resources, opportunities, roadmaps, forecasts and a research report. Lands in generated_planning.";
                break;
            case "problemsolving":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → SOLVE PROBLEMS";
                AiStatus.Text = $"Scans your brain for {min} min, decodes EEG → English words (eeg_map), then LM Studio models problem solving — reasoning, strategy, innovation and decision scores, solver archetype, solutions, simulations and a research report. Lands in generated_problem_solving.";
                break;
            case "processor":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → PROCESSOR MODEL";
                AiStatus.Text = $"Scans your brain for {min} min, decodes EEG → English words (eeg_map), then LM Studio models the brain as an information processor — input/throughput/logic/memory/scheduler scores, a multi-core brain, bottlenecks and optimization. Lands in generated_processor.";
                break;
            case "quantum":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → QUANTUM PACKAGE";
                AiStatus.Text = $"Scans your brain for {min} min, decodes EEG → English words (eeg_map), then LM Studio explores quantum computing — concepts, vocabulary, algorithms, theories, architectures, a curriculum and a research report (education/research framed). Lands in generated_quantum.";
                break;
            case "reactive":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → REACT NOW";
                AiStatus.Text = $"Scans your brain for {min} min, decodes EEG → English words (eeg_map), then LM Studio reacts to the present moment — current state, instant decisions, opportunities, innovations and actions (no memory, no history). Lands in generated_reactive.";
                break;
            case "reasoning":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → REASONING PACKAGE";
                AiStatus.Text = $"Scans your brain for {min} min, decodes EEG → English words (eeg_map), then LM Studio runs a full reasoning analysis — logical/scientific/engineering/mathematical reasoning, inference, hypotheses, reasoning chains, decision pathways, innovation, trends and a multi-user ranking. 24 files land in generated_reasoning.";
                break;
            case "mas":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → 10-AGENT TEAM";
                AiStatus.Text = $"Scans your brain for {min} min, decodes EEG → English words (eeg_map), then LM Studio runs a team of 10 cooperating AI agents — roster, task board, collaboration matrix, coordination metrics, consensus, 10 agent contributions, a mission report and a 10-slide deck. 28 files land in generated_mas.";
                break;
            case "rl":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → RL AGENT";
                AiStatus.Text = $"Scans your brain for {min} min, decodes EEG → English words (eeg_map), then LM Studio models your cognition as a reinforcement-learning agent — brain states, actions, rewards, a policy, decisions, goals, exploration/exploitation, learning episodes, a virtual agent, trends and a multi-agent competition. 21 files land in generated_rl.";
                break;
            case "robot":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → ROBOT BRAIN";
                AiStatus.Text = $"Scans your brain for {min} min, decodes EEG → English words (eeg_map), then LM Studio runs a cognitive robot brain — robot state, navigation plan, commands, environment map, memory, personality, skills, multi-robot coordination, a simulation, brain→robot actions and optional camera object recognition. 22 files land in generated_robot.";
                break;
            case "robotics":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → DESIGN ROBOTS";
                AiStatus.Text = $"Scans your brain for {min} min, decodes EEG → English words (eeg_map), then LM Studio designs robots — profile, classification, brain architecture, commands, control log, autonomous behavior, electronics, a Blender prompt, simulations, a swarm design, a learning plan, HRI and a forecast, plus a design report, a research paper, a concepts PDF and a deck. 22 files land in generated_robotics.";
                break;
            case "selfaware":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → SELF-REFLECTION";
                AiStatus.Text = $"Scans your brain for {min} min, decodes EEG → English words (eeg_map), then LM Studio runs an AI-assisted self-reflection — recurring thoughts, strengths, growth opportunities, goals, a curiosity map, 100 self-questions, a personal knowledge graph, a historical comparison and a long-term self model, plus narratives, a report and a deck. 21 files land in generated_self_awareness.";
                break;
            case "semisup":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → SEMI-SUPERVISED";
                AiStatus.Text = $"Scans your brain for {min} min, decodes EEG → English words (eeg_map), then LM Studio learns from labeled (frequent) and unlabeled (novel) patterns — classification, brain clusters, knowledge expansion, confidence scores, topic predictions, a brain model (JSON), session evolution and a multi-user network, plus narratives, a report and a deck. 20 files land in generated_semi_supervised.";
                break;
            case "sensorimotor":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → SENSORIMOTOR";
                AiStatus.Text = $"Scans your brain for {min} min, decodes EEG → English words (eeg_map), then LM Studio models the perception→motor loop — sensory processing, motor planning, reaction, coordination, motor learning, skills, states, an EEG→movement command map, driving performance, a BMI control log, adaptation, human-vs-AI control and movement patterns, plus a report and a deck. 20 files land in generated_sensorimotor.";
                break;
            case "smarthouse":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → SMART HOME";
                AiStatus.Text = $"Scans your brain for {min} min, decodes EEG → English words (eeg_map), then LM Studio adapts a smart home — a home profile, room/environment preferences, device recommendations, daily routines, occupancy predictions, mood automation and an IoT registry, plus energy/security/assistant/design/simulation narratives, a future plan, an analysis report and a deck. 20 files land in generated_smart_house.";
                break;
            case "weakai":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → WEAK AI ASSISTANT";
                AiStatus.Text = $"Scans your brain for {min} min, decodes EEG → English concepts (eeg_map), then runs a NARROW, task-oriented Weak AI — it detects dominant domains, classifies your cognition, extracts knowledge, runs research/engineering/programming/learning assistants, recommends ranked tasks, supports decisions and scores productivity & benchmarks. LM Studio drives the specialized outputs and writes a domain-knowledge {_program.AiApp.Font} .docx and a 10-slide .pptx. 19 files land in generated_weak_ai (a complete package is built deterministically even if LM Studio is offline).";
                break;
            case "voice":
                AiStartButton.Content = "▶ BEGIN 3-MIN SCAN → ANALYZE VOICE";
                AiStatus.Text = $"Records {min} min of EEG and decodes it to English concepts (eeg_map) used as the spoken transcript (no microphone/STT in this environment; a silent recorded_voice.wav placeholder is saved). It analyzes speech statistics, voice features, communication style, topics, sentiment, a speaker profile, EEG↔voice correlation, keywords, presentation scoring and a forecast. LM Studio writes a chat log, learning analysis and forecast, a {_program.AiApp.Font} .docx report and a 10-slide .pptx. 21 files land in generated_voice (a complete package is built deterministically even if LM Studio is offline).";
                break;
            case "vrworld":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → BUILD VR WORLD";
                AiStatus.Text = $"Scans your brain for {min} min, decodes EEG → English concepts (eeg_map), then (via LM Studio) generates immersive virtual worlds, detailed blueprints, characters, AI companions and EEG→VR control mappings, plus VR experiences, a story, training worlds, innovation/emotional worlds and engine prompts for Blender/Unreal/Unity/Godot/Meta Quest. A {_program.AiApp.Font} .docx report and a 10-slide .pptx are saved. 18 files land in generated_vr (a complete package is built deterministically even if LM Studio is offline).";
                break;
            case "unsup":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → DISCOVER STRUCTURE";
                AiStatus.Text = $"Scans your brain for {min} min, decodes EEG → English concepts (eeg_map), then extracts features and embeddings, clusters your cognitive states, reduces dimensions (PCA/t-SNE/UMAP), discovers latent topics, builds similarity and concept networks, detects anomalies and association rules, and infers emergent behaviors and archetypes — all WITHOUT predefined labels. LM Studio writes emergent-behavior and rare-pattern narratives, a {_program.AiApp.Font} .docx report and a 10-slide .pptx. 27 files land in generated_unsupervised (a complete package is built deterministically even if LM Studio is offline).";
                break;
            case "turing":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → HUMAN vs AI";
                AiStatus.Text = $"Scans your brain for {min} min, decodes EEG → English concepts (eeg_map), profiles your human thought patterns and (via LM Studio) generates AI thoughts to compare against. It scores human-likeness vs machine-likeness, runs a blind AI judge, compares creativity/reasoning/knowledge, computes Human/AI/Mixed probabilities and a leaderboard, and writes a {_program.AiApp.Font} .docx report and a 10-slide .pptx. 19 files land in generated_turing (a complete package is built deterministically even if LM Studio is offline).";
                break;
            case "transfer":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → TRANSFER KNOWLEDGE";
                AiStatus.Text = $"Scans your brain for {min} min, decodes EEG → English concepts (eeg_map), then builds a knowledge profile and maps how your knowledge transfers between 12 domains, scores skill transfer and cognitive adaptation, expands concepts across domains, and finds fast-to-learn subjects, careers and projects. LM Studio writes skill-transfer, knowledge-reuse, research and innovation reports, a {_program.AiApp.Font} .docx and a 10-slide .pptx. 19 files land in generated_transfer (a complete package is built deterministically even if LM Studio is offline).";
                break;
            case "tom":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → MODEL THE MIND";
                AiStatus.Text = $"Scans your brain for {min} min, decodes EEG → English concepts (eeg_map), then builds HYPOTHETICAL models of goals, intentions, beliefs, intents, perspectives, decision styles and social cognition, a belief map, a theory-of-mind graph, goal predictions, a human-vs-AI comparison and trends. LM Studio simulates 8 viewpoints and 6 cognitive scenarios and writes a {_program.AiApp.Font} .docx report and a 10-slide .pptx. Every inference is a probabilistic hypothesis, NOT a verified fact. 18 files land in generated_theory_of_mind (built deterministically even if LM Studio is offline).";
                break;
            case "taskauto":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → AUTOMATE TASKS";
                AiStatus.Text = $"Scans your brain for {min} min, decodes EEG → English concepts (eeg_map), then extracts tasks, classifies them into 10 categories, prioritizes them, and builds workflows, a project breakdown, a schedule, productivity metrics, a virtual workforce and ready-to-run automation scripts. LM Studio recommends tasks, designs a 10-agent team, and writes a project-manager .docx, a {_program.AiApp.Font} automation .docx and a 10-slide .pptx. 18 files land in generated_taskauto (a complete package is built deterministically even if LM Studio is offline).";
                break;
            case "swarm":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → FORM SWARM";
                AiStatus.Text = $"Scans your brain for {min} min, decodes EEG → English concepts (eeg_map), then turns each concept into a virtual agent and forms a swarm — network, idea colony, knowledge swarm, leadership roles, consensus, a human/artificial/hybrid comparison, a knowledge ecosystem and a global noosphere. LM Studio analyzes collective intelligence, emergent behavior, innovation and forecasting, and writes two {_program.AiApp.Font} .docx reports and a 10-slide .pptx. 20 files land in generated_swarm (a complete package is built deterministically even if LM Studio is offline).";
                break;
            case "supervised":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → TRAIN & PREDICT";
                AiStatus.Text = $"Scans your brain for {min} min, decodes EEG → English concepts (eeg_map), then builds a labeled training_dataset.csv, extracts 8 features, simulates 5 model types (Decision Tree, Random Forest, Logistic Regression, Neural Network, SVM), predicts your brain state, classifies the session, evaluates accuracy/precision/recall/F1, and predicts skills & careers. LM Studio explains the predictions and writes a {_program.AiApp.Font} .docx report and a 10-slide .pptx. 19 files land in generated_supervised (a complete package is built deterministically even if LM Studio is offline).";
                break;
            case "superintelligence":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → SUPERINTELLIGENCE";
                AiStatus.Text = $"Scans your brain for {min} min, decodes EEG → English concepts (eeg_map), then LM Studio runs a Superintelligence Research System — cognitive capabilities, knowledge integration, innovation, 10 domains, systems thinking, a 10-agent research council, 100 grand challenges, a knowledge graph, expert comparisons, future simulations and growth recommendations, plus a {_program.AiApp.Font} .docx report and a 12-slide .pptx. 18 files land in generated_superintelligence (a complete package is built deterministically even if LM Studio is offline).";
                break;
            case "strongai":
                AiStartButton.Content = "▶ BEGIN 3-MIN BRAIN SCAN → STRONG AI PACKAGE";
                AiStatus.Text = $"Scans your brain for {min} min, decodes EEG → English words (eeg_map), then builds a Strong-AI-inspired cognitive framework — a knowledge base, reasoning, 10 collaborating agents, a world model, problem-solving/research/analysis .docx and a 10-slide .pptx. 20 files land in generated_strong_ai (a complete package is built deterministically even if LM Studio is offline).";
                break;
            default:
                AiStartButton.Content = "▶ BEGIN 3-MIN EEG → PYTHON";
                AiStatus.Text = $"Accumulates {min} min of EEG-to-words, then asks LM Studio to generate an army-themed Python app.";
                break;
        }

        AiPanel.Visibility = Visibility.Visible;
    }

    private async void OnAiStart(object sender, RoutedEventArgs e)
    {
        if (_ai is null || _ai.IsRunning) return;

        // Use whatever address the operator typed as the output folder for this run.
        if (_program.AiApp is not null && !string.IsNullOrWhiteSpace(AiAddress.Text))
            _program.AiApp.OutputDir = AiAddress.Text.Trim();

        AiStartButton.IsEnabled = false;
        AiProgress.Value = 0;
        await _ai.RunAsync();
    }

    private void OnAiOpenFolder(object sender, RoutedEventArgs e)
    {
        if (_ai is null) return;
        try
        {
            Directory.CreateDirectory(_ai.OutputDirectory);
            Process.Start(new ProcessStartInfo { FileName = _ai.OutputDirectory, UseShellExecute = true });
        }
        catch { /* ignore */ }
    }

    // ===== Artificial Life (CPU vs human EEG comparison) ====================

    private string ArtLifeDir => string.IsNullOrWhiteSpace(_program.ArtLife!.OutputDir)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "mindedOS", "artificial_life")
        : _program.ArtLife!.OutputDir;

    private void SetupArtLife()
    {
        if (_program.ArtLife is null) return;
        ArtLifePanel.Visibility = Visibility.Visible;
        var cpu = Path.Combine(ArtLifeDir, _program.ArtLife.CpuFile);
        if (File.Exists(cpu))
            ArtLifeStatus.Text = $"cpu_eeg.csv found. Click ② to scan your EEG and compare ({_program.ArtLife.RecordSeconds / 60} min).";
    }

    private async void OnCreateCpuEeg(object sender, RoutedEventArgs e)
    {
        var cfg = _program.ArtLife!;
        CpuEegButton.IsEnabled = ScanCompareButton.IsEnabled = false;
        ArtLifeProgress.Value = 0;
        var cpuPath = Path.Combine(ArtLifeDir, cfg.CpuFile);
        var source = new ProcessorBrainSource();
        try
        {
            ArtLifeStatus.Text = "Generating the computer's (CPU) EEG…";
            var recorder = new EegCsvRecorder();
            await recorder.RecordAsync(source, cfg.RecordSeconds, cpuPath,
                p => Dispatcher.BeginInvoke(() => ArtLifeProgress.Value = p));
            ArtLifeStatus.Text = $"Saved {cfg.CpuFile}. Now click ② to scan your EEG and compare.";
        }
        catch (Exception ex) { ArtLifeStatus.Text = "CPU recording failed: " + ex.Message; }
        finally
        {
            await source.DisconnectAsync();
            CpuEegButton.IsEnabled = ScanCompareButton.IsEnabled = true;
        }
    }

    private async void OnScanCompare(object sender, RoutedEventArgs e)
    {
        var cfg = _program.ArtLife!;
        var cpuPath = Path.Combine(ArtLifeDir, cfg.CpuFile);
        if (!File.Exists(cpuPath))
        {
            ArtLifeStatus.Text = "No cpu_eeg.csv yet — click ① CREATE CPU EEG CSV first.";
            return;
        }

        CpuEegButton.IsEnabled = ScanCompareButton.IsEnabled = false;
        ArtLifeProgress.Value = 0;
        ArtLifeResult.Text = "";
        var userPath = Path.Combine(ArtLifeDir, cfg.UserFile);
        try
        {
            ArtLifeStatus.Text = "Scanning your EEG…";
            var recorder = new EegCsvRecorder();
            var userVec = await recorder.RecordAsync(_os.Source, cfg.RecordSeconds, userPath,
                p => Dispatcher.BeginInvoke(() => ArtLifeProgress.Value = p));

            var cpuVec = BrainFeatureVector.FromCsv(cpuPath);
            double pct = ArtificialityComparer.PercentArtificial(userVec, cpuVec);

            ArtLifeResult.Text = $"Your EEG is {pct:0.0}% artificial (vs the CPU).";
            ArtLifeStatus.Text = $"Compared {cfg.UserFile} ↔ {cfg.CpuFile}. " +
                $"You: focus {userVec.AvgAttention:0}, calm {userVec.AvgMeditation:0}. " +
                $"CPU: focus {cpuVec.AvgAttention:0}, calm {cpuVec.AvgMeditation:0}.";
        }
        catch (Exception ex) { ArtLifeStatus.Text = "Scan/compare failed: " + ex.Message; }
        finally { CpuEegButton.IsEnabled = ScanCompareButton.IsEnabled = true; }
    }

    private void OnArtLifeOpenFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(ArtLifeDir);
            Process.Start(new ProcessStartInfo { FileName = ArtLifeDir, UseShellExecute = true });
        }
        catch { /* ignore */ }
    }

    // ===== Artificial Neural Network (multi-user brain leaderboard) ==========

    private string NetDir => string.IsNullOrWhiteSpace(_program.Network!.OutputDir)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "mindedOS", "neural_network")
        : _program.Network!.OutputDir;

    private void SetupNetwork()
    {
        if (_program.Network is null) return;
        NetPanel.Visibility = Visibility.Visible;
        NetName.Text = Environment.UserName;
        Directory.CreateDirectory(NetDir);
        int count = Directory.EnumerateFiles(NetDir, "*.csv").Count();
        NetStatus.Text = count > 0
            ? $"{count} brain(s) in the network. Record yours (①) then rank (②)."
            : "No CSVs yet. Add other users' EEG CSVs to the folder, record yours (①), then rank (②).";
    }

    private static string SafeName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "player" : name.Trim();
    }

    private async void OnNetRecord(object sender, RoutedEventArgs e)
    {
        var cfg = _program.Network!;
        NetRecordButton.IsEnabled = NetRankButton.IsEnabled = false;
        NetProgress.Value = 0;
        var path = Path.Combine(NetDir, SafeName(NetName.Text) + ".csv");
        try
        {
            NetStatus.Text = "Recording your EEG into the network…";
            var rec = new EegCsvRecorder();
            await rec.RecordAsync(_os.Source, cfg.RecordSeconds, path,
                p => Dispatcher.BeginInvoke(() => NetProgress.Value = p));
            NetStatus.Text = $"Saved {Path.GetFileName(path)}. Click ② to rank the network.";
        }
        catch (Exception ex) { NetStatus.Text = "Recording failed: " + ex.Message; }
        finally { NetRecordButton.IsEnabled = NetRankButton.IsEnabled = true; }
    }

    private void OnNetRank(object sender, RoutedEventArgs e)
    {
        var board = BrainScorer.RankFolder(NetDir);
        NetLeaderboard.Items.Clear();
        if (board.Count == 0)
        {
            NetStatus.Text = "No CSVs found in the folder. Record yours and add others'.";
            NetLeaderboard.Visibility = Visibility.Collapsed;
            return;
        }

        string me = SafeName(NetName.Text);
        for (int i = 0; i < board.Count; i++)
        {
            var b = board[i];
            string crown = i == 0 ? "★" : "  ";
            string you = string.Equals(b.User, me, StringComparison.OrdinalIgnoreCase) ? " (you)" : "";
            NetLeaderboard.Items.Add(
                $"{crown} #{i + 1}  {b.User,-14}{you,-7}  overall {b.Overall,5:0.0}  ·  pos {b.Positivity,4:0.0}  adv {b.Advance,4:0.0}  part {b.Particular,4:0.0}");
        }
        NetLeaderboard.Visibility = Visibility.Visible;

        var best = board[0];
        bool youWin = string.Equals(best.User, me, StringComparison.OrdinalIgnoreCase);
        NetStatus.Text = youWin
            ? $"★ You have the best brain in the network — overall {best.Overall:0.0}!"
            : $"Best brain: {best.User} (overall {best.Overall:0.0}). {board.Count} brains ranked.";
    }

    private void OnNetOpenFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(NetDir);
            Process.Start(new ProcessStartInfo { FileName = NetDir, UseShellExecute = true });
        }
        catch { /* ignore */ }
    }

    // ===== Artificial Noosphere (CSV matrix → company roles) ================

    private string NoosDir => string.IsNullOrWhiteSpace(_program.Noosphere!.OutputDir)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "mindedOS", "noosphere")
        : _program.Noosphere!.OutputDir;

    private void SetupNoosphere()
    {
        if (_program.Noosphere is null) return;
        NoosPanel.Visibility = Visibility.Visible;
        NoosName.Text = Environment.UserName;
        Directory.CreateDirectory(NoosDir);
        int count = Directory.EnumerateFiles(NoosDir, "*.csv").Count();
        NoosStatus.Text = count > 0
            ? $"{count} node(s) in the folder. Click ② to build the noosphere and assign roles."
            : "No CSVs yet. Drop hundreds of EEG CSVs (one per node) in the folder, then scan (②).";
    }

    private async void OnNoosRecord(object sender, RoutedEventArgs e)
    {
        var cfg = _program.Noosphere!;
        NoosRecordButton.IsEnabled = NoosScanButton.IsEnabled = false;
        NoosProgress.Value = 0;
        var path = Path.Combine(NoosDir, SafeName(NoosName.Text) + ".csv");
        try
        {
            NoosStatus.Text = "Adding your EEG node to the noosphere…";
            var rec = new EegCsvRecorder();
            await rec.RecordAsync(_os.Source, cfg.RecordSeconds, path,
                p => Dispatcher.BeginInvoke(() => NoosProgress.Value = p));
            NoosStatus.Text = $"Saved {Path.GetFileName(path)}. Click ② to build the noosphere.";
        }
        catch (Exception ex) { NoosStatus.Text = "Recording failed: " + ex.Message; }
        finally { NoosRecordButton.IsEnabled = NoosScanButton.IsEnabled = true; }
    }

    private void OnNoosScan(object sender, RoutedEventArgs e)
    {
        var result = Noosphere.Build(NoosDir);
        NoosRoster.Items.Clear();
        if (result.Roles.Count == 0)
        {
            NoosStatus.Text = "No CSVs found in the folder. Add EEG CSVs (one per node) first.";
            NoosRoster.Visibility = Visibility.Collapsed;
            return;
        }

        string me = SafeName(NoosName.Text);
        foreach (var r in result.Roles)
        {
            string you = string.Equals(r.User, me, StringComparison.OrdinalIgnoreCase) ? " (you)" : "";
            string mark = r.Role == Noosphere.Leader ? "★" : "•";
            NoosRoster.Items.Add(
                $"{mark} {r.Role,-10} {r.User,-16}{you,-7} overall {r.Scores.Overall,5:0.0}");
        }
        NoosRoster.Visibility = Visibility.Visible;

        var comp = string.Join("  ", result.RoleCounts.OrderBy(k => k.Key).Select(k => $"{k.Key}:{k.Value}"));
        NoosStatus.Text = $"Noosphere of {result.Roles.Count} brains · leader ★ {result.Leader} · " +
                          $"cohesion {result.Cohesion:0.0}% · {comp}";
    }

    private void OnNoosOpenFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(NoosDir);
            Process.Start(new ProcessStartInfo { FileName = NoosDir, UseShellExecute = true });
        }
        catch { /* ignore */ }
    }

    // ===== EEG Autonomous Vehicle game =====================================

    private void OnOpenVehicleGame(object sender, RoutedEventArgs e)
    {
        var game = new VehicleGameWindow(_os, _program.Vehicle!) { Owner = this };
        game.Show();
    }

    private void OnOpenBmiGame(object sender, RoutedEventArgs e)
    {
        var game = new BmiGameWindow(_os, _program.Bmi!) { Owner = this };
        game.Show();
    }

    // ===== EEG Chatbot =====================================================

    private System.Windows.Threading.DispatcherTimer? _chatTimer;
    private bool _chatBusy;
    private readonly Queue<string> _chatContext = new();

    private void SetupChatbot()
    {
        if (_program.Chatbot is null) return;
        ChatPanel.Visibility = Visibility.Visible;
        _chatTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Max(2, _program.Chatbot.IntervalSeconds)),
        };
        _chatTimer.Tick += OnChatTick;
        Closed += (_, _) => _chatTimer?.Stop();
    }

    private async void OnChatToggle(object sender, RoutedEventArgs e)
    {
        if (_chatTimer is null) return;
        if (_chatTimer.IsEnabled)
        {
            _chatTimer.Stop();
            ChatStartButton.Content = "▶ START CHAT (read my EEG)";
            ChatStatus.Text = "Paused.";
            return;
        }

        if (_os.Source.State != LinkState.Streaming)
        {
            ChatStatus.Text = "Connecting EEG…";
            await _os.Source.ConnectAsync();
        }
        AppendChat("🤖", "I'm reading your mind now — say something with your brain…");
        _chatTimer.Start();
        ChatStartButton.Content = "⏸ PAUSE CHAT";
        ChatStatus.Text = "Listening to your EEG…";
    }

    private async void OnChatTick(object? sender, EventArgs e)
    {
        if (_chatBusy) return;
        var word = _os.Signals.CurrentWord;
        if (string.IsNullOrWhiteSpace(word) || word == "—") return;

        _chatBusy = true;
        AppendChat("🧠", word);
        try
        {
            using var client = new MindedOS.Ai.LmStudioClient(_program.Chatbot!.LmStudioUrl);
            var model = string.IsNullOrWhiteSpace(_program.Chatbot.Model)
                ? await client.GetFirstModelAsync() : _program.Chatbot.Model;
            if (string.IsNullOrWhiteSpace(model))
            {
                AppendChat("🤖", "(LM Studio has no model loaded — load one and start its server.)");
                return;
            }

            var p = MindedOS.Ai.ChatbotPromptBuilder.Build(word, string.Join(", ", _chatContext));
            var reply = MindedOS.Ai.RewritePromptBuilder.CleanReply(await client.CompleteAsync(model!, p.System, p.User));
            AppendChat("🤖", string.IsNullOrWhiteSpace(reply) ? "…" : reply);

            _chatContext.Enqueue(word);
            while (_chatContext.Count > 8) _chatContext.Dequeue();
        }
        catch (Exception ex)
        {
            AppendChat("🤖", "(LM Studio offline: " + ex.Message + ")");
        }
        finally { _chatBusy = false; }
    }

    private void AppendChat(string who, string text)
    {
        ChatLog.AppendText($"{who} {text}\n");
        ChatLog.CaretIndex = ChatLog.Text.Length;
        ChatLog.ScrollToEnd();
    }

    // ===== Choice Descriptor ===============================================

    private VehicleMoveMap? _choiceMap;
    private System.Windows.Threading.DispatcherTimer? _choiceTimer;
    private readonly List<string> _choiceRows = new();
    private int _choiceSecond;
    private IReadOnlyList<ChoiceTally> _lastTally = System.Array.Empty<ChoiceTally>();

    private string ChDir => string.IsNullOrWhiteSpace(_program.Choices!.OutputDir)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "mindedOS", "choices")
        : _program.Choices!.OutputDir;

    private void SetupChoices()
    {
        if (_program.Choices is null) return;
        ChoicePanel.Visibility = Visibility.Visible;
        Directory.CreateDirectory(ChDir);
        var mapPath = MindedOS.Core.DataFile.Resolve(Path.Combine(AppContext.BaseDirectory, "data", _program.Choices.MapFile));
        _choiceMap = File.Exists(mapPath) ? VehicleMoveMap.Load(mapPath) : VehicleMoveMap.Parse("0,default,,,Observe");
        ChRecordButton.Content = $"① RECORD CHOICES ({_program.Choices.RecordSeconds / 60} MIN)";
    }

    private async void OnChRecord(object sender, RoutedEventArgs e)
    {
        var cfg = _program.Choices!;
        if (_os.Source.State != LinkState.Streaming) await _os.Source.ConnectAsync();

        ChRecordButton.IsEnabled = ChProcessButton.IsEnabled = false;
        _choiceRows.Clear();
        _choiceRows.Add("t_sec,choice,attention,meditation");
        _choiceSecond = 0;
        ChProgress.Value = 0;
        ChStatus.Text = $"Recording your choices for {cfg.RecordSeconds / 60} minutes…";

        _choiceTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _choiceTimer.Tick += (_, _) =>
        {
            string choice = _choiceMap!.Resolve(_os.Signals.GetSignal);
            _choiceRows.Add($"{_choiceSecond},{choice},{_os.Signals.Attention},{_os.Signals.Meditation}");
            _choiceSecond++;
            ChProgress.Value = Math.Clamp(_choiceSecond / (double)Math.Max(1, cfg.RecordSeconds), 0, 1);
            if (_choiceSecond >= cfg.RecordSeconds)
            {
                _choiceTimer!.Stop();
                var path = Path.Combine(ChDir, $"recorded_choices_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                File.WriteAllLines(path, _choiceRows);
                ShowTally(ChoiceLog.Tally(_choiceRows.Skip(1).Select(r => r.Split(',')[1])));
                ChStatus.Text = $"Saved {Path.GetFileName(path)}. Click ② to describe these choices with LM Studio.";
                ChRecordButton.IsEnabled = ChProcessButton.IsEnabled = true;
            }
        };
        _choiceTimer.Start();
        Closed += (_, _) => _choiceTimer?.Stop();
    }

    private async void OnChProcess(object sender, RoutedEventArgs e)
    {
        IReadOnlyList<ChoiceTally> tally = _lastTally;

        // If there's nothing in memory, let the user pick a stored CSV.
        if (tally.Count == 0)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Choices CSV (*.csv)|*.csv",
                InitialDirectory = Directory.Exists(ChDir) ? ChDir : null,
                Title = "Process a recorded choices CSV",
            };
            if (dlg.ShowDialog() != true) return;
            tally = ChoiceLog.TallyFromCsv(dlg.FileName);
            ShowTally(tally);
        }
        if (tally.Count == 0) { ChStatus.Text = "No choices to describe."; return; }

        ChProcessButton.IsEnabled = false;
        try
        {
            ChStatus.Text = "Describing your choices with LM Studio…";
            using var client = new MindedOS.Ai.LmStudioClient(_program.Choices!.LmStudioUrl);
            var model = string.IsNullOrWhiteSpace(_program.Choices.Model) ? await client.GetFirstModelAsync() : _program.Choices.Model;
            if (string.IsNullOrWhiteSpace(model)) { ChStatus.Text = "LM Studio has no model loaded."; return; }

            var p = MindedOS.Ai.ChoiceDescriptorPromptBuilder.Build(tally, _program.Choices.RecordSeconds);
            ChDescription.Text = MindedOS.Ai.RewritePromptBuilder.CleanReply(await client.CompleteAsync(model!, p.System, p.User));
            ChDescription.Visibility = Visibility.Visible;
            ChStatus.Text = "Choice description ready.";
        }
        catch (Exception ex) { ChStatus.Text = "LM Studio unavailable: " + ex.Message; }
        finally { ChProcessButton.IsEnabled = true; }
    }

    private void ShowTally(IReadOnlyList<ChoiceTally> tally)
    {
        _lastTally = tally;
        ChResults.Items.Clear();
        foreach (var t in tally)
            ChResults.Items.Add($"{t.Choice,-10} {t.Count,5}  ·  {t.Percent,5:0.0}%");
        ChResults.Visibility = tally.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnChOpenFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(ChDir);
            Process.Start(new ProcessStartInfo { FileName = ChDir, UseShellExecute = true });
        }
        catch { /* ignore */ }
    }

    // ===== Data Ingestion (scan folder → EEG-based title per CSV) ===========

    private readonly List<(string Path, DatasetProfile Profile)> _datasets = new();

    private string DiDir => string.IsNullOrWhiteSpace(_program.DataIngest!.OutputDir)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "mindedOS", "data_ingestion")
        : _program.DataIngest!.OutputDir;

    private void SetupDataIngest()
    {
        if (_program.DataIngest is null) return;
        DataPanel.Visibility = Visibility.Visible;
        Directory.CreateDirectory(DiDir);
    }

    private void OnDiLoad(object sender, RoutedEventArgs e) // ① SCAN FOLDER
    {
        var folder = DiDir;
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Choose a folder of CSV files", InitialDirectory = Directory.Exists(folder) ? folder : null };
        if (dlg.ShowDialog() == true) folder = dlg.FolderName;

        try
        {
            _datasets.Clear();
            DiProfile.Items.Clear();
            DiSummary.Items.Clear();
            DiSummary.Visibility = Visibility.Collapsed;

            var files = Directory.EnumerateFiles(folder, "*.csv", SearchOption.TopDirectoryOnly)
                                 .Where(f => !f.EndsWith(".summary.txt") && !f.Contains(".titles"))
                                 .OrderBy(f => f).Take(200).ToList();
            foreach (var f in files)
            {
                try
                {
                    var d = DatasetProfiler.Profile(f, _program.DataIngest!.SampleRows);
                    _datasets.Add((f, d));
                    DiProfile.Items.Add($"{d.File,-32} {d.Rows,10:N0} rows × {d.Columns} cols");
                }
                catch { /* skip unreadable */ }
            }
            DiProfile.Visibility = Visibility.Visible;
            DiStatus.Text = _datasets.Count > 0
                ? $"Scanned {_datasets.Count} CSV file(s). Click ② to write an EEG-based title for each."
                : "No CSV files found in that folder.";
        }
        catch (Exception ex) { DiStatus.Text = "Scan failed: " + ex.Message; }
    }

    private async void OnDiSummarize(object sender, RoutedEventArgs e) // ② TITLE EACH (EEG)
    {
        if (_datasets.Count == 0) { DiStatus.Text = "Scan a folder first (①)."; return; }
        DiLoadButton.IsEnabled = DiSummarizeButton.IsEnabled = false;
        DiSummary.Items.Clear();
        DiSummary.Visibility = Visibility.Visible;
        var titles = new List<string>();
        try
        {
            if (_os.Source.State != LinkState.Streaming) await _os.Source.ConnectAsync();

            using var client = new MindedOS.Ai.LmStudioClient(_program.DataIngest!.LmStudioUrl);
            var model = string.IsNullOrWhiteSpace(_program.DataIngest.Model) ? await client.GetFirstModelAsync() : _program.DataIngest.Model;
            if (string.IsNullOrWhiteSpace(model)) { DiStatus.Text = "LM Studio has no model loaded."; return; }

            foreach (var (path, profile) in _datasets)
            {
                // each title is read through the live EEG word at this moment
                string eegWord = _os.Signals.CurrentWord;
                DiStatus.Text = $"Titling {profile.File} through EEG word '{eegWord}'…";

                var p = MindedOS.Ai.DatasetTitlePromptBuilder.Build(DatasetProfiler.ToText(profile), eegWord, _os.Signals.FocusWord);
                var title = MindedOS.Ai.RewritePromptBuilder.CleanReply(await client.CompleteAsync(model!, p.System, p.User)).Replace("\n", " ").Trim();

                DiSummary.Items.Add($"{profile.File}  →  {title}");
                titles.Add($"{profile.File},{eegWord},\"{title.Replace("\"", "'")}\"");
                await Task.Delay(400); // let the brain word change between files
            }
            try { File.WriteAllLines(Path.Combine(DiDir, $"dataset_titles_{DateTime.Now:yyyyMMdd_HHmmss}.csv"),
                new[] { "file,eeg_word,title" }.Concat(titles)); } catch { }
            DiStatus.Text = $"Wrote EEG-based titles for {_datasets.Count} file(s) (saved as dataset_titles_*.csv).";
        }
        catch (Exception ex) { DiStatus.Text = "LM Studio unavailable: " + ex.Message; }
        finally { DiLoadButton.IsEnabled = DiSummarizeButton.IsEnabled = true; }
    }

    private void OnDiOpenFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(DiDir);
            Process.Start(new ProcessStartInfo { FileName = DiDir, UseShellExecute = true });
        }
        catch { /* ignore */ }
    }

    // ===== Decision (EEG decides about PDF files) ==========================

    private readonly List<(string Path, string Name, string Text)> _pdfs = new();

    private string DcDir => string.IsNullOrWhiteSpace(_program.Decision!.OutputDir)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "mindedOS", "decisions")
        : _program.Decision!.OutputDir;

    private void SetupDecision()
    {
        if (_program.Decision is null) return;
        DecisionPanel.Visibility = Visibility.Visible;
        Directory.CreateDirectory(DcDir);
    }

    // ===== Intelligent Assistant =========================================

    private AssistMap? _assistMap;

    private void SetupAssist()
    {
        if (_program.Assist is null) return;
        AssistPanel.Visibility = Visibility.Visible;
        var path = MindedOS.Core.DataFile.Resolve(Path.Combine(AppContext.BaseDirectory, "data",
            string.IsNullOrWhiteSpace(_program.Assist.MapFile) ? "eeg_map_assist.csv" : _program.Assist.MapFile));
        try { _assistMap = File.Exists(path) ? AssistMap.Load(path) : null; }
        catch { _assistMap = null; }
        if (_assistMap is null || _assistMap.Entries.Count == 0)
            AsStatus.Text = "Assistant map not found or empty (expected data\\eeg_map_assist.csv).";
    }

    private async void OnAssistOffer(object sender, RoutedEventArgs e)
    {
        if (_assistMap is null || _assistMap.Entries.Count == 0) return;

        AsOfferButton.IsEnabled = false;
        AsTryAgainButton.IsEnabled = false;
        try
        {
            if (_os.Source.State != LinkState.Streaming)
            {
                AsStatus.Text = "Connecting EEG…";
                await _os.Source.ConnectAsync();
            }

            // Sample three live EEG readings, a moment apart, so each maps to its own offer.
            AsStatus.Text = "Reading your EEG…";
            var readings = new List<double>();
            for (int i = 0; i < 3; i++)
            {
                readings.Add(_os.Signals.LastRaw);
                await Task.Delay(200);
            }

            var offers = _assistMap.ThreeOffers(readings);
            AsResults.ItemsSource = offers.Select((o, i) => new
            {
                Service = $"{i + 1}.  {o.Service}",
                Detail = $"EEG {o.Reading:0} → matched {o.Eeg} · “{o.Word}”",
            }).ToList();

            AsStatus.Text = offers.Count > 0
                ? $"Here are {offers.Count} offers from your EEG. Press TRY AGAIN for 3 new ones."
                : "No offers — check the assistant map.";
            AsTryAgainButton.Visibility = Visibility.Visible;
        }
        finally
        {
            AsOfferButton.IsEnabled = true;
            AsTryAgainButton.IsEnabled = true;
        }
    }

    // ===== Internet of Things (robot control) ============================

    private IotCommandMap? _iotMap;
    private MindedOS.Sensor.RobotLink? _robot;
    private System.Windows.Threading.DispatcherTimer? _iotTimer;
    private string _iotLastCommand = "";

    private void SetupIot()
    {
        if (_program.Iot is null) return;
        IotPanel.Visibility = Visibility.Visible;

        var path = MindedOS.Core.DataFile.Resolve(Path.Combine(AppContext.BaseDirectory, "data",
            string.IsNullOrWhiteSpace(_program.Iot.MapFile) ? "eeg_map_iot.csv" : _program.Iot.MapFile));
        try { _iotMap = File.Exists(path) ? IotCommandMap.Load(path) : null; }
        catch { _iotMap = null; }
        if (_iotMap is null || _iotMap.Entries.Count == 0)
            IotStatus.Text = "IoT map not found or empty (expected data\\eeg_map_iot.csv).";

        _robot = new MindedOS.Sensor.RobotLink();
        PopulateIotPorts();

        _iotTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Math.Max(150, _program.Iot.IntervalMs)),
        };
        _iotTimer.Tick += OnIotTick;
        Closed += (_, _) => { _iotTimer?.Stop(); _robot?.Dispose(); };
    }

    private void PopulateIotPorts()
    {
        var current = IotPortBox.SelectedItem as string;
        IotPortBox.Items.Clear();
        foreach (var p in MindedOS.Sensor.RobotLink.AvailablePorts()) IotPortBox.Items.Add(p);
        if (current is not null && IotPortBox.Items.Contains(current)) IotPortBox.SelectedItem = current;
        else if (IotPortBox.Items.Count > 0) IotPortBox.SelectedIndex = 0;
    }

    private void OnIotRefresh(object sender, RoutedEventArgs e) => PopulateIotPorts();

    private void OnIotConnect(object sender, RoutedEventArgs e)
    {
        if (_robot is null) return;
        if (_robot.IsOpen)
        {
            _robot.Close();
            IotConnect.Content = "Connect robot";
            IotStatus.Text = "Robot disconnected. Live commands will be shown but not sent.";
            return;
        }

        if (IotPortBox.SelectedItem is not string port)
        {
            IotStatus.Text = "Pick a COM port first (pair your robot's Bluetooth, then ↻).";
            return;
        }
        if (_robot.Open(port, _program.Iot!.Baud))
        {
            IotConnect.Content = "Disconnect";
            IotStatus.Text = $"Connected to {port} @ {_program.Iot.Baud} baud. Press START CONTROL.";
        }
        else
        {
            IotStatus.Text = $"Could not open {port}. Is the robot paired and the port free?";
        }
    }

    private async void OnIotStream(object sender, RoutedEventArgs e)
    {
        if (_iotTimer is null || _iotMap is null) return;
        if (_iotTimer.IsEnabled)
        {
            _iotTimer.Stop();
            _robot?.Send("STOP");
            IotStream.Content = "▶ START CONTROL";
            IotStatus.Text = "Stopped. (Sent STOP.)";
            return;
        }

        if (_os.Source.State != LinkState.Streaming)
        {
            IotStatus.Text = "Connecting EEG…";
            await _os.Source.ConnectAsync();
        }
        _iotTimer.Start();
        IotStream.Content = "⏸ STOP CONTROL";
        IotStatus.Text = _robot?.IsOpen == true
            ? "Controlling the robot from your EEG…"
            : "Live (no robot connected — showing commands only).";
    }

    private void OnIotTick(object? sender, EventArgs e)
    {
        if (_iotMap is null) return;
        var match = _iotMap.Nearest(_os.Signals.LastRaw);
        if (match is null) return;

        IotCommand.Text = match.Command;
        if (match.Command == _iotLastCommand) return; // only act/log on change
        _iotLastCommand = match.Command;

        bool sent = _robot?.Send(match.Command) == true;
        IotLog.Items.Insert(0, $"{DateTime.Now:HH:mm:ss}  EEG {_os.Signals.LastRaw,5:0} → {match.Command,-8} {(sent ? "✓ sent" : "(shown)")}  “{match.Word}”");
        while (IotLog.Items.Count > 100) IotLog.Items.RemoveAt(IotLog.Items.Count - 1);
    }

    // ===== Limited Memory Machine ========================================

    private MachineMemory? _memory;

    private string MemDir => string.IsNullOrWhiteSpace(_program.Memory!.OutputDir)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "mindedOS", "memory")
        : _program.Memory!.OutputDir;

    private void SetupMemory()
    {
        if (_program.Memory is null) return;
        MemoryPanel.Visibility = Visibility.Visible;
        Directory.CreateDirectory(MemDir);
    }

    private void ShowMemory()
    {
        MemList.Items.Clear();
        if (_memory is null) return;
        int i = 1;
        foreach (var r in _memory.Rows)
            MemList.Items.Add($"{i++,3}.  EEG {r.Eeg,5:0}  {(string.IsNullOrEmpty(r.Word) ? "" : "“" + r.Word + "” "),-14} → {r.Command}");
    }

    private async void OnMemRecord(object sender, RoutedEventArgs e)
    {
        MemRecordButton.IsEnabled = false;
        try
        {
            if (_os.Source.State != LinkState.Streaming)
            {
                MemStatus.Text = "Connecting EEG…";
                await _os.Source.ConnectAsync();
            }

            int seconds = Math.Max(1, _program.Memory!.RecordSeconds);
            var rows = new List<MemoryRow>();
            string last = "";
            var start = DateTime.UtcNow;
            var window = TimeSpan.FromSeconds(seconds);
            while (DateTime.UtcNow - start < window && rows.Count < 400)
            {
                var word = _os.Signals.CurrentWord;
                if (!string.IsNullOrWhiteSpace(word) && word != "—" && word != last)
                {
                    rows.Add(new MemoryRow(_os.Signals.LastRaw, word, ""));
                    last = word;
                }
                int remain = (int)(window - (DateTime.UtcNow - start)).TotalSeconds;
                MemStatus.Text = $"Recording EEG memory… {rows.Count} rows · {remain}s left";
                await Task.Delay(250);
            }

            _memory = new MachineMemory(rows);
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var path = Path.Combine(MemDir, $"memory_{stamp}.csv");
            File.WriteAllText(path, _memory.ToRecordCsv());
            ShowMemory();
            MemStatus.Text = $"Recorded {rows.Count} rows → {Path.GetFileName(path)} (raw_eeg,word). Now ② assign commands, or edit column B by hand and ③ load.";
        }
        finally { MemRecordButton.IsEnabled = true; }
    }

    private async void OnMemAssign(object sender, RoutedEventArgs e)
    {
        if (_memory is null || _memory.Rows.Count == 0) { MemStatus.Text = "Record or load a memory first."; return; }

        MemAssignButton.IsEnabled = false;
        try
        {
            var words = _memory.Rows.Select(r => string.IsNullOrEmpty(r.Word) ? r.Command : r.Word).ToList();
            var assigned = _memory.WithDefaultCommands(); // offline fallback
            try
            {
                MemStatus.Text = "Writing column C with LM Studio…";
                using var client = new MindedOS.Ai.LmStudioClient(_program.Memory!.LmStudioUrl);
                var model = string.IsNullOrWhiteSpace(_program.Memory.Model)
                    ? await client.GetFirstModelAsync() : _program.Memory.Model;
                if (!string.IsNullOrWhiteSpace(model))
                {
                    var p = MindedOS.Ai.MemoryCommandPromptBuilder.Build(words);
                    var reply = await client.CompleteAsync(model!, p.System, p.User);
                    var commands = MindedOS.Ai.MemoryCommandPromptBuilder.ParseCommands(reply);
                    if (commands.Count > 0) assigned = _memory.WithCommands(commands);
                }
            }
            catch (Exception ex)
            {
                MemStatus.Text = "LM Studio offline — assigned default commands. " + ex.Message;
            }

            _memory = assigned;
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var path = Path.Combine(MemDir, $"memory_{stamp}.instructions.csv");
            File.WriteAllText(path, _memory.ToInstructionCsv());
            ShowMemory();
            MemStatus.Text = $"Assigned {_memory.Commands.Count} commands (column C) → {Path.GetFileName(path)}. This ordered list is the machine's memory.";
        }
        finally { MemAssignButton.IsEnabled = true; }
    }

    private void OnMemLoad(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Load a memory CSV (3-column raw_eeg,word,command — or 2-column raw_eeg,command)",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            InitialDirectory = Directory.Exists(MemDir) ? MemDir : null,
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            _memory = MachineMemory.Load(dlg.FileName);
            ShowMemory();
            bool twoCol = _memory.Rows.Count > 0 && _memory.Rows.All(r => string.IsNullOrEmpty(r.Word));
            MemStatus.Text = $"Loaded {_memory.Rows.Count} rows from {Path.GetFileName(dlg.FileName)} " +
                             (twoCol ? "(2-column: command read from column B)." : "(3-column raw_eeg,word,command).") +
                             " This is the machine's memory, in row order.";
        }
        catch (Exception ex) { MemStatus.Text = "Could not load: " + ex.Message; }
    }

    private void OnMemOpenFolder(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(MemDir);
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(MemDir) { UseShellExecute = true }); }
        catch { /* ignore */ }
    }

    private void OnDcScan(object sender, RoutedEventArgs e)
    {
        var folder = DcDir;
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Choose a folder of PDF files", InitialDirectory = Directory.Exists(folder) ? folder : null };
        if (dlg.ShowDialog() == true) folder = dlg.FolderName;

        try
        {
            _pdfs.Clear();
            DcResults.Items.Clear();
            var files = Directory.EnumerateFiles(folder, "*.pdf", SearchOption.TopDirectoryOnly).OrderBy(f => f).Take(100).ToList();
            foreach (var f in files)
            {
                string text;
                try { text = PdfReader.ExtractText(f, _program.Decision!.MaxChars); }
                catch (Exception ex) { text = ""; DcResults.Items.Add($"{Path.GetFileName(f)} — (unreadable: {ex.Message})"); continue; }
                _pdfs.Add((f, Path.GetFileName(f), text));
                DcResults.Items.Add($"{Path.GetFileName(f),-40} {text.Length,6} chars read");
            }
            DcResults.Visibility = Visibility.Visible;
            DcStatus.Text = _pdfs.Count > 0
                ? $"Read {_pdfs.Count} PDF(s). Click ② to let your EEG decide about each."
                : "No readable PDFs found in that folder.";
        }
        catch (Exception ex) { DcStatus.Text = "Scan failed: " + ex.Message; }
    }

    private async void OnDcDecide(object sender, RoutedEventArgs e)
    {
        if (_pdfs.Count == 0) { DcStatus.Text = "Scan a folder of PDFs first (①)."; return; }
        DcScanButton.IsEnabled = DcDecideButton.IsEnabled = false;
        DcResults.Items.Clear();
        var rows = new List<string>();
        try
        {
            if (_os.Source.State != LinkState.Streaming) await _os.Source.ConnectAsync();
            using var client = new MindedOS.Ai.LmStudioClient(_program.Decision!.LmStudioUrl);
            var model = string.IsNullOrWhiteSpace(_program.Decision.Model) ? await client.GetFirstModelAsync() : _program.Decision.Model;
            if (string.IsNullOrWhiteSpace(model)) { DcStatus.Text = "LM Studio has no model loaded."; return; }

            foreach (var (path, name, text) in _pdfs)
            {
                string eegWord = _os.Signals.CurrentWord;
                DcStatus.Text = $"Deciding about {name} through EEG word '{eegWord}'…";
                var p = MindedOS.Ai.DecisionPromptBuilder.Build(name, text, eegWord, _os.Signals.FocusWord);
                var decision = MindedOS.Ai.RewritePromptBuilder.CleanReply(await client.CompleteAsync(model!, p.System, p.User)).Replace("\n", " ").Trim();

                DcResults.Items.Add($"{name}  →  {decision}");
                rows.Add($"\"{name}\",{eegWord},\"{decision.Replace("\"", "'")}\"");
                await Task.Delay(400); // let the brain word change between files
            }
            try { File.WriteAllLines(Path.Combine(DcDir, $"decisions_{DateTime.Now:yyyyMMdd_HHmmss}.csv"),
                new[] { "file,eeg_word,decision" }.Concat(rows)); } catch { }
            DcStatus.Text = $"Your EEG decided about {_pdfs.Count} file(s) (saved as decisions_*.csv).";
        }
        catch (Exception ex) { DcStatus.Text = "LM Studio unavailable: " + ex.Message; }
        finally { DcScanButton.IsEnabled = DcDecideButton.IsEnabled = true; }
    }

    private void OnDcOpenFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(DcDir);
            Process.Start(new ProcessStartInfo { FileName = DcDir, UseShellExecute = true });
        }
        catch { /* ignore */ }
    }

    // ===== Big Data profession analyzer ====================================

    private string BdDir => string.IsNullOrWhiteSpace(_program.BigData!.OutputDir)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "mindedOS", "big_data")
        : _program.BigData!.OutputDir;

    private string BdMapPath => MindedOS.Core.DataFile.Resolve(Path.Combine(AppContext.BaseDirectory, "data", _program.BigData!.MapFile));

    private void SetupBigData()
    {
        if (_program.BigData is null) return;
        BigDataPanel.Visibility = Visibility.Visible;
        Directory.CreateDirectory(BdDir);
        int files = Directory.EnumerateFiles(BdDir, "*.csv").Count();
        BdStatus.Text = files > 0
            ? $"{files} Big Data file(s) on disk. Click ② to load one and compute professions."
            : "No Big Data files yet. Record one (①), or load an existing EEG word CSV (②).";
    }

    private async void OnBdRecord(object sender, RoutedEventArgs e)
    {
        var cfg = _program.BigData!;
        BdRecordButton.IsEnabled = BdLoadButton.IsEnabled = false;
        BdProgress.Value = 0;
        var path = Path.Combine(BdDir, $"bigdata_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        try
        {
            BdStatus.Text = $"Recording Big Data EEG ({cfg.RecordSeconds / 3600.0:0.#} h)… this runs long; leave it running.";
            var rec = new BigDataRecorder();
            int rows = await rec.RecordAsync(_os.Source, _os.Lexicon, cfg.RecordSeconds, path,
                p => Dispatcher.BeginInvoke(() => BdProgress.Value = p));
            BdStatus.Text = $"Saved {Path.GetFileName(path)} ({rows} rows). Click ② to analyze it.";
        }
        catch (Exception ex) { BdStatus.Text = "Recording failed: " + ex.Message; }
        finally { BdRecordButton.IsEnabled = BdLoadButton.IsEnabled = true; }
    }

    private void OnBdLoad(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "EEG CSV (*.csv)|*.csv",
            InitialDirectory = Directory.Exists(BdDir) ? BdDir : null,
            Title = "Load a Big Data EEG file",
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var words = ProfessionMap.WordsFromCsv(dlg.FileName);
            var map = File.Exists(BdMapPath) ? ProfessionMap.Load(BdMapPath) : ProfessionMap.Parse("profession,keywords\nWorker,work do task");
            var ranked = map.Analyze(words, top: 12);

            BdResults.Items.Clear();
            for (int i = 0; i < ranked.Count; i++)
            {
                var r = ranked[i];
                string crown = i == 0 ? "★" : " ";
                BdResults.Items.Add($"{crown} #{i + 1,-2} {r.Profession,-12} {r.Count,6} hits  ·  {r.Percent,5:0.0}%");
            }
            BdResults.Visibility = Visibility.Visible;
            var topProf = ranked.Count > 0 ? ranked[0].Profession : "—";
            BdStatus.Text = $"{Path.GetFileName(dlg.FileName)}: {words.Count} words analyzed · top profession ★ {topProf}.";
        }
        catch (Exception ex) { BdStatus.Text = "Analyze failed: " + ex.Message; }
    }

    private async void OnBdAiMap(object sender, RoutedEventArgs e)
    {
        var cfg = _program.BigData!;
        BdAiMapButton.IsEnabled = false;
        try
        {
            BdStatus.Text = "Asking LM Studio for a profession map…";
            using var client = new MindedOS.Ai.LmStudioClient(cfg.LmStudioUrl);
            var model = string.IsNullOrWhiteSpace(cfg.Model) ? await client.GetFirstModelAsync() : cfg.Model;
            if (string.IsNullOrWhiteSpace(model)) { BdStatus.Text = "LM Studio has no model loaded."; return; }

            var p = MindedOS.Ai.ProfessionMapPromptBuilder.Build();
            var reply = await client.CompleteAsync(model!, p.System, p.User);
            var csv = MindedOS.Ai.RewritePromptBuilder.CleanReply(reply);
            var parsed = ProfessionMap.Parse(csv);
            if (parsed.Professions.Count >= 3)
            {
                File.WriteAllText(BdMapPath, csv);
                BdStatus.Text = $"New profession map saved ({parsed.Professions.Count} professions). Load a file (②) to use it.";
            }
            else BdStatus.Text = "LM Studio returned an unusable map; kept the current one.";
        }
        catch (Exception ex) { BdStatus.Text = "LM Studio unavailable: " + ex.Message; }
        finally { BdAiMapButton.IsEnabled = true; }
    }

    private void OnBdOpenFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(BdDir);
            Process.Start(new ProcessStartInfo { FileName = BdDir, UseShellExecute = true });
        }
        catch { /* ignore */ }
    }

    // ===== Black Box Learning ==============================================

    private LearningStats? _bbStats;

    private string BbDir => string.IsNullOrWhiteSpace(_program.BlackBox!.OutputDir)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "mindedOS", "black_box")
        : _program.BlackBox!.OutputDir;

    private void SetupBlackBox()
    {
        if (_program.BlackBox is null) return;
        BlackBoxPanel.Visibility = Visibility.Visible;
        BbSubject.Text = "";
        Directory.CreateDirectory(BbDir);
        BbStudyButton.Content = $"① STUDY {_program.BlackBox.StudySeconds / 60} MIN (RECORD)";
    }

    private async void OnBbStudy(object sender, RoutedEventArgs e)
    {
        var cfg = _program.BlackBox!;
        BbStudyButton.IsEnabled = BbLoadButton.IsEnabled = false;
        BbProgress.Value = 0;
        var subj = SafeName(string.IsNullOrWhiteSpace(BbSubject.Text) ? "subject" : BbSubject.Text);
        var path = Path.Combine(BbDir, $"study_{subj}_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        try
        {
            BbStatus.Text = $"Studying for {cfg.StudySeconds / 60} minutes — focus on your subject…";
            var rec = new EegCsvRecorder();
            await rec.RecordAsync(_os.Source, cfg.StudySeconds, path,
                p => Dispatcher.BeginInvoke(() => BbProgress.Value = p));
            BbStatus.Text = $"Saved {Path.GetFileName(path)}. Click ② to analyze your learning.";
        }
        catch (Exception ex) { BbStatus.Text = "Study recording failed: " + ex.Message; }
        finally { BbStudyButton.IsEnabled = BbLoadButton.IsEnabled = true; }
    }

    private void OnBbLoad(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Study CSV (*.csv)|*.csv",
            InitialDirectory = Directory.Exists(BbDir) ? BbDir : null,
            Title = "Load a study-session CSV",
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var s = LearningStats.FromCsv(dlg.FileName);
            _bbStats = s;
            BbResults.Items.Clear();
            void Add(string name, double v) => BbResults.Items.Add($"{name,-18} {Bar(v)} {v,5:0.0}");
            Add("Focus", s.Focus);
            Add("Logic Reasoning", s.LogicReasoning);
            Add("Logic Bricks", s.LogicBricks);
            Add("Logical Thoughts", s.LogicalThoughts);
            Add("Mindfulness", s.Mindfulness);
            Add("Flow State", s.FlowState);
            Add("OVERALL", s.Overall);
            BbResults.Visibility = Visibility.Visible;
            BbStatus.Text = $"{Path.GetFileName(dlg.FileName)} analyzed — overall learning {s.Overall:0.0}/100.";
        }
        catch (Exception ex) { BbStatus.Text = "Analyze failed: " + ex.Message; }
    }

    private static string Bar(double v)
    {
        int filled = (int)Math.Round(Math.Clamp(v, 0, 100) / 10.0);
        return "[" + new string('#', filled) + new string('.', 10 - filled) + "]";
    }

    private async void OnBbReport(object sender, RoutedEventArgs e)
    {
        if (_bbStats is null) { BbStatus.Text = "Load a study CSV first (②)."; return; }
        var cfg = _program.BlackBox!;
        BbReportButton.IsEnabled = false;
        try
        {
            BbStatus.Text = "Writing your learning report with LM Studio…";
            using var client = new MindedOS.Ai.LmStudioClient(cfg.LmStudioUrl);
            var model = string.IsNullOrWhiteSpace(cfg.Model) ? await client.GetFirstModelAsync() : cfg.Model;
            if (string.IsNullOrWhiteSpace(model)) { BbStatus.Text = "LM Studio has no model loaded."; return; }

            var p = MindedOS.Ai.LearningReportPromptBuilder.Build(BbSubject.Text, _bbStats);
            var reply = await client.CompleteAsync(model!, p.System, p.User);
            BbReport.Text = MindedOS.Ai.RewritePromptBuilder.CleanReply(reply);
            BbReport.Visibility = Visibility.Visible;
            BbStatus.Text = "Learning report ready.";
        }
        catch (Exception ex) { BbStatus.Text = "LM Studio unavailable: " + ex.Message; }
        finally { BbReportButton.IsEnabled = true; }
    }

    private void OnBbOpenFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(BbDir);
            Process.Start(new ProcessStartInfo { FileName = BbDir, UseShellExecute = true });
        }
        catch { /* ignore */ }
    }

    // ===== Form building ====================================================

    private void BuildForm()
    {
        bool absolute = _program.Controls.Any(c => c.X.HasValue && c.Y.HasValue);
        if (absolute) BuildAbsolute();
        else BuildStacked();
    }

    private void BuildAbsolute()
    {
        var canvas = new Canvas { Width = 490, MinHeight = 120 };

        // Place positioned (template) controls first and track the lowest edge,
        // then flow un-positioned (JSON) controls in a column beneath them.
        double maxBottom = 0;
        var unplaced = new List<(FrameworkElement el, ControlSpec spec)>();

        foreach (var spec in _program.Controls)
        {
            var element = CreateControl(spec);
            if (element is null) continue;
            if (spec.W is { } w) element.Width = w;
            if (spec.H is { } h && element is not TextBlock) element.Height = h;

            if (spec.X is { } x && spec.Y is { } y)
            {
                Canvas.SetLeft(element, x);
                Canvas.SetTop(element, y);
                canvas.Children.Add(element);
                maxBottom = Math.Max(maxBottom, y + (spec.H ?? 28) + 6);
            }
            else
            {
                unplaced.Add((element, spec));
            }
        }

        double flowY = maxBottom + 12;
        foreach (var (element, spec) in unplaced)
        {
            element.Width = spec.W ?? 461;
            Canvas.SetLeft(element, 10);
            Canvas.SetTop(element, flowY);
            canvas.Children.Add(element);
            flowY += (spec.H ?? 30) + 10;
        }

        canvas.Height = Math.Max(maxBottom, flowY) + 10;
        FormHost.Children.Add(canvas);
    }

    private void BuildStacked()
    {
        var stack = new StackPanel();
        foreach (var spec in _program.Controls)
        {
            var element = CreateControl(spec);
            if (element is null) continue;
            element.Margin = new Thickness(0, 0, 0, 10);
            element.HorizontalAlignment = HorizontalAlignment.Stretch;
            stack.Children.Add(element);
        }
        FormHost.Children.Add(stack);
    }

    private FrameworkElement? CreateControl(ControlSpec spec)
    {
        switch (spec.Type)
        {
            case "Button":
                var btn = new Button { Content = spec.Label };
                if (spec.Action is { } actId)
                    btn.Click += (_, _) => _os.Executor.Run(actId);
                return btn;

            case "CheckBox":
                return new CheckBox { Content = spec.Label };

            case "RadioButton":
                return new RadioButton { Content = spec.Label };

            case "Slider":
                var slider = new Slider { Minimum = 0, Maximum = 100, Value = 50, Width = spec.W ?? 200 };
                return slider;

            case "ComboBox":
                var combo = new ComboBox();
                foreach (var item in spec.Items ?? new()) combo.Items.Add(item);
                if (combo.Items.Count > 0) combo.SelectedIndex = 0;
                return combo;

            case "ListBox":
                var lb = new ListBox { Height = spec.H ?? 90 };
                foreach (var item in spec.Items ?? new()) lb.Items.Add(item);
                return lb;

            case "ListView":
                var lv = new ListView { Height = spec.H ?? 110 };
                foreach (var item in spec.Items ?? new()) lv.Items.Add(item);
                return lv;

            case "TextBox":
                return new TextBox { Text = spec.Label, MinWidth = 120 };

            case "ProgressBar":
                var bar = new ProgressBar { Minimum = 0, Maximum = 100, Height = spec.H ?? 20 };
                var signal = spec.Trigger?.Signal ?? "attention";
                double max = signal is "attention" or "meditation" or "blink" or "signal" ? 100 : 1_000_000;
                _liveBars.Add((bar, signal, max));
                return bar;

            case "Text":
            default:
                var tb = new TextBlock { Text = Substitute(spec.Label), TextWrapping = TextWrapping.Wrap };
                if (spec.Label.Contains('{')) _liveTexts.Add((tb, spec.Label));
                return tb;
        }
    }

    // ===== Triggers =========================================================

    private void RegisterTriggers()
    {
        foreach (var spec in _program.Controls)
        {
            if (spec.Trigger is null || spec.Action is null) continue;
            _os.Triggers.Register(new TriggerBinding
            {
                ProgramName = _program.Name,
                Trigger = spec.Trigger,
                Condition = spec.Condition,
                ActionId = spec.Action,
            });
        }
    }

    // ===== Live updates =====================================================

    private void OnSignalUpdated(string signal)
    {
        Dispatcher.BeginInvoke(() =>
        {
            LiveFocus.Text = $"Focus {_os.Signals.Attention} ({_os.Signals.FocusWord})";
            LiveCalm.Text = $"Calm {_os.Signals.Meditation} ({_os.Signals.CalmWord})";
            LiveSignal.Text = $"Signal: {_os.Signals.SignalQuality} ({_os.Signals.SignalNoise})";
            LiveBlink.Text = $"Blink: {_os.Signals.LastBlink}";
            LiveRaw.Text = $"Raw: {_os.Signals.LastRaw} ({_os.Signals.Microvolts:0.0}µV)";
            LiveBands.Text = _os.Signals.BandSummary();
            LiveWord.Text = $"word: {_os.Signals.CurrentWord}";

            foreach (var (bar, sig, max) in _liveBars)
                bar.Value = Math.Clamp(_os.Signals.GetSignal(sig) / max * 100, 0, 100);

            foreach (var (tb, template) in _liveTexts)
                tb.Text = Substitute(template);
        });
    }

    private string Substitute(string text) => text
        .Replace("{word}", _os.Signals.CurrentWord)
        .Replace("{focus}", _os.Signals.FocusWord)
        .Replace("{calm}", _os.Signals.CalmWord)
        .Replace("{attention}", _os.Signals.Attention.ToString())
        .Replace("{meditation}", _os.Signals.Meditation.ToString());

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
        else if (e.Key == Key.F11)
        {
            if (WindowStyle == WindowStyle.None)
            {
                WindowStyle = WindowStyle.SingleBorderWindow;
                WindowState = WindowState.Normal;
            }
            else
            {
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;
            }
        }
    }

    private static int IndexOf(OsContext os, SubProgram program)
    {
        for (int i = 0; i < os.Programs.Count; i++)
            if (ReferenceEquals(os.Programs[i], program)) return i;
        return -1;
    }

    private static string GlyphFromHex(string hex) =>
        int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int code)
            ? char.ConvertFromUtf32(code) : "";
}
