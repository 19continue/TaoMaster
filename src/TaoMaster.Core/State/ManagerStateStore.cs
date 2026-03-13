using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using TaoMaster.Core.Models;
using TaoMaster.Core.Services;

namespace TaoMaster.Core.State;

public sealed class ManagerStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    static ManagerStateStore()
    {
        JsonOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }

    private readonly WorkspaceInitializer _workspaceInitializer;

    public ManagerStateStore(WorkspaceInitializer workspaceInitializer)
    {
        _workspaceInitializer = workspaceInitializer;
    }

    public ManagerState EnsureInitialized(WorkspaceLayout layout)
    {
        _workspaceInitializer.EnsureCreated(layout);

        if (!File.Exists(layout.StateFile))
        {
            var state = ManagerState.CreateDefault(layout);
            Save(layout, state);
            return state;
        }

        return Load(layout);
    }

    public ManagerState Load(WorkspaceLayout layout)
    {
        _workspaceInitializer.EnsureCreated(layout);

        using var stream = File.OpenRead(layout.StateFile);
        var state = JsonSerializer.Deserialize<ManagerState>(stream, JsonOptions);

        return state ?? ManagerState.CreateDefault(layout);
    }

    public void Save(WorkspaceLayout layout, ManagerState state)
    {
        _workspaceInitializer.EnsureCreated(layout);

        using var stream = File.Create(layout.StateFile);
        JsonSerializer.Serialize(stream, state, JsonOptions);
    }
}
