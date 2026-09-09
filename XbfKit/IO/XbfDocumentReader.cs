using System.Diagnostics;

namespace XbfKit.IO;

/// <summary>Reads validated XBF1, XBF2, and XBF2.1 documents.</summary>
public static class XbfDocumentReader
{
    private static ReadOnlySpan<byte> Magic => "XBF\0"u8;

    /// <summary>Reads one complete XBF document from the current stream position.</summary>
    /// <param name="stream">The readable source stream.</param>
    /// <returns>The parsed XBF document.</returns>
    public static XbfDocument Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        byte[] fileBytes = ReadAllBytes(stream);
        var reader = new XbfBufferReader(fileBytes);
        if (!reader.ReadSpan(4).SequenceEqual(Magic))
        {
            throw new InvalidDataException("The stream does not start with the XBF magic value.");
        }

        int metadataSize = CheckedSize(reader.ReadUInt32(), "metadata");
        int nodeSize = CheckedSize(reader.ReadUInt32(), "node");
        int expectedSize = checked(12 + metadataSize + nodeSize);
        if (fileBytes.Length != expectedSize)
        {
            throw new InvalidDataException($"The XBF header declares {expectedSize} bytes, but the stream contains {fileBytes.Length} bytes.");
        }

        ReadOnlySpan<byte> metadataBytes = reader.ReadSpan(metadataSize);
        ReadOnlySpan<byte> nodeBytes = reader.ReadSpan(nodeSize);
        return ReadSections(metadataBytes, nodeBytes);
    }

    private static XbfDocument ReadSections(ReadOnlySpan<byte> metadataBytes, ReadOnlySpan<byte> nodeBytes)
    {
        if (metadataBytes.Length < 120)
        {
            throw new InvalidDataException("The XBF metadata header is truncated.");
        }

        var header = new XbfBufferReader(metadataBytes);
        var version = new XbfVersion(header.ReadUInt32(), header.ReadUInt32());
        if (!version.IsSupported)
        {
            throw new NotSupportedException($"XBF version {version} is not supported.");
        }

        ulong[] tableOffsets = new ulong[6];
        for (int index = 0; index < tableOffsets.Length; index++)
        {
            tableOffsets[index] = header.ReadUInt64();
            if (tableOffsets[index] < 120 || tableOffsets[index] > (ulong)metadataBytes.Length)
            {
                throw new InvalidDataException($"Metadata table offset {tableOffsets[index]} is outside the metadata section.");
            }
        }

        byte[] hash = header.ReadBytes(64);
        var metadata = new XbfMetadata();
        ReadStrings(metadataBytes, tableOffsets[0], version, metadata.Strings);
        ReadFixedTable(metadataBytes, tableOffsets[1], metadata.Assemblies, static (ref r) => new XbfAssemblyEntry(r.ReadUInt32(), r.ReadUInt32()));
        ReadFixedTable(metadataBytes, tableOffsets[2], metadata.TypeNamespaces, static (ref r) => new XbfTypeNamespaceEntry(r.ReadUInt32(), r.ReadUInt32()));
        ReadFixedTable(metadataBytes, tableOffsets[3], metadata.Types, static (ref r) => new XbfTypeEntry((XbfTypeFlags)r.ReadUInt32(), r.ReadUInt32(), r.ReadUInt32()));
        ReadFixedTable(metadataBytes, tableOffsets[4], metadata.Properties, static (ref r) => new XbfPropertyEntry((XbfPropertyFlags)r.ReadUInt32(), r.ReadUInt32(), r.ReadUInt32()));
        ReadFixedTable(metadataBytes, tableOffsets[5], metadata.XmlNamespaces, static (ref r) => r.ReadUInt32());
        ValidateMetadata(metadata);

        XbfNodeData nodes = version.Major switch
        {
            1 => Xbf1NodeCodec.Decode(nodeBytes),
            2 => ReadXbf2NodeData(nodeBytes),
            _ => throw new UnreachableException(),
        };

        return new()
        {
            Version = version,
            Hash = hash,
            Metadata = metadata,
            Nodes = nodes,
        };
    }

    private static void ReadStrings(ReadOnlySpan<byte> metadataBytes, ulong offset, XbfVersion version, List<string> target)
    {
        var reader = ReaderAt(metadataBytes, offset);
        int count = CheckedCount(reader.ReadUInt32(), "string");
        for (int index = 0; index < count; index++)
        {
            target.Add(reader.ReadXbfString(version.NullTerminatesMetadataStrings));
        }
    }

    private delegate T TableItemReader<T>(ref XbfBufferReader reader);

    private static void ReadFixedTable<T>(ReadOnlySpan<byte> metadataBytes, ulong offset, List<T> target, TableItemReader<T> itemReader)
    {
        var reader = ReaderAt(metadataBytes, offset);
        int count = CheckedCount(reader.ReadUInt32(), typeof(T).Name);
        for (int index = 0; index < count; index++)
        {
            target.Add(itemReader(ref reader));
        }
    }

    private static XbfBufferReader ReaderAt(ReadOnlySpan<byte> buffer, ulong offset)
    {
        return new XbfBufferReader(buffer) { Position = checked((int)offset) };
    }

    private static Xbf2NodeData ReadXbf2NodeData(ReadOnlySpan<byte> nodeBytes)
    {
        var reader = new XbfBufferReader(nodeBytes);
        int count = CheckedCount(reader.ReadUInt32(), "substream");
        int tableSize = checked(4 + count * 8);
        if (tableSize > nodeBytes.Length)
        {
            throw new InvalidDataException("The XBF v2 substream table is truncated.");
        }

        var offsets = new (uint Node, uint Line)[count];
        for (int index = 0; index < count; index++)
        {
            offsets[index] = (reader.ReadUInt32(), reader.ReadUInt32());
        }

        int payloadSize = nodeBytes.Length - tableSize;
        var result = new Xbf2NodeData();
        for (int index = 0; index < count; index++)
        {
            int nodeStart = CheckedRelativeOffset(offsets[index].Node, payloadSize);
            int lineStart = CheckedRelativeOffset(offsets[index].Line, payloadSize);
            int streamEnd = index + 1 < count ? CheckedRelativeOffset(offsets[index + 1].Node, payloadSize) : payloadSize;
            if (nodeStart > lineStart || lineStart > streamEnd)
            {
                throw new InvalidDataException($"XBF v2 substream {index} has non-monotonic offsets.");
            }

            ReadOnlySpan<byte> payload = nodeBytes[tableSize..];
            result.Substreams.Add(new Xbf2Substream
            {
                NodeBytes = payload[nodeStart..lineStart].ToArray(),
                LineBytes = payload[lineStart..streamEnd].ToArray(),
            });
        }

        return result;
    }

    private static int CheckedRelativeOffset(uint offset, int payloadSize)
    {
        if (offset > payloadSize)
        {
            throw new InvalidDataException($"A substream offset ({offset}) is outside the v2 node payload ({payloadSize}).");
        }

        return (int)offset;
    }

    private static void ValidateMetadata(XbfMetadata metadata)
    {
        static void Check(uint id, int count, string description)
        {
            if (id >= count)
            {
                throw new InvalidDataException($"{description} identifier {id} is outside a table of {count} entries.");
            }
        }

        foreach (XbfAssemblyEntry assembly in metadata.Assemblies)
        {
            Check(assembly.NameStringId, metadata.Strings.Count, "Assembly name string");
        }

        foreach (XbfTypeNamespaceEntry typeNamespace in metadata.TypeNamespaces)
        {
            Check(typeNamespace.AssemblyId, metadata.Assemblies.Count, "Assembly");
            Check(typeNamespace.NameStringId, metadata.Strings.Count, "Type namespace string");
        }

        foreach (XbfTypeEntry type in metadata.Types)
        {
            if (!type.Flags.HasFlag(XbfTypeFlags.IsMarkupDirective))
            {
                int namespaceCount = type.Flags.HasFlag(XbfTypeFlags.IsUnknown) ? metadata.XmlNamespaces.Count : metadata.TypeNamespaces.Count;
                Check(type.NamespaceId, namespaceCount, "Type namespace");
            }

            Check(type.NameStringId, metadata.Strings.Count, "Type name string");
        }

        foreach (XbfPropertyEntry property in metadata.Properties)
        {
            if (!property.Flags.HasFlag(XbfPropertyFlags.IsMarkupDirective) && !property.Flags.HasFlag(XbfPropertyFlags.IsImplicitProperty))
            {
                Check(property.DeclaringTypeId, metadata.Types.Count, "Declaring type");
            }

            Check(property.NameStringId, metadata.Strings.Count, "Property name string");
        }

        foreach (uint namespaceStringId in metadata.XmlNamespaces)
        {
            Check(namespaceStringId, metadata.Strings.Count, "XML namespace string");
        }
    }

    private static byte[] ReadAllBytes(Stream stream)
    {
        if (stream.CanSeek)
        {
            long remaining = stream.Length - stream.Position;
            if (remaining < 0)
            {
                throw new InvalidDataException("The XBF stream position is past its declared length.");
            }

            if (remaining > int.MaxValue)
            {
                throw new InvalidDataException("XBF files larger than 2 GiB are not supported.");
            }

            byte[] result = new byte[(int)remaining];
            stream.ReadExactly(result);
            return result;
        }

        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        if (copy.Length > int.MaxValue)
        {
            throw new InvalidDataException("XBF files larger than 2 GiB are not supported.");
        }

        return copy.ToArray();
    }

    private static int CheckedSize(uint value, string description)
    {
        if (value > int.MaxValue)
        {
            throw new InvalidDataException($"The XBF {description} section is too large.");
        }

        return (int)value;
    }

    private static int CheckedCount(uint value, string description)
    {
        if (value > 16_777_216)
        {
            throw new InvalidDataException($"The XBF {description} table count is unreasonable: {value}.");
        }

        return (int)value;
    }
}
