using Application.Configuration;

namespace Infrastructure.Configuration;

public sealed class RuntimeConfigurationProvider
{
    public MonitorRuntimeOptions LoadDefaultOptions() => new();
}
