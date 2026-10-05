namespace Signage.Domain;

public static class SlideTiming
{
    public static int Normalize(int? sourceDurationMs, int defaultMs, int minimumMs, int maximumMs)
    {
        if (defaultMs <= 0 || minimumMs <= 0 || maximumMs < minimumMs)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultMs), "Slide timing settings are invalid.");
        }

        return Math.Clamp(sourceDurationMs.GetValueOrDefault(defaultMs), minimumMs, maximumMs);
    }
}

public static class PublicationRules
{
    public static Publication? SelectActive(IEnumerable<Publication> publications, DateTimeOffset utcNow) =>
        publications
            .Where(item => item.IsEnabled && item.StartsUtc <= utcNow && (item.EndsUtc is null || item.EndsUtc > utcNow))
            .OrderByDescending(item => item.Priority)
            .ThenByDescending(item => item.PublishedUtc)
            .ThenByDescending(item => item.Id)
            .FirstOrDefault();
}

public static class PairingRules
{
    public static bool IsUsable(PairingSession session, DateTimeOffset utcNow) =>
        session.ConsumedUtc is null && session.ExpiresUtc > utcNow;
}

public sealed record VersionRetentionCandidate(
    Guid Id,
    Guid PresentationId,
    DateTimeOffset CreatedUtc,
    bool IsProtected);

public static class RetentionRules
{
    public static IReadOnlySet<Guid> SelectRemovable(
        IEnumerable<VersionRetentionCandidate> candidates,
        int retainedVersionsPerPresentation)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(retainedVersionsPerPresentation, 1);
        return candidates
            .GroupBy(item => item.PresentationId)
            .SelectMany(group => group
                .OrderByDescending(item => item.CreatedUtc)
                .ThenByDescending(item => item.Id)
                .Skip(retainedVersionsPerPresentation)
                .Where(item => !item.IsProtected)
                .Select(item => item.Id))
            .ToHashSet();
    }
}
