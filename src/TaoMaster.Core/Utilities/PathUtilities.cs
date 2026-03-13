namespace TaoMaster.Core.Utilities;

public static class PathUtilities
{
    public static StringComparer Comparer { get; } = StringComparer.OrdinalIgnoreCase;

    public static string NormalizePath(string path)
    {
        var trimmed = path.Trim().Trim('"');
        var fullPath = Path.GetFullPath(trimmed);

        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public static bool IsDescendantOrSelf(string path, string root)
    {
        var normalizedPath = NormalizePath(path);
        var normalizedRoot = NormalizePath(root);

        return normalizedPath.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)
               || normalizedPath.StartsWith(
                   normalizedRoot + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }
}
