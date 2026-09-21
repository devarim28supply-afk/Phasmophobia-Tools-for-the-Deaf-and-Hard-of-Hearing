# Configuration

Everything lives in `app\config.json`, written on first run. Edit it and restart the overlay
(`Ctrl+F8`, then start it again).

## Audio devices

| Key | Default | Meaning |
|---|---|---|
| `Device` | `"auto"` | The device the **game plays on**, which the overlay listens to. `"auto"` = Windows default output. Or part of a name, e.g. `"LG ULTRAGEAR"`. |
| `SpeakDevice` | `"CABLE Input"` | Playback device the spoken voice is written into. |
| `MicDevice` | `"CABLE Output"` | Recording device the game should use. Made the Windows default at startup. |
| `SetDefaultMic` | `true` | Fix the Windows default devices at startup. See [AUDIO-ROUTING.md](AUDIO-ROUTING.md). |
| `DefaultMicVolume` | `70` | Windows level for that recording device. |
| `MicFormatRate` | `0` | `0` leaves the format alone; `48000` forces 48 kHz 16-bit mono. |

## Sensitivity

| Key | Default | Meaning |
|---|---|---|
| `MinDb` | `-42` | Loudness floor. **Lower = more sensitive.** The single most useful knob. |
| `MaxDb` | `-10` | Level treated as maximum brightness. |
| `OnsetDb` | `10` | Sudden jump (dB) that counts as a new sound. |
| `LoudOnsetDb` | `14` | Jump that also logs `LOUD !`. |
| `AdaptiveFloor` | `true` | Learn the constant background (hum, wind) and ignore it. |
| `AmbientGateDb` | `6` | How far above that background a sound must rise. |
| `AmbientRiseDbPerSec` | `2` | How fast a steady sound is absorbed into the background. |

## Naming sounds

| Key | Default | Meaning |
|---|---|---|
| `Profile` | `"phasmophobia"` | Only label sounds that exist in the game. `"all"` = every class. |
| `ClassifyThreshold` | `0.30` | Confidence needed to log a sound. Higher = fewer, better names. |
| `ClassifyIntervalMs` | `250` | Background classification interval. |
| `FastClassifyDelayMs` | `20` | Delay after a sudden sound before the first guess. |
| `CaptureBufferMs` | `10` | Audio capture buffer. Smaller = less delay. |
| `HideCategories` | `["env","custom"]` | Categories never shown: `move`, `door`, `object`, `ghost`, `voice`, `alert`, `env`, `music`, `other`, `custom` (your recordings), `game` (clips from the game's files). |
| `ShowVoice` / `ShowMusic` | `true` / `false` | Show speech / music detections. |
| `CustomMatch` | `0.82` | Similarity needed to accept one of **your** recorded examples. |
| `GameMatch` | `0.88` | Stricter bar for examples extracted from the game's own files. |
| `OwnSampleBonus` | `0.05` | How much your own in-game recordings outrank game files. |

## Hiding your own sounds

| Key | Default | Meaning |
|---|---|---|
| `HideOwnSounds` | `true` | Drop centred sounds you caused. |
| `MoveKeys` | `"W A S D Up Down Left Right Shift"` | Held = you are moving. |
| `ActionKeys` | `"E F G Q R T J C LButton RButton MButton"` | Pressed = you touched something. |
| `OwnMoveTailMs` | `500` | Keep hiding footsteps this long after you stop. |
| `OwnActionWindowMs` | `700` | Hide item/door sounds this long after an action key. |
| `CenteredConf` | `0.34` | Below this left/right difference a sound counts as centred (about 30°). |
| `OwnStepMaxDeg` | `30` | While moving, footsteps inside this angle are yours. |
| `StillStepMinDeg` | `15` | While standing still, footsteps beyond this get the footprint icon. |
| `StepIcon` | `true` | Show the footprint icon at all. |

## Look

| Key | Default | Meaning |
|---|---|---|
| `Style` | `"edges"` | `"edges"` = screen-edge glow, `"ring"` = a circle in the middle. |
| `EdgeThickness` | `70` | How far the glow reaches in. |
| `UiScale` | `0` | `0` = automatic from screen height. Or a fixed multiplier like `1.3`. |
| `EventLifetimeSec` / `MaxEvents` | `10` / `8` | Event log length. |
| `FollowGame` / `GameProcess` | `true` / `"Phasmophobia"` | Which window to follow. |
| `BorderlessGame` | `true` | Make the game borderless over its monitor **once at startup** (F9 toggles it later). It is never re-applied, so the game is never dragged out of the display mode you chose. |

## Captions

| Key | Default | Meaning |
|---|---|---|
| `WhisperCaptions` | `true` | Local Whisper captions drawn by the overlay. |
| `WhisperModel` | `"small.en"` | `tiny.en`, `base.en`, `small.en`, `medium.en`, `large-v3`. Bigger = better and slower. |
| `WhisperDevice` | `"auto"` | `auto` = GPU if it works, else CPU. |
| `CaptionLines` | `4` | How many lines to keep on screen. |
| `CaptionFontSize` | `17` | Caption text size (scaled by `UiScale`). |
| `CaptionLifetimeSec` | `12` | How long a line stays. |
| `LiveCaptions` | `false` | Start Windows Live Captions instead. Kept only as a fallback. |

## Speaking

| Key | Default | Meaning |
|---|---|---|
| `Speak` | `true` | Enable the wheel and F6. |
| `UseKokoro` | `true` | Local Kokoro voice. |
| `KokoroVoice` | `"am_adam"` | `am_adam`, `am_michael`, `am_eric`, `am_liam`, `am_onyx`, `bm_george`, `af_heart` … |
| `KokoroSpeed` | `1.0` | Speaking rate. |
| `SpeakVolume` | `0.4` | 0..1 into the cable. Lower if people say you are loud. |
| `SpeakLeadInMs` / `SpeakTailMs` | `350` / `400` | Silence padding that opens the voice gate. |
| `SpeakNoiseFloor` | `0.0015` | Faint noise bed under the voice. Raise if words get clipped, lower if you sound robotic. |
| `SpeakOverWalkie` | `false` | `true` = hold the radio key and broadcast to the map. |
| `PushToTalkKey` | `""` | The game's radio key, e.g. `"V"`. |
| `MouseWheelMenu` | `true` | Middle-click opens the phrase wheel. |
| `PageNames` | `["Quick","Evidence",...]` | Names of the seven wheel categories. |
| `SpeakVoice` / `SpeakRate` / `EdgeTtsPython` | | Online edge-tts fallback. Leave `EdgeTtsPython` empty to skip it. |

## Training

| Key | Default | Meaning |
|---|---|---|
| `TrainLabels` | 9 labels | What `Ctrl+1` … `Ctrl+9` save. |
| `TrainClipMs` | `2500` | How much audio each one saves. |

## Making it less sensitive

The three that matter, in order:

1. `MinDb` — raise it (`-36`) to ignore quiet sounds, lower it (`-48`) to catch more.
2. `ClassifyThreshold` — raise it for fewer, more confident names.
3. `HideCategories` — add whole groups you never want to see.
