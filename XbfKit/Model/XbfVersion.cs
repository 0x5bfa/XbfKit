namespace XbfKit;

/// <summary>Identifies an XBF file-format version.</summary>
/// <param name="Major">The major format version.</param>
/// <param name="Minor">The minor format version.</param>
public readonly record struct XbfVersion(uint Major, uint Minor)
{
    /// <summary>Gets the Windows 8.x XBF1 format version.</summary>
    public static XbfVersion Version1 { get; } = new(1, 0);

    /// <summary>Gets the original XBF2 format version.</summary>
    public static XbfVersion Version2 { get; } = new(2, 0);

    /// <summary>Gets the XBF2.1 format version.</summary>
    public static XbfVersion Version2_1 { get; } = new(2, 1);

    /// <summary>Gets whether this version is supported by the reader and writer.</summary>
    public bool IsSupported => this is { Major: 1, Minor: 0 } or { Major: 2, Minor: 0 or 1 };

    /// <summary>Gets whether metadata strings include a trailing UTF-16 null code unit.</summary>
    internal bool NullTerminatesMetadataStrings => Major == 2 && Minor >= 1;

    /// <inheritdoc/>
    public override string ToString() => $"{Major}.{Minor}";
}
