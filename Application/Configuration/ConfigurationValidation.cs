using Domain.Tags;

namespace Application.Configuration;

public static class ConfigurationValidation
{
    public static void ValidateTag(
        TagDefinition definition,
        TagRuntimeConfiguration configuration)
    {
        if (!string.Equals(definition.TagId, configuration.TagId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Tag configuration does not match its definition.");
        }

        var thresholds = new[]
        {
            configuration.AlarmLow,
            configuration.WarningLow,
            configuration.WarningHigh,
            configuration.AlarmHigh
        };
        if (thresholds.Any(value => value.HasValue && !double.IsFinite(value.Value)))
        {
            throw new ArgumentException($"{definition.TagId}: thresholds must be finite numbers.");
        }

        var isNumeric = definition.DataType is TagDataType.Double or TagDataType.Int or TagDataType.Number;
        if (!isNumeric && thresholds.Any(value => value.HasValue))
        {
            throw new ArgumentException($"{definition.TagId}: non-numeric tags cannot define numeric thresholds.");
        }

        ValidateBound(definition.TagId, "AlarmLow", configuration.AlarmLow, definition.MinValue, definition.MaxValue);
        ValidateBound(definition.TagId, "WarningLow", configuration.WarningLow, definition.MinValue, definition.MaxValue);
        ValidateBound(definition.TagId, "WarningHigh", configuration.WarningHigh, definition.MinValue, definition.MaxValue);
        ValidateBound(definition.TagId, "AlarmHigh", configuration.AlarmHigh, definition.MinValue, definition.MaxValue);

        if (configuration.AlarmLow.HasValue && configuration.WarningLow.HasValue
            && configuration.AlarmLow.Value > configuration.WarningLow.Value)
        {
            throw new ArgumentException($"{definition.TagId}: AlarmLow must be less than or equal to WarningLow.");
        }

        if (configuration.WarningLow.HasValue && configuration.WarningHigh.HasValue
            && configuration.WarningLow.Value >= configuration.WarningHigh.Value)
        {
            throw new ArgumentException($"{definition.TagId}: WarningLow must be less than WarningHigh.");
        }

        if (configuration.WarningHigh.HasValue && configuration.AlarmHigh.HasValue
            && configuration.WarningHigh.Value > configuration.AlarmHigh.Value)
        {
            throw new ArgumentException($"{definition.TagId}: WarningHigh must be less than or equal to AlarmHigh.");
        }

        if (configuration.AlarmEnabled && isNumeric && thresholds.All(value => !value.HasValue))
        {
            throw new ArgumentException($"{definition.TagId}: enabled numeric alarms require at least one threshold.");
        }

        if (configuration.HistoryIntervalMs <= 0)
        {
            throw new ArgumentException($"{definition.TagId}: HistoryIntervalMs must be greater than zero.");
        }
    }

    public static void ValidateRuntimeOptions(MonitorRuntimeOptions options)
    {
        if (options.DataGenerateInterval <= TimeSpan.Zero
            || options.UiRefreshInterval <= TimeSpan.Zero
            || options.HistoryBatchInterval <= TimeSpan.Zero
            || options.AlarmBatchInterval <= TimeSpan.Zero
            || options.OperationLogBatchInterval <= TimeSpan.Zero)
        {
            throw new ArgumentException("All runtime intervals must be greater than zero.");
        }

        if (options.TrendWindows.Count == 0 || options.TrendWindows.Any(window => window <= TimeSpan.Zero))
        {
            throw new ArgumentException("At least one positive trend window is required.");
        }

        if (options.HistoryRetentionDays <= 0 || options.HistoryRetentionDeleteBatchSize <= 0)
        {
            throw new ArgumentException("History retention days and delete batch size must be greater than zero.");
        }

        if (options.DataSourceTimeoutPeriods < 2)
        {
            throw new ArgumentException("Data source timeout periods must be at least 2.");
        }
    }

    private static void ValidateBound(
        string tagId,
        string name,
        double? value,
        double? minimum,
        double? maximum)
    {
        if (value.HasValue && minimum.HasValue && value.Value < minimum.Value)
        {
            throw new ArgumentException($"{tagId}: {name} is below MinValue.");
        }

        if (value.HasValue && maximum.HasValue && value.Value > maximum.Value)
        {
            throw new ArgumentException($"{tagId}: {name} is above MaxValue.");
        }
    }
}
