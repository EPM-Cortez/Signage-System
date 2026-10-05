using System.ComponentModel.DataAnnotations;

namespace Signage.Application;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";
    public bool EnableWal { get; set; } = true;
    [Range(1, 120)] public int BusyTimeoutSeconds { get; set; } = 5;
    public string InstanceLockPath { get; set; } = "./.local-data/signage.lock";
}

public sealed class SignageOptions
{
    public const string SectionName = "Signage";
    [Required] public string SiteName { get; set; } = "School Signage";
    [Required] public string TimeZone { get; set; } = "Europe/London";
    [Range(1, 120)] public int DefaultSlideDurationSeconds { get; set; } = 10;
    [Range(1, 120)] public int MinimumSlideDurationSeconds { get; set; } = 2;
    [Range(2, 600)] public int MaximumSlideDurationSeconds { get; set; } = 120;
    [Range(10, 600)] public int PlayerPollSeconds { get; set; } = 60;
    [Range(10, 600)] public int HeartbeatSeconds { get; set; } = 60;
    [Range(30, 3600)] public int OfflineAfterSeconds { get; set; } = 180;
}

public sealed class UploadOptions
{
    public const string SectionName = "Uploads";
    [Range(1, long.MaxValue)] public long MaximumBytes { get; set; } = 250L * 1024 * 1024;
    [Range(1, 5000)] public int MaximumSlides { get; set; } = 300;
    [Range(1, 100_000)] public int MaximumZipEntries { get; set; } = 10_000;
    [Range(1, long.MaxValue)] public long MaximumUncompressedBytes { get; set; } = 1024L * 1024 * 1024;
    [Range(1, 100_000)] public double MaximumCompressionRatio { get; set; } = 200;
}

public sealed class RenderingOptions
{
    public const string SectionName = "Rendering";
    [Required] public string NodeExecutable { get; set; } = "node";
    [Required] public string ConverterEntryPoint { get; set; } = "../Signage.Converter/dist/cli.js";
    [Required] public string WorkingDirectory { get; set; } = "../Signage.Converter";
    [Required] public string TempRoot { get; set; } = "./.local-data/temp";
    public string[] FontDirectories { get; set; } = [];
    public bool UseSystemFonts { get; set; }
    public bool FitTextToBox { get; set; } = true;
    public Dictionary<string, string> FontMapping { get; set; } = new();
    [Range(320, 7680)] public int OutputWidth { get; set; } = 1920;
    [Range(10, 3600)] public int TimeoutSeconds { get; set; } = 300;
    [Range(1, 10)] public int MaximumAttempts { get; set; } = 3;
    [Range(1, 4)] public int Concurrency { get; set; } = 1;
    [Range(4096, 16_777_216)] public int MaximumCapturedOutputBytes { get; set; } = 1_048_576;
}

public sealed class StorageOptions
{
    public const string SectionName = "Storage";
    [Required] public string RootPath { get; set; } = "./.local-data/content";
    [Range(1, 100)] public int RetainedVersionsPerPresentation { get; set; } = 5;
    [Range(0, long.MaxValue)] public long MinimumFreeBytes { get; set; } = 512L * 1024 * 1024;
    public bool CleanupDryRun { get; set; }
    [Range(5, 10_080)] public int CleanupIntervalMinutes { get; set; } = 360;
    [Range(1, 720)] public int TemporaryFileMaximumAgeHours { get; set; } = 24;
}

public sealed class MediaOptions
{
    public const string SectionName = "Media";
    public bool EnableYouTube { get; set; } = true;
    [Range(1, long.MaxValue)] public long MaximumEmbeddedVideoBytes { get; set; } = 250L * 1024 * 1024;
    [Range(2, 120)] public int StartupTimeoutSeconds { get; set; } = 20;
    [Range(5, 300)] public int StallTimeoutSeconds { get; set; } = 30;
    [Range(10, 14400)] public int MaximumPlaybackSeconds { get; set; } = 3600;
}

public sealed class BackupOptions
{
    public const string SectionName = "Backup";
    [Required] public string RootPath { get; set; } = "../../backups";
}

public sealed class AuthenticationOptions
{
    public const string SectionName = "Authentication";
    [Required] public string Mode { get; set; } = "Development";
    public string? Authority { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string TeacherRoleClaim { get; set; } = "Teacher";
    public string AdminRoleClaim { get; set; } = "SignageAdmin";
    [Range(15, 720)] public int SessionMinutes { get; set; } = 480;
}

public sealed class ActiveDirectoryOptions
{
    public const string SectionName = "ActiveDirectory";
    public string Host { get; set; } = string.Empty;
    [Range(1, 65535)] public int Port { get; set; } = 636;
    public string DomainName { get; set; } = string.Empty;
    public string UpnSuffix { get; set; } = string.Empty;
    public string NetBiosDomain { get; set; } = string.Empty;
    public string BaseDn { get; set; } = string.Empty;
    public string BootstrapAdminUpn { get; set; } = string.Empty;
    [Range(2, 30)] public int TimeoutSeconds { get; set; } = 10;

    public bool IsConfigured => Uri.CheckHostName(Host) == UriHostNameType.Dns
        && Uri.CheckHostName(DomainName) == UriHostNameType.Dns
        && (string.IsNullOrEmpty(UpnSuffix) || Uri.CheckHostName(UpnSuffix) == UriHostNameType.Dns)
        && !string.IsNullOrWhiteSpace(BaseDn);
    public string LoginSuffix => string.IsNullOrWhiteSpace(UpnSuffix) ? DomainName : UpnSuffix;
}
