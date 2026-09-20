using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace PhasmoSound;

public partial class App : System.Windows.Application
{
    Config cfg = new();
    readonly OverlayState state = new();
    AudioEngine? engine;
    Classifier? clf;
    OverlayWindow? win;
    System.Windows.Forms.NotifyIcon? tray;
    readonly CancellationTokenSource cts = new();
    DateTime lastLoudEvent = DateTime.MinValue;
    readonly System.Collections.Generic.Dictionary<string, DateTime> lastSeen = new();
    InputWatch? input;
    SampleStore? store;
    Speaker? speaker;
    SayWindow? say;
    CaptionClient? captions;
    float[] floorDb = Array.Empty<float>();   // slowly tracked background level per channel
    float floorAll = float.NaN;
    DateTime lastFrameTime = DateTime.MinValue;
    DateTime lastBackground = DateTime.MinValue, now0;
    readonly AutoResetEvent onsetSignal = new(false);
    DateTime lastPending = DateTime.MinValue;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Log.Write("---- PhasmoSound starting ----");
        cfg = Config.Load(Path.Combine(AppContext.BaseDirectory, "config.json"));
        try
        {
            if (cfg.HideOwnSounds) input = new InputWatch(cfg);
            var dev = AudioEngine.PickDevice(cfg.Device);
            engine = new AudioEngine(cfg, dev);
            engine.Frame += OnFrame;
            engine.Start();
            lock (state.Lock)
            {
                state.Channels = engine.Channels;
                state.Surround = Direction.IsSurround(engine.Channels);
                state.DeviceName = dev.FriendlyName;
                state.ChannelDb = Enumerable.Repeat(-100f, engine.Channels).ToArray();
            }
            clf = new Classifier(Path.Combine(AppContext.BaseDirectory, "model", "yamnet.onnx"),
                                 Path.Combine(AppContext.BaseDirectory, "model", "yamnet_class_map.csv"));
            store = new SampleStore(Path.Combine(AppContext.BaseDirectory, "samples"));
        }
        catch (Exception ex)
        {
            Log.Write(ex.ToString());
            System.Windows.MessageBox.Show("PhasmoSound could not start:\n\n" + ex.Message + "\n\nSee log.txt next to the app.", "PhasmoSound");
            Shutdown();
            return;
        }

        // start classifying before the window exists so a slow window never delays sound detection
        new Thread(ClassifyLoop) { IsBackground = true, Name = "classify", Priority = ThreadPriority.AboveNormal }.Start();
        if (cfg.WhisperCaptions && engine != null)
        {
            captions = new CaptionClient(cfg, state);
            captions.Start();
            engine.Samples16k += s => captions.Feed(s);
        }
        Log.Write("creating window");
        win = new OverlayWindow(cfg, state);
        win.TrainRequested += OnTrain;
        if (cfg.Speak)
        {
            if (cfg.SetDefaultMic) DefaultMic.Ensure(cfg.MicDevice, cfg.DefaultMicVolume, cfg.MicFormatRate, cfg.SpeakDevice, cfg.Device);
            speaker = new Speaker(cfg);
            speaker.StartKokoro();
            var phrases = new Phrases(Path.Combine(AppContext.BaseDirectory, "phrases"));
            speaker.Phrases = phrases;
            say = new SayWindow(cfg, speaker);
            win.BoardPage += page =>
            {
                if (page == 0) phrases.Reload();
                var lines = phrases.Items.Skip(page * 9).Take(9).Select(p => p.Text).ToList();
                int pages = Math.Max(1, (phrases.Items.Count + 8) / 9);
                lock (state.Lock)
                {
                    state.Board = lines; state.BoardPageNo = page; state.BoardPages = pages;
                    state.PageNames = Enumerable.Range(0, pages).Select(i => i < (cfg.PageNames?.Length ?? 0) ? cfg.PageNames![i] : "Page " + (i + 1)).ToList();
                }
            };
            win.PhraseChosen += (i, game) =>
            {
                if (i < 0 || i >= phrases.Items.Count) return;
                say.SetGame(game);
                _ = say.SpeakLine(phrases.Items[i].Text);
            };
            var wheel = new WheelWindow(cfg, phrases);
            IntPtr wheelGame = IntPtr.Zero;
            wheel.PhraseChosen += i =>
            {
                if (i < 0 || i >= phrases.Items.Count) return;
                say.SetGame(wheelGame);
                _ = say.SpeakLine(phrases.Items[i].Text);
            };
            win.WheelToggle += (l, t, r, b, game) => { wheelGame = game; wheel.Toggle(l, t, r, b, game); };
            say.Spoken += text =>
            {
                lock (state.Lock) AddEvent(new EventItem { Time = DateTime.UtcNow, Label = "You: " + text, Category = "voice", Angle = 0, Conf = 0, Score = 1f });
            };
            win.SayRequested += (l, t, r, b, game) => say.ShowOver(l, t, r, b, game);
            win.PresetRequested += (n, game) =>
            {
                say.SetGame(game);
                string line = n == 2 ? cfg.IntroLine2 : cfg.IntroLine;
                if (!string.IsNullOrWhiteSpace(line)) _ = say.SpeakLine(line);
            };
        }
        win.PrePosition();
        Log.Write("showing window");
        win.Show();
        Log.Write("window shown");
        SetupTray();
        Log.Write("tray ready");
        DispatcherUnhandledException += (_, a) => { Log.Write("UI exception: " + a.Exception); a.Handled = true; };
    }

    void SetupTray()
    {
        tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Information,
            Text = "PhasmoSound overlay  (F8 hide, Ctrl+F8 quit)",
            Visible = true,
        };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Show / hide overlay (F8)", null, (_, _) => win?.ToggleOverlay());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Quit PhasmoSound (Ctrl+F8)", null, (_, _) => Shutdown());
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => win?.ToggleOverlay();
    }

    /// Training hotkey: save the last few seconds of audio as a labeled example.
    void OnTrain(int labelIndex)
    {
        if (engine == null || clf == null || store == null) return;
        string label = labelIndex >= 0 && labelIndex < cfg.TrainLabels.Length ? cfg.TrainLabels[labelIndex] : "unlabeled";
        int n = AudioEngine.TargetRate * cfg.TrainClipMs / 1000;
        var wave = new float[n];
        if (!engine.GetLatest(n, wave)) return;
        Task.Run(() =>
        {
            try
            {
                var (_, emb) = clf.Run(wave);
                var s = store.Add(label, wave, emb);
                int count = store.CountOf(label);
                Log.Write($"TRAIN saved {s.File} label=\"{label}\" (now {count} example{(count == 1 ? "" : "s")})");
                lock (state.Lock)
                {
                    state.Toast = label == "unlabeled" ? $"Saved clip (unlabeled) #{store.Count}" : $"Saved: {label}  ({count} example{(count == 1 ? "" : "s")})";
                    state.ToastUntil = DateTime.UtcNow.AddSeconds(3);
                }
            }
            catch (Exception ex) { Log.Write("train: " + ex.Message); }
        });
    }

    // Audio thread, every ~20 ms.
    void OnFrame(LevelFrame f)
    {
        var (angle, conf) = Direction.Estimate(f.ChannelRms);
        input?.Poll();
        // a centered sudden sound while you are walking or just pressed something is most likely you
        bool ownOnset = input != null && conf < cfg.CenteredConf && (input.IsMoving || input.IsActing);
        // Adaptive floor: constant background is absorbed slowly; anything that drops resets the floor at once.
        float dt = lastFrameTime == DateTime.MinValue ? 0.005f : (float)Math.Clamp((f.Time - lastFrameTime).TotalSeconds, 0, 0.1);
        lastFrameTime = f.Time;
        // The floor follows the AVERAGE background level (slowly up, faster down), not the quietest
        // moment, so a fluttering drone like wind or a buzz stays inside the gate while a footstep pops out.
        float kUp = Math.Min(1f, dt / Math.Max(0.2f, 6f / Math.Max(0.1f, cfg.AmbientRiseDbPerSec)));  // 2 dB/s -> ~3 s time constant
        float kDown = Math.Min(1f, dt / 0.7f);
        if (floorDb.Length != f.ChannelRms.Length) { floorDb = new float[f.ChannelRms.Length]; Array.Fill(floorDb, -100f); floorAll = float.NaN; }
        float loudRel = f.LoudDb;
        if (cfg.AdaptiveFloor)
        {
            if (float.IsNaN(floorAll)) floorAll = f.LoudDb;
            floorAll += (f.LoudDb - floorAll) * (f.LoudDb > floorAll ? kUp : kDown);
            loudRel = cfg.MinDb + (f.LoudDb - floorAll) - cfg.AmbientGateDb;
        }
        lock (state.Lock)
        {
            if (state.ChannelDb.Length != f.ChannelRms.Length) state.ChannelDb = new float[f.ChannelRms.Length];
            for (int c = 0; c < f.ChannelRms.Length; c++)
            {
                float db = 20f * MathF.Log10(f.ChannelRms[c] + 1e-7f);
                if (cfg.AdaptiveFloor)
                {
                    if (floorDb[c] <= -99f) floorDb[c] = db;
                    floorDb[c] += (db - floorDb[c]) * (db > floorDb[c] ? kUp : kDown);
                    db = cfg.MinDb + (db - floorDb[c]) - cfg.AmbientGateDb;
                }
                float cur = state.ChannelDb[c];
                state.ChannelDb[c] = db > cur ? db : cur + (db - cur) * 0.18f;
            }
            state.LoudDb = loudRel > state.LoudDb ? loudRel : state.LoudDb + (loudRel - state.LoudDb) * 0.18f;
            if (loudRel > cfg.MinDb)
            {
                // smooth the direction only while there is something to hear
                float d = angle - state.DirAngle;
                while (d > 180) d -= 360; while (d < -180) d += 360;
                state.DirAngle += d * 0.35f;
                state.DirConf += (conf - state.DirConf) * 0.35f;
            }
            else state.DirConf *= 0.9f;

            if (loudRel > state.WinPeakDb) { state.WinPeakDb = loudRel; state.WinPeakAngle = angle; state.WinPeakConf = conf; }

            if (f.Onset && !ownOnset)
            {
                state.Onsets.Add(new OnsetMark { Time = f.Time, Angle = angle, Conf = conf, Strength = f.OnsetDb });
                if (state.Onsets.Count > 24) state.Onsets.RemoveAt(0);
                // show a row right now; the classifier names it a moment later
                if ((f.Time - lastPending).TotalMilliseconds > 350)
                {
                    lastPending = f.Time;
                    bool loud = f.OnsetDb >= cfg.LoudOnsetDb;
                    AddEvent(new EventItem { Time = f.Time, Label = loud ? "LOUD !" : "!", Category = loud ? "alert" : "other", Angle = angle, Conf = conf, Score = 1f, Pending = true });
                    onsetSignal.Set();
                    win?.RedrawNow();
                    Log.Write($"ONSET +{f.OnsetDb:F0}dB at {f.LoudDb:F0}dB dir={angle:F0}");
                }
            }
        }
    }

    // caller holds state.Lock
    void AddEvent(EventItem ev)
    {
        state.Events.Insert(0, ev);
        if (state.Events.Count > 40) state.Events.RemoveRange(40, state.Events.Count - 40);
    }

    /// Dedicated thread. Runs on a schedule, and immediately (after a short delay so the sound is in
    /// the buffer) whenever the audio thread reports a sudden sound; then once more to refine the name.
    void ClassifyLoop()
    {
        var buf = new float[24000];
        while (!cts.IsCancellationRequested)
        {
            try
            {
                bool fast = onsetSignal.WaitOne(cfg.ClassifyIntervalMs);
                if (fast)
                {
                    // three looks at a new sound: almost immediately, once its attack is in, and once it has played out
                    Thread.Sleep(cfg.FastClassifyDelayMs);
                    ClassifyOnce(buf, 15600, fast: true, final: false);   // shortest window the model accepts: the new sound dominates
                    Thread.Sleep(60);
                    ClassifyOnce(buf, 15600, fast: true, final: false);
                    Thread.Sleep(280);
                    ClassifyOnce(buf, 24000, fast: true, final: true);
                }
                else ClassifyOnce(buf, 24000, fast: false, final: false);
            }
            catch (Exception ex) { Log.Write("classify: " + ex.Message); Thread.Sleep(1000); }
        }
    }

    void ClassifyOnce(float[] buf, int window, bool fast, bool final)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        {
            {
                if (engine == null || clf == null || !engine.GetLatest(window, buf)) return;
                var wave = window == buf.Length ? buf : buf.AsSpan(0, window).ToArray();

                float peak, peakAngle, peakConf;
                lock (state.Lock)
                {
                    peak = state.WinPeakDb; peakAngle = state.WinPeakAngle; peakConf = state.WinPeakConf;
                    if (!fast || final) state.WinPeakDb = -100f;
                }
                if (peak < cfg.MinDb)
                {
                    lock (state.Lock) state.Current = new();
                    return;
                }
                if (!fast && store != null && store.Count > 0 && peak < cfg.MinDb + 22
                    && (DateTime.UtcNow - lastPending).TotalSeconds > 1.5   // nothing sudden happened lately
                    && (now0 = DateTime.UtcNow) - lastBackground > TimeSpan.FromSeconds(2))
                {
                    // quiet window: learn what the current room sounds like when nothing happens
                    lastBackground = now0;
                    var (_, bgEmb) = clf.Run(wave);
                    store.LearnBackground(bgEmb);
                    return;
                }
                var (scores, emb) = clf.Run(wave);
                Labels.Profiles.TryGetValue(cfg.Profile ?? "", out var allow);
                var dets = clf.Detect(scores, cfg.ClassifyThreshold * 0.7f, cfg.ShowVoice, cfg.ShowMusic, allow);
                var now = DateTime.UtcNow;
                string customNote = "";
                if (store != null && store.Count > 0)
                {
                    // "custom" = clips you recorded (Ctrl+1..9), "game" = clips taken from the game's own files
                    bool useOwn = !(cfg.HideCategories?.Contains("custom", StringComparer.OrdinalIgnoreCase) ?? false);
                    bool useGame = !(cfg.HideCategories?.Contains("game", StringComparer.OrdinalIgnoreCase) ?? false);
                    var (label, sim, file, isGame) = store.Nearest(emb, cfg.OwnSampleBonus, useOwn, useGame);
                    float bg = store.BackgroundSimilarity(emb);
                    customNote = $" | nearest={label} {sim:F2}{(isGame ? "g" : "")} bg={bg:F2}";
                    float need = isGame ? cfg.GameMatch : cfg.CustomMatch;
                    if (sim >= need && !string.IsNullOrEmpty(label) && (bg < 0 || sim - bg >= cfg.CustomMargin))
                    {
                        // a known example matches: it outranks the generic model's guess
                        dets.RemoveAll(d => d.Label == label);
                        dets.Insert(0, new Detection(label, isGame ? "game" : "custom", sim, (isGame ? "game:" : "trained:") + file));
                    }
                }
                if (cfg.HideCategories != null && cfg.HideCategories.Length > 0)
                    dets = dets.Where(d => !cfg.HideCategories.Contains(d.Category, StringComparer.OrdinalIgnoreCase)).ToList();
                if (input != null && peakConf < cfg.CenteredConf)
                {
                    bool moving = input.IsMoving, acting = input.IsActing;
                    var own = dets.Where(d => (moving && Labels.OwnMoveLabels.Contains(d.Label))
                                           || (acting && Labels.OwnActionLabels.Contains(d.Label))).ToList();
                    if (own.Count > 0)
                    {
                        Log.Write("own (hidden): " + string.Join(", ", own.Select(d => $"{d.Label}={d.Score:F2}")) + $" moving={moving} acting={acting}");
                        dets = dets.Except(own).ToList();
                    }
                }
                lock (state.Lock)
                {
                    state.Current = dets.Take(4).ToList();
                    // footsteps clearly off to one side are somebody else's: mark that side
                    float stepLine = (input != null && input.IsMoving) ? cfg.OwnStepMaxDeg : cfg.StillStepMinDeg;
                    if (cfg.StepIcon && Math.Abs(peakAngle) > stepLine
                        && dets.Any(d => d.Label == "Footsteps" || d.Label == "Running" || Labels.OwnMoveLabels.Contains(d.Label)))
                    {
                        state.Steps.Add(new StepMark { Time = now, Angle = peakAngle, Level = Math.Clamp((peak - cfg.MinDb) / (cfg.MaxDb - cfg.MinDb), 0f, 1f) });
                        if (state.Steps.Count > 16) state.Steps.RemoveAt(0);
                    }
                    // a row that appeared the instant the sound hit: give it its name now
                    var pending = state.Events.FirstOrDefault(e => e.Pending && (now - e.Time).TotalSeconds < 1.5);
                    var top = dets.FirstOrDefault(d => d.Score >= cfg.ClassifyThreshold * 0.7f);
                    if (pending != null)
                    {
                        if (top != null)
                        {
                            pending.Label = top.Label; pending.Category = top.Category; pending.Score = top.Score;
                            lastSeen[top.Label] = now;
                        }
                        else if (final && pending.Label == "!") pending.Label = "Sound";
                        if (final) pending.Pending = false;
                    }
                    foreach (var d in dets.Where(d => d.Score >= cfg.ClassifyThreshold))
                    {
                        // one sudden sound = one row: while its row is still being named, other guesses only feed that row
                        if (pending != null) { lastSeen[d.Label] = now; continue; }
                        // log a sound when it starts; a continuous sound (crickets, rain) logs once, not every window
                        bool ongoing = lastSeen.TryGetValue(d.Label, out var t) && (now - t).TotalSeconds < 2.5;
                        lastSeen[d.Label] = now;
                        if (ongoing) continue;
                        AddEvent(new EventItem { Time = now, Label = d.Label, Category = d.Category, Angle = peakAngle, Conf = peakConf, Score = d.Score });
                    }
                }
                if (fast) win?.RedrawNow();
                if (dets.Count > 0 || customNote.Length > 0)
                    Log.Write($"[{(fast ? (final ? "refine " : "fast ") : "")}{sw.ElapsedMilliseconds}ms] peak={peak:F0}dB dir={peakAngle:F0} " + string.Join(", ", dets.Take(5).Select(d => $"{d.Label}={d.Score:F2}")) + customNote);
            }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        cts.Cancel();
        captions?.Dispose();
        speaker?.StopKokoro();
        if (tray != null) { tray.Visible = false; tray.Dispose(); }
        engine?.Dispose();
        clf?.Dispose();
        Log.Write("---- PhasmoSound exit ----");
        base.OnExit(e);
    }
}
