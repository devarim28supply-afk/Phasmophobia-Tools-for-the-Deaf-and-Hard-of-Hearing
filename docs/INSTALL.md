# Install

Start to finish, about twenty minutes, most of it downloads.

## 1. Prerequisites

| | |
|---|---|
| **.NET 10 SDK** | <https://dotnet.microsoft.com/download> — needed to build the overlay |
| **uv** | `winget install astral-sh.uv` — installs Python 3.12 and the speech packages for you |
| **git** | to clone this repository |

An NVIDIA GPU is optional. With one, captions are transcribed in about half a second; without one the
captioner falls back to the CPU and takes a few seconds, which is still usable.

## 2. Build

```powershell
git clone https://github.com/<you>/Phasmophobia-Tools-for-the-Deaf-and-Hard-of-Hearing.git
cd Phasmophobia-Tools-for-the-Deaf-and-Hard-of-Hearing
powershell -ExecutionPolicy Bypass -File scripts\setup.ps1
```

`setup.ps1` does all of this:

1. Builds `PhasmoSound`, `PhasmoAudioExtract` and `SetDefaultMic` into `app\`.
2. Creates `app\python\.venv` and installs the speech packages.
3. Downloads YAMNet (16 MB sound classifier) into `model\`.
4. Pre-downloads Kokoro (voice) and Whisper `small.en` (captions), about 1 GB.
5. Writes `app\config.json` if it does not exist.

Useful flags:

```powershell
scripts\setup.ps1 -SkipModels      # build only, fetch models on first run instead
scripts\setup.ps1 -WhisperModel medium.en   # more accurate captions, slower
```

## 3. Run it

Start **Phasmophobia first**, then `app\PhasmoSound.exe`. The overlay finds the game window, places
itself over it, and follows it between monitors.

You should see a header across the top of the game naming your audio device. If it says
`no sound device`, see [AUDIO-ROUTING.md](AUDIO-ROUTING.md).

At this point **seeing sounds and reading captions already work**. The rest of this page is only
needed for speaking.

## 4. Speaking: the virtual microphone

The overlay speaks into a virtual audio cable, which the game reads as a microphone.

1. Download VB-CABLE from <https://vb-audio.com/Cable/> (donationware, free to use).
2. Run `VBCABLE_Setup_x64.exe` **as administrator** → *Install Driver* → reboot.
3. Start the game, then the overlay. On startup the overlay makes `CABLE Output` the default
   recording device and puts the default playback device back on your speakers.
4. In Phasmophobia: **Options → Audio → Microphone → CABLE Output**.
5. Join a lobby and press **middle mouse** → click a line, or **F6** → type and press Enter.

> Phasmophobia's voice engine follows the **Windows default communications microphone**, not only the
> device you pick in its own menu. The overlay sets that for you. If you change audio devices later,
> restart the overlay so it can set it again.

### Check it is working

* Your player card in the lobby shows a speaking indicator while a line plays.
* `app\log.txt` contains a line like `spoke "..." via CABLE Input`.
* If teammates say you sound chopped or garbled, read the format section of
  [AUDIO-ROUTING.md](AUDIO-ROUTING.md).

## 5. Game settings

| Setting | Value | Why |
|---|---|---|
| Video → Window mode | **Borderless** or **Windowed** | exclusive fullscreen hides all overlays |
| Audio → Microphone | **CABLE Output** | so your spoken lines reach the team |
| Audio → Voice chat volume | high | louder voices caption far better |

Press **F9** to flip the game between borderless fullscreen and a window, and **F10** to release the
mouse when you need it on another monitor.

## 6. Optional: teach it the game's real sounds

See [TRAINING.md](TRAINING.md). This extracts sound clips from your own Phasmophobia install so the
overlay can name ghost screams, whispers, spirit-box words and knocks precisely.

## Uninstall

Delete the folder. The only things outside it are:

* the VB-CABLE driver (Windows → Settings → Apps), and
* the model cache in `%USERPROFILE%\.cache\huggingface`.

## Trouble

| Symptom | Fix |
|---|---|
| Overlay does not appear | Game must be windowed/borderless. Press F8. Check `app\log.txt`. |
| `could not start: ... yamnet.onnx` | Run `scripts\setup.ps1` again, or `scripts\download-model.ps1`. |
| No captions | Look for `whisper ... ready` in `app\log.txt`. First run downloads the model. |
| Captions are slow | You are on CPU. Set `"WhisperModel": "base.en"` in `app\config.json`. |
| Nobody hears me | [AUDIO-ROUTING.md](AUDIO-ROUTING.md) — almost always the default-device trap. |
| Mouse stuck in the game | Press **F10**. |
