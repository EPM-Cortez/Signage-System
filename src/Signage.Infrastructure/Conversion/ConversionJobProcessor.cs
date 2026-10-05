using System.Buffers.Binary;
using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Signage.Application;
using Signage.Domain;
using Signage.Infrastructure.Persistence;

namespace Signage.Infrastructure.Conversion;

public sealed class ConversionJobProcessor(
    IDbContextFactory<SignageDbContext> dbFactory,
    IPresentationInspector inspector,
    IConverterRunner converter,
    PptxVideoExtractor videoExtractor,
    IContentStorage storage,
    IOptions<RenderingOptions> renderingOptions,
    IOptions<StorageOptions> storageOptions,
    IHostEnvironment environment,
    TimeProvider timeProvider,
    ILogger<ConversionJobProcessor> logger)
{
    private static readonly SemaphoreSlim ClaimLock = new(1, 1);
    private readonly RenderingOptions options = renderingOptions.Value;
    private readonly StorageOptions storageSettings = storageOptions.Value;

    public async Task<bool> ProcessNextAsync(string leaseOwner, CancellationToken cancellationToken)
    {
        var jobId = await ClaimAsync(leaseOwner, cancellationToken);
        if (jobId is null)
        {
            return false;
        }

        await ProcessAsync(jobId.Value, cancellationToken);
        return true;
    }

    private async Task<Guid?> ClaimAsync(string leaseOwner, CancellationToken cancellationToken)
    {
        await ClaimLock.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var now = timeProvider.GetUtcNow();
            var job = await db.ConversionJobs
                .Where(item =>
                    (item.Status == ConversionJobStatus.Queued && item.AvailableUtc <= now) ||
                    (item.Status == ConversionJobStatus.Processing && item.LeaseExpiresUtc < now))
                .OrderBy(item => item.AvailableUtc)
                .ThenBy(item => item.CreatedUtc)
                .FirstOrDefaultAsync(cancellationToken);

            if (job is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return null;
            }

            job.Status = ConversionJobStatus.Processing;
            job.AttemptCount++;
            job.LeaseOwner = leaseOwner;
            job.LeaseExpiresUtc = now.AddSeconds(options.TimeoutSeconds + 30);
            var version = await db.PresentationVersions.SingleAsync(item => item.Id == job.PresentationVersionId, cancellationToken);
            version.Status = PresentationVersionStatus.Converting;
            version.ConcurrencyToken++;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation("Claimed conversion job {JobId} attempt {AttemptCount}", job.Id, job.AttemptCount);
            return job.Id;
        }
        finally
        {
            ClaimLock.Release();
        }
    }

    private async Task ProcessAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var tempRoot = Path.GetFullPath(options.TempRoot, environment.ContentRootPath);
        var attemptDirectory = Path.Combine(tempRoot, $"attempt-{jobId:N}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(attemptDirectory);

        try
        {
            if (storage.GetAvailableBytes() < storageSettings.MinimumFreeBytes)
            {
                throw new IOException("Conversion paused because content storage is below the configured free-space threshold.");
            }
            await using var loadDb = await dbFactory.CreateDbContextAsync(cancellationToken);
            var job = await loadDb.ConversionJobs.AsNoTracking().SingleAsync(item => item.Id == jobId, cancellationToken);
            var version = await loadDb.PresentationVersions.AsNoTracking().SingleAsync(item => item.Id == job.PresentationVersionId, cancellationToken);
            var sourcePath = storage.GetSourcePath(version.SourceStorageKey);
            var inspection = await inspector.InspectAsync(sourcePath, cancellationToken);
            var outputDirectory = Path.Combine(attemptDirectory, "rendered");
            Directory.CreateDirectory(outputDirectory);
            var settingsPath = Path.Combine(attemptDirectory, "settings.json");
            await File.WriteAllTextAsync(settingsPath, JsonSerializer.Serialize(new
            {
                outputWidth = options.OutputWidth,
                fontDirectories = options.FontDirectories.Select(path => Path.GetFullPath(path, environment.ContentRootPath)).ToArray(),
                useSystemFonts = options.UseSystemFonts,
                fitTextToBox = options.FitTextToBox,
                fontMapping = options.FontMapping,
                requestedSourceSlideNumbers = inspection.Slides.Select(item => item.SourceSlideNumber).ToArray(),
                timeoutSeconds = options.TimeoutSeconds,
                diagnosticVerbosity = "normal"
            }), cancellationToken);

            var result = await converter.RenderAsync(sourcePath, outputDirectory, settingsPath, cancellationToken);
            if (result.ExitCode != 0)
            {
                throw new ConverterProcessException($"Converter exited with code {result.ExitCode}: {result.StandardError}");
            }

            var packageDirectory = Path.Combine(attemptDirectory, "package");
            var packageSlides = Path.Combine(packageDirectory, "slides");
            Directory.CreateDirectory(packageSlides);
            var extracted = await videoExtractor.ExtractAsync(sourcePath, packageDirectory, inspection, cancellationToken);
            inspection = inspection with { Warnings = inspection.Warnings.Concat(extracted.Warnings).Distinct().ToArray() };
            var manifestItems = new List<ManifestItem>(inspection.Slides.Count);
            for (var index = 0; index < inspection.Slides.Count; index++)
            {
                var plan = inspection.Slides[index];
                var fileName = $"slide-{index + 1:D4}.png";
                var renderedPath = Path.Combine(outputDirectory, "slides", fileName);
                ValidatePng(renderedPath, inspection.CanvasWidth, inspection.CanvasHeight);
                var packagePath = Path.Combine(packageSlides, fileName);
                File.Copy(renderedPath, packagePath);
                var hash = await CalculateFileHashAsync(packagePath, cancellationToken);
                var transition = new ManifestTransition(plan.Transition, plan.Transition == "fade" ? 500 : 0);
                if (extracted.Slides.TryGetValue(plan.SourceSlideNumber, out var videos))
                {
                    foreach (var video in videos)
                    {
                        manifestItems.Add(new ManifestItem(video.Type, plan.SourceSlideNumber,
                            video.Type == "youtube" ? $"slides/{fileName}" : video.Asset,
                            video.Type == "youtube" ? hash : video.Sha256, plan.DurationMs, transition,
                            new ManifestAsset($"slides/{fileName}", hash), video.Placement, video.Playback, video.YouTubeVideoId));
                    }
                    continue;
                }
                manifestItems.Add(new ManifestItem(
                    "image",
                    plan.SourceSlideNumber,
                    $"slides/{fileName}",
                    hash,
                    plan.DurationMs,
                    transition));
            }

            var canonical = new
            {
                schemaVersion = extracted.Slides.Count > 0 ? 2 : 1,
                presentationVersionId = version.Id,
                canvas = new { width = inspection.CanvasWidth, height = inspection.CanvasHeight },
                items = manifestItems
            };
            var contentId = TokenUtility.CalculateContentId(canonical);
            var manifest = new ContentManifest(
                extracted.Slides.Count > 0 ? 2 : 1,
                contentId,
                version.Id,
                timeProvider.GetUtcNow(),
                true,
                new ManifestCanvas(inspection.CanvasWidth, inspection.CanvasHeight),
                manifestItems);
            var manifestPath = Path.Combine(packageDirectory, "manifest.json");
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), cancellationToken);
            await storage.CommitPackageAsync(packageDirectory, contentId, cancellationToken);

            await CompleteAsync(jobId, inspection, result, contentId, manifestItems.Sum(item => (long)item.DurationMs), cancellationToken);
        }
        catch (PresentationRejectedException exception)
        {
            await FailAsync(jobId, exception.Code, exception.Message, deterministic: true, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await FailAsync(jobId, "CONVERTER_TIMEOUT", "The renderer exceeded its time limit.", deterministic: false, CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ConverterProcessException or JsonException)
        {
            await FailAsync(jobId, "CONVERTER_FAILED", exception.Message, deterministic: false, cancellationToken);
        }
        finally
        {
            try
            {
                if (Directory.Exists(attemptDirectory))
                {
                    Directory.Delete(attemptDirectory, recursive: true);
                }
            }
            catch (IOException exception)
            {
                logger.LogWarning(exception, "Could not remove conversion temporary directory {Directory}", attemptDirectory);
            }
        }
    }

    private async Task CompleteAsync(
        Guid jobId,
        PresentationInspection inspection,
        ConverterResult result,
        string contentId,
        long totalDurationMs,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var job = await db.ConversionJobs.SingleAsync(item => item.Id == jobId, cancellationToken);
        var version = await db.PresentationVersions
            .Include(item => item.PublishTargets)
            .SingleAsync(item => item.Id == job.PresentationVersionId, cancellationToken);
        var presentation = await db.Presentations.SingleAsync(item => item.Id == version.PresentationId, cancellationToken);
        var now = timeProvider.GetUtcNow();

        version.Status = PresentationVersionStatus.Ready;
        version.ContentId = contentId;
        version.ManifestStorageKey = $"packages/{contentId}/manifest.json";
        version.SlideCount = inspection.Slides.Count;
        version.TotalDurationMs = totalDurationMs;
        version.ReadyUtc = now;
        version.FailureCode = null;
        version.FailureDetail = null;
        version.DiagnosticsJson = JsonSerializer.Serialize(new
        {
            warnings = inspection.Warnings,
            fonts = inspection.Fonts,
            renderer = result.RendererVersion,
            exitCode = result.ExitCode,
            elapsedMs = (long)result.Elapsed.TotalMilliseconds,
            standardOutput = result.StandardOutput
        });
        version.ConcurrencyToken++;
        presentation.CurrentVersionId = version.Id;
        job.Status = ConversionJobStatus.Completed;
        job.CompletedUtc = now;
        job.LeaseOwner = null;
        job.LeaseExpiresUtc = null;

        // Queued conversion must not publish after the uploader loses access.
        var allowedGroups = await StaffPermissions.AllowedGroupsAsync(db, presentation.CreatedBySubject, cancellationToken);
        var requestedTargets = version.PublishTargets.Where(item => item.PublishWhenReady).ToList();
        foreach (var target in requestedTargets.Where(item => allowedGroups.Contains(item.ScreenGroupId)))
        {
            db.Publications.Add(new Publication
            {
                Id = Guid.NewGuid(),
                ScreenGroupId = target.ScreenGroupId,
                PresentationVersionId = version.Id,
                StartsUtc = target.StartsUtc,
                EndsUtc = target.EndsUtc,
                Priority = target.Priority,
                IsEnabled = true,
                PublishedBySubject = presentation.CreatedBySubject,
                PublishedUtc = now
            });
        }

        if (requestedTargets.Any(item => !allowedGroups.Contains(item.ScreenGroupId)))
            db.AuditEvents.Add(new AuditEvent
            {
                Id = Guid.NewGuid(), OccurredUtc = now, ActorType = "System", ActorId = "conversion-worker",
                Action = "PublicationSkippedAccessRevoked", EntityType = nameof(PresentationVersion), EntityId = version.Id.ToString(),
                Summary = "Some publish targets were skipped because staff or screen-group access changed during conversion."
            });

        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            OccurredUtc = now,
            ActorType = "System",
            ActorId = "conversion-worker",
            Action = "PresentationReady",
            EntityType = nameof(PresentationVersion),
            EntityId = version.Id.ToString(),
            Summary = $"Prepared {version.OriginalFileName} with {version.SlideCount} slides."
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Completed conversion job {JobId} as content {ContentId}", jobId, contentId);
    }

    private async Task FailAsync(
        Guid jobId,
        string failureCode,
        string detail,
        bool deterministic,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var job = await db.ConversionJobs.SingleAsync(item => item.Id == jobId, cancellationToken);
        var version = await db.PresentationVersions.SingleAsync(item => item.Id == job.PresentationVersionId, cancellationToken);
        var canRetry = !deterministic && job.AttemptCount < options.MaximumAttempts;
        job.Status = canRetry ? ConversionJobStatus.Queued : ConversionJobStatus.Failed;
        job.AvailableUtc = timeProvider.GetUtcNow().AddSeconds(Math.Pow(2, job.AttemptCount) * 5);
        job.LeaseOwner = null;
        job.LeaseExpiresUtc = null;
        job.LastError = Truncate(detail, 4000);
        job.CompletedUtc = canRetry ? null : timeProvider.GetUtcNow();
        version.Status = canRetry ? PresentationVersionStatus.Queued : PresentationVersionStatus.Failed;
        version.FailureCode = failureCode;
        version.FailureDetail = Truncate(detail, 8000);
        version.ConcurrencyToken++;
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            OccurredUtc = timeProvider.GetUtcNow(),
            ActorType = "System",
            ActorId = "conversion-worker",
            Action = canRetry ? "ConversionRetryScheduled" : "ConversionFailed",
            EntityType = nameof(PresentationVersion),
            EntityId = version.Id.ToString(),
            Summary = canRetry ? $"Scheduled retry {job.AttemptCount + 1}." : $"Preparation failed with {failureCode}."
        });
        await db.SaveChangesAsync(cancellationToken);
        logger.LogWarning("Conversion job {JobId} failed with {FailureCode}; retry={CanRetry}", jobId, failureCode, canRetry);
    }

    private static void ValidatePng(string path, int expectedWidth, int expectedHeight)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length is < 33 or > 100 * 1024 * 1024)
        {
            throw new InvalidDataException("A rendered slide is missing or has an invalid size.");
        }

        Span<byte> header = stackalloc byte[24];
        using var stream = info.OpenRead();
        if (stream.Read(header) != header.Length ||
            !header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            throw new InvalidDataException("Converter output is not a PNG image.");
        }
        var width = BinaryPrimitives.ReadInt32BigEndian(header[16..20]);
        var height = BinaryPrimitives.ReadInt32BigEndian(header[20..24]);
        if (width != expectedWidth || height != expectedHeight)
        {
            throw new InvalidDataException($"Rendered slide dimensions {width}x{height} do not match {expectedWidth}x{expectedHeight}.");
        }
    }

    private static async Task<string> CalculateFileHashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    private static string Truncate(string value, int maximum) => value.Length <= maximum ? value : value[..maximum];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private sealed class ConverterProcessException(string message) : Exception(message);
}
