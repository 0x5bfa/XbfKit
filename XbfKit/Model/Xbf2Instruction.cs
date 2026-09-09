namespace XbfKit;

/// <summary>Identifies an XBF2 object-writer instruction.</summary>
public enum Xbf2Opcode : byte
{
    /// <summary>Indicates that no operation is present.</summary>
    None = 0,

    /// <summary>Pushes a new object-writer scope.</summary>
    PushScope = 1,

    /// <summary>Pops the current object-writer scope.</summary>
    PopScope = 2,

    /// <summary>Adds an XML namespace declaration.</summary>
    AddNamespace = 3,

    /// <summary>Pushes a constant onto the object-writer stack.</summary>
    PushConstant = 4,

    /// <summary>Pushes a resolved type reference.</summary>
    PushResolvedType = 5,

    /// <summary>Pushes a resolved property reference.</summary>
    PushResolvedProperty = 6,

    /// <summary>Assigns the current value to a property.</summary>
    SetValue = 7,

    /// <summary>Adds the current value to a collection.</summary>
    AddToCollection = 8,

    /// <summary>Adds the current value to a dictionary.</summary>
    AddToDictionary = 9,

    /// <summary>Adds the current value to a dictionary using an explicit key.</summary>
    AddToDictionaryWithKey = 10,

    /// <summary>Checks the peer runtime type.</summary>
    CheckPeerType = 11,

    /// <summary>Assigns an x:ConnectionId value.</summary>
    SetConnectionId = 12,

    /// <summary>Assigns an x:Name value.</summary>
    SetName = 13,

    /// <summary>Retrieves the resource property bag.</summary>
    GetResourcePropertyBag = 14,

    /// <summary>Attaches custom runtime data to the current object.</summary>
    SetCustomRuntimeData = 15,

    /// <summary>Assigns deferred resource-dictionary items.</summary>
    SetResourceDictionaryItems = 16,

    /// <summary>Assigns a deferred property segment.</summary>
    SetDeferredProperty = 17,

    /// <summary>Pushes a scope and adds an XML namespace declaration.</summary>
    PushScopeAddNamespace = 18,

    /// <summary>Pushes a scope and retrieves a property value.</summary>
    PushScopeGetValue = 19,

    /// <summary>Pushes a scope, creates a type, and begins initialization.</summary>
    PushScopeCreateTypeBeginInit = 20,

    /// <summary>Pushes a scope, creates a type from a constant, and begins initialization.</summary>
    PushScopeCreateTypeWithConstantBeginInit = 21,

    /// <summary>Pushes a scope, creates a type from a converted constant, and begins initialization.</summary>
    PushScopeCreateTypeWithTypeConvertedConstantBeginInit = 22,

    /// <summary>Creates a type and begins initialization.</summary>
    CreateTypeBeginInit = 23,

    /// <summary>Creates a type from a constant and begins initialization.</summary>
    CreateTypeWithConstantBeginInit = 24,

    /// <summary>Creates a type from a converted constant and begins initialization.</summary>
    CreateTypeWithTypeConvertedConstantBeginInit = 25,

    /// <summary>Assigns a constant directly to a property.</summary>
    SetValueConstant = 26,

    /// <summary>Converts and assigns a constant to a property.</summary>
    SetValueTypeConvertedConstant = 27,

    /// <summary>Converts and assigns a resolved property reference.</summary>
    SetValueTypeConvertedResolvedProperty = 28,

    /// <summary>Converts and assigns a resolved type reference.</summary>
    SetValueTypeConvertedResolvedType = 29,

    /// <summary>Assigns a value obtained from a static resource.</summary>
    SetValueFromStaticResource = 30,

    /// <summary>Assigns a value obtained from a template binding.</summary>
    SetValueFromTemplateBinding = 31,

    /// <summary>Assigns a value obtained from a markup extension.</summary>
    SetValueFromMarkupExtension = 32,

    /// <summary>Ends initialization and pops the current scope.</summary>
    EndInitPopScope = 33,

    /// <summary>Provides the value of a static-resource extension.</summary>
    ProvideStaticResourceValue = 34,

    /// <summary>Provides the value of a theme-resource extension.</summary>
    ProvideThemeResourceValue = 35,

    /// <summary>Assigns a value obtained from a theme resource.</summary>
    SetValueFromThemeResource = 36,

    /// <summary>Marks the end of the object-writer stream.</summary>
    EndOfStream = 37,

    /// <summary>Begins a conditional XAML scope.</summary>
    BeginConditionalScope = 38,

    /// <summary>Ends a conditional XAML scope.</summary>
    EndConditionalScope = 39,

    /// <summary>Ends initialization, provides a value, and pops the current scope.</summary>
    EndInitProvideValuePopScope = 40,

    /// <summary>Creates an object of a resolved type.</summary>
    CreateType = 128,

    /// <summary>Creates an object using an initialization value.</summary>
    CreateTypeWithInitialValue = 129,

    /// <summary>Begins initialization of the current object.</summary>
    BeginInit = 130,

    /// <summary>Ends initialization of the current object.</summary>
    EndInit = 131,

    /// <summary>Retrieves a property value.</summary>
    GetValue = 132,

    /// <summary>Applies the target type converter to the current value.</summary>
    TypeConvertValue = 133,

    /// <summary>Pushes a scope and creates an object.</summary>
    PushScopeCreateType = 134,

    /// <summary>Pushes a scope and creates an object from a constant.</summary>
    PushScopeCreateTypeWithConstant = 135,

    /// <summary>Pushes a scope and creates an object from a converted constant.</summary>
    PushScopeCreateTypeWithTypeConvertedConstant = 136,

    /// <summary>Creates an object from a constant.</summary>
    CreateTypeWithConstant = 137,

    /// <summary>Creates an object from a converted constant.</summary>
    CreateTypeWithTypeConvertedConstant = 138,

    /// <summary>Invokes ProvideValue on the current markup extension.</summary>
    ProvideValue = 139,

    /// <summary>Provides a template-binding value.</summary>
    ProvideTemplateBindingValue = 140,

    /// <summary>Assigns a XAML directive property.</summary>
    SetDirectiveProperty = 141,

    /// <summary>Marks a node-stream offset used by deferred data.</summary>
    StreamOffsetMarker = 142,
}

/// <summary>Identifies either a local metadata entry or a trusted stable-schema entry.</summary>
/// <param name="ObjectId">The 15-bit local or trusted identifier.</param>
/// <param name="IsTrusted">Whether <paramref name="ObjectId"/> addresses the trusted schema.</param>
public readonly record struct Xbf2Reference(ushort ObjectId, bool IsTrusted)
{
    /// <summary>Gets the 16-bit persisted representation.</summary>
    public ushort Encoded => (ushort)(ObjectId | (IsTrusted ? 0x8000 : 0));

    /// <summary>Decodes a 16-bit persisted reference.</summary>
    public static Xbf2Reference FromEncoded(ushort value)
    {
        return new((ushort)(value & 0x7FFF), (value & 0x8000) != 0);
    }
}

/// <summary>Identifies the payload encoding used by an XBF2 constant.</summary>
public enum Xbf2ConstantType : byte
{
    /// <summary>Indicates that no constant payload is present.</summary>
    None = 0,

    /// <summary>Represents the Boolean value <see langword="false"/>.</summary>
    BoolFalse = 1,

    /// <summary>Represents the Boolean value <see langword="true"/>.</summary>
    BoolTrue = 2,

    /// <summary>Represents a single-precision floating-point value.</summary>
    Float = 3,

    /// <summary>Represents a signed 32-bit integer value.</summary>
    Signed = 4,

    /// <summary>Represents a shared string-table reference.</summary>
    SharedString = 5,

    /// <summary>Represents a four-sided thickness value.</summary>
    Thickness = 6,

    /// <summary>Represents a grid-length value.</summary>
    GridLength = 7,

    /// <summary>Represents a 32-bit ARGB color.</summary>
    Color = 8,

    /// <summary>Represents a unique string-table reference.</summary>
    UniqueString = 9,

    /// <summary>Represents a null string.</summary>
    NullString = 10,

    /// <summary>Represents a trusted enum type and numeric value.</summary>
    Enum = 11,
}

/// <summary>Contains a trusted enum type identifier and its numeric value.</summary>
/// <param name="StableTypeId">The trusted enum type identifier.</param>
/// <param name="Value">The persisted numeric enum value.</param>
public readonly record struct Xbf2EnumValue(ushort StableTypeId, uint Value);

/// <summary>Contains a decoded XBF2 constant and its value kind.</summary>
/// <param name="Type">The persisted constant kind.</param>
/// <param name="Data">The decoded constant payload.</param>
public readonly record struct Xbf2Constant(Xbf2ConstantType Type, object? Data)
{
    /// <summary>Gets the canonical false constant.</summary>
    public static Xbf2Constant False { get; } = new(Xbf2ConstantType.BoolFalse, false);

    /// <summary>Gets the canonical true constant.</summary>
    public static Xbf2Constant True { get; } = new(Xbf2ConstantType.BoolTrue, true);

    /// <summary>Gets the canonical null-string constant.</summary>
    public static Xbf2Constant NullString { get; } = new(Xbf2ConstantType.NullString, null);
}

/// <summary>Represents one decoded XBF2 object-writer instruction.</summary>
public sealed class Xbf2Instruction
{
    /// <summary>Gets the object-writer operation.</summary>
    public required Xbf2Opcode Opcode { get; init; }

    /// <summary>Gets the original byte offset in the decoded node stream.</summary>
    public required int Offset { get; init; }

    /// <summary>Gets the primary type reference, when used by the opcode.</summary>
    public Xbf2Reference? TypeReference { get; init; }

    /// <summary>Gets the primary property reference, when used by the opcode.</summary>
    public Xbf2Reference? PropertyReference { get; init; }

    /// <summary>Gets the opcode-specific secondary reference.</summary>
    public Xbf2Reference? SecondaryReference { get; init; }

    /// <summary>Gets the opcode-specific constant.</summary>
    public Xbf2Constant? Constant { get; init; }

    /// <summary>Gets the opcode-specific inline text.</summary>
    public string? Text { get; init; }

    /// <summary>Gets the opcode-specific deferred segment.</summary>
    public Xbf2Segment? Segment { get; init; }

    /// <summary>Gets the conditional-scope predicate.</summary>
    public Xbf2Predicate? Predicate { get; init; }

    internal Xbf2Reference RequiredTypeReference => TypeReference ?? throw Missing("type reference");
    internal Xbf2Reference RequiredPropertyReference => PropertyReference ?? throw Missing("property reference");
    internal Xbf2Reference RequiredSecondaryReference => SecondaryReference ?? throw Missing("secondary reference");
    internal Xbf2Constant RequiredConstant => Constant ?? throw Missing("constant");

    private InvalidDataException Missing(string field) => new($"XBF2 opcode {Opcode} has no {field}.");
}

/// <summary>Contains editable XBF2 instructions and their decoded source line records.</summary>
public sealed class Xbf2DecodedSubstream
{
    /// <summary>Gets the original encoded node-stream length used to translate line offsets.</summary>
    public int NodeLength { get; init; }

    /// <summary>Gets the editable instruction sequence.</summary>
    public List<Xbf2Instruction> Instructions { get; } = [];

    /// <summary>Gets the editable source line records.</summary>
    public List<Xbf2LineRecord> LineRecords { get; } = [];
}

/// <summary>Maps a node-stream byte offset to a XAML source location.</summary>
/// <param name="NodeOffset">The encoded node-stream byte offset.</param>
/// <param name="Line">The one-based source line.</param>
/// <param name="Column">The one-based source column.</param>
public readonly record struct Xbf2LineRecord(uint NodeOffset, int Line, int Column);

/// <summary>Describes an XBF2 deferred or custom-runtime-data segment.</summary>
public sealed class Xbf2Segment
{
    /// <summary>Gets the target substream token.</summary>
    public uint TargetSubstream { get; init; }

    /// <summary>Gets referenced static-resource keys.</summary>
    public List<Xbf2Reference> StaticResources { get; } = [];

    /// <summary>Gets referenced theme-resource keys.</summary>
    public List<Xbf2Reference> ThemeResources { get; } = [];

    /// <summary>Gets decoded custom runtime data associated with the segment.</summary>
    public Xbf2CustomRuntimeData? RuntimeData { get; init; }
}

/// <summary>Contains the type and argument references for an XBF2 conditional scope.</summary>
/// <param name="Type">The predicate type reference.</param>
/// <param name="Arguments">The predicate argument-string reference.</param>
public readonly record struct Xbf2Predicate(Xbf2Reference Type, Xbf2Reference Arguments);
