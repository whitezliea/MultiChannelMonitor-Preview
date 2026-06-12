using System.Globalization;
using Application.Abstractions.Persistence;
using Application.Configuration;
using AppLogging;
using Domain.Logs;
using Domain.Tags;

namespace Application.Services;

public sealed class ConfigurationService
{
    private readonly IReadOnlyDictionary<string, TagDefinition> _definitions;
    private readonly IConfigurationRepository _repository;
    private readonly ITagRuntimeConfigurationStore _tagStore;
    private readonly IRuntimeOptionsStore _runtimeStore;
    private readonly OperationLogService _operationLogService;

    public ConfigurationService(
        IEnumerable<TagDefinition> definitions,
        IConfigurationRepository repository,
        ITagRuntimeConfigurationStore tagStore,
        IRuntimeOptionsStore runtimeStore,
        OperationLogService operationLogService)
    {
        _definitions = definitions.ToDictionary(item => item.TagId, StringComparer.Ordinal);
        _repository = repository;
        _tagStore = tagStore;
        _runtimeStore = runtimeStore;
        _operationLogService = operationLogService;
    }

    public event EventHandler<MonitorRuntimeOptions>? RuntimeOptionsChanged;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var defaults = _definitions.Values
            .Select(TagRuntimeConfiguration.FromDefinition)
            .ToDictionary(item => item.TagId, StringComparer.Ordinal);
        foreach (var persisted in await _repository
            .LoadTagConfigurationsAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!_definitions.TryGetValue(persisted.TagId, out var definition))
            {
                AppLogger.Info("Ignoring configuration for unknown TagId: {0}", persisted.TagId);
                continue;
            }

            try
            {
                ConfigurationValidation.ValidateTag(definition, persisted);
                defaults[persisted.TagId] = persisted;
            }
            catch (Exception exception)
            {
                AppLogger.Error(exception, "Invalid persisted tag configuration; default used | TagId: {0}", persisted.TagId);
            }
        }

        _tagStore.Replace(defaults.Values);
        var options = ApplyRuntimeOverrides(
            _runtimeStore.Snapshot,
            await _repository.LoadRuntimeSettingsAsync(cancellationToken).ConfigureAwait(false));
        _runtimeStore.Replace(options);
    }

    public async Task SaveTagsAsync(
        IReadOnlyCollection<TagRuntimeConfiguration> configurations,
        CancellationToken cancellationToken = default)
    {
        if (configurations.Count != _definitions.Count)
        {
            throw new ArgumentException("A complete tag runtime configuration snapshot is required.");
        }

        foreach (var configuration in configurations)
        {
            if (!_definitions.TryGetValue(configuration.TagId, out var definition))
            {
                throw new ArgumentException($"Unknown TagId: {configuration.TagId}");
            }

            ConfigurationValidation.ValidateTag(definition, configuration);
        }

        await _repository.SaveTagConfigurationsAsync(configurations, cancellationToken).ConfigureAwait(false);
        _tagStore.Replace(configurations);
        await _operationLogService.WriteAsync(
            OperationLogLevel.Info,
            "Settings",
            "TagRuntimeSettings.Saved",
            nameof(ConfigurationService),
            "Tag runtime settings saved.",
            $"TagCount={configurations.Count}",
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<RuntimeSettingsSaveResult> SaveRuntimeAsync(
        MonitorRuntimeOptions options,
        CancellationToken cancellationToken = default)
    {
        ConfigurationValidation.ValidateRuntimeOptions(options);
        var settings = ToSettings(options);
        await _repository.SaveRuntimeSettingsAsync(settings, cancellationToken).ConfigureAwait(false);
        _runtimeStore.Replace(options);
        RuntimeOptionsChanged?.Invoke(this, options);
        await _operationLogService.WriteAsync(
            OperationLogLevel.Info,
            "Settings",
            "RuntimeSettings.Saved",
            nameof(ConfigurationService),
            "Runtime settings saved.",
            string.Join("; ", settings.Select(item => $"{item.Key}={item.Value}")),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return new RuntimeSettingsSaveResult(options, new Dictionary<string, SettingEffect>
        {
            [RuntimeSettingKeys.UiRefreshIntervalMs] = SettingEffect.Immediate,
            [RuntimeSettingKeys.DataGenerateIntervalMs] = SettingEffect.NextAcquisitionStart,
            [RuntimeSettingKeys.DataSourceTimeoutPeriods] = SettingEffect.NextAcquisitionStart,
            [RuntimeSettingKeys.HistoryBatchIntervalMs] = SettingEffect.NextApplicationStart,
            [RuntimeSettingKeys.HistoryRetentionDays] = SettingEffect.NextApplicationStart,
            [RuntimeSettingKeys.AlarmBatchIntervalMs] = SettingEffect.NextApplicationStart,
            [RuntimeSettingKeys.OperationLogBatchIntervalMs] = SettingEffect.NextApplicationStart,
            [RuntimeSettingKeys.MaximumTrendWindowMinutes] = SettingEffect.NextApplicationStart
        });
    }

    private static IReadOnlyDictionary<string, string> ToSettings(MonitorRuntimeOptions options) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RuntimeSettingKeys.DataGenerateIntervalMs] = options.DataGenerateInterval.TotalMilliseconds.ToString(CultureInfo.InvariantCulture),
            [RuntimeSettingKeys.DataSourceTimeoutPeriods] = options.DataSourceTimeoutPeriods.ToString(CultureInfo.InvariantCulture),
            [RuntimeSettingKeys.UiRefreshIntervalMs] = options.UiRefreshInterval.TotalMilliseconds.ToString(CultureInfo.InvariantCulture),
            [RuntimeSettingKeys.HistoryBatchIntervalMs] = options.HistoryBatchInterval.TotalMilliseconds.ToString(CultureInfo.InvariantCulture),
            [RuntimeSettingKeys.HistoryRetentionDays] = options.HistoryRetentionDays.ToString(CultureInfo.InvariantCulture),
            [RuntimeSettingKeys.AlarmBatchIntervalMs] = options.AlarmBatchInterval.TotalMilliseconds.ToString(CultureInfo.InvariantCulture),
            [RuntimeSettingKeys.OperationLogBatchIntervalMs] = options.OperationLogBatchInterval.TotalMilliseconds.ToString(CultureInfo.InvariantCulture),
            [RuntimeSettingKeys.MaximumTrendWindowMinutes] = options.MaximumTrendWindow.TotalMinutes.ToString(CultureInfo.InvariantCulture)
        };

    private static MonitorRuntimeOptions ApplyRuntimeOverrides(
        MonitorRuntimeOptions defaults,
        IReadOnlyDictionary<string, string> settings)
    {
        try
        {
            var options = defaults with
            {
                DataGenerateInterval = ReadInterval(settings, RuntimeSettingKeys.DataGenerateIntervalMs, defaults.DataGenerateInterval),
                DataSourceTimeoutPeriods = ReadInt(settings, RuntimeSettingKeys.DataSourceTimeoutPeriods, defaults.DataSourceTimeoutPeriods),
                UiRefreshInterval = ReadInterval(settings, RuntimeSettingKeys.UiRefreshIntervalMs, defaults.UiRefreshInterval),
                HistoryBatchInterval = ReadInterval(settings, RuntimeSettingKeys.HistoryBatchIntervalMs, defaults.HistoryBatchInterval),
                HistoryRetentionDays = ReadInt(settings, RuntimeSettingKeys.HistoryRetentionDays, defaults.HistoryRetentionDays),
                AlarmBatchInterval = ReadInterval(settings, RuntimeSettingKeys.AlarmBatchIntervalMs, defaults.AlarmBatchInterval),
                OperationLogBatchInterval = ReadInterval(settings, RuntimeSettingKeys.OperationLogBatchIntervalMs, defaults.OperationLogBatchInterval),
                TrendWindows = ReadTrendWindows(settings, defaults)
            };
            ConfigurationValidation.ValidateRuntimeOptions(options);
            return options;
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, "Invalid persisted runtime settings; defaults used.");
            return defaults;
        }
    }

    private static TimeSpan ReadInterval(
        IReadOnlyDictionary<string, string> settings,
        string key,
        TimeSpan fallback) =>
        settings.TryGetValue(key, out var text)
        && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var milliseconds)
            ? TimeSpan.FromMilliseconds(milliseconds)
            : fallback;

    private static IReadOnlyList<TimeSpan> ReadTrendWindows(
        IReadOnlyDictionary<string, string> settings,
        MonitorRuntimeOptions defaults)
    {
        if (!settings.TryGetValue(RuntimeSettingKeys.MaximumTrendWindowMinutes, out var text)
            || !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes))
        {
            return defaults.TrendWindows;
        }

        var maximum = TimeSpan.FromMinutes(minutes);
        return defaults.TrendWindows
            .Where(window => window < maximum)
            .Append(maximum)
            .Distinct()
            .OrderBy(window => window)
            .ToArray();
    }

    private static int ReadInt(
        IReadOnlyDictionary<string, string> settings,
        string key,
        int fallback) =>
        settings.TryGetValue(key, out var text)
        && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
}
