using System.Collections.Concurrent;

namespace Signage.Application;

public enum ConversionStage
{
    Starting,
    Rendering,
    Packaging
}

/// <summary>
/// How far a conversion has got. <see cref="NextPercent"/> is where the next report will land,
/// so the status page can ease towards it while a slow slide renders.
/// </summary>
public sealed record ConversionProgress(ConversionStage Stage, int Percent, int NextPercent, int SlidesRendered = 0, int SlideCount = 0)
{
    // Rasterising slides is nearly all of the conversion time, so it gets most of the range.
    private const int RenderingStart = 8;
    private const int RenderingEnd = 92;
    private const int PackagingPercent = 94;

    public static ConversionProgress Starting { get; } = new(ConversionStage.Starting, 3, RenderingStart);

    public static ConversionProgress Rendering(int rendered, int total)
    {
        rendered = Math.Clamp(rendered, 0, total);
        var next = rendered < total ? RenderingPercent(rendered + 1, total) : PackagingPercent;
        return new(ConversionStage.Rendering, RenderingPercent(rendered, total), next, rendered, total);
    }

    public static ConversionProgress Packaging(int total) => new(ConversionStage.Packaging, PackagingPercent, 99, total, total);

    private static int RenderingPercent(int rendered, int total) =>
        total <= 0 ? RenderingStart : RenderingStart + (RenderingEnd - RenderingStart) * rendered / total;
}

/// <summary>
/// In-memory progress for conversions running in this process. Progress is transient, so it is
/// not persisted; after a restart the status page falls back to an indeterminate spinner.
/// </summary>
public sealed class ConversionProgressTracker
{
    private readonly ConcurrentDictionary<Guid, ConversionProgress> entries = new();

    public void Report(Guid versionId, ConversionProgress progress) => entries[versionId] = progress;

    public ConversionProgress? Get(Guid versionId) => entries.TryGetValue(versionId, out var progress) ? progress : null;

    public void Clear(Guid versionId) => entries.TryRemove(versionId, out _);
}
