"""Generate pre-made audio for the overlay's phrase board with ElevenLabs (one-time, uses the free tier).

    python elevenlabs_phrases.py --key YOUR_API_KEY [--voice pNInz6obpgDQGcFmaJgB] [--model eleven_flash_v2_5]

Reads D:\Tools\phasmo-sound-overlay\app\phrases\phrases.json and writes <n>.mp3 next to it for every phrase
that does not have audio yet. The overlay plays those files instantly instead of synthesizing.
Default voice id is "Adam". List voices with --list.
"""
import argparse
import json
import os
import sys
import urllib.request

PHRASES_DIR = os.environ.get("PHRASES_DIR") or os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "phrases")
ap = argparse.ArgumentParser()
ap.add_argument("--key", required=True)
ap.add_argument("--voice", default="pNInz6obpgDQGcFmaJgB")  # Adam
ap.add_argument("--model", default="eleven_flash_v2_5")
ap.add_argument("--list", action="store_true")
ap.add_argument("--force", action="store_true")
a = ap.parse_args()

hdr = {"xi-api-key": a.key, "Content-Type": "application/json"}
if a.list:
    with urllib.request.urlopen(urllib.request.Request("https://api.elevenlabs.io/v1/voices", headers=hdr)) as r:
        for v in json.load(r)["voices"]:
            print(f'{v["voice_id"]}  {v["name"]:<14} {v.get("labels", {})}')
    sys.exit(0)

phrases = json.load(open(os.path.join(PHRASES_DIR, "phrases.json"), encoding="utf-8"))
made = 0
for i, p in enumerate(phrases, 1):
    out = os.path.join(PHRASES_DIR, f"{i}.mp3")
    if os.path.exists(out) and not a.force:
        continue
    body = json.dumps({"text": p["Text"], "model_id": a.model,
                       "voice_settings": {"stability": 0.5, "similarity_boost": 0.8, "style": 0.2}}).encode()
    req = urllib.request.Request(f"https://api.elevenlabs.io/v1/text-to-speech/{a.voice}?output_format=mp3_44100_128",
                                 data=body, headers=hdr, method="POST")
    try:
        with urllib.request.urlopen(req) as r:
            open(out, "wb").write(r.read())
        made += 1
        print(f"{i}: {p['Text'][:60]}")
    except urllib.error.HTTPError as e:
        print(f"{i}: FAILED {e.code} {e.read()[:200]}")
print(f"made {made} files; total characters used ~{sum(len(p['Text']) for p in phrases)}")
