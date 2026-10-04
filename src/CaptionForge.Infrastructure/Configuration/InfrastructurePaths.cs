namespace CaptionForge.Infrastructure.Configuration;

public static class InfrastructurePaths
{
    public static string DefaultCapCutUserData => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CapCut","User Data");
    public static string DefaultCapCutProjects => Path.Combine(DefaultCapCutUserData,"Projects","com.lveditor.draft");
    public static string DefaultWorkspace => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CaptionForge","Projects");
    public static string DefaultSettings => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CaptionForge","settings.json");
}
