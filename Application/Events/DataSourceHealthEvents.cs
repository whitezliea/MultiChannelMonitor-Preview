namespace Application.Events;

public sealed record DataSourceTimedOutEvent(
    Guid LastFrameId,
    long LastSequenceNo,
    DateTime LastFrameTimeUtc,
    DateTime TimedOutAtUtc);

public sealed record DataSourceRecoveredEvent(
    Guid FrameId,
    long SequenceNo,
    DateTime RecoveredAtUtc);
