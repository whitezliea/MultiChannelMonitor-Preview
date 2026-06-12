namespace Domain.Tags;

public sealed record CleanedTagValue(
    string TagId,
    double? NumericValue,
    string? TextValue,
    bool? BoolValue,
    TagDataType DataType,
    string? Unit,
    DateTimeOffset Timestamp,
    TagQuality Quality,
    string SourceDeviceId,
    string? SourceCode,
    Guid SourceFrameId,
    long SequenceNo,
    string? CleanMessage);
