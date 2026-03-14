using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using TaoMaster.Core.Models;
using TaoMaster.Core.Utilities;

namespace TaoMaster.Core.Services;

public sealed class ProjectCatalogService
{
    public (ManagerState State, ManagedProject Project) ImportOrRefreshProject(ManagerState state, string projectDirectory)
    {
        var normalizedDirectory = EnsureProjectDirectory(projectDirectory);
        var detection = DetectProject(normalizedDirectory);
        var existingProject = state.Projects.FirstOrDefault(project =>
            PathUtilities.NormalizePath(project.ProjectDirectory).Equals(normalizedDirectory, StringComparison.OrdinalIgnoreCase));

        var project = new ManagedProject(
            Id: existingProject?.Id ?? BuildProjectId(normalizedDirectory),
            DisplayName: Path.GetFileName(normalizedDirectory),
            ProjectDirectory: normalizedDirectory,
            BoundJdkId: NormalizeBoundId(existingProject?.BoundJdkId, state.Jdks),
            BoundMavenId: NormalizeBoundId(existingProject?.BoundMavenId, state.Mavens),
            AutoApplyBindingsOnOpen: existingProject?.AutoApplyBindingsOnOpen ?? false,
            LastScannedAtUtc: DateTimeOffset.UtcNow,
            Detection: detection);

        var projects = state.Projects
            .Where(item => !item.Id.Equals(project.Id, StringComparison.OrdinalIgnoreCase))
            .Append(project)
            .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ProjectDirectory, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return (state with
        {
            Projects = projects,
            ActiveProjectId = project.Id
        }, project);
    }

    public (ManagerState State, ManagedProject Project) UpdateBindings(
        ManagerState state,
        string projectId,
        string? jdkId,
        string? mavenId)
    {
        var project = FindProject(state, projectId);
        var updatedProject = project with
        {
            BoundJdkId = NormalizeBoundId(jdkId, state.Jdks),
            BoundMavenId = NormalizeBoundId(mavenId, state.Mavens)
        };

        return (ReplaceProject(state, updatedProject) with { ActiveProjectId = updatedProject.Id }, updatedProject);
    }

    public (ManagerState State, ManagedProject Project) RefreshProject(ManagerState state, string projectId)
    {
        var project = FindProject(state, projectId);
        return ImportOrRefreshProject(state with { ActiveProjectId = project.Id }, project.ProjectDirectory);
    }

    public (ManagerState State, ManagedProject Project) UpdateOpenBehavior(
        ManagerState state,
        string projectId,
        bool autoApplyBindingsOnOpen)
    {
        var project = FindProject(state, projectId);
        var updatedProject = project with
        {
            AutoApplyBindingsOnOpen = autoApplyBindingsOnOpen
        };

        return (ReplaceProject(state, updatedProject) with { ActiveProjectId = updatedProject.Id }, updatedProject);
    }

    public ManagerState RemoveProject(ManagerState state, string projectId)
    {
        var remainingProjects = state.Projects
            .Where(project => !project.Id.Equals(projectId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var activeProjectId = state.ActiveProjectId is not null
                              && state.ActiveProjectId.Equals(projectId, StringComparison.OrdinalIgnoreCase)
            ? remainingProjects.FirstOrDefault()?.Id
            : state.ActiveProjectId;

        return state with
        {
            Projects = remainingProjects,
            ActiveProjectId = activeProjectId
        };
    }

    public ManagerState SetActiveProject(ManagerState state, string? projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return state with { ActiveProjectId = null };
        }

        var project = FindProject(state, projectId);
        return state with { ActiveProjectId = project.Id };
    }

    public (ManagerState State, ManagedProject Project) ApplyProjectBindings(ManagerState state, string projectId)
    {
        var project = FindProject(state, projectId);
        var selection = state.ActiveSelection with
        {
            JdkId = NormalizeBoundId(project.BoundJdkId, state.Jdks) ?? state.ActiveSelection.JdkId,
            MavenId = NormalizeBoundId(project.BoundMavenId, state.Mavens) ?? state.ActiveSelection.MavenId
        };

        return (state with
        {
            ActiveProjectId = project.Id,
            ActiveSelection = selection
        }, project);
    }

    public ManagerState NormalizeProjects(ManagerState state)
    {
        var projects = state.Projects
            .Select(project => project with
            {
                BoundJdkId = NormalizeBoundId(project.BoundJdkId, state.Jdks),
                BoundMavenId = NormalizeBoundId(project.BoundMavenId, state.Mavens)
            })
            .OrderBy(project => project.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(project => project.ProjectDirectory, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var activeProjectId = projects.Any(project => project.Id.Equals(state.ActiveProjectId, StringComparison.OrdinalIgnoreCase))
            ? state.ActiveProjectId
            : projects.FirstOrDefault()?.Id;

        return state with
        {
            Projects = projects,
            ActiveProjectId = activeProjectId
        };
    }

    public ManagedProject? FindProjectOrDefault(ManagerState state, string? projectId) =>
        string.IsNullOrWhiteSpace(projectId)
            ? null
            : state.Projects.FirstOrDefault(project => project.Id.Equals(projectId, StringComparison.OrdinalIgnoreCase));

    private static ManagerState ReplaceProject(ManagerState state, ManagedProject updatedProject) =>
        state with
        {
            Projects = state.Projects
                .Select(project => project.Id.Equals(updatedProject.Id, StringComparison.OrdinalIgnoreCase) ? updatedProject : project)
                .ToList()
        };

    private static ManagedProject FindProject(ManagerState state, string projectId) =>
        state.Projects.FirstOrDefault(project => project.Id.Equals(projectId, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"No project with ID `{projectId}` was found.", nameof(projectId));

    private static string EnsureProjectDirectory(string projectDirectory)
    {
        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            throw new ArgumentException("Project directory is required.", nameof(projectDirectory));
        }

        var normalizedDirectory = PathUtilities.NormalizePath(projectDirectory);
        if (!Directory.Exists(normalizedDirectory))
        {
            throw new DirectoryNotFoundException($"Project directory does not exist: {normalizedDirectory}");
        }

        return normalizedDirectory;
    }

    private static string? NormalizeBoundId(string? installationId, IReadOnlyList<ManagedInstallation> installations) =>
        installations.Any(installation => installation.Id.Equals(installationId, StringComparison.OrdinalIgnoreCase))
            ? installations.First(installation => installation.Id.Equals(installationId, StringComparison.OrdinalIgnoreCase)).Id
            : null;

    private static ProjectDetectionSnapshot DetectProject(string projectDirectory)
    {
        var pomXmlPath = Path.Combine(projectDirectory, "pom.xml");
        var mavenDirectory = Path.Combine(projectDirectory, ".mvn");
        var ideaDirectory = Path.Combine(projectDirectory, ".idea");
        var javaVersionPath = Path.Combine(projectDirectory, ".java-version");
        var sdkmanrcPath = Path.Combine(projectDirectory, ".sdkmanrc");
        var mavenWrapperPropertiesPath = Path.Combine(mavenDirectory, "wrapper", "maven-wrapper.properties");
        var ideaMiscPath = Path.Combine(ideaDirectory, "misc.xml");

        var (sdkmanJavaVersion, sdkmanMavenVersion) = ReadSdkmanHints(sdkmanrcPath);

        return new ProjectDetectionSnapshot(
            HasPomXml: File.Exists(pomXmlPath),
            HasMavenDirectory: Directory.Exists(mavenDirectory),
            HasMavenWrapper: File.Exists(Path.Combine(projectDirectory, "mvnw")) || File.Exists(Path.Combine(projectDirectory, "mvnw.cmd")),
            HasIdeaDirectory: Directory.Exists(ideaDirectory),
            IdeaProjectJdkName: ReadIdeaProjectJdkName(ideaMiscPath),
            JavaVersionHint: ReadSingleLine(javaVersionPath),
            SdkmanJavaVersionHint: sdkmanJavaVersion,
            SdkmanMavenVersionHint: sdkmanMavenVersion,
            MavenWrapperDistributionUrl: ReadMavenWrapperDistributionUrl(mavenWrapperPropertiesPath));
    }

    private static (string? JavaVersion, string? MavenVersion) ReadSdkmanHints(string sdkmanrcPath)
    {
        if (!File.Exists(sdkmanrcPath))
        {
            return (null, null);
        }

        string? javaVersion = null;
        string? mavenVersion = null;

        foreach (var rawLine in File.ReadLines(sdkmanrcPath))
        {
            var line = rawLine.Trim();
            if (line.StartsWith('#') || !line.Contains('=', StringComparison.Ordinal))
            {
                continue;
            }

            var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
            {
                continue;
            }

            if (parts[0].Equals("java", StringComparison.OrdinalIgnoreCase))
            {
                javaVersion = parts[1];
            }
            else if (parts[0].Equals("maven", StringComparison.OrdinalIgnoreCase))
            {
                mavenVersion = parts[1];
            }
        }

        return (javaVersion, mavenVersion);
    }

    private static string? ReadIdeaProjectJdkName(string ideaMiscPath)
    {
        if (!File.Exists(ideaMiscPath))
        {
            return null;
        }

        try
        {
            var document = XDocument.Load(ideaMiscPath);
            var projectRootManager = document
                .Descendants("component")
                .FirstOrDefault(element => string.Equals((string?)element.Attribute("name"), "ProjectRootManager", StringComparison.OrdinalIgnoreCase));

            return projectRootManager?.Attribute("project-jdk-name")?.Value?.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadMavenWrapperDistributionUrl(string wrapperPropertiesPath)
    {
        if (!File.Exists(wrapperPropertiesPath))
        {
            return null;
        }

        foreach (var rawLine in File.ReadLines(wrapperPropertiesPath))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith("distributionUrl=", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return line["distributionUrl=".Length..].Trim();
        }

        return null;
    }

    private static string? ReadSingleLine(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        return File.ReadLines(filePath)
            .Select(line => line.Trim())
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));
    }

    private static string BuildProjectId(string projectDirectory)
    {
        var normalizedPath = PathUtilities.NormalizePath(projectDirectory);
        var displayName = Path.GetFileName(normalizedPath);
        var sanitizedName = new string(displayName
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray())
            .Trim('-');

        if (string.IsNullOrWhiteSpace(sanitizedName))
        {
            sanitizedName = "project";
        }

        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(normalizedPath));
        return $"{sanitizedName}-{Convert.ToHexString(hash)[..8].ToLowerInvariant()}";
    }
}
