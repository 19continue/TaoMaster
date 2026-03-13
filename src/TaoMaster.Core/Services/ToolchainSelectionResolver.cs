using TaoMaster.Core.Models;

namespace TaoMaster.Core.Services;

public sealed class ToolchainSelectionResolver
{
    public ActiveToolchainSelection Resolve(ManagerState state)
    {
        var jdk = ResolveById(state.Jdks, state.ActiveSelection.JdkId);
        var maven = ResolveById(state.Mavens, state.ActiveSelection.MavenId);

        return new ActiveToolchainSelection(jdk, maven);
    }

    public ManagedInstallation GetRequiredSelection(ManagerState state, ToolchainKind kind, string id)
    {
        var installation = kind switch
        {
            ToolchainKind.Jdk => ResolveById(state.Jdks, id),
            ToolchainKind.Maven => ResolveById(state.Mavens, id),
            _ => null
        };

        return installation ?? throw new ArgumentException(
            $"未找到 ID 为 `{id}` 的 {kind}，请先执行 `sync` 或 `list` 检查可用项。",
            nameof(id));
    }

    private static ManagedInstallation? ResolveById(
        IEnumerable<ManagedInstallation> installations,
        string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return installations.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }
}
