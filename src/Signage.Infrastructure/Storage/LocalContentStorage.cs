using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Signage.Application;

namespace Signage.Infrastructure.Storage;

public sealed class LocalContentStorage : IContentStorage
{
    private readonly string root;

    public LocalContentStorage(IOptions<StorageOptions> options, IHostEnvironment environment)
    {
        root = Path.GetFullPath(options.Value.RootPath, environment.ContentRootPath);
        Directory.CreateDirectory(Path.Combine(root, "sources"));
        Directory.CreateDirectory(Path.Combine(root, "packages"));
    }

    public async Task<(string Key, string Sha256)> SaveSourceAsync(
        Stream input,
        Guid versionId,
        CancellationToken cancellationToken)
    {
        var key = $"sources/{versionId:N}.pptx";
        var finalPath = Resolve(key);
        var temporaryPath = finalPath + ".uploading";
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (var output = new FileStream(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            1024 * 128,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            var buffer = new byte[1024 * 128];
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            await output.FlushAsync(cancellationToken);
        }

        File.Move(temporaryPath, finalPath);
        return (key, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }

    public string GetSourcePath(string key) => Resolve(key);

    public string GetPackagePath(string contentId)
    {
        ValidateSegment(contentId);
        return Resolve($"packages/{contentId}");
    }

    public Task CommitPackageAsync(string temporaryDirectory, string contentId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var destination = GetPackagePath(contentId);
        if (Directory.Exists(destination))
        {
            return Task.CompletedTask;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        Directory.Move(temporaryDirectory, destination);
        return Task.CompletedTask;
    }

    public async Task<bool> CanWriteAsync(CancellationToken cancellationToken)
    {
        var testPath = Resolve($".write-test-{Guid.NewGuid():N}");
        try
        {
            await File.WriteAllTextAsync(testPath, "ok", cancellationToken);
            File.Delete(testPath);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    public long GetAvailableBytes() => new DriveInfo(Path.GetPathRoot(root)!).AvailableFreeSpace;

    private string Resolve(string key)
    {
        var candidate = Path.GetFullPath(Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Storage key escaped the configured content root.");
        }
        return candidate;
    }

    private static void ValidateSegment(string value)
    {
        if (value.Length is < 16 or > 128 || value.Any(character => !char.IsAsciiHexDigit(character)))
        {
            throw new ArgumentException("Invalid content identifier.", nameof(value));
        }
    }
}
