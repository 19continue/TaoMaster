namespace TaoMaster.Core.Models;

public sealed record ProjectDetectionSnapshot(
    bool HasPomXml,
    bool HasMavenDirectory,
    bool HasMavenWrapper,
    bool HasIdeaDirectory,
    string? IdeaProjectJdkName,
    string? JavaVersionHint,
    string? SdkmanJavaVersionHint,
    string? SdkmanMavenVersionHint,
    string? MavenWrapperDistributionUrl)
{
    public static ProjectDetectionSnapshot Empty { get; } =
        new(
            HasPomXml: false,
            HasMavenDirectory: false,
            HasMavenWrapper: false,
            HasIdeaDirectory: false,
            IdeaProjectJdkName: null,
            JavaVersionHint: null,
            SdkmanJavaVersionHint: null,
            SdkmanMavenVersionHint: null,
            MavenWrapperDistributionUrl: null);
}
