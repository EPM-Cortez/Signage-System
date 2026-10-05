namespace Signage.IntegrationTests;

internal static class TestDataCleanup
{
    public static void DeleteDirectory(string root)
    {
        var absolute = Path.GetFullPath(root);
        var temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!absolute.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(absolute).StartsWith("signage-", StringComparison.Ordinal))
            throw new InvalidOperationException("Refusing to remove a directory outside the isolated signage test data.");
        // Entry-point/host disposal can release Windows handles just after the
        // test host stops. Retry only cleanup, never a failing test operation.
        for (var attempt = 0; ; attempt++)
        {
            try { if (Directory.Exists(absolute)) Directory.Delete(absolute, recursive: true); return; }
            catch (IOException) when (attempt < 19) { Thread.Sleep(50); }
        }
    }
}
