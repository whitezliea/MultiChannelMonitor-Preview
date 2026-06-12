namespace Simulator.Models;

public sealed class SimulationClock
{
    public SimulationClock(DateTime startTime)
    {
        Domain.Common.UtcDateTime.Require(startTime, nameof(startTime));
        StartTime = startTime;
        LastFrameTime = startTime;
    }

    public DateTime StartTime { get; }
    public DateTime LastFrameTime { get; private set; }

    public (TimeSpan Elapsed, double DeltaSeconds) Advance(DateTime now)
    {
        Domain.Common.UtcDateTime.Require(now, nameof(now));
        var elapsed = now - StartTime;
        var deltaSeconds = Math.Max(0.001, (now - LastFrameTime).TotalSeconds);
        LastFrameTime = now;
        return (elapsed, deltaSeconds);
    }
}
