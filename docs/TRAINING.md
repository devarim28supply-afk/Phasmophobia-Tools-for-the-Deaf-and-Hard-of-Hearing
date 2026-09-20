# Teaching it the game's real sounds

The built-in classifier, YAMNet, was trained on real-world recordings. It has never heard
Phasmophobia. It calls every wooden knock a "Door", and it called a glass clink "SHATTER" until that
was reined in. Two ways to fix that, and they work together.

## A. Record it yourself, in game (30 seconds)

1. Make the sound happen: open a door, let the ghost knock, drop an item.
2. Within about two seconds press **Ctrl+1 … Ctrl+9**. The last 2.5 s of audio is saved as an example
   of that label and a yellow box confirms it.

| Key | Default label |
|---|---|
| Ctrl+1 | Door open |
| Ctrl+2 | Door close |
| Ctrl+3 | Door slam |
| Ctrl+4 | Knock |
| Ctrl+5 | Ghost footsteps |
| Ctrl+6 | Item drop |
| Ctrl+7 | Light switch |
| Ctrl+8 | Breathing |
| Ctrl+9 | Whisper |

Rename them in `config.json` → `TrainLabels`. **F7** saves an unlabelled clip if you are not sure what
it was.

Three to five examples per label is plenty. Only press the key when the sound really happened; a
wrong example teaches the wrong thing. Clips live in `app\samples\`; delete a bad one's entry from
`index.json` to forget it.

These recordings are the most valuable kind, because they are the sound *as the game actually mixes
it*, through reverb and distance, which is what the overlay hears in play.

## B. Import the game's own sound files

`PhasmoAudioExtract` reads your installed copy of Phasmophobia, decodes every sound clip, and imports
the distinctive ones as labelled examples.

```powershell
cd app

# 1. see what is in there (4,138 clips)
.\PhasmoAudioExtract.exe list "D:\Steam\steamapps\common\Phasmophobia\Phasmophobia_Data"

# 2. decode them all to 16 kHz WAV (about 575 MB, a few minutes)
.\PhasmoAudioExtract.exe extract "D:\Steam\steamapps\common\Phasmophobia\Phasmophobia_Data" ..\gameclips

# 3. label them and import the useful ones
.\PhasmoAudioExtract.exe import ..\gameclips ..\PhasmoAudioExtract\labels.json model\yamnet.onnx samples
```

Roughly 753 clips are imported: ghost voices and screams, banshee screams, whispers, breathing,
spirit-box answer words (`Far`, `Close`, `Hate`, `Behind`, `Attack`, `Kill`…), doll voices, hunt and
death sounds, phones, alarms, piano, knocks, light switches and footsteps.

### Why not all of them

Mechanical bumps are deliberately left out: item drops, door open and close, cabinets, taps. They were
imported at first and made things worse. Validation against six real in-game door slams showed the
slams matching the game's dry "key lock" and "item drop" studio files (0.87–0.93) *better* than its own
"door close" files. The studio files have no room, no distance and no reverb, so short mechanical
sounds all collapse together.

Distinctive sounds — a scream, a whisper, a spirit-box word — survive that gap easily, which is why
those are the ones kept.

This is also why game-file matches are held to a stricter similarity (`GameMatch` 0.88) than your own
in-game recordings (`CustomMatch` 0.82), and why your recordings get a `OwnSampleBonus` when both are
close.

### Editing the labels

`PhasmoAudioExtract/labels.json` is a list of regular expressions matched against each clip's real
name inside the game files, in order, first match wins:

```json
{ "Pattern": "Banshee",                    "Label": "BANSHEE SCREAM" },
{ "Pattern": "Slam",                       "Label": "Door slam" },
{ "Pattern": "^AMB|Rain|Thunder|Crickets", "Label": "skip" }
```

`"skip"` drops the clip. Re-run the import after editing; it replaces everything previously imported
from the game files and leaves your own recordings alone.

### After a game update

Re-run `extract` and `import`. Sound file names are stable across updates, so the labels keep working.

## Copyright

The extracted audio is Kinetic Games' property. It is **not** in this repository and must not be
redistributed. The tool reads your own installed copy, on your own machine, to build an accessibility
aid for yourself. `gameclips/` and `app/samples/` are in `.gitignore` for that reason.

## Checking it worked

`app\log.txt` shows every classification with its scores:

```
[fast 3ms] peak=-24dB dir=33 Footsteps=0.89 | nearest=Door slam 0.91 bg=0.42
```

`nearest=` is the closest labelled example and its similarity; `bg=` is how much the sound resembles
the current room's background. A match only counts when it clears its threshold **and** beats the
background by `CustomMargin`. Matched labels appear in gold on screen.
