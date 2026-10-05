using System.Collections.Concurrent;
using System.Text.Json;
using Signage.Application;
using Signage.Domain;

namespace Signage.Web.Services;

/// <summary>
/// Finds the first still image of a prepared package so pages can show a thumbnail.
/// Packages are content-addressed and immutable, so the lookup is cached per content id.
/// </summary>
public sealed class SlidePosters(IContentStorage storage, ILogger<SlidePosters> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly ConcurrentDictionary<string, string> posters = new(StringComparer.Ordinal);

    public string? GetUrl(PresentationVersion? version)
    {
        if (version?.ContentId is null || version.Status != PresentationVersionStatus.Ready) return null;
        if (!posters.TryGetValue(version.ContentId, out string? asset))
        {
            asset = ReadPoster(version.ContentId);
            if (asset is not null) posters[version.ContentId] = asset;
        }
        return asset is null ? null : $"/api/presentation-versions/{version.Id}/preview/{asset}";
    }

    private string? ReadPoster(string contentId)
    {
        try
        {
            var path = Path.Combine(storage.GetPackagePath(contentId), "manifest.json");
            if (!File.Exists(path)) return null;
            var manifest = JsonSerializer.Deserialize<ContentManifest>(File.ReadAllText(path), JsonOptions);
            return manifest?.Items
                .Select(item => item.Background?.Asset ?? item.Asset)
                .FirstOrDefault(asset => asset.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException or ArgumentException)
        {
            logger.LogWarning(exception, "Could not read the poster for content {ContentId}", contentId);
            return null;
        }
    }
}
