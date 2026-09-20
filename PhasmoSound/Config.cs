using System;
using System.IO;
using System.Text.Json;

namespace PhasmoSound;

public sealed class Config
{
    // "auto" = use "CABLE Input" (7.1 virtual cable) if present, else the default output device.
    public string Device { get; set; } = "auto";   // the device the GAME plays on (what the overlay listens to). "auto" = default output.
    // Follow the Phasmophobia window (overlay covers whichever monitor the game is on).
    public bool FollowGame { get; set; } = true;
    public string GameProcess { get; set; } = "Phasmophobia";
    // Make the game window borderless and stretch it over its monitor (fullscreen look, overlay still works). F9 re-applies.
    public bool BorderlessGame { get; set; } = true;
    // Whisper captions: the overlay streams the game audio to a local faster-whisper process and draws the captions itself.
    public bool WhisperCaptions { get; set; } = true;
    public string WhisperScript { get; set; } = @"python\captions_server.py";
    public int WhisperPort { get; set; } = 8766;
    public string WhisperModel { get; set; } = "small.en";   // tiny.en / base.en / small.en / medium.en / large-v3
    public string WhisperDevice { get; set; } = "auto";      // auto = GPU if it works, else CPU
    public int CaptionLines { get; set; } = 4;
    public double CaptionFontSize { get; set; } = 17;
    public int CaptionLifetimeSec { get; set; } = 12;
    // Windows Live Captions (weak on game audio; off now that Whisper captions exist).
    public bool LiveCaptions { get; set; } = false;
    // Where to park the Live Captions bar: "bottom", "top", or "leave" (do not move it).
    public string LiveCaptionsPosition { get; set; } = "bottom";
    public int LiveCaptionsBottomMargin { get; set; } = 40;
    // "phasmophobia" = only label sounds that exist in Phasmophobia. "all" = every sound class.
    public string Profile { get; set; } = "phasmophobia";
    // "edges" = glow on the screen edges (top = front, bottom = back, sides = left/right). "ring" = circle in the middle.
    public string Style { get; set; } = "edges";
    public double EdgeThickness { get; set; } = 70;
    // Ring style only: radius as a fraction of the shorter screen edge.
    public double RingRadius { get; set; } = 0.17;
    public double RingThickness { get; set; } = 16;
    // Loudness range mapped to ring brightness (dBFS).
    public float MinDb { get; set; } = -42f;
    public float MaxDb { get; set; } = -10f;
    // Steady background (hum, buzz, outside drone) is learned and subtracted: the edges and the
    // classifier only react to sound that rises AmbientGateDb above the slowly-tracked floor.
    public bool AdaptiveFloor { get; set; } = true;
    public float AmbientRiseDbPerSec { get; set; } = 2f;   // how fast a constant sound gets absorbed into the floor
    public float AmbientGateDb { get; set; } = 6f;         // must be this far above the floor to count
    // A sudden jump of this many dB within ~20 ms counts as a "sudden sound".
    public float OnsetDb { get; set; } = 10f;
    public float LoudOnsetDb { get; set; } = 14f;
    // Sound classifier
    public float ClassifyThreshold { get; set; } = 0.30f;
    public int ClassifyIntervalMs { get; set; } = 250;  // background schedule
    public int FastClassifyDelayMs { get; set; } = 20;  // after a sudden sound: wait this long, then classify at once
    public int CaptureBufferMs { get; set; } = 10;      // audio capture buffer; smaller = less delay
    // Categories never shown (and, for custom/game, never used for matching):
    // move, door, object, ghost, voice, alert, env (teal), music, other,
    // custom (your own Ctrl+1..9 recordings), game (clips from the game's own files, gold)
    public string[] HideCategories { get; set; } = { "env", "custom" };
    public bool ShowVoice { get; set; } = true;
    public bool ShowMusic { get; set; } = false;
    // Hide your own sounds: centered footsteps while a movement key is held, and centered item/door
    // sounds right after you press an action key or mouse button.
    public bool HideOwnSounds { get; set; } = true;
    public int OwnMoveTailMs { get; set; } = 500;     // keep hiding footsteps this long after you stop
    public int OwnActionWindowMs { get; set; } = 700; // hide item/door sounds this long after a key press
    public float CenteredConf { get; set; } = 0.34f;  // below this left/right difference (about 30 degrees) a sound counts as "centered"
    // Footsteps heard further off-center than this (degrees) cannot be yours: a footprint icon is shown on that side.
    public bool StepIcon { get; set; } = true;
    public float OwnStepMaxDeg { get; set; } = 30f;    // while you are moving: steps inside this are yours
    public float StillStepMinDeg { get; set; } = 15f;  // while you stand still: steps beyond this get the icon
    public double StepIconSize { get; set; } = 64;
    public string MoveKeys { get; set; } = "W A S D Up Down Left Right Shift";
    public string ActionKeys { get; set; } = "E F G Q R T J C LButton RButton MButton";
    // Training: Ctrl+1 .. Ctrl+9 save the last 2.5 s of audio as an example of TrainLabels[n-1].
    // F7 saves an unlabeled clip. Saved clips live in app\samples\ and are matched at runtime.
    public string[] TrainLabels { get; set; } =
    {
        "Door open", "Door close", "Door slam", "Knock", "Ghost footsteps", "Item drop", "Light switch", "Breathing", "Whisper",
    };
    public float CustomMatch { get; set; } = 0.82f;  // cosine similarity needed to accept a trained label (0-1)
    public float CustomMargin { get; set; } = 0.04f; // and it must beat the similarity to the room's background by this much
    public float GameMatch { get; set; } = 0.88f;    // stricter bar for the game's own (dry, studio) sound files in samples\game
    public float OwnSampleBonus { get; set; } = 0.05f; // clips you recorded in-game outrank game files by this much
    public int TrainClipMs { get; set; } = 2500;
    // Speak: F6 opens a text box; Enter speaks the line into the virtual mic the game uses as your microphone.
    public bool Speak { get; set; } = true;
    public string SpeakDevice { get; set; } = "CABLE Input";     // output device that feeds the virtual mic (VB-CABLE)
    public string MicDevice { get; set; } = "CABLE Output";      // the virtual mic the game should use (made Windows default at startup)
    public int MicFormatRate { get; set; } = 0;                  // 0 = leave the mic format alone (VB-CABLE is already 48 kHz); e.g. 48000 to force it
    // Voice engine order: Kokoro (local, natural) -> edge-tts (Microsoft online) -> Windows voice.
    public bool UseKokoro { get; set; } = true;
    public string KokoroPython { get; set; } = @"python\.venv\Scripts\python.exe";
    public string KokoroScript { get; set; } = @"python\kokoro_server.py";
    public int KokoroPort { get; set; } = 8765;
    public string KokoroVoice { get; set; } = "am_adam";   // am_adam, am_michael, am_eric, am_liam, am_onyx, bm_george, af_heart ...
    public float KokoroSpeed { get; set; } = 1.0f;
    public string SpeakVoice { get; set; } = "en-US-BrianMultilingualNeural";
    public string SpeakRate { get; set; } = "-8%";
    public float SpeakVolume { get; set; } = 0.4f;       // 0..1, how loud the voice goes into the mic (1.0 was too loud for others)
    public int SpeakLeadInMs { get; set; } = 350;         // noise-only lead-in so the game's voice gate opens before the first word
    public int SpeakTailMs { get; set; } = 400;
    public float SpeakNoiseFloor { get; set; } = 0.0015f;  // faint room-noise bed under the voice (keeps the gate open between words)
    // F5 speaks this line (say it when you join a lobby). Ctrl+F5 speaks IntroLine2.
    public string IntroLine { get; set; } = "Hi everyone. I am deaf, so I talk through a computer voice and I read you with captions. Please just talk normally, I can follow you.";
    public string IntroLine2 { get; set; } = "Sorry, I did not catch that. Could you say it again a bit slower?";
    // Walkie (V) reaches the whole map with a radio filter; local voice reaches players near you and sounds natural.
    public bool SpeakOverWalkie { get; set; } = false;
    public string EdgeTtsPython { get; set; } = "";   // optional online fallback voice; path to a python.exe with edge-tts installed
    public string PushToTalkKey { get; set; } = "";       // e.g. "V" to hold the game's radio key while speaking; empty = none
    // Make the virtual mic the Windows default recording device at startup (the game's voice engine follows that, not its own dropdown).
    public bool SetDefaultMic { get; set; } = true;
    public int DefaultMicVolume { get; set; } = 70;
    public bool SayCloseOnFocusLoss { get; set; } = true;
    // Middle mouse click opens the phrase wheel; scroll wheel rotates the category; 1..9 says a line; middle click / Esc closes.
    public bool MouseWheelMenu { get; set; } = true;
    public string[] PageNames { get; set; } = { "Quick", "Evidence", "Ghost did", "Where", "Doing", "Status", "Ghost type" };
    public int EventLifetimeSec { get; set; } = 10;
    public int MaxEvents { get; set; } = 8;
    // 0 = automatic (scales with screen height so a 4K TV and a 1080p monitor look the same). Or a fixed multiplier.
    public double UiScale { get; set; } = 0;

    /// Absolute path for a setting that may be written relative to the app folder.
    public static string Resolve(string p) =>
        string.IsNullOrWhiteSpace(p) ? "" : (Path.IsPathRooted(p) ? p : Path.Combine(AppContext.BaseDirectory, p));

    public static Config Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<Config>(File.ReadAllText(path), Json) ?? new Config();
        }
        catch (Exception ex) { Log.Write("config load failed: " + ex.Message); }
        var c = new Config();
        try { File.WriteAllText(path, JsonSerializer.Serialize(c, Json)); } catch { }
        return c;
    }

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
}

public static class Log
{
    static readonly object L = new();
    public static string Path = System.IO.Path.Combine(AppContext.BaseDirectory, "log.txt");
    public static void Write(string msg)
    {
        lock (L)
        {
            try { File.AppendAllText(Path, $"{DateTime.Now:HH:mm:ss.fff} {msg}{Environment.NewLine}"); } catch { }
        }
    }
}
