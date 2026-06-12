using Domain.Tags;

namespace Domain.Alarms;

public static class AlarmRule
{
    public static TagAlarmState Evaluate(double value, TagQuality quality, AlarmDefinition definition)
    {
        if (quality == TagQuality.Offline)
        {
            return TagAlarmState.Offline;
        }

        if (quality != TagQuality.Good)
        {
            return TagAlarmState.Invalid;
        }

        if (definition.AlarmHigh.HasValue && value >= definition.AlarmHigh.Value)
        {
            return TagAlarmState.AlarmHigh;
        }

        if (definition.AlarmLow.HasValue && value <= definition.AlarmLow.Value)
        {
            return TagAlarmState.AlarmLow;
        }

        if (definition.WarningHigh.HasValue && value >= definition.WarningHigh.Value)
        {
            return TagAlarmState.WarningHigh;
        }

        if (definition.WarningLow.HasValue && value <= definition.WarningLow.Value)
        {
            return TagAlarmState.WarningLow;
        }

        return TagAlarmState.Normal;
    }
}
