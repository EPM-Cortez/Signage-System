using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Signage.Application;

public sealed record SlidePlanItem(int SourceSlideNumber, int DurationMs, string Transition);

public sealed record PresentationInspection(
    int CanvasWidth,
    int CanvasHeight,
    IReadOnlyList<SlidePlanItem> Slides,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Fonts);

public sealed record ManifestTransition(string Type, int DurationMs);

public sealed record ManifestAsset(string Asset, string Sha256);
public sealed record MediaPlacement(double X, double Y, double Width, double Height, double Rotation = 0, bool FlipHorizontal = false, bool FlipVertical = false);
public sealed record MediaPlayback(int StartupTimeoutMs, int StallTimeoutMs, int MaximumDurationMs, double StartSeconds = 0, double? EndSeconds = null);

public sealed record ManifestItem(
    string Type,
    int SourceSlideNumber,
    string Asset,
    string Sha256,
    int DurationMs,
    ManifestTransition Transition,
    ManifestAsset? Background = null,
    MediaPlacement? Placement = null,
    MediaPlayback? Playback = null,
    string? YouTubeVideoId = null);

public sealed record ManifestCanvas(int Width, int Height);

public sealed record ContentManifest(
    int SchemaVersion,
    string ContentId,
    Guid PresentationVersionId,
    DateTimeOffset CreatedUtc,
    bool Loop,
    ManifestCanvas Canvas,
    IReadOnlyList<ManifestItem> Items);

public sealed record ConverterResult(
    int ExitCode,
    TimeSpan Elapsed,
    string StandardOutput,
    string StandardError,
    string RendererVersion);

public interface IContentStorage
{
    Task<(string Key, string Sha256)> SaveSourceAsync(Stream input, Guid versionId, CancellationToken cancellationToken);
    string GetSourcePath(string key);
    string GetPackagePath(string contentId);
    Task CommitPackageAsync(string temporaryDirectory, string contentId, CancellationToken cancellationToken);
    Task<bool> CanWriteAsync(CancellationToken cancellationToken);
    long GetAvailableBytes();
}

public interface IPresentationInspector
{
    Task<PresentationInspection> InspectAsync(string sourcePath, CancellationToken cancellationToken);
}

public interface IConverterRunner
{
    Task<ConverterResult> RenderAsync(
        string sourcePath,
        string outputDirectory,
        string settingsPath,
        CancellationToken cancellationToken);

    Task<bool> CheckHealthAsync(CancellationToken cancellationToken);
}

public static class TokenUtility
{
    public static string CreateToken(int bytes = 32) => Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes))
        .Replace('+', '-')
        .Replace('/', '_')
        .TrimEnd('=');

    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static bool FixedTimeMatches(string value, string expectedHexHash)
    {
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        byte[] expected;
        try
        {
            expected = Convert.FromHexString(expectedHexHash);
        }
        catch (FormatException)
        {
            return false;
        }

        return actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public static string CalculateContentId<T>(T canonicalValue)
    {
        var json = JsonSerializer.Serialize(canonicalValue, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }
}

public sealed class PresentationRejectedException(string code, string detail) : Exception(detail)
{
    public string Code { get; } = code;
}
