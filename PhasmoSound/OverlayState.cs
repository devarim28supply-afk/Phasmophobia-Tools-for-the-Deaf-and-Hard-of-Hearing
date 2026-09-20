using System;
using System.Collections.Generic;

namespace PhasmoSound;

public sealed class OnsetMark
{
    public DateTime Time;
    public float Angle;
    public float Conf;
    public float Strength; // dB jump
}

public sealed class StepMark
{
    public DateTime Time;
    public float Angle;
    public float Level;   // 0..1 loudness
}

public sealed class EventItem
{
    public DateTime Time;
    public string Label = "";
    public string Category = "";
    public float Angle;
    public float Conf;
    public float Score;
    public bool Pending;   // shown immediately on a sudden sound; the classifier fills in the name
}

/// Everything the overlay draws. Written by the audio and classifier threads, read by the UI thread.
public sealed class OverlayState
{
    public readonly object Lock = new();
    public int Channels;
    public bool Surround;
    public string DeviceName = "";
    public string Status = "";
    public string Toast = "";
    public bool BoardOpen;                              // phrase board (F5) is showing
    public List<string> Board = new();                  // its lines, 1..9
    public int BoardPageNo, BoardPages;
    public List<string> PageNames = new();
    public DateTime ToastUntil = DateTime.MinValue;

    public float[] ChannelDb = Array.Empty<float>();   // smoothed per-channel level, dBFS
    public float LoudDb = -100f;                       // smoothed overall level
    public float DirAngle;                             // degrees, 0 = front, clockwise
    public float DirConf;

    public readonly List<OnsetMark> Onsets = new();
    public readonly List<StepMark> Steps = new();      // footsteps that are not yours (off-center)
    public readonly List<CaptionLine> Captions = new(); // whisper captions, oldest first
    public List<Detection> Current = new();
    public readonly List<EventItem> Events = new();    // newest first

    // loudest moment since the classifier last looked (used to tag detections with a direction)
    public float WinPeakDb = -100f;
    public float WinPeakAngle;
    public float WinPeakConf;
}
