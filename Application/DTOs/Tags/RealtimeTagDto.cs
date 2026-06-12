using Domain.Tags;

namespace Application.DTOs.Tags;

public sealed record RealtimeTagDto(
    string TagId,
    string DisplayName,
    string DisplayValue,
    string Unit,
    TagQuality Quality,
    TagAlarmState AlarmState,
    DateTimeOffset Timestamp);
