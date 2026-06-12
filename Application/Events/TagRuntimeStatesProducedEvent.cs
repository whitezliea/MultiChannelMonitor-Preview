using Domain.Tags;

namespace Application.Events;

public sealed record TagRuntimeStatesProducedEvent(
    Guid SourceFrameId,
    long SequenceNo,
    DateTime Timestamp,
    IReadOnlyList<TagRuntimeState> States);
