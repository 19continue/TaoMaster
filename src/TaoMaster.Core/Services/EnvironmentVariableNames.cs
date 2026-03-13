namespace TaoMaster.Core.Services;

public static class EnvironmentVariableNames
{
    public const string JavaHome = "JAVA_HOME";
    public const string MavenHome = "MAVEN_HOME";
    public const string M2Home = "M2_HOME";
    public const string Path = "PATH";
    public const string ManagedJavaId = "JDKMANAGER_JAVA_ID";
    public const string ManagedMavenId = "JDKMANAGER_MAVEN_ID";
    public const string ManagedJavaPathEntry = @"%JAVA_HOME%\bin";
    public const string ManagedMavenPathEntry = @"%MAVEN_HOME%\bin";
}
