using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Signage.Application;

namespace Signage.Infrastructure.Conversion;

public sealed class NodeConverterRunner : IConverterRunner
{
    // Progress events are short JSON lines; anything longer is not one of ours and fails to parse.
    private const int MaximumProgressLineLength = 4096;

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
        Action<int>? slideRendered,
        CancellationToken cancellationToken) =>
        RunAsync(
            [converterEntryPoint, "render", "--input", sourcePath, "--output", outputDirectory, "--settings", settingsPath],
            slideRendered is null ? null : line => ReportSlideRendered(line, slideRendered),
            cancellationToken);

    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await RunAsync([converterEntryPoint, "health"], null, cancellationToken);
            return result.ExitCode == 0;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            return false;
        }
    }

    private async Task<ConverterResult> RunAsync(IReadOnlyList<string> arguments, Action<string>? standardOutputLine, CancellationToken cancellationToken)
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
        var stdoutTask = ReadBoundedAsync(process.StandardOutput, options.MaximumCapturedOutputBytes, standardOutputLine, linked.Token);
        var stderrTask = ReadBoundedAsync(process.StandardError, options.MaximumCapturedOutputBytes, null, linked.Token);
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

    private static async Task<string> ReadBoundedAsync(StreamReader reader, int maximumBytes, Action<string>? onLine, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder(Math.Min(maximumBytes, 16_384));
        var line = new StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (builder.Length < maximumBytes)
            {
                builder.Append(buffer, 0, Math.Min(count, maximumBytes - builder.Length));
            }
            if (onLine is null)
            {
                continue;
            }
            for (var index = 0; index < count; index++)
            {
                if (buffer[index] == '\n')
                {
                    onLine(line.ToString());
                    line.Clear();
                }
                else if (line.Length < MaximumProgressLineLength)
                {
                    line.Append(buffer[index]);
                }
            }
        }
        return builder.ToString();
    }

    private static void ReportSlideRendered(string line, Action<int> slideRendered)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("event", out var name) && name.ValueEquals("slide-rasterized") &&
                root.TryGetProperty("index", out var index) && index.TryGetInt32(out var rendered))
            {
                slideRendered(rendered);
            }
        }
        catch (JsonException)
        {
        }
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
