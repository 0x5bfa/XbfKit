using System.Buffers.Binary;
using System.Text;

namespace XbfKit.IO;

/// <summary>Provides bounds-checked primitive reads over an in-memory XBF buffer.</summary>
internal ref struct XbfBufferReader
{
    private readonly ReadOnlySpan<byte> _buffer;

    /// <summary>Initializes a reader over the specified buffer.</summary>
    internal XbfBufferReader(ReadOnlySpan<byte> buffer)
    {
        _buffer = buffer;
    }

    /// <summary>Gets or sets the current byte offset.</summary>
    internal int Position { get; set; }

    /// <summary>Gets the number of unread bytes.</summary>
    internal readonly int Remaining => _buffer.Length - Position;

    /// <summary>Reads one byte.</summary>
    internal byte ReadByte()
    {
        EnsureAvailable(1);
        return _buffer[Position++];
    }

    /// <summary>Reads a little-endian unsigned 16-bit integer.</summary>
    internal ushort ReadUInt16()
    {
        EnsureAvailable(sizeof(ushort));
        ushort value = BinaryPrimitives.ReadUInt16LittleEndian(_buffer[Position..]);
        Position += sizeof(ushort);
        return value;
    }

    /// <summary>Reads a little-endian signed 16-bit integer.</summary>
    internal short ReadInt16()
    {
        return unchecked((short)ReadUInt16());
    }

    /// <summary>Reads a little-endian unsigned 32-bit integer.</summary>
    internal uint ReadUInt32()
    {
        EnsureAvailable(sizeof(uint));
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(_buffer[Position..]);
        Position += sizeof(uint);
        return value;
    }

    /// <summary>Reads a little-endian signed 32-bit integer.</summary>
    internal int ReadInt32()
    {
        return unchecked((int)ReadUInt32());
    }

    /// <summary>Reads a little-endian unsigned 64-bit integer.</summary>
    internal ulong ReadUInt64()
    {
        EnsureAvailable(sizeof(ulong));
        ulong value = BinaryPrimitives.ReadUInt64LittleEndian(_buffer[Position..]);
        Position += sizeof(ulong);
        return value;
    }

    /// <summary>Reads a little-endian single-precision floating-point value.</summary>
    internal float ReadSingle()
    {
        return BitConverter.Int32BitsToSingle(ReadInt32());
    }

    /// <summary>Reads and copies the requested number of bytes.</summary>
    internal byte[] ReadBytes(int count)
    {
        EnsureAvailable(count);
        byte[] result = _buffer.Slice(Position, count).ToArray();
        Position += count;
        return result;
    }

    /// <summary>Reads an XBF UTF-16 string with its encoded length.</summary>
    internal string ReadXbfString(bool nullTerminated = false)
    {
        uint codeUnitCount = ReadUInt32();
        int byteCount = checked((int)codeUnitCount * sizeof(char));
        EnsureAvailable(checked(byteCount + (nullTerminated ? sizeof(char) : 0)));
        string result = Encoding.Unicode.GetString(_buffer.Slice(Position, byteCount));
        Position += byteCount;

        if (nullTerminated && ReadUInt16() != 0)
        {
            throw new InvalidDataException("An XBF 2.1 metadata string has a non-zero terminator.");
        }

        return result;
    }

    /// <summary>Reads a span of the requested length without copying it.</summary>
    internal ReadOnlySpan<byte> ReadSpan(int count)
    {
        EnsureAvailable(count);
        ReadOnlySpan<byte> result = _buffer.Slice(Position, count);
        Position += count;
        return result;
    }

    /// <summary>Reads an XBF variable-length unsigned 32-bit integer.</summary>
    internal uint ReadVarUInt32()
    {
        uint value = 0;
        for (int index = 0; index < 5; index++)
        {
            byte current = ReadByte();
            if (index == 4 && (current & 0xF0) != 0)
            {
                throw new InvalidDataException("A 7-bit encoded uint32 overflowed.");
            }

            value |= (uint)(current & 0x7F) << (index * 7);
            if ((current & 0x80) == 0)
            {
                return value;
            }
        }

        throw new InvalidDataException("A 7-bit encoded uint32 is too long.");
    }

    /// <summary>Reads an XBF variable-length unsigned 64-bit integer.</summary>
    internal ulong ReadVarUInt64()
    {
        ulong value = 0;
        for (int index = 0; index < 10; index++)
        {
            byte current = ReadByte();
            if (index == 9 && (current & 0xFE) != 0)
            {
                throw new InvalidDataException("A 7-bit encoded uint64 overflowed.");
            }

            value |= (ulong)(current & 0x7F) << (index * 7);
            if ((current & 0x80) == 0)
            {
                return value;
            }
        }

        throw new InvalidDataException("A 7-bit encoded uint64 is too long.");
    }

    private readonly void EnsureAvailable(int count)
    {
        if (count < 0 || Position < 0 || count > _buffer.Length - Position)
        {
            throw new EndOfStreamException("The XBF stream ended in the middle of a field.");
        }
    }
}

/// <summary>Provides XBF-specific primitive encodings for <see cref="BinaryWriter"/>.</summary>
internal static class XbfBinaryWriterExtensions
{
    /// <summary>Writes an XBF UTF-16 string with its encoded length.</summary>
    internal static void WriteXbfString(this BinaryWriter writer, string value, bool nullTerminated = false)
    {
        ArgumentNullException.ThrowIfNull(value);
        uint codeUnitCount = checked((uint)value.Length);
        writer.Write(codeUnitCount);
        writer.Write(Encoding.Unicode.GetBytes(value));
        if (nullTerminated)
        {
            writer.Write((ushort)0);
        }
    }

    /// <summary>Writes an XBF variable-length unsigned 32-bit integer.</summary>
    internal static void WriteVarUInt32(this BinaryWriter writer, uint value)
    {
        while (value >= 0x80)
        {
            writer.Write((byte)(value | 0x80));
            value >>= 7;
        }

        writer.Write((byte)value);
    }

    /// <summary>Writes an XBF variable-length unsigned 64-bit integer.</summary>
    internal static void WriteVarUInt64(this BinaryWriter writer, ulong value)
    {
        while (value >= 0x80)
        {
            writer.Write((byte)(value | 0x80));
            value >>= 7;
        }

        writer.Write((byte)value);
    }
}
