namespace XbfKit;

/// <summary>Represents a parsed or compiled XBF document.</summary>
public sealed class XbfDocument
{
    private byte[] _hash = new byte[64];

    /// <summary>Gets the file-format version.</summary>
    public required XbfVersion Version { get; init; }

    /// <summary>Gets the document metadata tables.</summary>
    public XbfMetadata Metadata { get; init; } = new();

    /// <summary>Gets the 64-byte metadata hash stored by XBF.</summary>
    public byte[] Hash
    {
        get => _hash;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.Length != 64)
            {
                throw new ArgumentException("An XBF metadata hash must be exactly 64 bytes.", nameof(value));
            }

            _hash = (byte[])value.Clone();
        }
    }

    /// <summary>Gets the version-specific node payload.</summary>
    public required XbfNodeData Nodes { get; init; }
}
