namespace FolderSyncr.Services;

public sealed record BatchRunReport(int ExitCode, SyncRunResult Result);
