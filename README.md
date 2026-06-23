# FileSyncr

FileSyncr is a .NET 10 WPF folder synchronization tool inspired by FreeFileSync.

## Features

- Compare two folders by timestamp/size or SHA-256 content hash.
- Preview sync operations before writing files.
- Sync modes: two-way, mirror left to right, mirror right to left, update left to right, update right to left.
- Include and exclude wildcard filters.
- Conflict detection for same-timestamp files with different content.
- Cancellable compare and sync operations.
- Live operation log.

## Run

```powershell
dotnet run --project .\FileSyncr\FileSyncr.csproj
```
