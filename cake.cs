#!/usr/bin/env dotnet
#:sdk Cake.Sdk@6.3.0
#:package System.Security.Cryptography.Pkcs@10.0.13
#:property IncludeAdditionalFiles=./build/*.cs

/*****************************
 * Setup
 *****************************/
Setup(
    static context =>
    {
        InstallTool("dotnet:https://api.nuget.org/v3/index.json?package=DPI&version=2026.10.15.520");
        InstallTool("dotnet:https://api.nuget.org/v3/index.json?package=GitVersion.Tool&version=6.8.2");

        var assertedVersions = context.GitVersion(new GitVersionSettings
        {
            OutputType = GitVersionOutput.Json
        });

        var branchName = assertedVersions.BranchName;
        var isMainBranch = StringComparer.OrdinalIgnoreCase.Equals("main", branchName);

        var buildDate = DateTime.UtcNow;
        var runNumber = GitHubActions.IsRunningOnGitHubActions
            ? GitHubActions.Environment.Workflow.RunNumber
            : (short)((buildDate - buildDate.Date).TotalSeconds / 3);

        var version = FormattableString.Invariant($"{buildDate:yyyy.M.d}.{runNumber}");

        context.Information(
            "Building version {0} (Branch: {1}, IsMain: {2})",
            version,
            branchName,
            isMainBranch);

        var artifactsPath = context.MakeAbsolute(context.Directory("./artifacts"));
        var projectRoot = context.MakeAbsolute(context.Directory("./src"));
        var projectPath = projectRoot.CombineWithFilePath("SqlBulkSyncFunction/SqlBulkSyncFunction.csproj");

        return new BuildData(
            version,
            isMainBranch,
            !context.IsRunningOnWindows(),
            BuildSystem.IsLocalBuild,
            GitHubActions.IsRunningOnGitHubActions,
            GitHubActions.IsRunningOnGitHubActions ? GitHubActions.Environment.Workflow.Ref : null,
            projectRoot,
            projectPath,
            new DotNetMSBuildSettings()
                .SetConfiguration("Release")
                .SetVersion(version)
                .WithProperty("Copyright", $"Copyright (c) WCOM AB")
                .WithProperty("Authors", "Mattias Karlsson")
                .WithProperty("Company", "WCOM AB")
                .WithProperty("PackageLicenseExpression", "Apache-2.0")
                .WithProperty("PackageTags", "SQL;Sync;Azure Function")
                .WithProperty(
                    "PackageDescription",
                    "A lightweight, performant non-intrusive SQL Server data sync service.")
                .WithProperty("RepositoryUrl", "https://github.com/WCOMAB/SqlBulkSyncFunction.git")
                .WithProperty(
                    "ContinuousIntegrationBuild",
                    GitHubActions.IsRunningOnGitHubActions ? "true" : "false")
                .WithProperty("EmbedUntrackedSources", "true"),
            artifactsPath,
            artifactsPath.Combine(version));
    });

/*****************************
 * Tasks
 *****************************/
Task("NuGet-Login")
    .WithCriteria<BuildData>(static (_, data) => data.ShouldLoginNuGet())
    .Does<BuildData>(static async (context, data) =>
    {
        ArgumentException.ThrowIfNullOrEmpty(data.NuGetApiUser);

        context.Information("Logging in to NuGet...");
        data.NuGetApiKey = await GitHubActions.Commands.NuGetLogin(data.NuGetApiUser);
        ArgumentException.ThrowIfNullOrEmpty(data.NuGetApiKey);
    })
.Then("Clean")
    .Does<BuildData>(static (context, data) => context.CleanDirectories(data.DirectoryPathsToClean))
.Then("Restore")
    .Does<BuildData>(
        static (context, data) => context.DotNetRestore(
            data.ProjectRoot.FullPath,
            new DotNetRestoreSettings
            {
                MSBuildSettings = data.MSBuildSettings
            }))
.Then("DPI")
    .Does<BuildData>(
        static (context, data) => Command(
            ["dpi", "dpi.exe"],
            new ProcessArgumentBuilder()
                .Append("nuget")
                .Append("--silent")
                .AppendSwitchQuoted("--output", "table")
                .Append(
                    !string.IsNullOrWhiteSpace(context.EnvironmentVariable("NuGetReportSettings_SharedKey"))
                    && !string.IsNullOrWhiteSpace(context.EnvironmentVariable("NuGetReportSettings_WorkspaceId"))
                        ? "report"
                        : "analyze")
                .AppendSwitchQuoted("--buildversion", data.Version)))
.Then("Build")
    .Does<BuildData>(
        static (context, data) => context.DotNetBuild(
            data.ProjectRoot.FullPath,
            new DotNetBuildSettings
            {
                NoRestore = true,
                MSBuildSettings = data.MSBuildSettings
            }))
.Then("Pack")
    .Does<BuildData>(
        static (context, data) => context.DotNetPack(
            data.ProjectPath.FullPath,
            new DotNetPackSettings
            {
                NoBuild = true,
                NoRestore = true,
                OutputDirectory = data.NuGetOutputPath,
                MSBuildSettings = data.MSBuildSettings
            }))
.Then("Upload-Artifacts")
    .WithCriteria(BuildSystem.IsRunningOnGitHubActions, nameof(BuildSystem.IsRunningOnGitHubActions))
    .Does<BuildData>(
        static (context, data) => GitHubActions
            .Commands
            .UploadArtifact(
                data.ArtifactsPath,
                $"Artifact_{GitHubActions.Environment.Runner.ImageOS ?? GitHubActions.Environment.Runner.OS}_{context.Environment.Runtime.BuiltFramework.Identifier}_{context.Environment.Runtime.BuiltFramework.Version}"))
    .Default()
.Then("Push-GitHub-Packages")
    .WithCriteria<BuildData>((_, data) => data.ShouldPushGitHubPackages())
    .DoesForEach<BuildData, FilePath>(
        static (data, context) => context.GetFiles(data.NuGetOutputPath.FullPath + "/*.nupkg"),
        static (data, item, context) => context.DotNetNuGetPush(
            item.FullPath,
            new DotNetNuGetPushSettings
            {
                Source = data.GitHubNuGetSource,
                ApiKey = data.GitHubNuGetApiKey
            }))
.Then("Push-NuGet-Packages")
    .WithCriteria<BuildData>((_, data) => data.ShouldPushNuGetPackages())
    .DoesForEach<BuildData, FilePath>(
        static (data, context) => context.GetFiles(data.NuGetOutputPath.FullPath + "/*.nupkg"),
        static (data, item, context) => context.DotNetNuGetPush(
            item.FullPath,
            new DotNetNuGetPushSettings
            {
                Source = data.NuGetSource,
                ApiKey = data.NuGetApiKey
            }))
.Then("Create-GitHub-Release")
    .WithCriteria<BuildData>((_, data) => data.ShouldPushNuGetPackages())
    .Does<BuildData>(
        static (context, data) => context.Command(
            new CommandSettings
            {
                ToolName = "GitHub CLI",
                ToolExecutableNames = ["gh.exe", "gh"],
                EnvironmentVariables = { ["GH_TOKEN"] = data.GitHubNuGetApiKey }
            },
            new ProcessArgumentBuilder()
                .Append("release")
                .Append("create")
                .Append(data.Version)
                .AppendSwitchQuoted("--title", data.Version)
                .Append("--generate-notes")
                .Append(string.Join(
                    ' ',
                    context
                        .GetFiles(data.NuGetOutputPath.FullPath + "/*.nupkg")
                        .Select(path => path.FullPath.Quote())))))
.Then("GitHub-Actions")
.Run();
