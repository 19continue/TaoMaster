using System.IO.Compression;

namespace TaoMaster.Core.Services;

public sealed class ZipExtractionService
{
    public string ExtractPackageRoot(string zipFile, string tempRoot)
    {
        var extractionRoot = Path.Combine(tempRoot, Path.GetFileNameWithoutExtension(zipFile) + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractionRoot);

        ZipFile.ExtractToDirectory(zipFile, extractionRoot);

        return FindContentRoot(extractionRoot);
    }

    private static string FindContentRoot(string extractionRoot)
    {
        var directories = Directory.EnumerateDirectories(extractionRoot, "*", SearchOption.TopDirectoryOnly).ToList();
        var files = Directory.EnumerateFiles(extractionRoot, "*", SearchOption.TopDirectoryOnly).ToList();

        if (directories.Count == 1 && files.Count == 0)
        {
            return directories[0];
        }

        return extractionRoot;
    }
}
