namespace Simulator.Models;

public sealed record SimulationFrameContext(long SequenceNo, DateTime Timestamp, TimeSpan Elapsed, double DeltaSeconds);
