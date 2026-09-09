namespace XbfKit;

/// <summary>Contains the string, assembly, namespace, type, and property tables referenced by XBF nodes.</summary>
public sealed class XbfMetadata
{
    /// <summary>Gets the persisted string table.</summary>
    public List<string> Strings { get; } = [];

    /// <summary>Gets the persisted assembly table.</summary>
    public List<XbfAssemblyEntry> Assemblies { get; } = [];

    /// <summary>Gets the persisted runtime namespace table.</summary>
    public List<XbfTypeNamespaceEntry> TypeNamespaces { get; } = [];

    /// <summary>Gets the persisted local type table.</summary>
    public List<XbfTypeEntry> Types { get; } = [];

    /// <summary>Gets the persisted local property table.</summary>
    public List<XbfPropertyEntry> Properties { get; } = [];

    /// <summary>Gets the string identifiers for the persisted XML namespace table.</summary>
    public List<uint> XmlNamespaces { get; } = [];
}

/// <summary>Represents one persisted assembly metadata entry.</summary>
/// <param name="ProviderKind">The persisted metadata provider kind.</param>
/// <param name="NameStringId">The assembly-name string identifier.</param>
public readonly record struct XbfAssemblyEntry(uint ProviderKind, uint NameStringId);

/// <summary>Represents one persisted runtime namespace metadata entry.</summary>
/// <param name="AssemblyId">The owning assembly-table identifier.</param>
/// <param name="NameStringId">The runtime-namespace string identifier.</param>
public readonly record struct XbfTypeNamespaceEntry(uint AssemblyId, uint NameStringId);

/// <summary>Represents one persisted local type metadata entry.</summary>
/// <param name="Flags">The persisted type flags.</param>
/// <param name="NamespaceId">The runtime or XML namespace identifier.</param>
/// <param name="NameStringId">The type-name string identifier.</param>
public readonly record struct XbfTypeEntry(XbfTypeFlags Flags, uint NamespaceId, uint NameStringId);

/// <summary>Represents one persisted local property metadata entry.</summary>
/// <param name="Flags">The persisted property flags.</param>
/// <param name="DeclaringTypeId">The declaring local type identifier.</param>
/// <param name="NameStringId">The property-name string identifier.</param>
public readonly record struct XbfPropertyEntry(XbfPropertyFlags Flags, uint DeclaringTypeId, uint NameStringId);

/// <summary>Identifies persisted local type traits.</summary>
[Flags]
public enum XbfTypeFlags : uint
{
    /// <summary>Indicates that no type flags are set.</summary>
    None = 0,

    /// <summary>Indicates that the entry represents a XAML directive type.</summary>
    IsMarkupDirective = 0x01,

    /// <summary>Indicates that the runtime type was unresolved when compiled.</summary>
    IsUnknown = 0x02,
}

/// <summary>Identifies persisted local property traits.</summary>
[Flags]
public enum XbfPropertyFlags : uint
{
    /// <summary>Indicates that no property flags are set.</summary>
    None = 0,

    /// <summary>Indicates that the entry represents an XML property.</summary>
    IsXmlProperty = 0x01,

    /// <summary>Indicates that the entry represents a XAML directive.</summary>
    IsMarkupDirective = 0x02,

    /// <summary>Indicates that the entry represents an implicit object-writer property.</summary>
    IsImplicitProperty = 0x04,

    /// <summary>Indicates that the entry represents a custom dependency property.</summary>
    IsCustomDependencyProperty = 0x08,

    /// <summary>Indicates that the property was unresolved when compiled.</summary>
    IsUnknown = 0x10,
}
