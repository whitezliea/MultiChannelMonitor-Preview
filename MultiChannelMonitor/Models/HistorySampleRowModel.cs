using Domain.Tags;

namespace Presentation.Wpf.Models;

public sealed record HistorySampleRowModel(
    string TimestampLocal,
    double Value,
    TagQuality Quality,
    TagAlarmState AlarmState,
    long SequenceNo);
