/*****************************
 * Helpers
 *****************************/

public partial class Program
{
    static void Main_SetupExtensions()
    {
        if (BuildSystem.GitHubActions.IsRunningOnGitHubActions)
        {
            TaskSetup(context => BuildSystem.GitHubActions.Commands.StartGroup(context.Task.Name));
            TaskTeardown(context => BuildSystem.GitHubActions.Commands.EndGroup());
        }
    }
}

public static partial class CakeTaskBuilderExtensions
{
    private static readonly ExtensionHelper ExtensionHelper = new(Task, () => RunTarget(Argument("target", "Default")));

    public static CakeTaskBuilder Then(this CakeTaskBuilder cakeTaskBuilder, string name)
        => ExtensionHelper
            .TaskCreate(name)
            .IsDependentOn(cakeTaskBuilder);

    public static CakeReport Run(this CakeTaskBuilder cakeTaskBuilder)
        => ExtensionHelper.Run();

    public static CakeTaskBuilder Default(this CakeTaskBuilder cakeTaskBuilder)
    {
        ExtensionHelper
            .TaskCreate("Default")
            .IsDependentOn(cakeTaskBuilder);
        return cakeTaskBuilder;
    }
}
