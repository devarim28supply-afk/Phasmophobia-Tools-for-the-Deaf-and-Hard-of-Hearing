# Audio routing

This page is the hard-won part of the project. Getting a synthetic voice into a Unity game's voice
chat, while still listening to the game yourself, is fiddly, and every trap below cost real hours.

## The shape of it

```
                      ┌──────────────────────────────┐
  the game's sound ──►│ your speakers / monitor / TV  │──► the overlay listens here (loopback)
                      └──────────────────────────────┘
                                                          Config: "Device"

  phrase wheel / F6 ──► Kokoro ──►┌─────────────┐
                                  │ CABLE Input │  (a playback device)
                                  └──────┬──────┘
                                         │ the virtual cable
                                  ┌──────▼───────┐
                                  │ CABLE Output │  (a recording device)
                                  └──────┬───────┘
                                         │
                                  the game reads this as your microphone ──► your team
```

Two rules follow from the picture:

1. **The overlay must listen to the device the game plays on**, not to the cable. If it listens to
   the cable it hears only your own voice.
2. **The game's normal sound must not go into the cable**, or your team hears the game instead of
   you.

## Trap 1: the installer hijacks your default output

VB-CABLE sets `CABLE Input` as the **default playback device** when it installs. Everything then
plays into the microphone cable: the game's audio goes to your team, and you hear silence.

The overlay detects and corrects this at every start. To do it by hand:

```powershell
app\SetDefaultMic.exe "CABLE Output" 70 --out "your speaker device" --comm-out-same
```

## Trap 2: the game ignores the microphone you chose in its menu

Phasmophobia uses Vivox for voice. Vivox follows the **Windows default communications recording
device**. You can select `CABLE Output` in the game's own audio menu and still have it record from
your webcam microphone, because that is what Windows calls the default.

Symptoms: the game's microphone level bar never moves, and nobody hears you, while everything else
looks correct.

The overlay sets the default recording device (all three roles: console, multimedia, communications)
on startup. After changing it, **re-pick the microphone in the game's menu or restart the game**, so
its voice engine opens the new device.

## Trap 3: the sample rate garbles the voice

This is the one that made teammates say *"you sound weird"* and *"I couldn't understand you"*.

A virtual device left at **44,100 Hz, 32-bit float** is resampled badly by the game's voice engine and
comes out smeared and chopped. Voice chat expects **48,000 Hz, 16-bit, mono**.

Check and set it:

```powershell
app\SetDefaultMic.exe "CABLE Output" 70 --show-format
app\SetDefaultMic.exe "CABLE Output" 70 --format 48000 16 1
```

or in Windows: *Sound settings → the device → Properties → Advanced → Default format*.

The game only reads the format when it **opens** the microphone, so restart the game afterwards.

VB-CABLE is 48 kHz by default, which is one reason it is recommended here over repurposing something
like the Steam Streaming Microphone.

## Trap 4: voice chat plays to a different device than the game

Windows has a separate **default communications playback device**. Voice chat uses it. If it points
at a headset you are not using, other players' voices go there, and neither you nor the captioner
ever hear them.

Keep it the same as your normal output. The overlay does this on startup; by hand it is
`--comm-out-same` on the command above.

## Trap 5: it is too loud

Full-scale synthetic speech clips the game's voice pipeline and sounds harsh and distorted. The
defaults here are deliberately quiet:

```jsonc
"SpeakVolume": 0.4,        // 0..1, how loud the voice is written into the cable
"DefaultMicVolume": 70     // the Windows level of the recording device
```

If people say you are loud, halve `SpeakVolume`. If you are too quiet, raise it to 0.6.

## Making it sound like a real microphone

A real microphone never sends digital silence. Voice chat noise gates use that: silence, then a word
starting at full volume, gets the first syllable cut off.

So the overlay wraps every spoken line in:

* `SpeakLeadInMs` (350 ms) of very quiet noise **before** the first word, which opens the gate,
* `SpeakNoiseFloor` (0.0015) of the same noise **under** the words, which keeps it open, and
* `SpeakTailMs` (400 ms) after the last word.

Too much noise floor and you sound like a robot in a wind tunnel; too little and words get clipped.
The default is deliberately faint.

## Local voice or the walkie-talkie

```jsonc
"SpeakOverWalkie": false,   // false = local voice (people near you), true = radio to everyone
"PushToTalkKey": "V"        // the game's walkie key, held automatically while speaking
```

Local voice sounds like a normal player standing next to you. The walkie adds a radio filter and
broadcasts to the whole map. Local is friendlier; the walkie is for emergencies ("it is hunting").

## Checking the whole chain

```powershell
# what are the defaults right now?
app\SetDefaultMic.exe "CABLE Output" 70 --show-format

# does audio actually reach the cable? speak a line, then look for:
#   spoke "..." via CABLE Input
Get-Content app\log.txt -Tail 20
```

If the log says it spoke and teammates still hear nothing, the break is between Windows and the game:
that is trap 2 or trap 3, in that order.
