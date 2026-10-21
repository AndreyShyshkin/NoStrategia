namespace TelemetryAgent.Core.Throttling;

public enum ThrottleMode
{
    FullRate,          // 60 Hz or 100 Hz (On track, AC_LIVE)
    ThrottledPitLane,  // 10 Hz (Pit lane, AC_LIVE)
    StandbyHeartbeat,  // 0.5 Hz (Pause, Replay, Menu)
    Suppressed         // Frame dropped completely
}

public interface IThrottlePolicy
{
    bool IsThrottlingEnabled { get; set; }
    int TargetBaseFrequencyHz { get; set; } // 60 or 100
    int PitFrequencyHz { get; set; }        // 10
    double StandbyFrequencyHz { get; set; } // 0.5

    /// <summary>
    /// Evaluates whether the current frame should be transmitted or suppressed.
    /// </summary>
    /// <param name="status">ACC status (0=OFF, 1=REPLAY, 2=LIVE, 3=PAUSE)</param>
    /// <param name="isInPitLane">1 if car is in pit lane, 0 otherwise</param>
    /// <param name="currentMode">The resulting throttle mode for this tick</param>
    /// <returns>True if frame should be transmitted, false if dropped</returns>
    bool ShouldTransmit(int status, int isInPitLane, out ThrottleMode currentMode);

    void Reset();
    (long Captured, long Transmitted, long Dropped, double SavingsPercentage) GetStatistics();
}
