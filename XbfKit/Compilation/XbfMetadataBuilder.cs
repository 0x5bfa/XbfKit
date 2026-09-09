using XbfKit.Schema;
using XbfKit.Xaml;
using System.Xml.Linq;

namespace XbfKit.Compilation;

/// <summary>Builds local XBF metadata tables and resolves trusted schema references.</summary>
internal sealed class XbfMetadataBuilder
{
    private readonly XbfTrustedSchema _schema;
    private readonly XbfRuntimeProfile _profile;
    private readonly Dictionary<string, uint> _stringIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, uint> _xmlNamespaceIds = new(StringComparer.Ordinal);
    private readonly Dictionary<XName, uint> _typeIds = [];
    private readonly Dictionary<PropertyKey, uint> _propertyIds = [];
    private readonly Dictionary<string, uint> _xbf1KnownTypeIds = new(StringComparer.Ordinal);
    private readonly Dictionary<Xbf1PropertyKey, uint> _xbf1PropertyIds = [];
    private readonly Dictionary<string, uint> _assemblyIds = new(StringComparer.Ordinal);
    private readonly Dictionary<(uint AssemblyId, string Name), uint> _typeNamespaceIds = [];

    /// <summary>Initializes a metadata builder for the specified target runtime profile.</summary>
    internal XbfMetadataBuilder(XbfRuntimeProfile profile)
    {
        _profile = profile;
        _schema = XbfSchemaCatalog.Get(profile.Dialect);
    }

    /// <summary>Gets the target runtime profile used to select trusted references and runtime data.</summary>
    internal XbfRuntimeProfile RuntimeProfile => _profile;

    /// <summary>Gets the metadata tables accumulated by this builder.</summary>
    internal XbfMetadata Metadata { get; } = new();

    /// <summary>Gets or adds a deduplicated string and returns its table identifier.</summary>
    internal uint String(string value)
    {
        if (_stringIds.TryGetValue(value, out uint id))
        {
            return id;
        }

        id = checked((uint)Metadata.Strings.Count);
        Metadata.Strings.Add(value);
        _stringIds[value] = id;
        return id;
    }

    /// <summary>Adds a string without deduplication and returns its table identifier.</summary>
    internal uint UniqueString(string value)
    {
        uint id = checked((uint)Metadata.Strings.Count);
        Metadata.Strings.Add(value);
        return id;
    }

    /// <summary>Gets or adds an XML namespace URI and returns its table identifier.</summary>
    internal uint XmlNamespace(string namespaceUri)
    {
        if (_xmlNamespaceIds.TryGetValue(namespaceUri, out uint id))
        {
            return id;
        }

        id = checked((uint)Metadata.XmlNamespaces.Count);
        Metadata.XmlNamespaces.Add(String(namespaceUri));
        _xmlNamespaceIds[namespaceUri] = id;
        return id;
    }

    /// <summary>Gets or adds an unresolved local type and returns its table identifier.</summary>
    internal uint Type(XName name)
    {
        if (_typeIds.TryGetValue(name, out uint id))
        {
            return id;
        }

        id = checked((uint)Metadata.Types.Count);
        EnsureReferenceFits(id, "type");
        Metadata.Types.Add(new XbfTypeEntry(XbfTypeFlags.IsUnknown, XmlNamespace(name.NamespaceName), String(name.LocalName)));
        _typeIds[name] = id;
        return id;
    }

    /// <summary>Creates an XBF2 trusted or local reference for a XAML type name.</summary>
    internal Xbf2Reference TypeReference(XName name)
    {
        if (name.NamespaceName == XamlNamespaces.Presentation && _schema.TryGetTypeId(name.LocalName, out ushort stableId) && _profile.SupportsStableType(stableId))
        {
            return new Xbf2Reference(stableId, IsTrusted: true);
        }

        return LocalReference(Type(name));
    }

    /// <summary>Creates an XBF1 metadata reference for a XAML type name.</summary>
    internal Xbf1Reference Xbf1TypeReference(XName name, Xbf1NodeFlags additionalFlags = Xbf1NodeFlags.None)
    {
        uint id;
        Xbf1NodeFlags flags = additionalFlags;
        if (TryGetXbf1FrameworkType(name, out XbfTrustedType frameworkType))
        {
            id = Xbf1KnownType(frameworkType);
        }
        else if (name.NamespaceName == XamlNamespaces.Xaml)
        {
            id = Xbf1DirectiveType(name.LocalName);
        }
        else
        {
            id = Type(name);
            flags |= Xbf1NodeFlags.IsUnknown;
        }

        return new Xbf1Reference(id, flags);
    }

    /// <summary>Gets or adds a local property and returns its table identifier.</summary>
    internal uint Property(XName declaringType, string propertyName, XbfPropertyFlags flags = XbfPropertyFlags.IsUnknown)
    {
        var key = new PropertyKey(declaringType, propertyName, flags);
        if (_propertyIds.TryGetValue(key, out uint id))
        {
            return id;
        }

        id = checked((uint)Metadata.Properties.Count);
        EnsureReferenceFits(id, "property");
        Metadata.Properties.Add(new XbfPropertyEntry(flags, Type(declaringType), String(propertyName)));
        _propertyIds[key] = id;
        return id;
    }

    /// <summary>Creates an XBF2 trusted or local reference for a property.</summary>
    internal Xbf2Reference PropertyReference(XName declaringType, string propertyName, XbfPropertyFlags flags = XbfPropertyFlags.IsUnknown)
    {
        if (declaringType.NamespaceName == XamlNamespaces.Presentation && _schema.TryGetPropertyId(declaringType.LocalName, propertyName, out ushort stableId) && _profile.SupportsStableProperty(stableId))
        {
            return new Xbf2Reference(stableId, IsTrusted: true);
        }

        return LocalReference(Property(declaringType, propertyName, flags));
    }

    /// <summary>Creates an XBF1 metadata reference for a property.</summary>
    internal Xbf1Reference Xbf1PropertyReference(XName declaringType, string propertyName, XbfPropertyFlags flags = XbfPropertyFlags.IsUnknown)
    {
        uint id;
        Xbf1NodeFlags nodeFlags = Xbf1NodeFlags.None;
        if (flags.HasFlag(XbfPropertyFlags.IsMarkupDirective))
        {
            id = Xbf1Property(declaringTypeId: 0, propertyName, (flags & ~XbfPropertyFlags.IsUnknown) | XbfPropertyFlags.IsMarkupDirective);
        }
        else if (declaringType.NamespaceName == XamlNamespaces.Presentation && _schema.TryGetPropertyId(declaringType.LocalName, propertyName, out ushort stableId) && _schema.GetProperty(stableId) is XbfTrustedProperty propertyInfo && _schema.GetType(propertyInfo.DeclaringTypeId) is XbfTrustedType ownerInfo)
        {
            id = Xbf1Property(Xbf1KnownType(ownerInfo), propertyName, flags & ~XbfPropertyFlags.IsUnknown);
        }
        else
        {
            id = Property(declaringType, propertyName, flags);
            nodeFlags |= Xbf1NodeFlags.IsUnknown;
        }

        return new Xbf1Reference(id, nodeFlags);
    }

    /// <summary>Creates the XBF1 implicit-items property reference.</summary>
    internal Xbf1Reference Xbf1ImplicitItemsReference()
    {
        return Xbf1ImplicitPropertyReference("__implicit_items");
    }

    /// <summary>Creates the XBF1 implicit-initialization property reference.</summary>
    internal Xbf1Reference Xbf1ImplicitInitializationReference()
    {
        return Xbf1ImplicitPropertyReference("__implicit_initialization");
    }

    /// <summary>Attempts to resolve the collection type used by an XBF1 property.</summary>
    internal bool TryGetXbf1CollectionTypeReference(XName declaringType, string propertyName, out Xbf1Reference reference)
    {
        reference = default;
        if (declaringType.NamespaceName != XamlNamespaces.Presentation || !_schema.TryGetPropertyId(declaringType.LocalName, propertyName, out ushort propertyId) || _schema.GetProperty(propertyId) is not XbfTrustedProperty property || _schema.GetType(property.PropertyTypeId) is not XbfTrustedType propertyType || (!propertyType.Flags.HasFlag(XbfTrustedTypeFlags.IsCollection) && !propertyType.Flags.HasFlag(XbfTrustedTypeFlags.IsDictionary)))
        {
            return false;
        }

        reference = new Xbf1Reference(Xbf1KnownType(propertyType), Xbf1NodeFlags.IsRetrieved);
        return true;
    }

    /// <summary>Attempts to resolve an XBF1 property and its value type in the trusted schema.</summary>
    internal bool TryGetXbf1PropertyMetadata(XName declaringType, string propertyName, out XbfTrustedProperty property, out XbfTrustedType propertyType)
    {
        property = default;
        propertyType = default;
        if (declaringType.NamespaceName != XamlNamespaces.Presentation || !_schema.TryGetPropertyId(declaringType.LocalName, propertyName, out ushort propertyId) || _schema.GetProperty(propertyId) is not XbfTrustedProperty resolvedProperty || _schema.GetType(resolvedProperty.PropertyTypeId) is not XbfTrustedType type)
        {
            return false;
        }

        property = resolvedProperty;
        propertyType = type;
        return true;
    }

    private Xbf1Reference Xbf1ImplicitPropertyReference(string name)
    {
        uint id = Xbf1Property(declaringTypeId: 0, name, XbfPropertyFlags.IsImplicitProperty);
        return new Xbf1Reference(id, Xbf1NodeFlags.None);
    }

    /// <summary>Gets the fallback content-property name for a XAML type.</summary>
    internal static string GetContentPropertyName(XName ownerType)
    {
        return XamlFallbackSchema.GetContentPropertyName(ownerType.LocalName);
    }

    /// <summary>Determines whether a XAML type is treated as a collection.</summary>
    internal bool IsCollectionType(XName name)
    {
        if (name.LocalName.EndsWith("Collection", StringComparison.Ordinal))
        {
            return true;
        }

        return name.NamespaceName == XamlNamespaces.Presentation && _schema.TryGetTypeId(name.LocalName, out ushort id) && _schema.GetType(id) is XbfTrustedType info && info.Flags.HasFlag(XbfTrustedTypeFlags.IsCollection);
    }

    /// <summary>Determines whether a trusted framework type lacks the specified property.</summary>
    internal bool IsFrameworkTypeWithoutProperty(XName name, string propertyName)
    {
        return name.NamespaceName == XamlNamespaces.Presentation && _schema.TryGetTypeId(name.LocalName, out _) && !_schema.TryGetPropertyId(name.LocalName, propertyName, out _);
    }

    /// <summary>Gets the trusted type flags for a property's value type.</summary>
    internal XbfTrustedTypeFlags PropertyTypeFlags(XName declaringType, string propertyName)
    {
        if (declaringType.NamespaceName != XamlNamespaces.Presentation || !_schema.TryGetPropertyId(declaringType.LocalName, propertyName, out ushort propertyId) || _schema.GetProperty(propertyId) is not XbfTrustedProperty property || _schema.GetType(property.PropertyTypeId) is not XbfTrustedType propertyType)
        {
            return XbfTrustedTypeFlags.None;
        }

        return propertyType.Flags;
    }

    private static void EnsureReferenceFits(uint id, string description)
    {
        if (id >= 0x8000)
        {
            throw new InvalidDataException($"The generated XBF contains too many {description} entries for a 15-bit v2 reference.");
        }
    }

    private static Xbf2Reference LocalReference(uint id)
    {
        return new Xbf2Reference(checked((ushort)id), IsTrusted: false);
    }

    private bool TryGetXbf1FrameworkType(XName name, out XbfTrustedType type)
    {
        type = default;
        if (name.NamespaceName is not XamlNamespaces.Presentation and not XamlNamespaces.Xaml || !_schema.TryGetTypeId(name.LocalName, out ushort id) || _schema.GetType(id) is not XbfTrustedType info)
        {
            return false;
        }

        if (name.NamespaceName == XamlNamespaces.Xaml && !info.FullName.StartsWith("Windows.Foundation.", StringComparison.Ordinal))
        {
            return false;
        }

        type = info;
        return true;
    }

    private uint Xbf1KnownType(XbfTrustedType type)
    {
        if (_xbf1KnownTypeIds.TryGetValue(type.FullName, out uint id))
        {
            return id;
        }

        int separator = type.FullName.LastIndexOf('.');
        if (separator <= 0 || separator == type.FullName.Length - 1)
        {
            throw new InvalidDataException($"The XBF1 framework type '{type.FullName}' has no runtime namespace.");
        }

        string namespaceName = type.FullName[..separator];
        string persistedName = type.Name switch
        {
            "VisualStateCollection" or "VisualStateGroupCollection" or "VisualTransitionCollection" => $"!{type.Name}",
            _ => type.Name,
        };
        id = checked((uint)Metadata.Types.Count);
        Metadata.Types.Add(new XbfTypeEntry(XbfTypeFlags.None, Xbf1TypeNamespace(namespaceName), String(persistedName)));
        _xbf1KnownTypeIds[type.FullName] = id;
        return id;
    }

    private uint Xbf1DirectiveType(string name)
    {
        string key = $"directive:{name}";
        if (_xbf1KnownTypeIds.TryGetValue(key, out uint id))
        {
            return id;
        }

        id = checked((uint)Metadata.Types.Count);
        Metadata.Types.Add(new XbfTypeEntry(XbfTypeFlags.IsMarkupDirective, 0, String(name)));
        _xbf1KnownTypeIds[key] = id;
        return id;
    }

    private uint Xbf1Property(uint declaringTypeId, string name, XbfPropertyFlags flags)
    {
        var key = new Xbf1PropertyKey(declaringTypeId, name, flags);
        if (_xbf1PropertyIds.TryGetValue(key, out uint id))
        {
            return id;
        }

        id = checked((uint)Metadata.Properties.Count);
        Metadata.Properties.Add(new XbfPropertyEntry(flags, declaringTypeId, String(name)));
        _xbf1PropertyIds[key] = id;
        return id;
    }

    private uint Xbf1TypeNamespace(string name)
    {
        uint assemblyId = Xbf1Assembly("System.Windows", providerKind: 1);
        var key = (assemblyId, name);
        if (_typeNamespaceIds.TryGetValue(key, out uint id))
        {
            return id;
        }

        id = checked((uint)Metadata.TypeNamespaces.Count);
        Metadata.TypeNamespaces.Add(new XbfTypeNamespaceEntry(assemblyId, String(name)));
        _typeNamespaceIds[key] = id;
        return id;
    }

    private uint Xbf1Assembly(string name, uint providerKind)
    {
        if (_assemblyIds.TryGetValue(name, out uint id))
        {
            return id;
        }

        id = checked((uint)Metadata.Assemblies.Count);
        Metadata.Assemblies.Add(new XbfAssemblyEntry(providerKind, String(name)));
        _assemblyIds[name] = id;
        return id;
    }

    private readonly record struct PropertyKey(XName DeclaringType, string PropertyName, XbfPropertyFlags Flags);

    private readonly record struct Xbf1PropertyKey(uint DeclaringTypeId, string PropertyName, XbfPropertyFlags Flags);
}
