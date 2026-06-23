namespace FolderSyncr.Services;

public enum RemoteSyncProtocol
{
    Sftp,
    Ftp
}

public sealed record RemoteSyncRoot(
    RemoteSyncProtocol Protocol,
    Uri Uri,
    string Host,
    int Port,
    string Username,
    string Password,
    string RootPath)
{
    public static bool TryParse(string value, out RemoteSyncRoot? root)
    {
        root = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var protocol = uri.Scheme.ToLowerInvariant() switch
        {
            "sftp" => RemoteSyncProtocol.Sftp,
            "ftp" => RemoteSyncProtocol.Ftp,
            _ => (RemoteSyncProtocol?)null
        };
        if (protocol is null)
        {
            return false;
        }

        var userInfo = uri.UserInfo.Split(':', 2);
        var username = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : string.Empty;
        var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty;
        var port = uri.IsDefaultPort ? protocol == RemoteSyncProtocol.Sftp ? 22 : 21 : uri.Port;
        var path = string.IsNullOrWhiteSpace(uri.AbsolutePath) ? "/" : Uri.UnescapeDataString(uri.AbsolutePath);

        root = new RemoteSyncRoot(protocol.Value, uri, uri.Host, port, username, password, NormalizeDirectory(path));
        return true;
    }

    public static bool IsRemotePath(string value)
    {
        return TryParse(value, out _);
    }

    public string Combine(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return RootPath;
        }

        return CombineRemotePath(RootPath, relativePath.Replace('\\', '/'));
    }

    public string ToDisplayPath(string remotePath)
    {
        var relativePath = GetRelativePath(remotePath);
        var builder = new UriBuilder(Uri)
        {
            Password = string.IsNullOrEmpty(Password) ? string.Empty : "***",
            Path = Combine(remotePath == RootPath ? string.Empty : relativePath)
        };
        return builder.Uri.ToString();
    }

    public string GetRelativePath(string remotePath)
    {
        var normalizedRemote = NormalizeFilePath(remotePath);
        var root = RootPath.TrimEnd('/');
        if (string.Equals(normalizedRemote, root, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var prefix = root == string.Empty ? "/" : $"{root}/";
        return normalizedRemote.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? normalizedRemote[prefix.Length..]
            : normalizedRemote.TrimStart('/');
    }

    public static string CombineRemotePath(string directory, string name)
    {
        return $"{NormalizeDirectory(directory).TrimEnd('/')}/{name.TrimStart('/')}";
    }

    public static string NormalizeDirectory(string path)
    {
        var normalized = NormalizeFilePath(path);
        return string.IsNullOrEmpty(normalized) ? "/" : normalized;
    }

    private static string NormalizeFilePath(string path)
    {
        var normalized = path.Replace('\\', '/');
        while (normalized.Contains("//", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
        }

        return normalized == "/" ? "/" : normalized.TrimEnd('/');
    }
}
