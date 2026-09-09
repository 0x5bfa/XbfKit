using System.Text;

namespace XbfKit.IO;

/// <summary>Writes validated in-memory XBF documents in their requested format version.</summary>
public static class XbfDocumentWriter
{
    /// <summary>Writes an XBF document at the current destination stream position.</summary>
    /// <param name="document">The document to serialize.</param>
    /// <param name="stream">The writable destination stream.</param>
    public static void Write(XbfDocument document, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanWrite)
        {
            throw new ArgumentException("The output stream must be writable.", nameof(stream));
        }

        ValidateDocument(document);
        byte[] metadataBytes = BuildMetadata(document);
        byte[] nodeBytes = BuildNodes(document);

        using var writer = new BinaryWriter(stream, Encoding.Unicode, leaveOpen: true);
        writer.Write("XBF\0"u8);
        writer.Write(checked((uint)metadataBytes.Length));
        writer.Write(checked((uint)nodeBytes.Length));
        writer.Write(metadataBytes);
        writer.Write(nodeBytes);
    }

    private static byte[] BuildMetadata(XbfDocument document)
    {
        byte[][] tables =
        [
            BuildStringTable(document.Metadata.Strings, document.Version.NullTerminatesMetadataStrings),
            BuildTable(document.Metadata.Assemblies, static (w, value) =>
            {
                w.Write(value.ProviderKind);
                w.Write(value.NameStringId);
            }),
            BuildTable(document.Metadata.TypeNamespaces, static (w, value) =>
            {
                w.Write(value.AssemblyId);
                w.Write(value.NameStringId);
            }),
            BuildTable(document.Metadata.Types, static (w, value) =>
            {
                w.Write((uint)value.Flags);
                w.Write(value.NamespaceId);
                w.Write(value.NameStringId);
            }),
            BuildTable(document.Metadata.Properties, static (w, value) =>
            {
                w.Write((uint)value.Flags);
                w.Write(value.DeclaringTypeId);
                w.Write(value.NameStringId);
            }),
            BuildTable(document.Metadata.XmlNamespaces, static (w, value) => w.Write(value)),
        ];

        ulong[] offsets = new ulong[tables.Length];
        ulong nextOffset = 120;
        for (int index = 0; index < tables.Length; index++)
        {
            offsets[index] = nextOffset;
            nextOffset = checked(nextOffset + (uint)tables[index].Length);
        }

        using var stream = new MemoryStream(checked((int)nextOffset));
        using var writer = new BinaryWriter(stream, Encoding.Unicode, leaveOpen: true);
        writer.Write(document.Version.Major);
        writer.Write(document.Version.Minor);
        foreach (ulong offset in offsets)
        {
            writer.Write(offset);
        }

        writer.Write(document.Hash);
        foreach (byte[] table in tables)
        {
            writer.Write(table);
        }

        return stream.ToArray();
    }

    private static byte[] BuildStringTable(IReadOnlyCollection<string> strings, bool nullTerminated)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Unicode, leaveOpen: true);
        writer.Write(checked((uint)strings.Count));
        foreach (string value in strings)
        {
            writer.WriteXbfString(value, nullTerminated);
        }

        return stream.ToArray();
    }

    private static byte[] BuildTable<T>(IReadOnlyCollection<T> values, Action<BinaryWriter, T> write)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Unicode, leaveOpen: true);
        writer.Write(checked((uint)values.Count));
        foreach (T value in values)
        {
            write(writer, value);
        }

        return stream.ToArray();
    }

    private static byte[] BuildNodes(XbfDocument document)
    {
        return document.Nodes switch
        {
            Xbf1NodeData v1 when document.Version.Major == 1 => Xbf1NodeCodec.Encode(v1),
            Xbf2NodeData v2 when document.Version.Major == 2 => BuildXbf2Nodes(v2),
            _ => throw new InvalidDataException($"The node model does not match XBF version {document.Version}."),
        };
    }

    private static byte[] BuildXbf2Nodes(Xbf2NodeData nodeData)
    {
        using var payload = new MemoryStream();
        var offsets = new (uint Node, uint Line)[nodeData.Substreams.Count];
        for (int index = 0; index < nodeData.Substreams.Count; index++)
        {
            Xbf2Substream substream = nodeData.Substreams[index];
            offsets[index].Node = checked((uint)payload.Position);
            payload.Write(substream.NodeBytes);
            offsets[index].Line = checked((uint)payload.Position);
            payload.Write(substream.LineBytes);
        }

        using var result = new MemoryStream();
        using var writer = new BinaryWriter(result, Encoding.Unicode, leaveOpen: true);
        writer.Write(checked((uint)nodeData.Substreams.Count));
        foreach ((uint node, uint line) in offsets)
        {
            writer.Write(node);
            writer.Write(line);
        }

        payload.Position = 0;
        payload.CopyTo(result);
        return result.ToArray();
    }

    private static void ValidateDocument(XbfDocument document)
    {
        if (!document.Version.IsSupported)
        {
            throw new NotSupportedException($"XBF version {document.Version} is not supported.");
        }

        if (document.Hash.Length != 64)
        {
            throw new InvalidDataException("The XBF metadata hash must contain exactly 64 bytes.");
        }

        if (document.Version.Major == 2 && document.Nodes is Xbf2NodeData { Substreams.Count: 0 })
        {
            throw new InvalidDataException("An XBF v2 document must contain at least one substream.");
        }
    }
}
