namespace Domain.Tags;

public sealed record TagValue(
    string TagId,
    double Value,
    DateTime Timestamp,
    TagQuality Quality,
    TagAlarmState AlarmState,
    string Source,
    long SequenceNo);
