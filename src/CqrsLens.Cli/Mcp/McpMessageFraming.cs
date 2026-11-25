using System.Globalization;
using System.Text;

namespace CqrsLens;

/// <summary>Reads and writes MCP's newline-delimited or Content-Length framed stdio messages.</summary>
internal static class McpMessageFraming
{
    internal const int MaxContentLength = 32 * 1024 * 1024;
    internal const int MaxHeaderLineBytes = 8 * 1024;
    internal const int MaxHeaders = 32;

    private const int MaxHeaderDrainLines = 256;

    public static McpMessage Read(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        while (true)
        {
            var (line, exceeded, isHeader) = ReadBoundedLine(reader, MaxContentLength, detectHeader: true);
            if (exceeded)
            {
                if (isHeader)
                {
                    DrainHeadersToBlankLine(reader);
                    return new McpMessage(null, true, $"MCP header line exceeds the {MaxHeaderLineBytes}-byte limit.");
                }

                return new McpMessage(null, false, $"Message exceeds the {MaxContentLength}-byte stdio limit.");
            }

            if (line is null)
                return new McpMessage(null, false);

            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (!IsHeaderLine(line))
                return new McpMessage(line, false);

            return ReadHeaderFramedMessage(reader, line);
        }
    }

    public static void Write(TextWriter writer, string json, bool useContentLength)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(json);

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

    private static McpMessage ReadHeaderFramedMessage(TextReader reader, string firstHeaderLine)
    {
        int? contentLength = null;
        string? headerError = null;
        var headerCount = 1;

        ProcessHeaderLine(firstHeaderLine, ref contentLength, ref headerError);

        while (true)
        {
            var (headerLine, lineExceeded, _) = ReadBoundedLine(reader, MaxHeaderLineBytes, detectHeader: false);
            if (lineExceeded)
            {
                headerError ??= $"MCP header line exceeds the {MaxHeaderLineBytes}-byte limit.";
                headerCount++;
                if (headerCount > MaxHeaderDrainLines)
                    return new McpMessage(null, true, headerError);
                continue;
            }

            if (headerLine is null)
                return new McpMessage(null, true, headerError ?? "Unexpected EOF while reading MCP headers.");

            if (headerLine.Length == 0)
                break;

            headerCount++;
            if (headerCount > MaxHeaders)
            {
                headerError ??= $"MCP message exceeds the maximum of {MaxHeaders} headers.";
                if (headerCount > MaxHeaderDrainLines)
                    return new McpMessage(null, true, headerError);
                continue;
            }

            ProcessHeaderLine(headerLine, ref contentLength, ref headerError);
        }

        if (headerError is not null)
            return new McpMessage(null, true, headerError);

        if (!contentLength.HasValue)
            return new McpMessage(null, true, "Missing Content-Length header.");

        var (payload, payloadError) = ReadPayload(reader, contentLength.Value);
        return payloadError is not null
            ? new McpMessage(null, true, payloadError)
            : new McpMessage(payload, true);
    }

    private static void ProcessHeaderLine(string headerLine, ref int? contentLength, ref string? headerError)
    {
        var colonIndex = headerLine.IndexOf(':');
        if (colonIndex <= 0)
        {
            headerError ??= "Malformed MCP header line.";
            return;
        }

        var name = headerLine[..colonIndex];
        if (!IsValidHeaderName(name))
        {
            headerError ??= "Malformed MCP header name.";
            return;
        }

        if (!string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase))
            return;

        if (contentLength.HasValue)
        {
            headerError ??= "Duplicate Content-Length header.";
            return;
        }

        var value = headerLine[(colonIndex + 1)..].Trim();
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedLength))
        {
            headerError ??= "Invalid Content-Length header value.";
            return;
        }

        if (parsedLength > MaxContentLength)
        {
            headerError ??= $"Content-Length {parsedLength} exceeds the {MaxContentLength}-byte limit.";
            return;
        }

        contentLength = (int)parsedLength;
    }

    private static void DrainHeadersToBlankLine(TextReader reader)
    {
        for (var i = 0; i < MaxHeaderDrainLines; i++)
        {
            var (headerLine, exceeded, _) = ReadBoundedLine(reader, MaxHeaderLineBytes, detectHeader: false);
            if (!exceeded && (headerLine is null || headerLine.Length == 0))
                break;
        }
    }

    private static (string? Line, bool Exceeded, bool IsHeader) ReadBoundedLine(
        TextReader reader,
        int maxBytes,
        bool detectHeader)
    {
        var line = new StringBuilder();
        var bytesRead = 0;
        var previousHighSurrogate = false;
        var exceeded = false;
        var isHeader = false;
        var validHeaderNamePrefix = detectHeader;
        var effectiveLimit = maxBytes;
        int next;

        while ((next = reader.Read()) >= 0 && next is not ('\r' or '\n'))
        {
            if (exceeded)
                continue; // Drain this line without buffering so the next message can still be read.

            var character = (char)next;
            // A high surrogate was counted as three bytes. Its matching low surrogate adds one.
            bytesRead += next < 0x80 || (previousHighSurrogate && char.IsLowSurrogate(character))
                ? 1
                : next < 0x800 ? 2 : 3;

            if (detectHeader && !isHeader)
            {
                if (character == ':' && validHeaderNamePrefix && line.Length > 0 && reader.Peek() != '/')
                {
                    isHeader = true;
                    effectiveLimit = MaxHeaderLineBytes;
                }
                else
                {
                    validHeaderNamePrefix &= line.Length == 0
                        ? char.IsAsciiLetter(character)
                        : char.IsAsciiLetterOrDigit(character) || character == '-';
                }
            }

            exceeded = bytesRead > effectiveLimit;
            if (!exceeded)
                line.Append(character);
            previousHighSurrogate = char.IsHighSurrogate(character);
        }

        if (next == '\r' && reader.Peek() == '\n')
            reader.Read();
        if (exceeded || (next < 0 && line.Length == 0))
            return (null, exceeded, isHeader);
        return (line.ToString(), false, isHeader);
    }

    private static bool IsHeaderLine(string line)
    {
        var colonIndex = line.IndexOf(':');
        if (colonIndex <= 0)
            return false;

        if (!IsValidHeaderName(line.AsSpan(0, colonIndex)))
            return false;

        if (colonIndex + 2 < line.Length && line[colonIndex + 1] == '/' && line[colonIndex + 2] == '/')
            return false;

        return true;
    }

    private static bool IsValidHeaderName(ReadOnlySpan<char> name)
    {
        if (name.Length == 0 || !char.IsAsciiLetter(name[0]))
            return false;

        for (var i = 1; i < name.Length; i++)
        {
            var c = name[i];
            if (!char.IsAsciiLetterOrDigit(c) && c != '-')
                return false;
        }

        return true;
    }

    /// <summary>
    /// Content-Length is a UTF-8 byte count while TextReader exposes characters,
    /// so read character by character (keeping valid surrogate pairs together) until
    /// the encoded byte count is reached.
    /// </summary>
    private static (string? Payload, string? Error) ReadPayload(TextReader reader, int contentLength)
    {
        var payload = new StringBuilder(Math.Min(contentLength, 64 * 1024));
        var bytesRead = 0;
        while (bytesRead < contentLength)
        {
            var next = reader.Read();
            if (next < 0)
                return (null, "Unexpected EOF while reading MCP payload.");

            var ch = (char)next;
            if (char.IsHighSurrogate(ch))
            {
                var peek = reader.Peek();
                if (peek >= 0 && char.IsLowSurrogate((char)peek))
                {
                    var low = reader.Read();
                    payload.Append(ch).Append((char)low);
                    bytesRead += 4;
                    continue;
                }

                payload.Append(ch);
                bytesRead += 3;
                continue;
            }

            payload.Append(ch);
            bytesRead += next < 0x80 ? 1 : next < 0x800 ? 2 : 3;
        }

        if (bytesRead > contentLength)
            return (null, "MCP payload UTF-8 byte length does not match Content-Length.");

        return (payload.ToString(), null);
    }
}
