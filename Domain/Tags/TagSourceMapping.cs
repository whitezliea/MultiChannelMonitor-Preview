namespace Domain.Tags;

public sealed record TagSourceMapping(
    string TagId,
    string SourceDeviceId,
    SourceType SourceType,
    string? SourceCode = null,
    string? SourcePath = null,
    double Scale = 1.0,
    double Offset = 0.0,
    string? Formula = null,
    string? InputTagIds = null,
    bool IsEnabled = true);
