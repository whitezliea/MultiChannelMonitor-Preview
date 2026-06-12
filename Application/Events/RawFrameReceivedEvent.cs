using Domain.Measurements;

namespace Application.Events;

public sealed record RawFrameReceivedEvent(RawMeasurementFrame Frame);
