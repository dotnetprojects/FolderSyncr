namespace FolderSyncr.Services;

public sealed record FreeFileSyncGlobalSettings(
    int? FileTimeToleranceSeconds,
    bool? VerifyCopiedFiles);
