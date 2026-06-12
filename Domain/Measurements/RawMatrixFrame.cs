using Domain.Tags;

namespace Domain.Measurements;

public sealed record RawMatrixFrame(
    int Rows,
    int Columns,
    string ValueType,
    string Unit,
    double[,] Values,
    TagQuality Quality,
    int ErrorCode);
