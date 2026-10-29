using System.Text;

namespace CqrsLens;

/// <summary>Reads and writes MCP's newline-delimited or Content-Length framed stdio messages.</summary>
internal static class McpMessageFraming
{
    private const int MaxContentLength = 32 * 1024 * 1024;

    public static McpMessage Read(TextReader reader)
    {
        while (true)
        {
            var line = reader.ReadLine();
            if (line is null)
                return new McpMessage(null, false);

            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (!line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                return new McpMessage(line, false);

            var lengthText = line["Content-Length:".Length..].Trim();
            if (!int.TryParse(lengthText, out var contentLength) || contentLength < 0)
                return new McpMessage(null, true);

            // Read the remaining headers up to the blank line.
            while (true)
            {
                var headerLine = reader.ReadLine();
                if (headerLine is null || headerLine.Length == 0)
                    break;
            }

            if (contentLength > MaxContentLength)
                return new McpMessage(null, true);

            return new McpMessage(ReadPayload(reader, contentLength), true);
        }
    }

    public static void Write(TextWriter writer, string json, bool useContentLength)
    {
        if (useContentLength)
        {
            var byteCount = Encoding.UTF8.GetByteCount(json);
            writer.Write($"Content-Length: {byteCount}\r\n\r\n{json}");
        }
        else
        {
            writer.WriteLine(json);
        }

        writer.Flush();
    }

    /// <summary>
    /// Content-Length is a UTF-8 byte count while TextReader exposes characters,
    /// so read character by character (keeping surrogate pairs together) until
    /// the encoded byte count is reached.
    /// </summary>
    private static string? ReadPayload(TextReader reader, int contentLength)
    {
        var payload = new StringBuilder(Math.Min(contentLength, 64 * 1024));
        var bytesRead = 0;
        while (bytesRead < contentLength)
        {
            var next = reader.Read();
            if (next < 0)
                return null;

            if (char.IsHighSurrogate((char)next))
            {
                var low = reader.Read();
                if (low < 0)
                    return null;

                payload.Append((char)next).Append((char)low);
                bytesRead += 4;
                continue;
            }

            payload.Append((char)next);
            bytesRead += next < 0x80 ? 1 : next < 0x800 ? 2 : 3;
        }

        return payload.ToString();
    }
}
