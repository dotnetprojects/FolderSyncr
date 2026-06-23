# MTP Support Investigation

## Summary

FolderSyncr should not treat MTP devices as normal folders. Most MTP phones, cameras, and players do not expose drive letters or UNC paths, so the current `System.IO`-based scanner cannot enumerate, hash, copy, delete, or timestamp MTP items directly.

The practical Windows desktop implementation path is a dedicated storage adapter built on Windows Portable Devices (WPD). Microsoft's Portable Devices COM API sample demonstrates the operations FolderSyncr would need: enumerate devices, enumerate device content, read/write content properties, transfer content on or off a device, and receive portable-device events.

The WinRT `Windows.Devices.Portable.StorageDevice` API also identifies storage on WPD/MTP devices, but it is tied to Windows app capabilities such as `removableStorage`. That makes it less suitable as the first implementation path for this WPF desktop app than the WPD COM API.

## Relevant Sources

- Microsoft Portable Devices COM API sample: <https://learn.microsoft.com/en-us/samples/microsoft/windows-classic-samples/portable-devices-com-api/>
- Microsoft `Windows.Devices.Portable` namespace: <https://learn.microsoft.com/en-us/uwp/api/windows.devices.portable>
- Microsoft `StorageDevice` class: <https://learn.microsoft.com/en-us/uwp/api/windows.devices.portable.storagedevice>

## Proposed FolderSyncr Design

Add a storage abstraction below `SyncEngine` instead of pushing MTP logic into the existing file scanner:

```text
IStorageProvider
  LocalFileSystemStorageProvider
  PortableDeviceStorageProvider
```

The provider must support:

- Enumerating folders and files with stable relative paths.
- Reading item streams for copy and content-hash comparison.
- Writing item streams to a destination.
- Deleting items where the device allows deletion.
- Reading and setting best-effort metadata: size, modified time, and link/type flags.
- Returning stable item IDs separately from display paths, because MTP object IDs may be required for operations.

## Risks

- MTP devices often have weaker timestamp fidelity than NTFS.
- Some devices expose media libraries rather than a complete filesystem tree.
- Some devices may reject overwrite, delete, or timestamp writes.
- Copy verification should read back through WPD, which may be slow.
- Long-running transfers need cancellation and progress reporting at the provider boundary.

## Recommended Implementation Order

1. Introduce `IStorageProvider` while keeping local filesystem behavior unchanged.
2. Move the current local scan/copy/delete code behind `LocalFileSystemStorageProvider`.
3. Add a small WPD probe command or test utility that enumerates connected device names and root object IDs.
4. Implement read-only MTP compare support.
5. Add copy-to/from-MTP support.
6. Add delete support only after device capability checks.
7. Add UI path selection for portable devices.

## Current Decision

MTP support is feasible, but it is not a path-string feature. It should be implemented as a storage-provider layer using WPD COM APIs, with local filesystem behavior protected by tests before the engine is generalized.
