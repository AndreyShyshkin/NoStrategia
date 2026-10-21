using System.Runtime.InteropServices;

namespace TelemetryAgent.Core.Models.Memory;

[StructLayout(LayoutKind.Sequential, Pack = 4, CharSet = CharSet.Unicode)]
public struct SPageFileGraphic
{
    public int PacketId;
    public int Status; // 0=AC_OFF, 1=AC_REPLAY, 2=AC_LIVE, 3=AC_PAUSE
    public int Session; // Practice, Qualify, Race

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 15)]
    public string CurrentTime;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 15)]
    public string LastTime;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 15)]
    public string BestTime;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 15)]
    public string Split;

    public int CompletedLaps;
    public int Position;
    public int ICurrentTime; // ms
    public int ILastTime;    // ms
    public int IBestTime;    // ms
    public float SessionTimeLeft;
    public float DistanceTraveled;
    public int IsInPit;
    public int CurrentSectorIndex;
    public int LastSectorTime;
    public int NumberOfLaps;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)]
    public string TyreCompound;

    public float ReplayTimeMultiplier;
    public float NormalizedCarPosition; // 0.0 to 1.0 along spline
    public int ActiveCars;

    // X, Y, Z coordinates for 60 opponents (used by Virtual Re-join Tracker)
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 60 * 3)]
    public float[] CarCoordinates;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 60)]
    public int[] CarID;

    public int PlayerCarID;
    public float PenaltyTime;
    public int Flag;
    public int Penalty;
    public int IdealLineOn;
    public int IsInPitLane;
    public float SurfaceGrip;
    public int MandatoryPitDone;
    public float WindSpeed;
    public float WindDirection;
    public int IsSetupMenuVisible;
    public int MainDisplayIndex;
    public int SecondaryDisplayIndex;
    public int TC;
    public int TCCut;
    public int EngineMap;
    public int ABS;
    public int FuelXLap;
    public int RainLights;
    public int FlashingLights;
    public int LightsStage;
    public float ExhaustTemperature;
    public int WiperLV;
    public int DriverStintTotalTimeLeft;
    public int DriverStintTimeLeft;
    public int RainTyres;
}
