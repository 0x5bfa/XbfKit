using XbfKit.Schema;
using XbfKit.Xaml;

namespace XbfKit.Decompilation;

/// <summary>Resolves persisted local and trusted references into XAML names.</summary>
internal sealed class XbfReferenceResolver
{
    private readonly XbfMetadata _metadata;
    private readonly XbfTrustedSchema _schema;

    /// <summary>Initializes a resolver for local metadata and a selected trusted schema.</summary>
    internal XbfReferenceResolver(XbfMetadata metadata, XbfTrustedSchema schema)
    {
        _metadata = metadata;
        _schema = schema;
    }

    /// <summary>Resolves a local string-table identifier.</summary>
    internal string String(uint id)
    {
        return Get(_metadata.Strings, id, "string");
    }

    /// <summary>Resolves a local XBF2 string reference.</summary>
    internal string String(Xbf2Reference reference)
    {
        if (reference.IsTrusted)
        {
            throw new InvalidDataException("A shared-string reference has the trusted bit set.");
        }

        return String(reference.ObjectId);
    }

    /// <summary>Resolves a local XML namespace-table identifier.</summary>
    internal string XmlNamespace(uint id)
    {
        uint stringId = Get(_metadata.XmlNamespaces, id, "XML namespace");
        return String(stringId);
    }

    /// <summary>Resolves a local XBF2 XML namespace reference.</summary>
    internal string XmlNamespace(Xbf2Reference reference)
    {
        if (reference.IsTrusted)
        {
            throw new InvalidDataException("An XML namespace reference has the trusted bit set.");
        }

        return XmlNamespace(reference.ObjectId);
    }

    /// <summary>Resolves an XBF2 type reference to a XAML type name.</summary>
    internal XamlTypeName Type(Xbf2Reference reference)
    {
        return reference.IsTrusted ? TrustedType(reference.ObjectId) : LocalType(reference.ObjectId);
    }

    /// <summary>Resolves an XBF1 type reference to a XAML type name.</summary>
    internal XamlTypeName Type(Xbf1Reference reference)
    {
        // The v1 writer clears IsTrustedXbfIndex and the native v1 reader always
        // resolves this identifier through the file-local metadata table. Keep
        // the bit in the format model for lossless reserialization, but do not
        // reinterpret the identifier through a v2 stable schema.
        return LocalType(reference.ObjectId);
    }

    /// <summary>Resolves an XBF2 property reference to a XAML property name.</summary>
    internal XamlPropertyName Property(Xbf2Reference reference, XamlTypeName? currentType = null)
    {
        return reference.IsTrusted ? TrustedProperty(reference.ObjectId, currentType) : LocalProperty(reference.ObjectId);
    }

    /// <summary>Resolves an XBF1 property reference to a XAML property name.</summary>
    internal XamlPropertyName Property(Xbf1Reference reference, XamlTypeName? currentType = null)
    {
        return LocalProperty(reference.ObjectId);
    }

    private XamlTypeName TrustedType(ushort id)
    {
        XbfTrustedType? info = _schema.GetType(id);
        string name = info?.Name ?? $"StableType_{id}";
        string namespaceUri = info?.FullName switch
        {
            "Windows.Foundation.Boolean" or "Windows.Foundation.Double" or "Windows.Foundation.Int32" or "Windows.Foundation.Object" or "Windows.Foundation.String" or "Windows.Foundation.UInt32" => XamlNamespaces.Xaml,
            _ => XamlNamespaces.Presentation,
        };
        return new XamlTypeName(name, namespaceUri);
    }

    private XamlPropertyName TrustedProperty(ushort id, XamlTypeName? currentType)
    {
        XbfTrustedProperty? info = _schema.GetProperty(id);
        string name = info?.Name ?? $"StableProperty_{id}";
        bool directive = name.StartsWith("x:", StringComparison.Ordinal);
        XamlTypeName? declaringType = currentType;
        bool attached = info?.Flags.HasFlag(XbfTrustedPropertyFlags.IsAttached) == true;
        if (attached)
        {
            XbfTrustedProperty propertyInfo = info!.Value;
            string declaringTypeName = _schema.GetType(propertyInfo.DeclaringTypeId)?.Name ?? $"StableType_{propertyInfo.DeclaringTypeId}";
            declaringType = new XamlTypeName(declaringTypeName, XamlNamespaces.Presentation);
        }

        return new XamlPropertyName(name, declaringType, directive ? XamlNamespaces.Xaml : null, directive, IsAttached: attached);
    }

    private XamlTypeName LocalType(uint id)
    {
        XbfTypeEntry entry = Get(_metadata.Types, id, "type");
        string name = String(entry.NameStringId);
        if (entry.Flags.HasFlag(XbfTypeFlags.IsMarkupDirective))
        {
            return new XamlTypeName(name, XamlNamespaces.Xaml);
        }

        string namespaceUri;
        if (entry.Flags.HasFlag(XbfTypeFlags.IsUnknown))
        {
            namespaceUri = BaseNamespace(XmlNamespace(entry.NamespaceId));
        }
        else
        {
            XbfTypeNamespaceEntry typeNamespace = Get(_metadata.TypeNamespaces, entry.NamespaceId, "type namespace");
            string namespaceName = String(typeNamespace.NameStringId);
            namespaceUri = MarkupNamespace(namespaceName, name);
        }

        return new XamlTypeName(name, namespaceUri);
    }

    private static string BaseNamespace(string namespaceUri)
    {
        int question = namespaceUri.IndexOf('?');
        string result = question < 0 ? namespaceUri : namespaceUri[..question];
        return result == XamlNamespaces.LegacyPresentation ? XamlNamespaces.Presentation : result;
    }

    private XamlPropertyName LocalProperty(uint id)
    {
        XbfPropertyEntry entry = Get(_metadata.Properties, id, "property");
        string name = String(entry.NameStringId);
        if (entry.Flags.HasFlag(XbfPropertyFlags.IsMarkupDirective))
        {
            return new XamlPropertyName(name, NamespaceUri: XamlNamespaces.Xaml, IsDirective: true);
        }

        if (entry.Flags.HasFlag(XbfPropertyFlags.IsImplicitProperty))
        {
            return new XamlPropertyName(name, IsImplicit: true);
        }

        XamlTypeName declaringType = LocalType(entry.DeclaringTypeId);
        bool attached = IsAttachedProperty(declaringType, name);
        XamlTypeName? emittedDeclaringType = ShouldQualifyProperty(entry, declaringType, name) ? declaringType : null;
        return new XamlPropertyName(name, emittedDeclaringType, IsAttached: attached);
    }

    private bool ShouldQualifyProperty(XbfPropertyEntry property, XamlTypeName declaringType, string propertyName)
    {
        if (property.Flags.HasFlag(XbfPropertyFlags.IsUnknown) || declaringType.NamespaceUri != XamlNamespaces.Presentation || !IsKnownFrameworkType(property.DeclaringTypeId))
        {
            return true;
        }

        if (!_schema.TryGetPropertyId(declaringType.Name, propertyName, out ushort id))
        {
            return true;
        }

        return _schema.GetProperty(id)?.Flags.HasFlag(XbfTrustedPropertyFlags.IsAttached) == true;
    }

    private bool IsAttachedProperty(XamlTypeName declaringType, string propertyName)
    {
        return declaringType.NamespaceUri == XamlNamespaces.Presentation && _schema.TryGetPropertyId(declaringType.Name, propertyName, out ushort id) && _schema.GetProperty(id)?.Flags.HasFlag(XbfTrustedPropertyFlags.IsAttached) == true;
    }

    private bool IsKnownFrameworkType(uint id)
    {
        XbfTypeEntry entry = Get(_metadata.Types, id, "type");
        if (entry.Flags.HasFlag(XbfTypeFlags.IsUnknown) || entry.Flags.HasFlag(XbfTypeFlags.IsMarkupDirective))
        {
            return false;
        }

        XbfTypeNamespaceEntry typeNamespace = Get(_metadata.TypeNamespaces, entry.NamespaceId, "type namespace");
        string namespaceName = String(typeNamespace.NameStringId);
        return namespaceName == "Windows.UI" || namespaceName.StartsWith("Windows.UI.Xaml", StringComparison.Ordinal) || namespaceName.StartsWith("Microsoft.UI.Xaml", StringComparison.Ordinal);
    }

    private static string MarkupNamespace(string namespaceName, string typeName)
    {
        if (namespaceName.StartsWith("using:", StringComparison.Ordinal))
        {
            return namespaceName;
        }

        if (namespaceName.StartsWith("Windows.UI.Xaml", StringComparison.Ordinal) || namespaceName.StartsWith("Microsoft.UI.Xaml", StringComparison.Ordinal) || (namespaceName == "Windows.UI" && typeName == "Color"))
        {
            return XamlNamespaces.Presentation;
        }

        if (namespaceName == "Windows.Foundation" && typeName is "Boolean" or "Double" or "Int32" or "Object" or "String" or "UInt32")
        {
            return XamlNamespaces.Xaml;
        }

        return $"using:{namespaceName}";
    }

    private static T Get<T>(IReadOnlyList<T> values, uint id, string description)
    {
        if (id >= values.Count)
        {
            throw new InvalidDataException($"The {description} identifier {id} is outside a table of {values.Count} entries.");
        }

        return values[(int)id];
    }
}
