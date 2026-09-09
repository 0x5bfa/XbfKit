namespace XbfKit.Schema;

/// <summary>Describes behavioral traits of a trusted XAML type.</summary>
[Flags]
internal enum XbfTrustedTypeFlags
{
    /// <summary>Indicates that no trusted type traits are set.</summary>
    None = 0,

    /// <summary>Indicates that the type accepts collection items.</summary>
    IsCollection = 0x01,

    /// <summary>Indicates that the type accepts keyed dictionary items.</summary>
    IsDictionary = 0x02,

    /// <summary>Indicates that the type is a markup extension.</summary>
    IsMarkupExtension = 0x04,
}

/// <summary>Describes behavioral traits of a trusted XAML property.</summary>
[Flags]
internal enum XbfTrustedPropertyFlags
{
    /// <summary>Indicates that no trusted property traits are set.</summary>
    None = 0,

    /// <summary>Indicates that the property participates in the visual tree.</summary>
    IsVisualTreeProperty = 0x01,

    /// <summary>Indicates that the property is attached.</summary>
    IsAttached = 0x02,
}

/// <summary>Contains one trusted type entry reconstructed from GenXbf.</summary>
/// <param name="Name">The simple XAML type name.</param>
/// <param name="FullName">The fully qualified runtime type name.</param>
/// <param name="BaseTypeId">The trusted base type identifier, or zero.</param>
/// <param name="Flags">The trusted type traits.</param>
internal readonly record struct XbfTrustedType(string Name, string FullName, ushort BaseTypeId, XbfTrustedTypeFlags Flags);

/// <summary>Contains one trusted property entry reconstructed from GenXbf.</summary>
/// <param name="Name">The simple property name.</param>
/// <param name="FullName">The fully qualified property name.</param>
/// <param name="DeclaringTypeId">The trusted declaring type identifier.</param>
/// <param name="PropertyTypeId">The trusted property-value type identifier.</param>
/// <param name="Flags">The trusted property traits.</param>
internal readonly record struct XbfTrustedProperty(string Name, string FullName, ushort DeclaringTypeId, ushort PropertyTypeId, XbfTrustedPropertyFlags Flags);

/// <summary>
/// Provides indexed and reverse lookups over one immutable trusted XAML schema.
/// </summary>
internal sealed class XbfTrustedSchema
{
    private readonly IReadOnlyDictionary<ushort, XbfTrustedType> _types;
    private readonly IReadOnlyDictionary<ushort, XbfTrustedProperty> _properties;
    private readonly IReadOnlyDictionary<ushort, IReadOnlyDictionary<int, string>> _enumValues;
    private readonly IReadOnlySet<ushort> _flagEnumTypeIds;
    private readonly Dictionary<string, ushort> _typeIds = new(StringComparer.Ordinal);
    private readonly Dictionary<(ushort DeclaringTypeId, string Name), ushort> _propertyIds = [];

    /// <summary>Builds indexed and reverse lookup tables from generated schema data.</summary>
    internal XbfTrustedSchema(
        IReadOnlyDictionary<ushort, XbfTrustedType> types,
        IReadOnlyDictionary<ushort, XbfTrustedProperty> properties,
        IReadOnlyDictionary<ushort, IReadOnlyDictionary<int, string>> enumValues,
        IReadOnlySet<ushort> flagEnumTypeIds)
    {
        _types = types;
        _properties = properties;
        _enumValues = enumValues;
        _flagEnumTypeIds = flagEnumTypeIds;

        foreach ((ushort id, XbfTrustedType type) in types)
        {
            _typeIds.TryAdd(type.Name, id);
            _typeIds.TryAdd(type.FullName, id);
        }

        foreach ((ushort id, XbfTrustedProperty property) in properties)
        {
            _propertyIds.TryAdd((property.DeclaringTypeId, property.Name), id);
        }
    }

    /// <summary>Gets a trusted type by stable identifier, or <see langword="null"/> when unknown.</summary>
    internal XbfTrustedType? GetType(int id)
    {
        return id is > 0 and <= ushort.MaxValue && _types.TryGetValue((ushort)id, out XbfTrustedType type)
            ? type
            : null;
    }

    /// <summary>Gets a trusted property by stable identifier, or <see langword="null"/> when unknown.</summary>
    internal XbfTrustedProperty? GetProperty(int id)
    {
        return id is > 0 and <= ushort.MaxValue && _properties.TryGetValue((ushort)id, out XbfTrustedProperty property)
            ? property
            : null;
    }

    /// <summary>Attempts to resolve a simple or fully qualified trusted type name.</summary>
    internal bool TryGetTypeId(string typeName, out ushort id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);

        return _typeIds.TryGetValue(typeName, out id);
    }

    /// <summary>Attempts to resolve a trusted property, walking the declaring type's base chain.</summary>
    internal bool TryGetPropertyId(string declaringTypeName, string propertyName, out ushort id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(declaringTypeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);

        if (!TryGetTypeId(declaringTypeName, out ushort typeId))
        {
            id = 0;
            return false;
        }

        while (typeId != 0)
        {
            if (_propertyIds.TryGetValue((typeId, propertyName), out id))
            {
                return true;
            }

            XbfTrustedType? type = GetType(typeId);
            if (type is null || type.Value.BaseTypeId == typeId)
            {
                break;
            }

            typeId = type.Value.BaseTypeId;
        }

        id = 0;

        return false;
    }

    /// <summary>Formats a trusted enum value, including valid flags combinations.</summary>
    internal string? FormatEnumValue(int typeId, int value)
    {
        if (typeId is < 0 or > ushort.MaxValue ||
            !_enumValues.TryGetValue((ushort)typeId, out IReadOnlyDictionary<int, string>? values))
        {
            return null;
        }

        if (values.TryGetValue(value, out string? name))
        {
            return name;
        }

        if (value == 0 || !_flagEnumTypeIds.Contains((ushort)typeId))
        {
            return null;
        }

        List<string> names = [];
        int remainingValue = value;
        foreach ((int flag, string flagName) in values)
        {
            if (flag == 0 || (remainingValue & flag) != flag)
            {
                continue;
            }

            names.Add(flagName);
            remainingValue &= ~flag;
        }

        return remainingValue == 0 ? string.Join(',', names) : null;
    }
}

/// <summary>Owns the generated trusted schemas and selects one by XAML dialect.</summary>
internal static partial class XbfSchemaCatalog
{
    private static readonly Lazy<XbfTrustedSchema> Schema_WUX = new(() => new(Types_WUX, Properties_WUX, EnumValues_WUX, FlagEnumTypeIds_WUX));

    private static readonly Lazy<XbfTrustedSchema> Schema_MUX = new(() => new(Types_MUX, Properties_MUX, EnumValues_MUX.Value, FlagEnumTypeIds_MUX));

    /// <summary>Gets the immutable trusted schema for the requested XAML dialect.</summary>
    internal static XbfTrustedSchema Get(XbfDialect dialect)
    {
        return dialect switch
        {
            XbfDialect.WUX => Schema_WUX.Value,
            XbfDialect.MUX => Schema_MUX.Value,
            _ => throw new ArgumentOutOfRangeException(nameof(dialect)),
        };
    }
}
