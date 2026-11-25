using System.Xml;
using System.Xml.Linq;

namespace CqrsLens;

/// <summary>Loads XML documents safely with DTDs prohibited, external resolution disabled, and strict size bounds.</summary>
public static class SafeXmlLoader
{
    public const long DefaultMaxBytes = 10 * 1024 * 1024;

    private const long MaxEntityCharacters = 1024;

    public static XDocument LoadDocument(string path, long maxBytes = DefaultMaxBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);

        using var stream = BoundedFileReader.ReadAllBytesStream(path, maxBytes);
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = maxBytes,
            MaxCharactersFromEntities = MaxEntityCharacters,
        };
        using var reader = XmlReader.Create(stream, settings);
        return XDocument.Load(reader);
    }

    public static XDocument ParseDocument(TextReader reader, long maxCharacters = DefaultMaxBytes)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCharacters);

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = maxCharacters,
            MaxCharactersFromEntities = MaxEntityCharacters,
        };
        using var xmlReader = XmlReader.Create(reader, settings);
        return XDocument.Load(xmlReader);
    }
}
