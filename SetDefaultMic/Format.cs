using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

static class Format
{
    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    struct WAVEFORMATEXTENSIBLE
    {
        public ushort wFormatTag, nChannels; public uint nSamplesPerSec, nAvgBytesPerSec; public ushort nBlockAlign, wBitsPerSample, cbSize;
        public ushort wValidBitsPerSample; public uint dwChannelMask; public Guid SubFormat;
    }
    static readonly Guid PCM = new("00000001-0000-0010-8000-00aa00389b71"), FLOAT = new("00000003-0000-0010-8000-00aa00389b71");

    public static string Describe(IPolicyConfig pc, MMDevice d, int which)
    {
        try
        {
            Marshal.ThrowExceptionForHR(pc.GetDeviceFormat(d.ID, which, out var p));
            var f = Marshal.PtrToStructure<WAVEFORMATEXTENSIBLE>(p);
            string kind = f.wFormatTag == 65534 ? (f.SubFormat == FLOAT ? "float" : "pcm") : f.wFormatTag.ToString();
            return $"{f.nSamplesPerSec} Hz, {f.wBitsPerSample}-bit {kind}, {f.nChannels} ch";
        }
        catch (Exception ex) { return "unknown (" + ex.Message + ")"; }
    }

    public static int Set(IPolicyConfig pc, MMDevice d, int rate, int bits, int channels, bool isFloat)
    {
        var f = new WAVEFORMATEXTENSIBLE
        {
            wFormatTag = 65534, nChannels = (ushort)channels, nSamplesPerSec = (uint)rate, wBitsPerSample = (ushort)bits,
            nBlockAlign = (ushort)(channels * bits / 8), nAvgBytesPerSec = (uint)(rate * channels * bits / 8), cbSize = 22,
            wValidBitsPerSample = (ushort)bits, dwChannelMask = channels == 1 ? 0x4u : 0x3u, SubFormat = isFloat ? FLOAT : PCM,
        };
        IntPtr p = Marshal.AllocHGlobal(Marshal.SizeOf<WAVEFORMATEXTENSIBLE>());
        try { Marshal.StructureToPtr(f, p, false); return pc.SetDeviceFormat(d.ID, p, p); }
        finally { Marshal.FreeHGlobal(p); }
    }
}
