namespace Domain.Tags;

public sealed record TagDefinition(
    string TagId,
    string DisplayName,
    TagCategory Category,
    string Unit,
    double? MinValue = null,
    double? MaxValue = null,
    double? WarningHigh = null,
    double? AlarmHigh = null,
    double? WarningLow = null,
    double? AlarmLow = null,
    bool IsEnabled = true,
    string Description = "",
    TagDataType DataType = TagDataType.Double,
    TagValueKind ValueKind = TagValueKind.Numeric,
    bool IsHistorized = true,
    int? HistoryIntervalMs = 1000,
    int DisplayOrder = 0);
