namespace Application.DTOs.MeasurementMap;

public enum MatrixAbnormalType
{
    InvalidValue,
    HighLimit,
    LowLimit,
    StatisticalHotspot,
    StatisticalColdspot,
    LocalHotspot,
    LocalColdspot
}

public enum MatrixSeverity
{
    Info,
    Warning,
    Alarm
}

public sealed record AbnormalMatrixPointDto(
    int Row,
    int Column,
    double Value,
    MatrixAbnormalType Type,
    MatrixSeverity Severity,
    string Message);
