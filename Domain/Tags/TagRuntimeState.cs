namespace Domain.Tags;

public sealed record TagRuntimeState(
    string TagId,
    string DisplayName,
    TagCategory Category,
    double? NumericValue,
    string? TextValue,
    bool? BoolValue,
    string? Unit,
    TagDataType DataType,
    TagQuality Quality,
    TagAlarmState AlarmState,
    DateTimeOffset Timestamp,
    Guid SourceFrameId,
    long SequenceNo,
    DateTimeOffset LastUpdateTime);
