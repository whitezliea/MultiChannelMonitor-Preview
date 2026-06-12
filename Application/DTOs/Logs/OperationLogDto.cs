using Domain.Logs;

namespace Application.DTOs.Logs;

public sealed record OperationLogDto(DateTime Timestamp, OperationLogLevel Level, string Category, string Message);
