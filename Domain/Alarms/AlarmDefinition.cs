using Domain.Tags;

namespace Domain.Alarms;

public sealed record AlarmDefinition(
    string TagId,
    double? WarningHigh,
    double? AlarmHigh,
    double? WarningLow,
    double? AlarmLow,
    double Hysteresis = 0,
    int DebounceCount = 1)
{
    public static AlarmDefinition FromTagDefinition(TagDefinition definition) =>
        new(definition.TagId, definition.WarningHigh, definition.AlarmHigh, definition.WarningLow, definition.AlarmLow);
}
