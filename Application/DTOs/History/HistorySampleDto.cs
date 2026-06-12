using Domain.Tags;

namespace Application.DTOs.History;

public sealed record HistorySampleDto(string TagId, double Value, DateTime Timestamp, TagQuality Quality);
