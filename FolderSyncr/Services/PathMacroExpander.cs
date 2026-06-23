namespace FolderSyncr.Services;

public static class PathMacroExpander
{
    public static string Expand(string path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? path
            : Environment.ExpandEnvironmentVariables(path);
    }
}
