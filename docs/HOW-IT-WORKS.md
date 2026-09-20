# How it works

For anyone who wants to change it, port it to another game, or reuse a piece of it.

## Pipeline

```
Windows (WASAPI loopback of the game's output device, 10 ms buffer)
   │
   ▼
AudioEngine.cs ── 5 ms blocks ──┬── per-channel RMS ──► direction, edge glow, footprint icons
   │                            └── onset detection ──► instant "!" row + edge flash
   │
   ├── 16 kHz mono ring buffer ──► Classifier.cs (YAMNet / ONNX) ──► sound names ──► event log
   │                          └──► SampleStore.cs ──► nearest trained example ──► exact names
   │
   └── 16 kHz mono stream ──► CaptionClient.cs ──► captions_server.py (Whisper) ──► captions

InputWatch.cs (GetAsyncKeyState) ──► "you caused that sound" ──► hide it

WheelWindow / SayWindow ──► Speaker.cs ──► kokoro_server.py ──► WasapiOut ──► CABLE Input
```

## Files

### Listening

| File | Job |
|---|---|
| `AudioEngine.cs` | WASAPI loopback capture with a 10 ms buffer. Per-channel RMS every 5 ms, onset detection, resampling to a 16 kHz mono ring buffer. `Direction.Estimate` turns channel levels into an angle. |
| `Classifier.cs` | YAMNet through ONNX Runtime: 521 sound classes mapped to game-friendly labels, with a Phasmophobia allow-list. Merges look-alike classes into honest labels (`Knock / thump`, `Clink / click`). Returns the loudest frame's 1024-d embedding for matching. |
| `SampleStore.cs` | Labelled example clips and their embeddings. Cosine nearest-neighbour, with a running "what does this room sound like" reference so background noise cannot win. |
| `InputWatch.cs` | Polls movement and action keys so centred sounds you caused can be dropped. |

### Showing

| File | Job |
|---|---|
| `OverlayWindow.xaml.cs` | The click-through, always-on-top window. Follows the game window across monitors, makes it borderless, global hotkeys, the mouse hook, and keeping click-through intact (WPF strips the layered style). |
| `RadarElement.cs` | All drawing: edge glows, onset flashes, footprints, event log, chips, captions, toasts. |
| `OverlayState.cs` | The shared state between the audio, classifier and UI threads. |
| `WheelWindow.xaml.cs` | The phrase wheel. A **focusable** window, which is how the mouse gets released from the game. |
| `SayWindow.xaml.cs` | The F6 text box, and the single path that speaks any line. |

### Speaking

| File | Job |
|---|---|
| `Speaker.cs` | Engine order: pre-made file → cached audio → Kokoro (local) → edge-tts (online) → Windows SAPI. `MicLikeProvider` adds the lead-in, noise floor and tail that keep voice-chat gates open. |
| `Phrases.cs` | The phrase list, pre-made audio lookup, and the synthesis cache (hashed by text + engine + voice). |
| `DefaultMic.cs` | `IPolicyConfig` (undocumented COM) to set default recording/playback devices and formats. |
| `ChildJob.cs` | A Windows job object with `KILL_ON_JOB_CLOSE`, so the Python servers die with the overlay even if it is killed from Task Manager. |
| `MouseHook.cs` | Low-level mouse hook for middle-click, swallowed so the game never sees it. |

### Python side

| File | Job |
|---|---|
| `kokoro_server.py` | Kokoro-82M text to speech on `127.0.0.1:8765`, plus it serves the phrase editor page and the save/generate endpoints. |
| `captions_server.py` | Accepts a raw 16 kHz PCM stream on TCP `127.0.0.1:8766`, cuts it into utterances, transcribes with faster-whisper, returns JSON lines. |
| `phrases.html` | The phrase editor. Plain HTML and JavaScript, no build step. |

### Tools

| File | Job |
|---|---|
| `PhasmoAudioExtract` | Reads Unity `.assets` / `levelN` files with AssetsTools.NET, finds `AudioClip` objects, decodes the FMOD FSB5 banks with Fmod5Sharp + NVorbis, writes 16 kHz WAVs, and imports labelled ones as training examples. |
| `SetDefaultMic` | Sets Windows default audio devices and formats from the command line. |

## Decisions worth knowing

**Loopback, not injection.** The overlay only reads what Windows is already playing. Nothing is
hooked inside the game, so nothing can be mistaken for cheating and nothing breaks on a game update.

**Direction from channel levels.** Each channel is a direction vector; the level-weighted sum gives an
angle and a confidence. In stereo that is left/right only. The channel layout table is in
`Direction.Angles`.

**Own sounds are identified by input, not by sound.** The game plays the *same* footstep clips for you
and for the ghost — there is no separate "ghost footstep" audio. The only reliable separators are
whether you are holding a movement key and whether the sound is centred. Measured over 942 of one
player's own footsteps: median 2° off centre, 99% within 18°, never past 20°. Other footsteps averaged
26°.

**Captions are cut on pauses, not on a timer.** The gate adapts to the room: speech must clear the
20th percentile of recent loudness by a factor of 2.2. Utterances end after a 0.45 s pause or 4.5 s of
speech, cut at the quietest recent moment so words are not chopped.

**Two label tiers.** YAMNet was trained on real-world audio, so it calls every wooden knock a "Door".
Look-alike classes collapse into one honest label unless the model is at least 50% sure; rare but
important ones (`SHATTER`, `Breaking`, `Crack`) need 60%. Examples taken from the game's own files are
held to a stricter match (0.88) than clips you record yourself in-game, which get a small bonus,
because the game's studio files are dry and the live mix is not.

**Everything the overlay spawns is in a job object.** Killing the overlay kills the voice and caption
servers. Without this, restarts leave orphaned Python processes holding the ports, and you end up
debugging a server from twenty minutes ago.

## Porting to another game

Most of it is game-independent. What is not:

1. `Labels.Profiles` in `Classifier.cs` — the list of sounds that exist in your game.
2. `config.json` → `GameProcess` — the process name to follow.
3. `MoveKeys` / `ActionKeys` — for own-sound filtering.
4. `config/phrases.example.json` — the phrase board.

Everything else (capture, direction, onsets, captions, voice, the wheel) carries over unchanged.
