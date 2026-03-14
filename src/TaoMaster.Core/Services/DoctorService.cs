using System.Runtime.Versioning;
using TaoMaster.Core.Models;
using TaoMaster.Core.Utilities;

namespace TaoMaster.Core.Services;

[SupportedOSPlatform("windows")]
public sealed class DoctorService
{
    private readonly ToolchainSelectionResolver _selectionResolver;
    private readonly WindowsUserEnvironmentService _environmentService;

    public DoctorService(
        ToolchainSelectionResolver selectionResolver,
        WindowsUserEnvironmentService environmentService)
    {
        _selectionResolver = selectionResolver;
        _environmentService = environmentService;
    }

    public DoctorReport Run(ManagerState state) => Run(state, null);

    public DoctorReport Run(ManagerState state, ManagedProject? project)
    {
        var checks = new List<DoctorCheck>();
        var selection = _selectionResolver.Resolve(state);

        AddSelectionChecks(checks, state, selection);

        var userJavaHome = _environmentService.GetUserVariable(EnvironmentVariableNames.JavaHome);
        var userMavenHome = _environmentService.GetUserVariable(EnvironmentVariableNames.MavenHome);
        var userM2Home = _environmentService.GetUserVariable(EnvironmentVariableNames.M2Home);
        var userPath = _environmentService.GetUserVariable(EnvironmentVariableNames.Path);
        var expectedUserPath = _environmentService.BuildManagedUserPath(
            userPath,
            includeJavaEntry: selection.Jdk is not null,
            includeMavenEntry: selection.Maven is not null);

        if (selection.Jdk is not null)
        {
            checks.Add(PathUtilities.Comparer.Equals(selection.Jdk.HomeDirectory, userJavaHome)
                ? new DoctorCheck(DoctorCheckStatus.Pass, "java-home", "User JAVA_HOME matches the selected JDK.", userJavaHome)
                : new DoctorCheck(
                    DoctorCheckStatus.Fail,
                    "java-home",
                    "User JAVA_HOME does not match the selected JDK.",
                    $"Expected: {selection.Jdk.HomeDirectory}{Environment.NewLine}Actual: {userJavaHome ?? "(empty)"}"));
        }
        else
        {
            checks.Add(new DoctorCheck(DoctorCheckStatus.Warn, "java-home", "No JDK is selected, so JAVA_HOME is not validated."));
        }

        if (selection.Maven is not null)
        {
            checks.Add(PathUtilities.Comparer.Equals(selection.Maven.HomeDirectory, userMavenHome)
                ? new DoctorCheck(DoctorCheckStatus.Pass, "maven-home", "User MAVEN_HOME matches the selected Maven.", userMavenHome)
                : new DoctorCheck(
                    DoctorCheckStatus.Fail,
                    "maven-home",
                    "User MAVEN_HOME does not match the selected Maven.",
                    $"Expected: {selection.Maven.HomeDirectory}{Environment.NewLine}Actual: {userMavenHome ?? "(empty)"}"));

            checks.Add(PathUtilities.Comparer.Equals(selection.Maven.HomeDirectory, userM2Home)
                ? new DoctorCheck(DoctorCheckStatus.Pass, "m2-home", "User M2_HOME matches the selected Maven.", userM2Home)
                : new DoctorCheck(
                    DoctorCheckStatus.Fail,
                    "m2-home",
                    "User M2_HOME does not match the selected Maven.",
                    $"Expected: {selection.Maven.HomeDirectory}{Environment.NewLine}Actual: {userM2Home ?? "(empty)"}"));
        }
        else
        {
            checks.Add(new DoctorCheck(DoctorCheckStatus.Warn, "maven-home", "No Maven is selected, so MAVEN_HOME is not validated."));
            checks.Add(new DoctorCheck(DoctorCheckStatus.Warn, "m2-home", "No Maven is selected, so M2_HOME is not validated."));
        }

        checks.Add(
            string.Equals(userPath ?? string.Empty, expectedUserPath, StringComparison.OrdinalIgnoreCase)
                ? new DoctorCheck(DoctorCheckStatus.Pass, "user-path", "User PATH contains the managed entries in the expected order.", userPath)
                : new DoctorCheck(
                    DoctorCheckStatus.Fail,
                    "user-path",
                    "User PATH is missing managed entries, contains duplicates, or has an unexpected order.",
                    $"Expected: {expectedUserPath}{Environment.NewLine}Actual: {userPath ?? "(empty)"}"));

        var effectivePath = _environmentService.BuildEffectivePathForNewProcesses(expectedUserPath);
        var variableMap = _environmentService.BuildVariableMap(
            selection.Jdk?.HomeDirectory ?? userJavaHome,
            selection.Maven?.HomeDirectory ?? userMavenHome,
            selection.Maven?.HomeDirectory ?? userM2Home);

        if (selection.Jdk is not null)
        {
            var javaCandidates = _environmentService.FindExecutableCandidates("java.exe", expectedUserPath, variableMap);
            checks.Add(ResolvedExecutableCheck(
                "java-resolve",
                "java.exe",
                javaCandidates,
                Path.Combine(selection.Jdk.HomeDirectory, "bin", "java.exe")));
        }

        if (selection.Maven is not null)
        {
            var mavenCandidates = _environmentService.FindExecutableCandidates("mvn.cmd", expectedUserPath, variableMap);
            checks.Add(ResolvedExecutableCheck(
                "maven-resolve",
                "mvn.cmd",
                mavenCandidates,
                Path.Combine(selection.Maven.HomeDirectory, "bin", "mvn.cmd")));
        }

        if (selection.Jdk is not null && selection.Maven is not null)
        {
            var probe = _environmentService.RunMavenProbe(
                selection.Jdk.HomeDirectory,
                selection.Maven.HomeDirectory,
                effectivePath,
                TimeSpan.FromSeconds(20));

            checks.Add(probe.Success
                ? new DoctorCheck(
                    DoctorCheckStatus.Pass,
                    "maven-probe",
                    "The selected Maven starts with the selected JDK.",
                    probe.JavaVersionLine ?? probe.Output)
                : new DoctorCheck(
                    DoctorCheckStatus.Fail,
                    "maven-probe",
                    "The selected Maven could not start with the selected JDK.",
                    probe.Output));
        }

        if (project is not null)
        {
            AddProjectChecks(checks, state, selection, project);
        }

        return new DoctorReport(checks);
    }

    private static void AddSelectionChecks(
        ICollection<DoctorCheck> checks,
        ManagerState state,
        ActiveToolchainSelection selection)
    {
        checks.Add(string.IsNullOrWhiteSpace(state.ActiveSelection.JdkId)
            ? new DoctorCheck(DoctorCheckStatus.Warn, "selected-jdk", "No JDK is selected in state.")
            : selection.Jdk is not null
                ? new DoctorCheck(DoctorCheckStatus.Pass, "selected-jdk", "Active JDK selection is valid.", selection.Jdk.DisplayName)
                : new DoctorCheck(DoctorCheckStatus.Fail, "selected-jdk", "The saved JDK selection points to a missing installation.", state.ActiveSelection.JdkId));

        checks.Add(string.IsNullOrWhiteSpace(state.ActiveSelection.MavenId)
            ? new DoctorCheck(DoctorCheckStatus.Warn, "selected-maven", "No Maven is selected in state.")
            : selection.Maven is not null
                ? new DoctorCheck(DoctorCheckStatus.Pass, "selected-maven", "Active Maven selection is valid.", selection.Maven.DisplayName)
                : new DoctorCheck(DoctorCheckStatus.Fail, "selected-maven", "The saved Maven selection points to a missing installation.", state.ActiveSelection.MavenId));
    }

    private static void AddProjectChecks(
        ICollection<DoctorCheck> checks,
        ManagerState state,
        ActiveToolchainSelection selection,
        ManagedProject project)
    {
        checks.Add(Directory.Exists(project.ProjectDirectory)
            ? new DoctorCheck(DoctorCheckStatus.Pass, "project-directory", "The selected project directory is available.", project.ProjectDirectory)
            : new DoctorCheck(DoctorCheckStatus.Fail, "project-directory", "The selected project directory is missing.", project.ProjectDirectory));

        checks.Add(project.Detection.HasPomXml
            ? new DoctorCheck(DoctorCheckStatus.Pass, "project-pom", "pom.xml was detected in the selected project.", project.ProjectDirectory)
            : new DoctorCheck(DoctorCheckStatus.Warn, "project-pom", "pom.xml was not detected in the selected project.", project.ProjectDirectory));

        checks.Add(project.Detection.HasMavenWrapper
            ? new DoctorCheck(DoctorCheckStatus.Pass, "project-wrapper", "A Maven wrapper was detected for the selected project.", project.Detection.MavenWrapperDistributionUrl)
            : new DoctorCheck(DoctorCheckStatus.Warn, "project-wrapper", "No Maven wrapper was detected for the selected project."));

        checks.Add(project.Detection.HasIdeaDirectory
            ? new DoctorCheck(DoctorCheckStatus.Pass, "project-idea", "The selected project contains IntelliJ IDEA metadata.", project.Detection.IdeaProjectJdkName)
            : new DoctorCheck(DoctorCheckStatus.Warn, "project-idea", "The selected project does not contain an .idea directory."));

        checks.Add(BuildProjectBindingCheck(
            "project-jdk-binding",
            "JDK",
            project.BoundJdkId,
            state.Jdks,
            selection.Jdk?.Id));

        checks.Add(BuildProjectBindingCheck(
            "project-maven-binding",
            "Maven",
            project.BoundMavenId,
            state.Mavens,
            selection.Maven?.Id));

        if (project.Detection.HasIdeaDirectory)
        {
            checks.Add(!string.IsNullOrWhiteSpace(project.Detection.IdeaProjectJdkName)
                ? new DoctorCheck(
                    DoctorCheckStatus.Pass,
                    "idea-jdk-hint",
                    "The IntelliJ IDEA project JDK hint was detected.",
                    project.Detection.IdeaProjectJdkName)
                : new DoctorCheck(
                    DoctorCheckStatus.Warn,
                    "idea-jdk-hint",
                    "The IntelliJ IDEA project JDK hint was not found in .idea/misc.xml."));
        }
    }

    private static DoctorCheck BuildProjectBindingCheck(
        string code,
        string label,
        string? boundInstallationId,
        IReadOnlyList<ManagedInstallation> installations,
        string? selectedInstallationId)
    {
        if (string.IsNullOrWhiteSpace(boundInstallationId))
        {
            return new DoctorCheck(DoctorCheckStatus.Warn, code, $"No project {label} binding is stored in TaoMaster.");
        }

        var installation = installations.FirstOrDefault(item => item.Id.Equals(boundInstallationId, StringComparison.OrdinalIgnoreCase));
        if (installation is null)
        {
            return new DoctorCheck(
                DoctorCheckStatus.Fail,
                code,
                $"The project {label} binding points to a missing installation.",
                boundInstallationId);
        }

        var detail = $"{installation.DisplayName}{Environment.NewLine}{installation.HomeDirectory}";
        if (!string.IsNullOrWhiteSpace(selectedInstallationId)
            && !installation.Id.Equals(selectedInstallationId, StringComparison.OrdinalIgnoreCase))
        {
            return new DoctorCheck(
                DoctorCheckStatus.Warn,
                code,
                $"The project {label} binding differs from the current active selection.",
                detail);
        }

        return new DoctorCheck(
            DoctorCheckStatus.Pass,
            code,
            $"The project {label} binding is available and aligned.",
            detail);
    }

    private static DoctorCheck ResolvedExecutableCheck(
        string code,
        string executableName,
        IReadOnlyList<ResolvedExecutableCandidate> candidates,
        string expectedPath)
    {
        var winningCandidate = candidates.FirstOrDefault();
        if (winningCandidate is null)
        {
            return new DoctorCheck(
                DoctorCheckStatus.Fail,
                code,
                $"{executableName} cannot be resolved from a new process PATH.",
                $"Expected: {expectedPath}");
        }

        var detail = BuildExecutableResolutionDetail(winningCandidate, expectedPath);
        return PathUtilities.Comparer.Equals(winningCandidate.CandidatePath, expectedPath)
            ? new DoctorCheck(DoctorCheckStatus.Pass, code, $"{executableName} resolves to the selected toolchain in a new process.", detail)
            : new DoctorCheck(DoctorCheckStatus.Warn, code, $"{executableName} resolves to a different location in a new process.", detail);
    }

    private static string BuildExecutableResolutionDetail(
        ResolvedExecutableCandidate winningCandidate,
        string expectedPath)
    {
        var scope = winningCandidate.Scope == EnvironmentPathScope.Machine ? "machine" : "user";
        var recommendation = winningCandidate.Scope == EnvironmentPathScope.Machine
            ? "Run `taomaster repair machine-path-script` or remove/reorder the winning machine PATH entry with administrator privileges."
            : "Run `taomaster repair user-path` or use the desktop action to remove conflicting user PATH entries.";

        return $"Expected: {expectedPath}{Environment.NewLine}"
               + $"Actual: {winningCandidate.CandidatePath}{Environment.NewLine}"
               + $"Scope: {scope}{Environment.NewLine}"
               + $"PATH entry: {winningCandidate.OriginalPathSegment}{Environment.NewLine}"
               + $"Recommendation: {recommendation}";
    }
}
