using Application.Configuration;
using Application.Services;

namespace Application.UseCases.Settings;

public sealed class SaveRuntimeSettingsUseCase(ConfigurationService configurationService)
{
    public Task<RuntimeSettingsSaveResult> ExecuteAsync(
        MonitorRuntimeOptions options,
        CancellationToken cancellationToken = default) =>
        configurationService.SaveRuntimeAsync(options, cancellationToken);
}
