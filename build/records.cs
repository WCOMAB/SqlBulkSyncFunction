/*****************************
 * Records
 *****************************/
public record BuildData(
    string Version,
    bool IsMainBranch,
    bool ShouldNotPublish,
    bool IsLocalBuild,
    bool IsRunningOnGitHubActions,
    string? WorkflowRef,
    DirectoryPath ProjectRoot,
    FilePath ProjectPath,
    DotNetMSBuildSettings MSBuildSettings,
    DirectoryPath ArtifactsPath,
    DirectoryPath OutputPath
    )
{
    public DirectoryPath NuGetOutputPath { get; } = OutputPath.Combine("nuget");

    public string? GitHubNuGetSource { get; } = Environment.GetEnvironmentVariable("GH_PACKAGES_NUGET_SOURCE");
    public string? GitHubNuGetApiKey { get; } = Environment.GetEnvironmentVariable("GITHUB_TOKEN");

    public bool ShouldPushGitHubPackages() =>
        !ShouldNotPublish
        && !string.IsNullOrWhiteSpace(GitHubNuGetSource)
        && !string.IsNullOrWhiteSpace(GitHubNuGetApiKey);

    public string? NuGetSource { get; } = Environment.GetEnvironmentVariable("NUGET_SOURCE");
    public string? NuGetApiUser { get; } = Environment.GetEnvironmentVariable("NUGET_USER");
    public string? NuGetApiKey { get; set; } = Environment.GetEnvironmentVariable("NUGET_APIKEY");

    public bool ShouldLoginNuGet() =>
        !ShouldNotPublish
        && IsRunningOnGitHubActions
        && (IsMainBranch
            || (WorkflowRef?.StartsWith("refs/tags/v", StringComparison.Ordinal) ?? false));

    public bool ShouldPushNuGetPackages() =>
        IsMainBranch
        && !ShouldNotPublish
        && !string.IsNullOrWhiteSpace(NuGetSource)
        && !string.IsNullOrWhiteSpace(NuGetApiKey);

    public ICollection<DirectoryPath> DirectoryPathsToClean { get; } =
    [
        ArtifactsPath,
        OutputPath
    ];
}

internal record ExtensionHelper(Func<string, CakeTaskBuilder> TaskCreate, Func<CakeReport> Run);
