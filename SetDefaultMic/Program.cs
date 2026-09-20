// SetDefaultMic "<capture device name part>" [volume 0-100] [--comm-out "<render device name part>"|--comm-out-same]
// Makes the capture device the Windows default (all roles). With --comm-out, also makes that render device the
// default COMMUNICATIONS output (voice chat engines play other players through it). --comm-out-same = same as
// the normal default output.
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

string wanted = args.Length > 0 ? args[0] : "Steam Streaming Microphone";
int vol = args.Length > 1 && int.TryParse(args[1], out var v0) ? v0 : 100;
string? commOut = null; bool commSame = false; string? mainOut = null; int fmtRate = 0, fmtBits = 16, fmtCh = 1; bool showFmt = false;
for (int i = 0; i < args.Length; i++) { if (args[i] == "--out" && i + 1 < args.Length) mainOut = args[i+1]; if (args[i] == "--show-format") showFmt = true; if (args[i] == "--format" && i + 3 < args.Length) { fmtRate = int.Parse(args[i+1]); fmtBits = int.Parse(args[i+2]); fmtCh = int.Parse(args[i+3]); } if (args[i] == "--comm-out" && i + 1 < args.Length) commOut = args[i + 1]; if (args[i] == "--comm-out-same") commSame = true; }
var e = new MMDeviceEnumerator();
var pc = (IPolicyConfig)new PolicyConfigClient();
var dev = e.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
           .FirstOrDefault(d => d.FriendlyName.Contains(wanted, StringComparison.OrdinalIgnoreCase));
if (dev == null) { Console.WriteLine("capture device not found: " + wanted); return 1; }
foreach (var role in new[] { ERole.eConsole, ERole.eMultimedia, ERole.eCommunications })
    Marshal.ThrowExceptionForHR(pc.SetDefaultEndpoint(dev.ID, role));
dev.AudioEndpointVolume.Mute = false;
dev.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(vol, 0, 100) / 100f;
Console.WriteLine($"default mic (all roles): {dev.FriendlyName} level={dev.AudioEndpointVolume.MasterVolumeLevelScalar:P0}");
if (mainOut != null) { var md = e.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).FirstOrDefault(d => d.FriendlyName.Contains(mainOut, StringComparison.OrdinalIgnoreCase)); if (md != null) { foreach (var role in new[] { ERole.eConsole, ERole.eMultimedia }) Marshal.ThrowExceptionForHR(pc.SetDefaultEndpoint(md.ID, role)); Console.WriteLine("default output: " + e.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console).FriendlyName); } else Console.WriteLine("output device not found: " + mainOut); }
MMDevice? outDev = null;
if (commSame) outDev = e.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
else if (commOut != null) outDev = e.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).FirstOrDefault(d => d.FriendlyName.Contains(commOut, StringComparison.OrdinalIgnoreCase));
if (outDev != null)
{
    Marshal.ThrowExceptionForHR(pc.SetDefaultEndpoint(outDev.ID, ERole.eCommunications));
    Console.WriteLine($"default communications output: {e.GetDefaultAudioEndpoint(DataFlow.Render, Role.Communications).FriendlyName}");
}
var micOut = e.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).FirstOrDefault(d => d.FriendlyName.Contains(wanted, StringComparison.OrdinalIgnoreCase));
if (showFmt || fmtRate > 0)
{
    Console.WriteLine($"format now: capture default={Format.Describe(pc, dev, 1)} current={Format.Describe(pc, dev, 0)}" + (micOut != null ? $"; render current={Format.Describe(pc, micOut, 0)}" : ""));
    if (fmtRate > 0)
    {
        foreach (var fl in new[] { false, true }) { int hr = Format.Set(pc, dev, fmtRate, fmtBits, fmtCh, fl); Console.WriteLine($"set capture {(fl ? "float" : "pcm")} -> hr=0x{hr:X8}"); if (hr == 0) break; }
        if (micOut != null) { int hr = Format.Set(pc, micOut, fmtRate, 16, 2, false); Console.WriteLine($"set render 48k pcm stereo -> hr=0x{hr:X8}"); }
        Console.WriteLine($"format after: capture current={Format.Describe(pc, dev, 0)}" + (micOut != null ? $"; render current={Format.Describe(pc, micOut, 0)}" : ""));
    }
}
return 0;

public enum ERole { eConsole = 0, eMultimedia = 1, eCommunications = 2 }

[ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
class PolicyConfigClient { }

[ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IPolicyConfig
{
    int GetMixFormat(string dev, out IntPtr fmt);
    int GetDeviceFormat(string dev, int def, out IntPtr fmt);
    int ResetDeviceFormat(string dev);
    int SetDeviceFormat(string dev, IntPtr a, IntPtr b);
    int GetProcessingPeriod(string dev, int def, out long a, out long b);
    int SetProcessingPeriod(string dev, ref long p);
    int GetShareMode(string dev, out IntPtr mode);
    int SetShareMode(string dev, IntPtr mode);
    int GetPropertyValue(string dev, int a, IntPtr key, out IntPtr val);
    int SetPropertyValue(string dev, int a, IntPtr key, IntPtr val);
    int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string dev, ERole role);
    int SetEndpointVisibility(string dev, int visible);
}
