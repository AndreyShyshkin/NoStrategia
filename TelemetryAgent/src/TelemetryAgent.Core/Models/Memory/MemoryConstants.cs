namespace TelemetryAgent.Core.Models.Memory;

public static class MemoryConstants
{
    public const string PhysicsMmfName = @"Local\acpmf_physics";
    public const string GraphicsMmfName = @"Local\acpmf_graphics";
    public const string StaticMmfName = @"Local\acpmf_static";

    // Alternate / Wine fallback names (sometimes in Wine prefixes without Local\ prefix)
    public const string FallbackPhysicsMmfName = "acpmf_physics";
    public const string FallbackGraphicsMmfName = "acpmf_graphics";
    public const string FallbackStaticMmfName = "acpmf_static";
}

public enum AcStatus
{
    AcOff = 0,
    AcReplay = 1,
    AcLive = 2,
    AcPause = 3
}

public enum AcSessionType
{
    Practice = 0,
    Qualify = 1,
    Race = 2,
    Hotlap = 3,
    TimeAttack = 4,
    Drift = 5,
    Drag = 6,
    Hotstint = 7,
    HotlapSuperpole = 8
}
