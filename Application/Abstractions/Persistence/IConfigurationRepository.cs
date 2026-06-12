using Application.Configuration;

namespace Application.Abstractions.Persistence;

public interface IConfigurationRepository
{
    Task<IReadOnlyList<TagRuntimeConfiguration>> LoadTagConfigurationsAsync(
        CancellationToken cancellationToken);
    Task SaveTagConfigurationsAsync(
        IReadOnlyCollection<TagRuntimeConfiguration> configurations,
        CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<string, string>> LoadRuntimeSettingsAsync(
        CancellationToken cancellationToken);
    Task SaveRuntimeSettingsAsync(
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken);
}
