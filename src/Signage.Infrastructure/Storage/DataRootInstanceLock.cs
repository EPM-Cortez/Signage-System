using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Signage.Application;

namespace Signage.Infrastructure.Storage;

public sealed class DataRootInstanceLock : IDisposable
{
    private readonly FileStream stream;

    public DataRootInstanceLock(IOptions<DatabaseOptions> options, IHostEnvironment? environment = null)
    {
        var path = Path.GetFullPath(options.Value.InstanceLockPath, environment?.ContentRootPath ?? Directory.GetCurrentDirectory());
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            stream.SetLength(0);
            using var writer = new StreamWriter(stream, leaveOpen: true);
            writer.Write($"{Environment.ProcessId}|{DateTimeOffset.UtcNow:O}");
            writer.Flush();
            stream.Flush(true);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException(
                $"Another School Signage process is already using this data root. Lock: {path}", exception);
        }
    }

    public void Dispose() => stream.Dispose();
}
