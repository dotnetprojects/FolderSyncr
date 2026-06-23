using System.IO;
using System.Management;

namespace FolderSyncr.Services;

internal sealed class VolumeShadowCopyService
{
    public Stream OpenRead(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var volumeRoot = Path.GetPathRoot(fullPath)
            ?? throw new InvalidOperationException($"Cannot determine the volume for {path}.");
        var relativePath = Path.GetRelativePath(volumeRoot, fullPath);
        var shadow = CreateShadowCopy(volumeRoot);
        try
        {
            var deviceObject = shadow.DeviceObject.TrimEnd('\\');
            var shadowPath = Path.Combine(deviceObject + "\\", relativePath);
            var stream = File.Open(shadowPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return new ShadowCopyReadStream(stream, shadow);
        }
        catch
        {
            shadow.Dispose();
            throw;
        }
    }

    private static ShadowCopyHandle CreateShadowCopy(string volumeRoot)
    {
        using var shadowCopyClass = new ManagementClass("Win32_ShadowCopy");
        using var inParameters = shadowCopyClass.GetMethodParameters("Create");
        inParameters["Volume"] = volumeRoot;
        inParameters["Context"] = "ClientAccessible";

        using var outParameters = shadowCopyClass.InvokeMethod("Create", inParameters, null)
            ?? throw new InvalidOperationException("The Volume Shadow Copy Service did not return a result.");
        var returnValue = Convert.ToUInt32(outParameters["ReturnValue"], System.Globalization.CultureInfo.InvariantCulture);
        if (returnValue != 0)
        {
            throw new InvalidOperationException($"Volume Shadow Copy creation failed with VSS code {returnValue}.");
        }

        var shadowId = Convert.ToString(outParameters["ShadowID"], System.Globalization.CultureInfo.InvariantCulture)
            ?? throw new InvalidOperationException("The Volume Shadow Copy Service did not return a shadow copy ID.");
        using var searcher = new ManagementObjectSearcher($"SELECT * FROM Win32_ShadowCopy WHERE ID = '{shadowId.Replace("'", "''", StringComparison.Ordinal)}'");
        var shadow = searcher.Get().OfType<ManagementObject>().FirstOrDefault()
            ?? throw new InvalidOperationException("The created Volume Shadow Copy could not be found.");
        var deviceObject = Convert.ToString(shadow["DeviceObject"], System.Globalization.CultureInfo.InvariantCulture)
            ?? throw new InvalidOperationException("The created Volume Shadow Copy did not expose a device object.");

        return new ShadowCopyHandle(shadow, deviceObject);
    }

    private sealed class ShadowCopyHandle(ManagementObject shadowCopy, string deviceObject) : IDisposable
    {
        private bool _disposed;

        public string DeviceObject { get; } = deviceObject;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                shadowCopy.Delete();
            }
            finally
            {
                shadowCopy.Dispose();
            }
        }
    }

    private sealed class ShadowCopyReadStream(Stream inner, ShadowCopyHandle shadowCopy) : Stream
    {
        private bool _disposed;

        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => inner.CanSeek;

        public override bool CanWrite => false;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush()
        {
            inner.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return inner.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            return inner.Read(buffer);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return inner.ReadAsync(buffer, cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return inner.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                inner.Dispose();
                shadowCopy.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                await inner.DisposeAsync();
                shadowCopy.Dispose();
            }

            GC.SuppressFinalize(this);
        }
    }
}
