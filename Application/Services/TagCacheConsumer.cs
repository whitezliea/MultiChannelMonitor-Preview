using Application.Abstractions.Events;
using Application.Events;

namespace Application.Services;

public sealed class TagCacheConsumer : IApplicationEventHandler<TagRuntimeStatesProducedEvent>
{
    private readonly TagService _tagService;

    public TagCacheConsumer(TagService tagService)
    {
        _tagService = tagService;
    }

    public ValueTask HandleAsync(TagRuntimeStatesProducedEvent applicationEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _tagService.UpdateTags(applicationEvent.States);
        return ValueTask.CompletedTask;
    }
}
