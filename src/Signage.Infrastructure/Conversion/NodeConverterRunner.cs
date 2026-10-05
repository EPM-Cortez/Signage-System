using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Signage.Application;

namespace Signage.Infrastructure.Conversion;

public sealed class NodeConverterRunner : IConverterRunner
{
    private readonly RenderingOptions options;
    private readonly string nodeExecutable;
    private readonly string converterEntryPoint;
    private readonly string workingDirectory;

    public NodeConverterRunner(IOptions<RenderingOptions> options, IHostEnvironment environment)
    {
        this.options = options.Value;
        nodeExecutable = ResolveExecutable(this.options.NodeExecutable, environment.ContentRootPath);
        converterEntryPoint = Path.GetFullPath(this.options.ConverterEntryPoint, environment.ContentRootPath);
        workingDirectory = Path.GetFullPath(this.options.WorkingDirectory, environment.ContentRootPath);
    }

    public Task<ConverterResult> RenderAsync(
        string sourcePath,
        string outputDirectory,
        string settingsPath,
        CancellationToken cancellationToken) =>
        RunAsync([converterEntryPoint, "render", "--input", sourcePath, "--output", outputDirectory, "--settings", settingsPath], cancellationToken);

    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await RunAsync([converterEntryPoint, "health"], cancellationToken);
            return result.ExitCode == 0;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            return false;
        }
    }

    private async Task<ConverterResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = nodeExecutable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        var stopwatch = Stopwatch.StartNew();
        if (!process.Start())
        {
            throw new InvalidOperationException("The converter process could not be started.");
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(options.TimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var stdoutTask = ReadBoundedAsync(process.StandardOutput, options.MaximumCapturedOutputBytes, linked.Token);
        var stderrTask = ReadBoundedAsync(process.StandardError, options.MaximumCapturedOutputBytes, linked.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        stopwatch.Stop();
        return new ConverterResult(process.ExitCode, stopwatch.Elapsed, stdout, stderr, "pptx-glimpse/3.2.8");
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, int maximumBytes, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder(Math.Min(maximumBytes, 16_384));
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (builder.Length < maximumBytes)
            {
                builder.Append(buffer, 0, Math.Min(count, maximumBytes - builder.Length));
            }
        }
        return builder.ToString();
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static string ResolveExecutable(string configured, string contentRoot)
    {
        if (configured.Contains(Path.DirectorySeparatorChar) || configured.Contains(Path.AltDirectorySeparatorChar))
        {
            return Path.GetFullPath(configured, contentRoot);
        }
        return configured;
    }
}
