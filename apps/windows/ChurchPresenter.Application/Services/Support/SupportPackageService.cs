using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;


namespace ChurchPresenter.Services.Support;

/// <inheritdoc />
public sealed class SupportPackageService(
    IContentDirectoryService paths,
    IContentStore? contentStore = null,
    IContentChangeBus? contentChanges = null) : ISupportPackageService
{
    private const string PackageFormatVersion = "1.0.0";
    private const string SupportPackageType = "support";
    private const string ConfigurationPrefix = "Configurations/";
    private const string MachineStatePrefix = "MachineState/";

    private readonly IContentDirectoryService _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly IContentStore _contentStore = contentStore ?? ContentStoreDefaults.Instance;
    private readonly IContentChangeBus? _contentChanges = contentChanges;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <inheritdoc />
    public async Task ExportAsync(string destinationPath, SupportPackageExportOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        await _paths.EnsureDocumentsLayoutAsync().ConfigureAwait(false);

        var fullPath = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        var configDirectory = _paths.GetConfigurationsDirectory();
        var supportFiles = Directory.Exists(configDirectory)
            ? Directory.EnumerateFiles(configDirectory, "*.json", SearchOption.TopDirectoryOnly)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList()
            : [];

        var manifest = new SupportPackageManifest
        {
            FormatVersion = PackageFormatVersion,
            PackageType = SupportPackageType,
            ExportedAt = DateTimeOffset.UtcNow.ToString("O"),
            ReplaceMissingFiles = options?.ReplaceMissingFilesOnImport ?? false,
            Files = supportFiles
                .Select(file => new SupportPackageFileManifest
                {
                    Path = ToPackageConfigurationPath(file),
                    Sha256 = ComputeSha256(file),
                })
                .ToList(),
        };

        using var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using var archive = new ZipArchive(fileStream, ZipArchiveMode.Create, leaveOpen: false);
        WriteStringEntry(archive, "manifest.json", JsonSerializer.Serialize(manifest, JsonOptions));

        foreach (var file in supportFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = archive.CreateEntry(ToPackageConfigurationPath(file), CompressionLevel.Optimal);
            await using var source = File.OpenRead(file);
            await using var target = entry.Open();
            await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public Task<SupportPackagePreview> PreviewImportAsync(string packagePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        using var archive = ZipFile.OpenRead(packagePath);
        var manifest = ReadManifest(archive);
        ValidateManifest(manifest);
        var changes = BuildPreviewChanges(archive, manifest, cancellationToken);

        return Task.FromResult(new SupportPackagePreview
        {
            PackageType = manifest.PackageType,
            ReplaceMissingFiles = manifest.ReplaceMissingFiles,
            Changes = changes,
            PackageStamp = _contentStore.GetStamp(packagePath, includeHash: true).Value,
        });
    }

    /// <inheritdoc />
    public async Task<SupportPackagePreview> ImportAsync(string packagePath, SupportPackageImportOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        SupportPackagePreview preview = await PreviewImportAsync(packagePath, cancellationToken).ConfigureAwait(false);
        if (preview.HasDestructiveChanges && options?.AllowDestructiveReplace != true)
            throw new InvalidOperationException("Support package import would replace or delete local support files. Preview the changes and enable destructive replace to continue.");

        await _paths.EnsureDocumentsLayoutAsync().ConfigureAwait(false);
        var revalidated = await PreviewImportAsync(packagePath, cancellationToken).ConfigureAwait(false);
        EnsurePreviewStillCurrent(preview, revalidated);
        using var archive = ZipFile.OpenRead(packagePath);
        var manifest = ReadManifest(archive);
        ValidateManifest(manifest);

        foreach (var file in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = GetRequiredEntry(archive, file.Path);
            var destinationPath = ResolveConfigurationDestination(file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

            await using var source = entry.Open();
            await using var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        }

        if (manifest.ReplaceMissingFiles)
        {
            var packagePaths = manifest.Files.Select(static file => file.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var currentFile in EnumerateCurrentConfigurationFiles())
            {
                var currentPackagePath = ToPackageConfigurationPath(currentFile);
                if (!packagePaths.Contains(currentPackagePath))
                    File.Delete(currentFile);
            }
        }

        _contentChanges?.Publish(new ContentChangeEvent
        {
            Kind = ContentChangeKind.PackageImportCompleted,
            SubjectId = packagePath,
            Stamp = preview.PackageStamp,
            Source = nameof(SupportPackageService),
        });

        return preview;
    }

    private List<SupportPackagePreviewChange> BuildPreviewChanges(
        ZipArchive archive,
        SupportPackageManifest manifest,
        CancellationToken cancellationToken)
    {
        var changes = new List<SupportPackagePreviewChange>();
        var packageFiles = manifest.Files.Select(static file => file.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = NormalizePackagePath(entry.FullName);
            if (path.StartsWith(MachineStatePrefix, StringComparison.OrdinalIgnoreCase))
            {
                changes.Add(new SupportPackagePreviewChange
                {
                    Kind = SupportPackageChangeKind.Warning,
                    Path = path,
                    Message = "Machine-local state is not portable and will not be imported.",
                    IsDestructive = false,
                });
            }
        }

        foreach (var file in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destinationPath = ResolveConfigurationDestination(file.Path);
            var packageHash = ReadEntrySha256(archive, file.Path);

            if (!File.Exists(destinationPath))
            {
                changes.Add(CreateChange(SupportPackageChangeKind.Add, file.Path, "Will add support file.", isDestructive: false, destinationPath));
                continue;
            }

            var currentHash = ComputeSha256(destinationPath);
            changes.Add(string.Equals(currentHash, packageHash, StringComparison.OrdinalIgnoreCase)
                ? CreateChange(SupportPackageChangeKind.Unchanged, file.Path, "Already matches package.", isDestructive: false, destinationPath)
                : CreateChange(SupportPackageChangeKind.Replace, file.Path, "Will replace local support file.", isDestructive: true, destinationPath));
        }

        if (manifest.ReplaceMissingFiles)
        {
            foreach (var currentFile in EnumerateCurrentConfigurationFiles())
            {
                var packagePath = ToPackageConfigurationPath(currentFile);
                if (!packageFiles.Contains(packagePath))
                    changes.Add(CreateChange(SupportPackageChangeKind.Delete, packagePath, "Will delete local support file missing from sync snapshot.", isDestructive: true, currentFile));
            }
        }

        return changes;
    }

    private IEnumerable<string> EnumerateCurrentConfigurationFiles()
    {
        var configDirectory = _paths.GetConfigurationsDirectory();
        return Directory.Exists(configDirectory)
            ? Directory.EnumerateFiles(configDirectory, "*.json", SearchOption.TopDirectoryOnly)
            : [];
    }

    private string ResolveConfigurationDestination(string packagePath)
    {
        var normalized = NormalizePackagePath(packagePath);
        if (!normalized.StartsWith(ConfigurationPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Support package file '{packagePath}' is outside the portable configuration boundary.");

        var fileName = Path.GetFileName(normalized);
        if (string.IsNullOrWhiteSpace(fileName) || !fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Support package file '{packagePath}' is not a supported JSON configuration file.");

        return Path.Combine(_paths.GetConfigurationsDirectory(), fileName);
    }

    private string ToPackageConfigurationPath(string filePath)
    {
        var relative = Path.GetRelativePath(_paths.GetConfigurationsDirectory(), Path.GetFullPath(filePath));
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new InvalidDataException($"Support file '{filePath}' is outside the portable configuration boundary.");

        return ConfigurationPrefix + relative.Replace('\\', '/');
    }

    private SupportPackagePreviewChange CreateChange(
        SupportPackageChangeKind kind,
        string path,
        string message,
        bool isDestructive,
        string? destinationPath = null) =>
        new()
        {
            Kind = kind,
            Path = path,
            Message = message,
            IsDestructive = isDestructive,
            RequiresConfirmation = isDestructive,
            DestinationStamp = string.IsNullOrWhiteSpace(destinationPath)
                ? null
                : _contentStore.GetStamp(destinationPath).Value,
        };

    private static void EnsurePreviewStillCurrent(SupportPackagePreview expected, SupportPackagePreview actual)
    {
        var expectedSignature = BuildPreviewSignature(expected.Changes);
        var actualSignature = BuildPreviewSignature(actual.Changes);
        if (!string.Equals(expectedSignature, actualSignature, StringComparison.Ordinal))
            throw new InvalidOperationException("Support package import preview is stale. Reopen the preview and try again.");
    }

    private static string BuildPreviewSignature(IEnumerable<SupportPackagePreviewChange> changes) =>
        string.Join(
            "|",
            changes
                .OrderBy(static change => change.Path, StringComparer.OrdinalIgnoreCase)
                .Select(static change => $"{change.Kind}:{change.Path}:{change.DestinationStamp?.LastWriteTimeUtc?.Ticks}:{change.DestinationStamp?.Length}:{change.DestinationStamp?.Sha256}"));

    private static SupportPackageManifest ReadManifest(ZipArchive archive)
    {
        var entry = GetRequiredEntry(archive, "manifest.json");
        using var stream = entry.Open();
        return JsonSerializer.Deserialize<SupportPackageManifest>(stream, JsonOptions)
               ?? throw new InvalidDataException("Support package manifest is invalid.");
    }

    private static void ValidateManifest(SupportPackageManifest manifest)
    {
        if (!string.Equals(manifest.FormatVersion, PackageFormatVersion, StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported support package format '{manifest.FormatVersion}'.");
        if (!string.Equals(manifest.PackageType, SupportPackageType, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Expected a support package but found '{manifest.PackageType}'.");

        manifest.Files ??= [];
        foreach (var file in manifest.Files)
        {
            var normalized = NormalizePackagePath(file.Path);
            if (!normalized.StartsWith(ConfigurationPrefix, StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("../", StringComparison.Ordinal)
                || normalized.Contains("/..", StringComparison.Ordinal)
                || normalized.Contains("//", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Invalid support package path '{file.Path}'.");
            }

            file.Path = normalized;
        }
    }

    private static ZipArchiveEntry GetRequiredEntry(ZipArchive archive, string path)
    {
        var normalized = NormalizePackagePath(path);
        return archive.GetEntry(normalized)
               ?? archive.Entries.FirstOrDefault(entry => string.Equals(NormalizePackagePath(entry.FullName), normalized, StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidDataException($"Support package is missing required entry '{path}'.");
    }

    private static string ReadEntrySha256(ZipArchive archive, string path)
    {
        var entry = GetRequiredEntry(archive, path);
        using var stream = entry.Open();
        return ComputeSha256(stream);
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return ComputeSha256(stream);
    }

    private static string ComputeSha256(Stream stream)
    {
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void WriteStringEntry(ZipArchive archive, string entryPath, string content)
    {
        var entry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
        using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        stream.Write(bytes);
    }

    private static string NormalizePackagePath(string path) =>
        path.Replace('\\', '/').TrimStart('/');

    private sealed class SupportPackageManifest
    {
        [JsonPropertyName("formatVersion")]
        public string FormatVersion { get; set; } = PackageFormatVersion;

        [JsonPropertyName("packageType")]
        public string PackageType { get; set; } = SupportPackageType;

        [JsonPropertyName("exportedAt")]
        public string ExportedAt { get; set; } = string.Empty;

        [JsonPropertyName("replaceMissingFiles")]
        public bool ReplaceMissingFiles { get; set; }

        [JsonPropertyName("files")]
        public List<SupportPackageFileManifest> Files { get; set; } = [];
    }

    private sealed class SupportPackageFileManifest
    {
        [JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;

        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; } = string.Empty;
    }
}