namespace XbfKit;

/// <summary>Identifies the persisted operation represented by an XBF1 node.</summary>
public enum Xbf1NodeType : byte
{
    /// <summary>Indicates that no node operation is present.</summary>
    None = 0,

    /// <summary>Begins construction of an object.</summary>
    StartObject = 1,

    /// <summary>Ends construction of the current object.</summary>
    EndObject = 2,

    /// <summary>Begins assignment of a property.</summary>
    StartProperty = 3,

    /// <summary>Ends assignment of the current property.</summary>
    EndProperty = 4,

    /// <summary>Provides text content.</summary>
    Text = 5,

    /// <summary>Provides a typed constant value.</summary>
    Value = 6,

    /// <summary>Declares an XML namespace prefix.</summary>
    Namespace = 7,

    /// <summary>Marks the end of an object's attribute section.</summary>
    EndOfAttributes = 8,

    /// <summary>Marks the end of the node stream.</summary>
    EndOfStream = 9,

    /// <summary>Updates the source location by relative deltas.</summary>
    LineInfo = 10,

    /// <summary>Sets an absolute source location.</summary>
    LineInfoAbsolute = 11,

    /// <summary>Begins a conditional XAML scope.</summary>
    StartConditionalScope = 12,

    /// <summary>Ends a conditional XAML scope.</summary>
    EndConditionalScope = 13,
}

/// <summary>Identifies reference and value traits attached to an XBF1 node.</summary>
[Flags]
public enum Xbf1NodeFlags : uint
{
    /// <summary>Indicates that no flags are set.</summary>
    None = 0,

    /// <summary>Indicates that the referenced object is retrieved rather than constructed.</summary>
    IsRetrieved = 0x01,

    /// <summary>Indicates that the reference targets an unknown local schema entry.</summary>
    IsUnknown = 0x02,

    /// <summary>Indicates that a string value must occupy a unique string-table entry.</summary>
    IsStringValueAndUnique = 0x04,

    /// <summary>Indicates that the reference is a trusted XBF identifier.</summary>
    IsTrustedXbfIndex = 0x08,
}

/// <summary>Identifies a local XBF1 metadata entry and its persisted flags.</summary>
/// <param name="ObjectId">The local metadata-table identifier.</param>
/// <param name="Flags">The persisted reference flags.</param>
public readonly record struct Xbf1Reference(uint ObjectId, Xbf1NodeFlags Flags);

/// <summary>Identifies the payload encoding used by an XBF1 value node.</summary>
public enum Xbf1ValueType : byte
{
    /// <summary>Indicates that no value payload is present.</summary>
    None = 0,

    /// <summary>Represents the Boolean value <see langword="false"/>.</summary>
    BoolFalse = 1,

    /// <summary>Represents the Boolean value <see langword="true"/>.</summary>
    BoolTrue = 2,

    /// <summary>Represents a single-precision floating-point value.</summary>
    Float = 3,

    /// <summary>Represents a signed 32-bit integer value.</summary>
    Signed = 4,

    /// <summary>Represents a string-table reference.</summary>
    String = 5,

    /// <summary>Represents a key-time value stored in seconds.</summary>
    KeyTime = 6,

    /// <summary>Represents a four-sided thickness value.</summary>
    Thickness = 7,

    /// <summary>Represents a length-converter value.</summary>
    LengthConverter = 8,

    /// <summary>Represents a grid-length value.</summary>
    GridLength = 9,

    /// <summary>Represents a 32-bit ARGB color.</summary>
    Color = 10,

    /// <summary>Represents a duration stored in seconds.</summary>
    Duration = 11,
}

/// <summary>Represents a four-sided XBF thickness constant.</summary>
/// <param name="Left">The left value.</param>
/// <param name="Top">The top value.</param>
/// <param name="Right">The right value.</param>
/// <param name="Bottom">The bottom value.</param>
public readonly record struct XbfThickness(float Left, float Top, float Right, float Bottom);

/// <summary>Represents an XBF grid-length constant.</summary>
/// <param name="UnitType">The persisted grid unit kind.</param>
/// <param name="Value">The numeric length or weight.</param>
public readonly record struct XbfGridLength(byte UnitType, float Value);

/// <summary>Contains a decoded XBF1 constant and its value kind.</summary>
/// <param name="Type">The persisted value kind.</param>
/// <param name="Data">The decoded value payload.</param>
public readonly record struct Xbf1Value(Xbf1ValueType Type, object? Data)
{
    /// <summary>Gets the canonical false constant.</summary>
    public static Xbf1Value False { get; } = new(Xbf1ValueType.BoolFalse, false);

    /// <summary>Gets the canonical true constant.</summary>
    public static Xbf1Value True { get; } = new(Xbf1ValueType.BoolTrue, true);
}

/// <summary>Represents one record in a linear XBF1 node stream.</summary>
public sealed class Xbf1Node
{
    /// <summary>Gets the persisted node operation.</summary>
    public required Xbf1NodeType Type { get; init; }

    /// <summary>Gets the optional metadata reference.</summary>
    public Xbf1Reference? Reference { get; init; }

    /// <summary>Gets the optional decoded constant.</summary>
    public Xbf1Value? Value { get; init; }

    /// <summary>Gets the namespace prefix carried by a namespace node.</summary>
    public string? NamespacePrefix { get; init; }

    /// <summary>Gets the relative source-line delta.</summary>
    public short LineDelta { get; init; }

    /// <summary>Gets the relative source-column delta.</summary>
    public short ColumnDelta { get; init; }

    /// <summary>Gets the absolute source line.</summary>
    public uint AbsoluteLine { get; init; }

    /// <summary>Gets the absolute source column.</summary>
    public uint AbsoluteColumn { get; init; }
}
