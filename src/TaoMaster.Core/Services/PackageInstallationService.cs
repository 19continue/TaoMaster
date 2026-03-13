using TaoMaster.Core.Discovery;
using TaoMaster.Core.Models;

namespace TaoMaster.Core.Services;

public sealed class PackageInstallationService
{
    private readonly PackageDownloadService _downloadService;
    private readonly ChecksumService _checksumService;
    private readonly ZipExtractionService _zipExtractionService;
    private readonly InstallationInspector _installationInspector;

    public PackageInstallationService(
        PackageDownloadService downloadService,
        ChecksumService checksumService,
        ZipExtractionService zipExtractionService,
        InstallationInspector installationInspector)
    {
        _downloadService = downloadService;
        _checksumService = checksumService;
        _zipExtractionService = zipExtractionService;
        _installationInspector = installationInspector;
    }

    public async Task<ManagedInstallation> InstallAsync(
        RemotePackageDescriptor package,
        WorkspaceLayout layout,
        CancellationToken cancellationToken,
        IProgress<PackageInstallProgress>? progress = null)
    {
        var installRoot = package.Kind == ToolchainKind.Jdk ? layout.JdkRoot : layout.MavenRoot;
        var preferredDirectory = Path.Combine(installRoot, package.SuggestedInstallDirectoryName);

        if (Directory.Exists(preferredDirectory))
        {
            var existingInstallation = ValidateInstalledDirectory(package.Kind, preferredDirectory, layout, "download");
            progress?.Report(new PackageInstallProgress(PackageInstallStage.Completed));
            return existingInstallation;
        }

        var finalDirectory = GetUniqueInstallDirectory(installRoot, package.SuggestedInstallDirectoryName);

        var cacheFile = Path.Combine(layout.CacheRoot, package.FileName);
        await _downloadService.DownloadAsync(package.DownloadUrl, cacheFile, cancellationToken, progress);
        progress?.Report(new PackageInstallProgress(PackageInstallStage.Verifying));
        await _checksumService.VerifyAsync(cacheFile, package.Checksum, package.ChecksumAlgorithm, cancellationToken);

        progress?.Report(new PackageInstallProgress(PackageInstallStage.Extracting));
        var extractedRoot = _zipExtractionService.ExtractPackageRoot(cacheFile, layout.TempRoot);
        Directory.Move(extractedRoot, finalDirectory);

        var installation = ValidateInstalledDirectory(package.Kind, finalDirectory, layout, "download");
        progress?.Report(new PackageInstallProgress(PackageInstallStage.Completed));
        return installation;
    }

    private ManagedInstallation ValidateInstalledDirectory(
        ToolchainKind kind,
        string homeDirectory,
        WorkspaceLayout layout,
        string source)
    {
        ManagedInstallation? installation = null;

        var success = kind switch
        {
            ToolchainKind.Jdk => _installationInspector.TryInspectJdkHome(homeDirectory, source, layout, out installation),
            ToolchainKind.Maven => _installationInspector.TryInspectMavenHome(homeDirectory, source, layout, out installation),
            _ => false
        };

        if (!success || installation is null)
        {
            throw new InvalidOperationException($"下载包已解压，但无法识别为有效的 {kind} 安装目录: {homeDirectory}");
        }

        return installation;
    }

    private static string GetUniqueInstallDirectory(string installRoot, string suggestedDirectoryName)
    {
        Directory.CreateDirectory(installRoot);
        var baseDirectory = Path.Combine(installRoot, suggestedDirectoryName);

        if (!Directory.Exists(baseDirectory))
        {
            return baseDirectory;
        }

        var index = 2;
        while (true)
        {
            var candidate = $"{baseDirectory}-{index}";
            if (!Directory.Exists(candidate))
            {
                return candidate;
            }

            index++;
        }
    }
}
