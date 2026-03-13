namespace TaoMaster.Core;

public sealed record WorkspaceLayout(
    string RootDirectory,
    string JdkRoot,
    string MavenRoot,
    string CacheRoot,
    string TempRoot,
    string LogRoot,
    string ScriptRoot,
    string StateFile)
{
    public static WorkspaceLayout CreateDefault()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ProductInfo.WorkspaceDirectoryName);

        return new WorkspaceLayout(
            RootDirectory: root,
            JdkRoot: Path.Combine(root, "jdks"),
            MavenRoot: Path.Combine(root, "mavens"),
            CacheRoot: Path.Combine(root, "cache"),
            TempRoot: Path.Combine(root, "temp"),
            LogRoot: Path.Combine(root, "logs"),
            ScriptRoot: Path.Combine(root, "scripts"),
            StateFile: Path.Combine(root, "state.json"));
    }
}
