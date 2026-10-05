using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.Options;
using Signage.Application;
using Signage.Domain;

namespace Signage.Infrastructure.Conversion;

public sealed class PptxPackageInspector(
    IOptions<UploadOptions> uploadOptions,
    IOptions<SignageOptions> signageOptions,
    IOptions<RenderingOptions> renderingOptions) : IPresentationInspector
{
    public Task<PresentationInspection> InspectAsync(string sourcePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateZip(sourcePath, cancellationToken);

        try
        {
            using var document = PresentationDocument.Open(sourcePath, false);
            var presentationPart = document.PresentationPart
                ?? throw new PresentationRejectedException("INVALID_PPTX", "Missing presentation part.");
            var presentation = presentationPart.Presentation
                ?? throw new PresentationRejectedException("INVALID_PPTX", "Missing presentation XML.");
            var slideIds = presentation.SlideIdList?.Elements<DocumentFormat.OpenXml.Presentation.SlideId>().ToList()
                ?? throw new PresentationRejectedException("INVALID_PPTX", "Missing slide list.");

            if (slideIds.Count > uploadOptions.Value.MaximumSlides)
            {
                throw new PresentationRejectedException("TOO_MANY_SLIDES", $"Slide count {slideIds.Count} exceeds the configured limit.");
            }

            var size = presentation.SlideSize;
            var nativeWidth = size?.Cx?.Value ?? 12_192_000L;
            var nativeHeight = size?.Cy?.Value ?? 6_858_000L;
            var width = renderingOptions.Value.OutputWidth;
            var height = Math.Max(1, (int)Math.Round(width * (double)nativeHeight / nativeWidth));
            var warnings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var fonts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var slides = new List<SlidePlanItem>();

            for (var index = 0; index < slideIds.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relationshipId = slideIds[index].RelationshipId?.Value
                    ?? throw new PresentationRejectedException("INVALID_PPTX", "A slide relationship is missing.");
                var slidePart = (SlidePart)presentationPart.GetPartById(relationshipId);
                var xml = LoadXml(slidePart.GetStream(FileMode.Open, FileAccess.Read));
                var root = xml.Root!;
                var isHidden = root.Attribute("show")?.Value is "0" or "false";
                if (isHidden)
                {
                    continue;
                }

                var transition = root.Descendants().FirstOrDefault(element => element.Name.LocalName == "transition");
                int? sourceDuration = null;
                if (int.TryParse(transition?.Attribute("advTm")?.Value, out var parsedDuration) && parsedDuration > 0)
                {
                    sourceDuration = parsedDuration;
                }

                var duration = SlideTiming.Normalize(
                    sourceDuration,
                    signageOptions.Value.DefaultSlideDurationSeconds * 1000,
                    signageOptions.Value.MinimumSlideDurationSeconds * 1000,
                    signageOptions.Value.MaximumSlideDurationSeconds * 1000);
                var transitionType = transition is not null && transition.Elements().Any() ? "fade" : "cut";
                slides.Add(new SlidePlanItem(index + 1, duration, transitionType));
                if (root.Descendants().Any(element => element.Name.LocalName == "audioFile"))
                    warnings.Add("Embedded audio is not played; videos autoplay muted.");

                foreach (var typeface in root.Descendants().Attributes("typeface").Select(attribute => attribute.Value))
                {
                    if (!string.IsNullOrWhiteSpace(typeface) && !typeface.StartsWith('+'))
                    {
                        fonts.Add(typeface);
                    }
                }
            }

            if (slides.Count == 0)
            {
                throw new PresentationRejectedException("INVALID_PPTX", "The presentation contains no visible slides.");
            }

            if (document.PresentationPart.ExternalRelationships.Any() ||
                document.PresentationPart.Parts.Any(pair => pair.OpenXmlPart.ExternalRelationships.Any()))
            {
                warnings.Add("External relationships are not fetched by the server. Recognised YouTube video references may play online in the browser.");
            }
            if (document.PresentationPart.Parts.Any(pair => pair.OpenXmlPart.Uri.OriginalString.Contains("/embeddings/", StringComparison.OrdinalIgnoreCase)))
            {
                warnings.Add("Embedded OLE content was ignored.");
            }

            return Task.FromResult(new PresentationInspection(width, height, slides, warnings.ToList(), fonts.ToList()));
        }
        catch (PresentationRejectedException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidDataException or OpenXmlPackageException or XmlException)
        {
            throw new PresentationRejectedException("INVALID_PPTX", exception.Message);
        }
    }

    private void ValidateZip(string sourcePath, CancellationToken cancellationToken)
    {
        using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        Span<byte> signature = stackalloc byte[4];
        if (input.Read(signature) != signature.Length || signature[0] != (byte)'P' || signature[1] != (byte)'K')
        {
            throw new PresentationRejectedException("INVALID_PPTX", "The file is not a ZIP-based PowerPoint package.");
        }
        input.Position = 0;
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: false);

        if (archive.Entries.Count > uploadOptions.Value.MaximumZipEntries)
        {
            throw new PresentationRejectedException("ZIP_LIMIT_EXCEEDED", "The package contains too many entries.");
        }

        long uncompressedTotal = 0;
        var hasContentTypes = false;
        var hasPresentation = false;
        var slideCount = 0;
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalized = entry.FullName.Replace('\\', '/');
            if (!paths.Add(normalized)) throw new PresentationRejectedException("INVALID_PPTX", "The package contains duplicate entries.");
            if (normalized.StartsWith('/') || normalized.Split('/').Any(segment => segment == ".."))
            {
                throw new PresentationRejectedException("INVALID_PPTX", "The package contains an unsafe path.");
            }
            uncompressedTotal = checked(uncompressedTotal + entry.Length);
            if (uncompressedTotal > uploadOptions.Value.MaximumUncompressedBytes)
            {
                throw new PresentationRejectedException("ZIP_LIMIT_EXCEEDED", "The expanded package is too large.");
            }
            if (entry.Length > 0)
            {
                var ratio = entry.CompressedLength == 0 ? double.PositiveInfinity : (double)entry.Length / entry.CompressedLength;
                if (ratio > uploadOptions.Value.MaximumCompressionRatio)
                {
                    throw new PresentationRejectedException("ZIP_LIMIT_EXCEEDED", "The package compression ratio is unsafe.");
                }
            }
            hasContentTypes |= normalized.Equals("[Content_Types].xml", StringComparison.OrdinalIgnoreCase);
            hasPresentation |= normalized.Equals("ppt/presentation.xml", StringComparison.OrdinalIgnoreCase);
            if (normalized.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase) && normalized.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                slideCount++;
            }
            if (normalized.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase))
            {
                throw new PresentationRejectedException("INVALID_PPTX", "Macro-enabled PowerPoint packages are not accepted.");
            }
        }

        if (!hasContentTypes || !hasPresentation || slideCount == 0)
        {
            throw new PresentationRejectedException("INVALID_PPTX", "Required PowerPoint package parts are missing.");
        }
    }

    private static XDocument LoadXml(Stream stream)
    {
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 64 * 1024 * 1024
        });
        return XDocument.Load(reader, LoadOptions.None);
    }
}
