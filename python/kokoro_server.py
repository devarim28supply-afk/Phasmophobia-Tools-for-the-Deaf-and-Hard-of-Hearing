"""Local voice server for the PhasmoSound overlay (http://127.0.0.1:8765, local only).

  GET  /                 phrase editor page (phrases.html)
  GET  /health           "ok"
  GET  /say?text=&voice=&speed=   Kokoro speech as 24 kHz mono WAV
  GET  /phrases          the overlay's phrase list with audio status
  POST /phrases          save the list (JSON [{Text}])
  POST /generate         {"engine":"kokoro"} or {"engine":"eleven","key":..,"voice":..} -> make missing audio files
  POST /eleven/voices    {"key":..} -> ElevenLabs voice list
"""
import hashlib
import io
import json
import os
import re
import sys
import time
import urllib.parse
import urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

import numpy as np
import soundfile as sf

PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 8765
PHRASES_DIR = sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "phrases")
HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_VOICE = "am_adam"
ELEVEN_MODEL = "eleven_flash_v2_5"

t0 = time.time()
try:
    import espeakng_loader
    espeakng_loader.make_library_available()
except Exception as e:  # pragma: no cover
    print("espeak fallback not available:", e, flush=True)
from kokoro import KPipeline  # noqa: E402

pipeline = KPipeline(lang_code="a")
print(f"kokoro ready in {time.time() - t0:.1f}s", flush=True)


# ---------------------------------------------------------------- helpers shared with the overlay's Phrases.cs
def norm(s: str) -> str:
    return " ".join(s.lower().split()).rstrip(".!?")


def key(s: str) -> str:
    return hashlib.sha1(norm(s).encode("utf-8")).hexdigest()[:16]


def load_phrases():
    p = os.path.join(PHRASES_DIR, "phrases.json")
    try:
        return json.load(open(p, encoding="utf-8"))
    except Exception:
        return []


def save_phrases(items):
    os.makedirs(PHRASES_DIR, exist_ok=True)
    json.dump([{"Text": i["Text"]} for i in items], open(os.path.join(PHRASES_DIR, "phrases.json"), "w", encoding="utf-8"), indent=2)


def audio_status(text: str):
    """'eleven' if a pre-made mp3 for this text exists, 'kokoro' if cached, else None."""
    if os.path.exists(os.path.join(PHRASES_DIR, key(text) + ".mp3")):
        return "eleven"
    if os.path.exists(os.path.join(PHRASES_DIR, "cache", key(text + "|kokoro|" + DEFAULT_VOICE) + ".wav")):
        return "kokoro"
    return None


def synthesize(text: str, voice: str, speed: float) -> np.ndarray:
    chunks = [np.asarray(a, dtype=np.float32) for _g, _p, a in pipeline(text, voice=voice, speed=speed)]
    return np.concatenate(chunks) if chunks else np.zeros(2400, dtype=np.float32)


def wav_bytes(wave: np.ndarray) -> bytes:
    buf = io.BytesIO()
    sf.write(buf, wave, 24000, format="WAV", subtype="PCM_16")
    return buf.getvalue()


def eleven(path: str, key_: str, method="GET", body=None):
    req = urllib.request.Request("https://api.elevenlabs.io/v1" + path, data=body, method=method,
                                 headers={"xi-api-key": key_, "Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=60) as r:
        return r.read()


class Handler(BaseHTTPRequestHandler):
    def log_message(self, fmt, *args):
        pass

    def _send(self, code, body, ctype="text/plain; charset=utf-8"):
        if isinstance(body, str):
            body = body.encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def _json(self):
        n = int(self.headers.get("Content-Length") or 0)
        return json.loads(self.rfile.read(n) or b"{}")

    def do_GET(self):
        url = urllib.parse.urlparse(self.path)
        q = urllib.parse.parse_qs(url.query)
        if url.path in ("/", "/index.html"):
            self._send(200, open(os.path.join(HERE, "phrases.html"), "rb").read(), "text/html; charset=utf-8")
        elif url.path == "/health":
            self._send(200, "ok")
        elif url.path == "/phrases":
            items = load_phrases()
            for it in items:
                it["audio"] = audio_status(it.get("Text", ""))
            self._send(200, json.dumps(items), "application/json")
        elif url.path == "/say":
            text = (q.get("text") or [""])[0].strip()
            voice = (q.get("voice") or [DEFAULT_VOICE])[0]
            try:
                speed = float((q.get("speed") or ["1.0"])[0])
            except ValueError:
                speed = 1.0
            if not text:
                self._send(400, "no text"); return
            t = time.time()
            try:
                data = wav_bytes(synthesize(text, voice, speed))
            except Exception as e:
                self._send(500, str(e)); return
            print(f"{time.time() - t:.2f}s  {voice}  {text[:60]}", flush=True)
            self._send(200, data, "audio/wav")
        else:
            self._send(404, "not found")

    def do_POST(self):
        url = urllib.parse.urlparse(self.path)
        try:
            body = self._json()
        except Exception as e:
            self._send(400, "bad json: " + str(e)); return
        if url.path == "/phrases":
            items = [i for i in body if isinstance(i, dict) and i.get("Text", "").strip()]
            save_phrases(items)
            self._send(200, "saved")
        elif url.path == "/eleven/voices":
            try:
                data = json.loads(eleven("/voices", body.get("key", "")))
                out = [{"id": v["voice_id"], "name": v["name"], "labels": ", ".join(str(x) for x in v.get("labels", {}).values())} for v in data.get("voices", [])]
                self._send(200, json.dumps(out), "application/json")
            except Exception as e:
                self._send(400, str(e))
        elif url.path == "/generate":
            items = load_phrases()
            engine = body.get("engine", "kokoro")
            made, failed = 0, []
            for it in items:
                text = it.get("Text", "").strip()
                if not text:
                    continue
                try:
                    if engine == "eleven":
                        out = os.path.join(PHRASES_DIR, key(text) + ".mp3")
                        if os.path.exists(out):
                            continue
                        payload = json.dumps({"text": text, "model_id": body.get("model", ELEVEN_MODEL),
                                              "voice_settings": {"stability": 0.5, "similarity_boost": 0.8, "style": 0.2}}).encode()
                        data = eleven(f"/text-to-speech/{body.get('voice', 'pNInz6obpgDQGcFmaJgB')}?output_format=mp3_44100_128", body.get("key", ""), "POST", payload)
                        open(out, "wb").write(data)
                    else:
                        out = os.path.join(PHRASES_DIR, "cache", key(text + "|kokoro|" + DEFAULT_VOICE) + ".wav")
                        if os.path.exists(out):
                            continue
                        os.makedirs(os.path.dirname(out), exist_ok=True)
                        open(out, "wb").write(wav_bytes(synthesize(text, DEFAULT_VOICE, 1.0)))
                    made += 1
                except Exception as e:
                    failed.append(f"{text[:30]}: {str(e)[:80]}")
            msg = f"{engine}: made {made} new audio file(s)"
            if failed:
                msg += "; failed: " + " | ".join(failed[:3])
            self._send(200, msg)
        else:
            self._send(404, "not found")


if __name__ == "__main__":
    synthesize("warm up", DEFAULT_VOICE, 1.0)
    print(f"listening on http://127.0.0.1:{PORT}", flush=True)
    ThreadingHTTPServer(("127.0.0.1", PORT), Handler).serve_forever()
