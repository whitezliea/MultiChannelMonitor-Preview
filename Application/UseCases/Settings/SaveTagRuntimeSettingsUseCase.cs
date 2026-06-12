using Application.Configuration;
using Application.Services;

namespace Application.UseCases.Settings;

public sealed class SaveTagRuntimeSettingsUseCase(ConfigurationService configurationService)
{
    public Task ExecuteAsync(
        IReadOnlyCollection<TagRuntimeConfiguration> configurations,
        CancellationToken cancellationToken = default) =>
        configurationService.SaveTagsAsync(configurations, cancellationToken);
}
