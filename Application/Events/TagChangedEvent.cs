using Domain.Tags;

namespace Application.Events;

public sealed record TagChangedEvent(IReadOnlyList<TagRuntimeState> Values, DateTime PublishedAt);
