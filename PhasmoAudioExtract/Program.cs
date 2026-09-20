// Pulls every AudioClip out of Phasmophobia's Unity data files, decodes it to 16 kHz mono WAV,
// and (in "import" mode) computes YAMNet embeddings and writes them into the overlay's sample index.
//
//   PhasmoAudioExtract list    <Phasmophobia_Data>                                  -> prints clip names
//   PhasmoAudioExtract extract <Phasmophobia_Data> <outDir>                         -> WAVs + clips.json
//   PhasmoAudioExtract import  <outDir> <labels.json> <model.onnx> <app\samples>    -> adds labeled embeddings
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Fmod5Sharp;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using NVorbis;

record Clip(string Name, string Source, long Offset, long Size, int Channels, int Frequency, float Length, string File);

static class Program
{
    const int AudioClipClassId = 83;

    static int Main(string[] args)
    {
        if (args.Length < 2) { Console.WriteLine("usage: list|extract|import ..."); return 1; }
        switch (args[0])
        {
            case "list": foreach (var c in Enumerate(args[1])) Console.WriteLine($"{c.Name}\t{c.Length:F2}s\t{c.Channels}ch\t{c.Frequency}Hz\t{c.Source}"); return 0;
            case "extract": Extract(args[1], args[2]); return 0;
            case "import": Import(args[1], args[2], args[3], args[4]); return 0;
        }
        return 1;
    }

    // ---------------------------------------------------------------- enumerate AudioClips
    static IEnumerable<Clip> Enumerate(string dataDir)
    {
        var files = Directory.GetFiles(dataDir).Where(f =>
        {
            var n = Path.GetFileName(f);
            return n.EndsWith(".assets") || Regex.IsMatch(n, @"^level\d+$") || n == "globalgamemanagers";
        });
        var am = new AssetsManager();
        foreach (var f in files)
        {
            AssetsFileInstance inst;
            try { inst = am.LoadAssetsFile(f, false); }
            catch (Exception ex) { Console.Error.WriteLine($"skip {Path.GetFileName(f)}: {ex.Message}"); continue; }
            var af = inst.file;
            int n = 0;
            var found = new List<Clip>();
            foreach (var info in af.Metadata.AssetInfos)
            {
                if (info.TypeId != AudioClipClassId) continue;
                try
                {
                    var r = af.Reader;
                    r.Position = info.GetAbsoluteByteOffset(af);
                    found.Add(ParseAudioClip(r, Path.GetFileName(f)));
                    n++;
                }
                catch (Exception ex) { Console.Error.WriteLine($"bad clip in {Path.GetFileName(f)}: {ex.Message}"); }
            }
            if (n > 0) Console.Error.WriteLine($"{Path.GetFileName(f)}: {n} audio clips");
            am.UnloadAll();
            foreach (var c in found) yield return c;
        }
    }

    // Unity 2019+ AudioClip layout (no type tree needed):
    // string m_Name; int m_LoadType; int m_Channels; int m_Frequency; int m_BitsPerSample; float m_Length;
    // bool m_IsTrackerFormat; bool m_Ambisonic; [align] int m_SubsoundIndex; bool m_PreloadAudioData;
    // bool m_LoadInBackground; bool m_Legacy3D; [align] StreamedResource { string m_Source; ulong m_Offset; ulong m_Size } int m_CompressionFormat
    static Clip ParseAudioClip(AssetsFileReader r, string file)
    {
        string name = r.ReadCountStringInt32(); r.Align();
        int loadType = r.ReadInt32(); int channels = r.ReadInt32(); int freq = r.ReadInt32(); int bits = r.ReadInt32();
        float length = r.ReadSingle();
        r.ReadBoolean(); r.ReadBoolean(); r.Align();
        r.ReadInt32();
        r.ReadBoolean(); r.ReadBoolean(); r.ReadBoolean(); r.Align();
        string source = r.ReadCountStringInt32(); r.Align();
        long offset = (long)r.ReadUInt64(); long size = (long)r.ReadUInt64();
        return new Clip(name, source, offset, size, channels, freq, length, file);
    }

    // ---------------------------------------------------------------- extract
    static void Extract(string dataDir, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var clips = Enumerate(dataDir).ToList();
        Console.Error.WriteLine($"{clips.Count} clips");
        var done = new List<object>();
        var seen = new HashSet<string>();
        int ok = 0, fail = 0;
        foreach (var c in clips)
        {
            if (c.Size <= 0 || string.IsNullOrEmpty(c.Source)) { fail++; continue; }
            string src = Path.Combine(dataDir, Path.GetFileName(c.Source.Replace("archive:/", "")));
            if (!File.Exists(src)) { fail++; Console.Error.WriteLine($"{c.Name}: missing {c.Source}"); continue; }
            string safe = Regex.Replace(c.Name, @"[^A-Za-z0-9_\-]", "_");
            string fn = safe;
            int k = 1; while (!seen.Add(fn)) fn = $"{safe}__{k++}";
            try
            {
                byte[] bank;
                using (var fs = File.OpenRead(src)) { fs.Position = c.Offset; bank = new byte[c.Size]; int read = 0; while (read < bank.Length) { int got = fs.Read(bank, read, bank.Length - read); if (got <= 0) break; read += got; } }
                var pcm = DecodeBank(bank, out int rate, out int ch);
                var mono16 = ToMono16k(pcm, rate, ch);
                WriteWav(Path.Combine(outDir, fn + ".wav"), mono16);
                done.Add(new { c.Name, File = fn + ".wav", Seconds = mono16.Length / 16000.0, c.Channels, c.Frequency, c.Source, AssetFile = c.File });
                ok++;
            }
            catch (Exception ex) { fail++; Console.Error.WriteLine($"{c.Name}: {ex.Message}"); }
        }
        File.WriteAllText(Path.Combine(outDir, "clips.json"), JsonSerializer.Serialize(done, new JsonSerializerOptions { WriteIndented = true }));
        Console.Error.WriteLine($"extracted {ok}, failed {fail}");
    }

    static float[] DecodeBank(byte[] bank, out int rate, out int channels)
    {
        if (!FsbLoader.TryLoadFsbFromByteArray(bank, out var fsb) || fsb == null || fsb.Samples.Count == 0)
            throw new Exception("not an FSB5 bank");
        var s = fsb.Samples[0];
        rate = (int)s.Metadata.Frequency; channels = (int)s.Metadata.Channels;
        if (!s.RebuildAsStandardFileFormat(out var data, out var ext) || data == null)
            throw new Exception($"cannot rebuild ({fsb.Header.AudioType})");
        if (ext == "ogg")
        {
            using var ms = new MemoryStream(data);
            using var v = new VorbisReader(ms, false);
            rate = v.SampleRate; channels = v.Channels;
            var all = new List<float>((int)Math.Max(0, v.TotalSamples * channels));
            var buf = new float[4096 * channels];
            int got;
            while ((got = v.ReadSamples(buf, 0, buf.Length)) > 0) all.AddRange(buf.Take(got));
            return all.ToArray();
        }
        if (ext == "wav")
        {
            int pos = 12; short bps = 16; short wch = (short)channels; int wrate = rate;
            while (pos + 8 <= data.Length)
            {
                string id = Encoding.ASCII.GetString(data, pos, 4); int len = BitConverter.ToInt32(data, pos + 4);
                if (id == "fmt ") { wch = BitConverter.ToInt16(data, pos + 10); wrate = BitConverter.ToInt32(data, pos + 12); bps = BitConverter.ToInt16(data, pos + 22); }
                if (id == "data")
                {
                    rate = wrate; channels = wch;
                    int n = len / (bps / 8); var f = new float[n];
                    for (int i = 0; i < n; i++)
                        f[i] = bps == 16 ? BitConverter.ToInt16(data, pos + 8 + i * 2) / 32768f
                             : bps == 8 ? (data[pos + 8 + i] - 128) / 128f
                             : bps == 32 ? BitConverter.ToInt32(data, pos + 8 + i * 4) / 2147483648f
                             : ((data[pos + 8 + i * 3] << 8 | data[pos + 9 + i * 3] << 16 | data[pos + 10 + i * 3] << 24) / 2147483648f);
                    return f;
                }
                pos += 8 + len + (len & 1);
            }
            throw new Exception("no data chunk");
        }
        throw new Exception("unsupported format " + ext);
    }

    static float[] ToMono16k(float[] pcm, int rate, int ch)
    {
        int frames = pcm.Length / ch;
        var mono = new float[frames];
        for (int i = 0; i < frames; i++) { float s = 0; for (int c = 0; c < ch; c++) s += pcm[i * ch + c]; mono[i] = s / ch; }
        if (rate == 16000) return mono;
        double ratio = rate / 16000.0;
        int outN = (int)(frames / ratio);
        var o = new float[outN];
        int span = Math.Max(1, (int)Math.Round(ratio));
        for (int i = 0; i < outN; i++)
        {
            double p = i * ratio; int i0 = (int)p;
            double acc = 0; int cnt = 0;
            for (int k = -span / 2; k <= span / 2; k++) { int j = i0 + k; if (j >= 0 && j < frames) { acc += mono[j]; cnt++; } }
            o[i] = cnt > 0 ? (float)(acc / cnt) : 0;
        }
        return o;
    }

    static void WriteWav(string path, float[] wave)
    {
        using var w = new BinaryWriter(File.Create(path));
        int n = wave.Length, rate = 16000;
        w.Write("RIFF"u8); w.Write(36 + n * 2); w.Write("WAVE"u8); w.Write("fmt "u8);
        w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(n * 2);
        foreach (var v in wave) w.Write((short)Math.Clamp(v * 32767f, -32768f, 32767f));
    }

    static float[] ReadWav16k(string path)
    {
        var d = File.ReadAllBytes(path);
        int pos = 12;
        while (pos + 8 <= d.Length)
        {
            string id = Encoding.ASCII.GetString(d, pos, 4); int len = BitConverter.ToInt32(d, pos + 4);
            if (id == "data") { int n = len / 2; var f = new float[n]; for (int i = 0; i < n; i++) f[i] = BitConverter.ToInt16(d, pos + 8 + i * 2) / 32768f; return f; }
            pos += 8 + len + (len & 1);
        }
        return Array.Empty<float>();
    }

    // ---------------------------------------------------------------- import (labels + embeddings)
    sealed class LabelRule { public string Pattern { get; set; } = ""; public string Label { get; set; } = ""; }
    sealed class Sample { public string File { get; set; } = ""; public string Label { get; set; } = ""; public DateTime Time { get; set; } public float[] Embedding { get; set; } = Array.Empty<float>(); }

    static void Import(string clipDir, string labelsPath, string modelPath, string samplesDir)
    {
        var rules = JsonSerializer.Deserialize<List<LabelRule>>(File.ReadAllText(labelsPath))!
            .Select(r => (re: new Regex(r.Pattern, RegexOptions.IgnoreCase), label: r.Label)).ToList();
        var clips = JsonSerializer.Deserialize<List<JsonElement>>(File.ReadAllText(Path.Combine(clipDir, "clips.json")))!;
        using var session = new InferenceSession(modelPath);
        string indexPath = Path.Combine(samplesDir, "index.json");
        var index = File.Exists(indexPath) ? JsonSerializer.Deserialize<List<Sample>>(File.ReadAllText(indexPath)) ?? new() : new();
        index.RemoveAll(s => s.File.StartsWith("game/"));   // re-import cleanly
        Directory.CreateDirectory(Path.Combine(samplesDir, "game"));
        int added = 0, skipped = 0;
        var perLabel = new Dictionary<string, int>();
        foreach (var c in clips)
        {
            string name = c.GetProperty("Name").GetString()!;
            string file = c.GetProperty("File").GetString()!;
            double secs = c.GetProperty("Seconds").GetDouble();
            string? label = rules.FirstOrDefault(r => r.re.IsMatch(name)).label;
            if (label == null || label == "skip" || secs < 0.05 || secs > 20) { skipped++; continue; }
            var wave = ReadWav16k(Path.Combine(clipDir, file));
            if (wave.Length == 0) { skipped++; continue; }
            // normalise loudness so quiet game files still look like the level we hear in play
            float peak = wave.Max(v => Math.Abs(v)); if (peak > 0) for (int i = 0; i < wave.Length; i++) wave[i] *= 0.5f / peak;
            if (wave.Length < 15600) { var p = new float[15600]; Array.Copy(wave, 0, p, 0, wave.Length); wave = p; }
            var emb = Embed(session, wave);
            string dst = Path.Combine(samplesDir, "game", file);
            File.Copy(Path.Combine(clipDir, file), dst, true);
            index.Add(new Sample { File = "game/" + file, Label = label, Time = DateTime.Now, Embedding = Normalize(emb) });
            perLabel[label] = perLabel.GetValueOrDefault(label) + 1;
            added++;
        }
        File.WriteAllText(indexPath, JsonSerializer.Serialize(index));
        Console.Error.WriteLine($"imported {added}, skipped {skipped}");
        foreach (var kv in perLabel.OrderByDescending(k => k.Value)) Console.Error.WriteLine($"  {kv.Key}: {kv.Value}");
    }

    /// Same as the overlay: embedding of the loudest 0.975 s frame.
    static float[] Embed(InferenceSession session, float[] wave)
    {
        var t = new DenseTensor<float>(wave, new[] { wave.Length });
        string inName = session.InputMetadata.Keys.First();
        var outs = session.OutputMetadata.Keys.ToList();
        using var res = session.Run(new[] { NamedOnnxValue.CreateFromTensor(inName, t) }, outs.Take(2).ToList());
        var emb = res.Skip(1).First().AsTensor<float>();
        int ef = emb.Dimensions[0], dim = emb.Dimensions[1];
        const int win = 15600, hop = 7800;
        int bestF = 0; double bestRms = -1;
        for (int f = 0; f < ef; f++)
        {
            int s0 = Math.Min(f * hop, Math.Max(0, wave.Length - 1)), s1 = Math.Min(wave.Length, s0 + win);
            double acc = 0; for (int i = s0; i < s1; i++) acc += wave[i] * wave[i];
            double rms = s1 > s0 ? acc / (s1 - s0) : 0;
            if (rms > bestRms) { bestRms = rms; bestF = f; }
        }
        var e = new float[dim];
        for (int i = 0; i < dim; i++) e[i] = emb[bestF, i];
        return e;
    }

    static float[] Normalize(float[] v)
    {
        double n = 0; foreach (var x in v) n += x * x;
        float k = n > 0 ? (float)(1 / Math.Sqrt(n)) : 0;
        return v.Select(x => x * k).ToArray();
    }
}
