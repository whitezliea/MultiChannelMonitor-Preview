using Application.Caches;
using Application.Abstractions.Time;
using Application.Events;
using AppLogging;
using Domain.Tags;

namespace Application.Services;

public sealed class TagService
{
    private readonly TagCache _tagCache;
    private readonly IClock _clock;

    public TagService(TagCache tagCache, IClock clock)
    {
        _tagCache = tagCache;
        _clock = clock;
    }

    public event EventHandler<TagChangedEvent>? TagsChanged;

    public void UpdateTags(IReadOnlyList<TagRuntimeState> values)
    {
        AppLogger.Info("TagService | UpdateTags");
        _tagCache.Update(values);
        TagsChanged?.Invoke(this, new TagChangedEvent(values, _clock.UtcNow));
    }

    public TagSnapshot GetSnapshot() 
    {
        AppLogger.Info("TagService | GetSnapshot");    
        return _tagCache.GetSnapshot();
    }
}
