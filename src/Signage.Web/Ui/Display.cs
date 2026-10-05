using System.Text;
using Signage.Domain;

namespace Signage.Web.Ui;

/// <summary>Formatting shared by the staff-facing pages.</summary>
public static class Display
{
    public static string DateTime(DateTimeOffset value) => value.LocalDateTime.ToString("d MMM yyyy, HH:mm");

    public static string ShortDateTime(DateTimeOffset value) => value.LocalDateTime.ToString("ddd d MMM, HH:mm");

    public static string Ago(DateTimeOffset? value, DateTimeOffset now)
    {
        if (value is null) return "Never";
        var elapsed = now - value.Value;
        if (elapsed < TimeSpan.FromMinutes(1)) return "just now";
        if (elapsed < TimeSpan.FromHours(1)) return $"{(int)elapsed.TotalMinutes} min ago";
        if (elapsed < TimeSpan.FromDays(1)) return $"{(int)elapsed.TotalHours} h ago";
        if (elapsed < TimeSpan.FromDays(7)) return $"{(int)elapsed.TotalDays} d ago";
        return value.Value.LocalDateTime.ToString("d MMM yyyy");
    }

    public static string Duration(long milliseconds)
    {
        var span = TimeSpan.FromMilliseconds(milliseconds);
        if (span.TotalSeconds < 60) return $"{span.TotalSeconds:0.#} s";
        if (span.TotalHours < 1) return span.Seconds == 0 ? $"{span.Minutes} min" : $"{span.Minutes} min {span.Seconds} s";
        if (span.TotalDays < 1) return $"{span.Hours} h {span.Minutes} min";
        return $"{(int)span.TotalDays} d {span.Hours} h";
    }

    /// <summary>Turns "PublisherAccessGranted" into "Publisher access granted".</summary>
    public static string Humanize(string pascalCase)
    {
        var builder = new StringBuilder(pascalCase.Length + 8);
        for (var index = 0; index < pascalCase.Length; index++)
        {
            var character = pascalCase[index];
            if (index > 0 && char.IsUpper(character))
            {
                builder.Append(' ').Append(char.ToLowerInvariant(character));
            }
            else
            {
                builder.Append(character);
            }
        }
        return builder.ToString();
    }

    public static string Initials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var parts = name.Split([' ', '-', '_', '.', '@'], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant(),
            _ => $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[1][0])}"
        };
    }

    /// <summary>Shortens a user-agent string to something like "Edge 140 on Windows".</summary>
    public static string Browser(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return "Browser not reported";
        static string? Version(string agent, string token)
        {
            var start = agent.IndexOf(token, StringComparison.Ordinal);
            if (start < 0) return null;
            start += token.Length;
            var end = start;
            while (end < agent.Length && char.IsDigit(agent[end])) end++;
            return end > start ? agent[start..end] : string.Empty;
        }
        var browser = Version(userAgent, "Edg/") is { } edge ? $"Edge {edge}"
            : Version(userAgent, "Firefox/") is { } firefox ? $"Firefox {firefox}"
            : Version(userAgent, "Chrome/") is { } chrome ? $"Chrome {chrome}"
            : Version(userAgent, "Version/") is { } safari && userAgent.Contains("Safari/", StringComparison.Ordinal) ? $"Safari {safari}"
            : null;
        var system = userAgent.Contains("Windows", StringComparison.Ordinal) ? "Windows"
            : userAgent.Contains("CrOS", StringComparison.Ordinal) ? "ChromeOS"
            : userAgent.Contains("Android", StringComparison.Ordinal) ? "Android"
            : userAgent.Contains("Mac OS X", StringComparison.Ordinal) ? "macOS"
            : userAgent.Contains("Linux", StringComparison.Ordinal) ? "Linux"
            : null;
        return browser is null ? userAgent : system is null ? browser : $"{browser} on {system}";
    }

    public static string StatusLabel(PresentationVersionStatus status) => status switch
    {
        PresentationVersionStatus.Ready => "Ready",
        PresentationVersionStatus.Failed => "Failed",
        PresentationVersionStatus.Archived => "Archived",
        PresentationVersionStatus.Uploaded => "Uploaded",
        PresentationVersionStatus.Queued => "Queued",
        _ => "Preparing"
    };

    /// <summary>CSS tone for a status pill: good, bad, busy or neutral.</summary>
    public static string StatusTone(PresentationVersionStatus status) => status switch
    {
        PresentationVersionStatus.Ready => "good",
        PresentationVersionStatus.Failed => "bad",
        PresentationVersionStatus.Archived => "neutral",
        _ => "busy"
    };

    /// <summary>Filter key used by list pages to group in-progress states together.</summary>
    public static string StatusFilter(PresentationVersionStatus status) => status switch
    {
        PresentationVersionStatus.Ready => "ready",
        PresentationVersionStatus.Failed => "failed",
        PresentationVersionStatus.Archived => "archived",
        _ => "preparing"
    };
}
