namespace Domain.Devices;

public sealed record SamplingConfig(TimeSpan DataGenerateInterval, TimeSpan MatrixGenerateInterval);
