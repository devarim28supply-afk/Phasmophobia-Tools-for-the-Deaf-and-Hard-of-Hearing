# Changelog

## Unreleased

- Step meter: times footsteps that are not yours and shows steps per second with SLOW / NORMAL / FAST
  (every ghost uses the same footstep sounds, so the type shows in the speed).
- Hunts record themselves: when the hunt heartbeat is heard, 30 s of sound plus a direction timeline is
  saved to sampleshunts if footsteps were heard from the side. Ctrl+0 saves the last 60 s by hand.
- Ctrl+3 = hunt mode on / hunt over (ends the recording and the ghost watch).
- Stopwatch: Ctrl+1 start, Ctrl+2 reset, with smudge timing marks (Demon 60 / 90 / Spirit 180).
  These keys replace the Door open / Door close training keys; Ctrl+5 is no longer a training key.
- Ghost watch (experimental): during a hunt, watches the screen for the ghost blinking and saves the frames.
  Light flicker and camera movement still fool it, so its BLINK guesses are not reliable yet.
- Optional trained sound model (modelhead.onnx) is used when present; not included in the repo.

## v0.1 — 2026-09-20

First public release. Built and tested in real multiplayer matches by a deaf player.

### Seeing sounds
- Screen-edge glow showing the direction of every sound, with a pointer for the strongest.
- Footprint icons on the side when footsteps are heard that are too far off-centre to be your own.
- Event log naming sounds with a direction arrow and age, colour-coded by category.
- Sudden sounds show instantly as `!` (about 50 ms) and are named about 80 ms later.
- Own-sound filtering from keyboard and mouse input, measured against 942 real footsteps.
- Adaptive background floor, so constant hum and wind stop lighting the edges.
- Phasmophobia sound profile: only labels that exist in the game, with honest merged labels
  (`Knock / thump`, `Clink / click`) when the classifier cannot be specific.

### Reading speech
- Local Whisper captions drawn by the overlay, GPU accelerated, with CPU fallback.
- Adaptive utterance segmentation: cuts on real pauses, never mid-word.
- Replaced Windows Live Captions, which stalled for whole matches on game voice chat.

### Speaking
- Phrase wheel on middle-click: seven categories, scroll to turn, point and click to say.
- 63 game-specific lines covering evidence, ghost behaviour, locations, equipment, status and
  ghost-type reasoning.
- F6 free-text speaking.
- Local Kokoro voice, pre-generated and cached so every wheel line is instant.
- Browser phrase editor at `127.0.0.1:8765`, with optional ElevenLabs generation.
- Microphone-like shaping (lead-in, noise floor, tail) so voice-chat gates do not clip words.
- Automatic Windows audio-device and format correction.

### Training
- `Ctrl+1…9` to record labelled examples in game.
- `PhasmoAudioExtract`: reads Phasmophobia's own asset files, decodes all 4,138 clips, imports 753
  distinctive ones as labelled examples. No game audio is redistributed.

### Known issues
- Front/back direction needs a surround setup; stereo gives left/right only.
- Captions appear at sentence pauses, not word by word.
- A ghost approaching head-on while you walk is hidden by the own-footsteps filter.
