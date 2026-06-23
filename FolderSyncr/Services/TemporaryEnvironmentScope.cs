namespace FolderSyncr.Services;

public sealed class TemporaryEnvironmentScope : IDisposable
{
    private readonly Dictionary<string, string?> _originalValues;

    public TemporaryEnvironmentScope(IReadOnlyDictionary<string, string>? variables)
    {
        _originalValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (variables is null)
        {
            return;
        }

        foreach (var (name, value) in variables.Where(variable => !string.IsNullOrWhiteSpace(variable.Key)))
        {
            _originalValues.TryAdd(name, Environment.GetEnvironmentVariable(name));
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    public void Dispose()
    {
        foreach (var (name, value) in _originalValues)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }
}
