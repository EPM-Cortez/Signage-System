using System.Security.Cryptography;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using Microsoft.Extensions.Options;
using Signage.Application;
using Signage.Infrastructure.Conversion;

namespace Signage.UnitTests;

public sealed class PptxVideoTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"signage-video-tests-{Guid.NewGuid():N}");
    private static readonly byte[] Mp4 = [0, 0, 0, 20, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'i', (byte)'s', (byte)'o', (byte)'m', 0, 0, 0, 0, (byte)'i', (byte)'s', (byte)'o', (byte)'m'];

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=M7lc1UVf-VE", "M7lc1UVf-VE")]
    [InlineData("https://youtu.be/M7lc1UVf-VE?t=4s", "M7lc1UVf-VE")]
    [InlineData("https://www.youtube-nocookie.com/embed/M7lc1UVf-VE?start=3&end=8", "M7lc1UVf-VE")]
    [InlineData("<iframe src='//www.youtube.com/embed/M7lc1UVf-VE'></iframe><script>alert(1)</script>", "M7lc1UVf-VE")]
    [InlineData("https://youtube.com.evil.test/embed/M7lc1UVf-VE", null)]
    [InlineData("https://www.youtube.com@evil.test/embed/M7lc1UVf-VE", null)]
    [InlineData("https://www.youtube.com:8443/embed/M7lc1UVf-VE", null)]
    [InlineData("file:///M7lc1UVf-VE", null)]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("https://www.youtube.com/embed/%22bad%22", null)]
    [InlineData("https://vimeo.com/12345", null)]
    public void Only_validated_YouTube_ids_are_retained(string input, string? expected) => Assert.Equal(expected, PptxVideoExtractor.TryYouTube(input)?.Id);

    [Fact]
    public void YouTube_start_and_end_times_are_retained()
    {
        var result = PptxVideoExtractor.TryYouTube("https://youtube.com/embed/M7lc1UVf-VE?start=3&end=8");
        Assert.Equal(3, result!.Value.Start);
        Assert.Equal(8, result.Value.End);
    }

    [Fact]
    public async Task Embedded_video_is_copied_hashed_and_placed_without_changing_the_source()
    {
        var source = CreatePresentation();
        var original = SHA256.HashData(await File.ReadAllBytesAsync(source));
        var inspection = await Inspect(source);
        var result = await new PptxVideoExtractor(Options.Create(new MediaOptions())).ExtractAsync(source, Path.Combine(root, "package"), inspection, CancellationToken.None);
        var video = Assert.Single(result.Slides[1]);
        Assert.Equal("video", video.Type);
        Assert.Equal(.1, video.Placement.X, 6);
        Assert.Equal(.2, video.Placement.Y, 6);
        Assert.Equal(.5, video.Placement.Width, 6);
        Assert.Equal(.4, video.Placement.Height, 6);
        Assert.Equal(Mp4, await File.ReadAllBytesAsync(Path.Combine(root, "package", video.Asset)));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Mp4)).ToLowerInvariant(), video.Sha256);
        Assert.Equal(original, SHA256.HashData(await File.ReadAllBytesAsync(source)));
    }

    [Fact]
    public async Task Hidden_video_slides_are_not_packaged()
    {
        var source = CreatePresentation(hiddenVideo: true);
        var inspection = await Inspect(source);
        Assert.Equal(2, Assert.Single(inspection.Slides).SourceSlideNumber);
        var result = await new PptxVideoExtractor(Options.Create(new MediaOptions())).ExtractAsync(source, Path.Combine(root, "package"), inspection, CancellationToken.None);
        Assert.Empty(result.Slides);
    }

    [Theory]
    [InlineData(".wmv", false)]
    [InlineData(".mp4", true)]
    public async Task Unsupported_or_corrupt_video_retains_static_preview_with_warning(string extension, bool corrupt)
    {
        var source = CreatePresentation(extension: extension, data: corrupt ? new byte[20] : Mp4);
        var result = await new PptxVideoExtractor(Options.Create(new MediaOptions())).ExtractAsync(source, Path.Combine(root, "package"), await Inspect(source), CancellationToken.None);
        Assert.Empty(result.Slides);
        Assert.Contains(result.Warnings, warning => warning.Contains("static preview retained"));
    }

    [Fact]
    public async Task Size_limit_does_not_extract_oversized_video()
    {
        var source = CreatePresentation();
        var result = await new PptxVideoExtractor(Options.Create(new MediaOptions { MaximumEmbeddedVideoBytes = 10 }))
            .ExtractAsync(source, Path.Combine(root, "package"), await Inspect(source), CancellationToken.None);
        Assert.Empty(result.Slides);
        Assert.False(Directory.Exists(Path.Combine(root, "package", "media")));
    }

    [Fact]
    public async Task External_YouTube_is_metadata_only_and_can_be_disabled()
    {
        var source = CreatePresentation(online: "https://youtube.com/embed/M7lc1UVf-VE?start=3&end=8");
        var inspection = await Inspect(source);
        var result = await new PptxVideoExtractor(Options.Create(new MediaOptions())).ExtractAsync(source, Path.Combine(root, "package"), inspection, CancellationToken.None);
        var item = Assert.Single(result.Slides[1]);
        Assert.Equal("youtube", item.Type);
        Assert.Equal("M7lc1UVf-VE", item.YouTubeVideoId);
        Assert.Equal(3, item.Playback.StartSeconds);
        Assert.Equal(8, item.Playback.EndSeconds);
        Assert.False(Directory.Exists(Path.Combine(root, "package", "media")));
        var disabled = await new PptxVideoExtractor(Options.Create(new MediaOptions { EnableYouTube = false }))
            .ExtractAsync(source, Path.Combine(root, "disabled"), inspection, CancellationToken.None);
        Assert.Empty(disabled.Slides);
    }

    [Fact]
    public async Task External_arbitrary_video_is_never_downloaded()
    {
        var source = CreatePresentation(online: "http://127.0.0.1/private.mp4");
        var result = await new PptxVideoExtractor(Options.Create(new MediaOptions())).ExtractAsync(source, Path.Combine(root, "package"), await Inspect(source), CancellationToken.None);
        Assert.Empty(result.Slides);
        Assert.Contains(result.Warnings, warning => warning.Contains("No external URL was fetched"));
    }

    [Fact]
    public async Task Identical_videos_on_two_slides_are_stored_once_and_keep_slide_order()
    {
        var source = CreatePresentation(videoOnSecond: true);
        var result = await new PptxVideoExtractor(Options.Create(new MediaOptions())).ExtractAsync(source, Path.Combine(root, "package"), await Inspect(source), CancellationToken.None);
        Assert.Equal(new[] { 1, 2 }, result.Slides.Keys);
        Assert.Equal(result.Slides[1][0].Asset, result.Slides[2][0].Asset);
        Assert.Single(Directory.GetFiles(Path.Combine(root, "package", "media")));
    }

    private static Task<PresentationInspection> Inspect(string path) => new PptxPackageInspector(Options.Create(new UploadOptions()), Options.Create(new SignageOptions()), Options.Create(new RenderingOptions { OutputWidth = 960 })).InspectAsync(path, CancellationToken.None);

    private string CreatePresentation(string extension = ".mp4", byte[]? data = null, bool hiddenVideo = false, string? online = null, bool videoOnSecond = false)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, Guid.NewGuid().ToString("N") + ".pptx");
        using var document = PresentationDocument.Create(path, PresentationDocumentType.Presentation);
        var presentation = document.AddPresentationPart();
        presentation.Presentation = new Presentation(new SlideIdList(), new SlideSize { Cx = 10_000_000, Cy = 5_000_000 });
        for (var i = 0; i < 2; i++)
        {
            var slide = presentation.AddNewPart<SlidePart>();
            var videoXml = "";
            if (i == 0 || videoOnSecond)
            {
                if (online is null)
                {
                    var media = document.CreateMediaDataPart("video/" + extension.TrimStart('.'), extension);
                    media.FeedData(new MemoryStream(data ?? Mp4));
                    slide.AddVideoReferenceRelationship(media, "video");
                    slide.AddMediaReferenceRelationship(media, "media");
                }
                else slide.AddExternalRelationship("http://schemas.openxmlformats.org/officeDocument/2006/relationships/video", new Uri(online), "video");
                videoXml = """
                    <p:pic><p:nvPicPr><p:cNvPr id="2" name="Video"/><p:cNvPicPr/><p:nvPr><a:videoFile r:link="video"/>
                    </p:nvPr></p:nvPicPr><p:spPr><a:xfrm><a:off x="1000000" y="1000000"/><a:ext cx="5000000" cy="2000000"/></a:xfrm></p:spPr></p:pic>
                    """;
            }
            slide.Slide = new Slide($"<p:sld xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"{(i == 0 && hiddenVideo ? " show=\"false\"" : "")}><p:cSld><p:spTree>{videoXml}</p:spTree></p:cSld></p:sld>");
            presentation.Presentation.SlideIdList!.Append(new SlideId { Id = (uint)(256 + i), RelationshipId = presentation.GetIdOfPart(slide) });
        }
        presentation.Presentation.Save();
        return path;
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
