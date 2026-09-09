namespace XbfKit.IO;

/// <summary>Encodes and decodes the persisted XBF1 node stream.</summary>
internal static class Xbf1NodeCodec
{
    /// <summary>Decodes XBF1 node bytes into an editable node sequence.</summary>
    internal static Xbf1NodeData Decode(ReadOnlySpan<byte> data)
    {
        var reader = new XbfBufferReader(data);
        var result = new Xbf1NodeData();

        while (reader.Remaining > 0)
        {
            var type = (Xbf1NodeType)reader.ReadByte();
            var node = type switch
            {
                Xbf1NodeType.StartObject or Xbf1NodeType.StartProperty or Xbf1NodeType.Text => new Xbf1Node()
                {
                    Type = type,
                    Reference = ReadReference(ref reader)
                },
                Xbf1NodeType.Namespace => new Xbf1Node()
                {
                    Type = type,
                    Reference = ReadReference(ref reader),
                    NamespacePrefix = reader.ReadXbfString(),
                },
                Xbf1NodeType.Value => new Xbf1Node()
                {
                    Type = type,
                    Value = ReadValue(ref reader)
                },
                Xbf1NodeType.LineInfo => new Xbf1Node()
                {
                    Type = type,
                    LineDelta = reader.ReadInt16(),
                    ColumnDelta = reader.ReadInt16(),
                },
                Xbf1NodeType.LineInfoAbsolute => new Xbf1Node()
                {
                    Type = type,
                    AbsoluteLine = reader.ReadUInt32(),
                    AbsoluteColumn = reader.ReadUInt32(),
                },
                Xbf1NodeType.EndObject or Xbf1NodeType.EndProperty or Xbf1NodeType.EndOfAttributes or Xbf1NodeType.EndOfStream or Xbf1NodeType.StartConditionalScope or Xbf1NodeType.EndConditionalScope or Xbf1NodeType.None => new Xbf1Node()
                {
                    Type = type
                },
                _ => throw new InvalidDataException($"Unknown XBF v1 node type 0x{(byte)type:X2}."),
            };

            result.Nodes.Add(node);
        }

        return result;
    }

    /// <summary>Encodes an editable XBF1 node sequence.</summary>
    internal static byte[] Encode(Xbf1NodeData nodeData)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.Unicode, leaveOpen: true);

        foreach (Xbf1Node node in nodeData.Nodes)
        {
            writer.Write((byte)node.Type);
            switch (node.Type)
            {
                case Xbf1NodeType.StartObject:
                case Xbf1NodeType.StartProperty:
                case Xbf1NodeType.Text:
                    WriteReference(writer, RequiredReference(node));
                    break;
                case Xbf1NodeType.Namespace:
                    WriteReference(writer, RequiredReference(node));
                    writer.WriteXbfString(node.NamespacePrefix ?? string.Empty);
                    break;
                case Xbf1NodeType.Value:
                    WriteValue(writer, node.Value ?? throw new InvalidDataException("A value node has no value."));
                    break;
                case Xbf1NodeType.LineInfo:
                    writer.Write(node.LineDelta);
                    writer.Write(node.ColumnDelta);
                    break;
                case Xbf1NodeType.LineInfoAbsolute:
                    writer.Write(node.AbsoluteLine);
                    writer.Write(node.AbsoluteColumn);
                    break;
                case Xbf1NodeType.EndObject:
                case Xbf1NodeType.EndProperty:
                case Xbf1NodeType.EndOfAttributes:
                case Xbf1NodeType.EndOfStream:
                case Xbf1NodeType.StartConditionalScope:
                case Xbf1NodeType.EndConditionalScope:
                case Xbf1NodeType.None:
                    break;
                default:
                    throw new InvalidDataException($"Cannot encode XBF1 node type 0x{(byte)node.Type:X2}.");
            }
        }

        return stream.ToArray();
    }

    private static Xbf1Reference ReadReference(ref XbfBufferReader reader)
    {
        return new(reader.ReadUInt32(), (Xbf1NodeFlags)reader.ReadUInt32());
    }

    private static void WriteReference(BinaryWriter writer, Xbf1Reference reference)
    {
        writer.Write(reference.ObjectId);
        writer.Write((uint)reference.Flags);
    }

    private static Xbf1Reference RequiredReference(Xbf1Node node)
    {
        return node.Reference ?? throw new InvalidDataException($"A {node.Type} node has no reference.");
    }

    private static Xbf1Value ReadValue(ref XbfBufferReader reader)
    {
        var type = (Xbf1ValueType)reader.ReadByte();
        object? value = type switch
        {
            Xbf1ValueType.None => ReadNullValuePadding(ref reader),
            Xbf1ValueType.BoolFalse => false,
            Xbf1ValueType.BoolTrue => true,
            Xbf1ValueType.Float or Xbf1ValueType.KeyTime or Xbf1ValueType.LengthConverter or Xbf1ValueType.Duration => reader.ReadSingle(),
            Xbf1ValueType.Signed => reader.ReadInt32(),
            Xbf1ValueType.String => reader.ReadXbfString(),
            Xbf1ValueType.Thickness => new XbfThickness(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
            Xbf1ValueType.GridLength => ReadGridLength(ref reader),
            Xbf1ValueType.Color => reader.ReadUInt32(),
            _ => throw new InvalidDataException($"Unknown XBF1 value type 0x{(byte)type:X2}."),
        };

        return new Xbf1Value(type, value);
    }

    private static object? ReadNullValuePadding(ref XbfBufferReader reader)
    {
        ReadOnlySpan<byte> padding = reader.ReadSpan(7);
        if (padding.IndexOfAnyExcept((byte)0) >= 0)
        {
            throw new InvalidDataException("An XBF null value has non-zero padding.");
        }

        return null;
    }

    private static XbfGridLength ReadGridLength(ref XbfBufferReader reader)
    {
        byte unitType = reader.ReadByte();
        ReadOnlySpan<byte> padding = reader.ReadSpan(3);
        if (padding[0] != 0 || padding[1] != 0 || padding[2] != 0)
        {
            throw new InvalidDataException("An XBF GridLength has non-zero padding.");
        }

        return new XbfGridLength(unitType, reader.ReadSingle());
    }

    private static void WriteValue(BinaryWriter writer, Xbf1Value value)
    {
        writer.Write((byte)value.Type);
        switch (value.Type)
        {
            case Xbf1ValueType.None:
                writer.Write(new byte[7]);
                return;
            case Xbf1ValueType.BoolFalse:
            case Xbf1ValueType.BoolTrue:
                return;
            case Xbf1ValueType.Float:
            case Xbf1ValueType.KeyTime:
            case Xbf1ValueType.LengthConverter:
            case Xbf1ValueType.Duration:
                writer.Write(RequireData<float>(value));
                return;
            case Xbf1ValueType.Signed:
                writer.Write(RequireData<int>(value));
                return;
            case Xbf1ValueType.String:
                writer.WriteXbfString(RequireData<string>(value));
                return;
            case Xbf1ValueType.Thickness:
                XbfThickness thickness = RequireData<XbfThickness>(value);
                writer.Write(thickness.Left);
                writer.Write(thickness.Top);
                writer.Write(thickness.Right);
                writer.Write(thickness.Bottom);
                return;
            case Xbf1ValueType.GridLength:
                XbfGridLength gridLength = RequireData<XbfGridLength>(value);
                writer.Write(gridLength.UnitType);
                writer.Write(new byte[3]);
                writer.Write(gridLength.Value);
                return;
            case Xbf1ValueType.Color:
                writer.Write(RequireData<uint>(value));
                return;
            default:
                throw new InvalidDataException($"Cannot encode XBF v1 value type 0x{(byte)value.Type:X2}.");
        }
    }

    private static T RequireData<T>(Xbf1Value value)
    {
        return value.Data is T data ? data : throw new InvalidDataException($"An XBF {value.Type} value has an invalid payload.");
    }
}
