using System.Text.Json;

namespace Engineering.Safety;

/// <summary>Bounded local-only backup/audit store. It never uses proposal fields in filenames.</summary>
public sealed class FileEngineeringStore : IEngineeringStore
{
    private const int MaxRecordBytes = 2 * 1024 * 1024;
    private const int MaxFiles = 512;
    private readonly string _root;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { WriteIndented = false };
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileEngineeringStore(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory)) throw new ArgumentException("storage directory is required", nameof(rootDirectory));
        _root = Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(_root);
        TrySetPrivateDirectory(_root);
    }

    public async Task PersistBackupAsync(BackupRecord backup, CancellationToken cancellationToken)
    {
        await PersistAsync("backup", backup, cancellationToken).ConfigureAwait(false);
    }

    public async Task AppendAuditAsync(AuditRecord audit, CancellationToken cancellationToken)
    {
        await PersistAsync("audit", audit, cancellationToken).ConfigureAwait(false);
    }

    private async Task PersistAsync<T>(string prefix, T value, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, _json);
        if (bytes.Length > MaxRecordBytes) throw new IOException("engineering record exceeds the local storage bound");
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var files = Directory.EnumerateFiles(_root, $"{prefix}-*.json", SearchOption.TopDirectoryOnly).Take(MaxFiles + 1).Count();
            if (files >= MaxFiles) throw new IOException("engineering local storage file bound reached");
            var path = Path.Combine(_root, $"{prefix}-{Guid.NewGuid():N}.json");
            await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough | FileOptions.Asynchronous);
            TrySetPrivateFile(path);
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private static void TrySetPrivateDirectory(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            try { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
            catch (PlatformNotSupportedException) { }
        }
    }

    private static void TrySetPrivateFile(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            try { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
            catch (PlatformNotSupportedException) { }
        }
    }
}
