using System.Text;

namespace CqrsLens;

/// <summary>Reads files up to a strict byte limit so oversized files or special streams cannot exhaust memory.</summary>
public static class BoundedFileReader
{
    public const long DefaultMaxBytes = 32 * 1024 * 1024;

    private const int ChunkSize = 81920;

    public static string ReadAllText(string path, long maxBytes = DefaultMaxBytes)
    {
        using var stream = ReadAllBytesStream(path, maxBytes);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return reader.ReadToEnd();
    }

    public static MemoryStream ReadAllBytesStream(string path, long maxBytes = DefaultMaxBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);

        var fileInfo = new FileInfo(path);
        if (fileInfo.Exists && fileInfo.Length > maxBytes)
        {
            throw new InvalidDataException(
                $"File '{path}' exceeds the maximum allowed size of {maxBytes} bytes ({fileInfo.Length} bytes).");
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.CanSeek && stream.Length > maxBytes)
        {
            throw new InvalidDataException(
                $"File '{path}' exceeds the maximum allowed size of {maxBytes} bytes ({stream.Length} bytes).");
        }

        var initialCapacity = fileInfo.Exists && fileInfo.Length > 0 && fileInfo.Length <= maxBytes
            ? (int)Math.Min(fileInfo.Length, DefaultMaxBytes)
            : 0;
        var output = new MemoryStream(initialCapacity);
        try
        {
            var buffer = new byte[ChunkSize];
            var totalRead = 0L;
            while (true)
            {
                var remaining = maxBytes - totalRead;
                var toRead = remaining >= buffer.Length ? buffer.Length : (int)remaining + 1;
                var bytesRead = stream.Read(buffer, 0, toRead);
                if (bytesRead == 0)
                    break;

                totalRead += bytesRead;
                if (totalRead > maxBytes)
                {
                    throw new InvalidDataException(
                        $"File '{path}' exceeds the maximum allowed size of {maxBytes} bytes.");
                }

                output.Write(buffer, 0, bytesRead);
            }

            output.Position = 0;
            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }
}
