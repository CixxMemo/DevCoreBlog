using Microsoft.AspNetCore.OutputCaching;

namespace DevCoreBlog.Services.Publishing;

/// <summary>Invalidates in-process public HTML lists after committed writes.</summary>
public sealed class PublicListCacheInvalidator(IOutputCacheStore store)
{
    public const string Tag = "public-post-lists";
    private long _generation;

    public long Generation => Interlocked.Read(ref _generation);

    // Advance the key before eviction so an in-flight old response cannot be reused.
    public async Task InvalidateAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _generation);
        await store.EvictByTagAsync(Tag, cancellationToken);
    }
}
