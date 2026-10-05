using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using Signage.Application;

namespace Signage.Infrastructure.Conversion;

public sealed record ExtractedVideo(string Type, string Asset, string Sha256, MediaPlacement Placement, MediaPlayback Playback, string? YouTubeVideoId = null);
public sealed record ExtractedVideos(IReadOnlyDictionary<int, IReadOnlyList<ExtractedVideo>> Slides, IReadOnlyList<string> Warnings);

/// <summary>Reads media references, never executes embed HTML or fetches external URLs.</summary>
public sealed class PptxVideoExtractor(IOptions<MediaOptions> options)
{
    private static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private readonly MediaOptions settings = options.Value;

    public async Task<ExtractedVideos> ExtractAsync(string sourcePath, string packageDirectory, PresentationInspection inspection, CancellationToken token)
    {
        using var archive = ZipFile.OpenRead(sourcePath);
        var presentation = ReadXml(archive.GetEntry("ppt/presentation.xml")!);
        var presentationRelationships = ReadRelationships(archive, "ppt/presentation.xml");
        var slideIds = presentation.Descendants().Where(e => e.Name.LocalName == "sldId").ToArray();
        var size = presentation.Descendants().FirstOrDefault(e => e.Name.LocalName == "sldSz");
        var nativeWidth = size is null ? 12_192_000 : Number(size, "cx");
        var nativeHeight = size is null ? 6_858_000 : Number(size, "cy");
        var warnings = new List<string>();
        var slides = new Dictionary<int, IReadOnlyList<ExtractedVideo>>();
        foreach (var plan in inspection.Slides)
        {
            token.ThrowIfCancellationRequested();
            var id = slideIds[plan.SourceSlideNumber - 1].Attribute(Relationships + "id")!.Value;
            var slidePath = ResolvePart("ppt/presentation.xml", presentationRelationships[id].Target);
            if (slidePath is null || archive.GetEntry(slidePath) is not { } slideEntry) continue;
            var xml = ReadXml(slideEntry);
            var rels = ReadRelationships(archive, slidePath);
            var videos = new List<ExtractedVideo>();
            foreach (var shape in xml.Descendants().Where(e => e.Name.LocalName is "pic" or "graphicFrame"))
            {
                token.ThrowIfCancellationRequested();
                var references = shape.Descendants().Where(e => e.Name.LocalName is "videoFile" or "media").ToList();
                var web = shape.Descendants().FirstOrDefault(e => e.Name.LocalName == "webVideoPr");
                if (references.Count == 0 && web is null) continue;
                // p14:media can also denote audio; never turn audio-only shapes into video.
                if (shape.Descendants().Any(e => e.Name.LocalName == "audioFile")) continue;
                var candidates = references.SelectMany(e => e.Attributes().Where(a => a.Name.Namespace == Relationships && a.Name.LocalName is "embed" or "link"))
                    .Select(a => rels.GetValueOrDefault(a.Value)).Where(r => r is not null).Cast<PartRelationship>().ToList();
                var embedded = candidates.FirstOrDefault(r => !r.External);
                var placement = Placement(shape, nativeWidth, nativeHeight, warnings, plan.SourceSlideNumber);
                var trim = shape.Descendants().FirstOrDefault(e => e.Name.LocalName == "trim");
                var start = Math.Max(0, Number(trim, "st") / 1000);
                var endValue = Number(trim, "end");
                double? end = endValue > start * 1000 ? endValue / 1000 : null;
                var playback = new MediaPlayback(settings.StartupTimeoutSeconds * 1000, settings.StallTimeoutSeconds * 1000,
                    settings.MaximumPlaybackSeconds * 1000, start, end);
                if (embedded is not null)
                {
                    var mediaPath = ResolvePart(slidePath, embedded.Target);
                    // OPC media parts may live under /media/ or /ppt/media/.
                    // Read only the resolved ZIP entry; output names are our own hashes.
                    var entry = mediaPath is not null ? archive.GetEntry(mediaPath) : null;
                    var extension = entry is null ? "" : Path.GetExtension(entry.FullName).ToLowerInvariant();
                    if (entry is null || extension is not (".mp4" or ".webm") || entry.Length > settings.MaximumEmbeddedVideoBytes)
                    {
                        warnings.Add($"Slide {plan.SourceSlideNumber}: embedded video missing, too large, or not MP4/WebM; static preview retained.");
                        continue;
                    }
                    await using var input = entry.Open();
                    var header = new byte[12];
                    var headerLength = await input.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken: token);
                    var valid = extension == ".mp4" ? headerLength >= 12 && header.AsSpan(4, 4).SequenceEqual("ftyp"u8)
                        : headerLength >= 4 && header.AsSpan(0, 4).SequenceEqual(new byte[] { 0x1a, 0x45, 0xdf, 0xa3 });
                    if (!valid)
                    {
                        warnings.Add($"Slide {plan.SourceSlideNumber}: invalid video container; static preview retained.");
                        continue;
                    }
                    var mediaDirectory = Path.Combine(packageDirectory, "media");
                    Directory.CreateDirectory(mediaDirectory);
                    var temporary = Path.Combine(mediaDirectory, $"extract-{Guid.NewGuid():N}.tmp");
                    await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        await output.WriteAsync(header.AsMemory(0, headerLength), token);
                        var buffer = new byte[81920];
                        long copied = headerLength;
                        int read;
                        while ((read = await input.ReadAsync(buffer, token)) > 0)
                        {
                            copied += read;
                            if (copied > entry.Length || copied > settings.MaximumEmbeddedVideoBytes)
                                throw new PresentationRejectedException("ZIP_LIMIT_EXCEEDED", "Video data exceeds its declared or configured size.");
                            await output.WriteAsync(buffer.AsMemory(0, read), token);
                        }
                    }
                    string hash;
                    await using (var bytes = File.OpenRead(temporary)) hash = Convert.ToHexString(await SHA256.HashDataAsync(bytes, token)).ToLowerInvariant();
                    var asset = $"media/{hash}{extension}";
                    var destination = Path.Combine(packageDirectory, asset.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(destination)) File.Delete(temporary); else File.Move(temporary, destination);
                    videos.Add(new ExtractedVideo("video", asset, hash, placement, playback));
                    continue;
                }
                var onlineSource = candidates.Where(r => r.External).Select(r => r.Target)
                    .Concat(web?.Attributes().Where(a => a.Name.LocalName is "embeddedHtml" or "embed" or "src" or "url").Select(a => a.Value) ?? []);
                var online = onlineSource.Select(value => TryYouTube(value)).FirstOrDefault(value => value is not null);
                if (online is null || !settings.EnableYouTube)
                {
                    warnings.Add($"Slide {plan.SourceSlideNumber}: external video unsupported or YouTube disabled; static preview retained. No external URL was fetched.");
                    continue;
                }
                // YouTube is never copied into the package; only a validated ID is retained.
                videos.Add(new ExtractedVideo("youtube", "", "", placement,
                    playback with { StartSeconds = online.Value.Start, EndSeconds = online.Value.End }, online.Value.Id));
                warnings.Add($"Slide {plan.SourceSlideNumber}: YouTube playback requires internet access and embedding permission; unavailable playback is skipped.");
            }
            if (videos.Count > 0) slides.Add(plan.SourceSlideNumber, videos);
            if (videos.Count > 1) warnings.Add($"Slide {plan.SourceSlideNumber}: multiple videos play sequentially in shape order before advancing; PowerPoint trigger timing is not executed.");
        }
        return new ExtractedVideos(slides, warnings);
    }

    public static (string Id, double Start, double? End)? TryYouTube(string source)
    {
        if (source.Length > 32_768) return null;
        source = WebUtility.HtmlDecode(source.Trim());
        if (source.StartsWith('<'))
        {
            var match = Regex.Match(source, "<iframe\\b[^>]*?\\bsrc\\s*=\\s*(['\"])(?<url>.*?)\\1", RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromMilliseconds(100));
            if (!match.Success) return null;
            source = WebUtility.HtmlDecode(match.Groups["url"].Value);
        }
        if (source.StartsWith("//", StringComparison.Ordinal)) source = "https:" + source;
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || !uri.IsDefaultPort || uri.UserInfo.Length != 0) return null;
        var host = uri.IdnHost.ToLowerInvariant();
        if (host is not ("youtube.com" or "www.youtube.com" or "m.youtube.com" or "youtube-nocookie.com" or "www.youtube-nocookie.com" or "youtu.be")) return null;
        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2)).GroupBy(pair => WebUtility.UrlDecode(pair[0])).ToDictionary(group => group.Key, group => WebUtility.UrlDecode(group.First().ElementAtOrDefault(1) ?? ""));
        var segments = uri.AbsolutePath.Trim('/').Split('/');
        var id = host == "youtu.be" ? segments[0] : segments.Length == 2 && segments[0] is "embed" or "v" or "shorts" or "live" ? segments[1]
            : uri.AbsolutePath == "/watch" ? query.GetValueOrDefault("v") : null;
        if (id is null || !Regex.IsMatch(id, "^[A-Za-z0-9_-]{11}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))) return null;
        var start = Seconds(query.GetValueOrDefault("start") ?? query.GetValueOrDefault("t"));
        var end = Seconds(query.GetValueOrDefault("end"));
        return (id, start, end > start ? end : null);
    }

    private static double Seconds(string? value) => double.TryParse(value?.TrimEnd('s'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0 && seconds <= 86400 ? seconds : 0;
    private static double Number(XElement? e, string name) => double.TryParse(e?.Attribute(name)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : 0;

    private static MediaPlacement Placement(XElement shape, double slideWidth, double slideHeight, List<string> warnings, int slideNumber)
    {
        var xfrm = shape.Elements().Where(e => e.Name.LocalName is "spPr" or "xfrm").SelectMany(e => e.Name.LocalName == "xfrm" ? new[] { e } : e.Elements()).FirstOrDefault(e => e.Name.LocalName == "xfrm");
        var off = xfrm?.Elements().FirstOrDefault(e => e.Name.LocalName == "off");
        var ext = xfrm?.Elements().FirstOrDefault(e => e.Name.LocalName == "ext");
        double x = Number(off, "x"), y = Number(off, "y"), w = Number(ext, "cx"), h = Number(ext, "cy");
        foreach (var group in shape.Ancestors().Where(e => e.Name.LocalName == "grpSp"))
        {
            var transform = group.Elements().FirstOrDefault(e => e.Name.LocalName == "grpSpPr")?.Elements().FirstOrDefault(e => e.Name.LocalName == "xfrm");
            var groupOff = transform?.Elements().FirstOrDefault(e => e.Name.LocalName == "off");
            var groupExt = transform?.Elements().FirstOrDefault(e => e.Name.LocalName == "ext");
            var childOff = transform?.Elements().FirstOrDefault(e => e.Name.LocalName == "chOff");
            var childExt = transform?.Elements().FirstOrDefault(e => e.Name.LocalName == "chExt");
            if (Number(transform, "rot") != 0 || IsTrue(transform, "flipH") || IsTrue(transform, "flipV") || Number(childExt, "cx") <= 0 || Number(childExt, "cy") <= 0)
            {
                warnings.Add($"Slide {slideNumber}: complex video group transform is unsupported; video plays full-slide.");
                return new MediaPlacement(0, 0, 1, 1);
            }
            var sx = Number(groupExt, "cx") / Number(childExt, "cx");
            var sy = Number(groupExt, "cy") / Number(childExt, "cy");
            x = Number(groupOff, "x") + (x - Number(childOff, "x")) * sx;
            y = Number(groupOff, "y") + (y - Number(childOff, "y")) * sy;
            w *= sx; h *= sy;
        }
        if (slideWidth <= 0 || slideHeight <= 0 || w <= 0 || h <= 0 || Math.Abs(x / slideWidth) > 10 || Math.Abs(y / slideHeight) > 10 ||
            w / slideWidth > 20 || h / slideHeight > 20 || w / slideWidth < 0.000001 || h / slideHeight < 0.000001)
        {
            warnings.Add($"Slide {slideNumber}: invalid/extreme video placement; video plays full-slide.");
            return new MediaPlacement(0, 0, 1, 1);
        }
        return new MediaPlacement(x / slideWidth, y / slideHeight, w / slideWidth, h / slideHeight, Number(xfrm, "rot") / 60000, IsTrue(xfrm, "flipH"), IsTrue(xfrm, "flipV"));
    }

    private static bool IsTrue(XElement? element, string name) => element?.Attribute(name)?.Value is "1" or "true";
    private sealed record PartRelationship(string Target, bool External);
    private static Dictionary<string, PartRelationship> ReadRelationships(ZipArchive archive, string part)
    {
        var path = part.Insert(part.LastIndexOf('/') + 1, "_rels/") + ".rels";
        if (archive.GetEntry(path) is not { } entry) return [];
        var result = new Dictionary<string, PartRelationship>();
        foreach (var relation in ReadXml(entry).Root!.Elements())
        {
            var id = relation.Attribute("Id")?.Value;
            var target = relation.Attribute("Target")?.Value;
            if (string.IsNullOrWhiteSpace(id) || target is null || !result.TryAdd(id, new PartRelationship(target, relation.Attribute("TargetMode")?.Value == "External")))
                throw new PresentationRejectedException("INVALID_PPTX", "Invalid or duplicate media relationship.");
        }
        return result;
    }

    private static string? ResolvePart(string part, string target)
    {
        if (target.Contains('\\') || target.Contains('#') || target.Contains('?')) return null;
        var origin = new Uri("https://package.invalid/" + part);
        if (!Uri.TryCreate(origin, target, out var resolved) || resolved.Host != origin.Host || resolved.Scheme != origin.Scheme) return null;
        var path = Uri.UnescapeDataString(resolved.AbsolutePath.TrimStart('/'));
        return path.Contains('\\') || path.Split('/').Any(segment => segment is "." or "..") ? null : path;
    }

    private static XDocument ReadXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 64 * 1024 * 1024 });
        return XDocument.Load(reader);
    }
}
