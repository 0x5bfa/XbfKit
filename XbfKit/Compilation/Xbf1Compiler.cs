using XbfKit.Schema;
using XbfKit.Xaml;
using System.Globalization;
using System.Xml.Linq;

namespace XbfKit.Compilation;

/// <summary>Compiles an XML XAML tree into the linear XBF1 node format.</summary>
internal static class Xbf1Compiler
{
    /// <summary>Compiles a parsed XAML document for the requested XBF1 version.</summary>
    internal static XbfDocument Compile(XDocument xaml, XbfVersion version, XbfRuntimeProfile profile)
    {
        XElement root = xaml.Root ?? throw new InvalidDataException("The XAML document has no root element.");
        var metadata = new XbfMetadataBuilder(profile);
        var nodeData = new Xbf1NodeData();

        foreach (XAttribute declaration in NamespaceDeclarations(root))
        {
            uint namespaceId = metadata.XmlNamespace(declaration.Value);
            nodeData.Nodes.Add(new Xbf1Node
            {
                Type = Xbf1NodeType.Namespace,
                Reference = new Xbf1Reference(namespaceId, Xbf1NodeFlags.None),
                NamespacePrefix = declaration.Name.LocalName == "xmlns" ? string.Empty : declaration.Name.LocalName,
            });
        }

        CompileElement(root, metadata, nodeData.Nodes);
        nodeData.Nodes.Add(new Xbf1Node { Type = Xbf1NodeType.EndOfStream });
        return new XbfDocument
        {
            Version = version,
            Hash = System.Text.Encoding.Unicode.GetBytes(new string(' ', 32)),
            Metadata = metadata.Metadata,
            Nodes = nodeData,
        };
    }

    private static void CompileElement(XElement element, XbfMetadataBuilder metadata, List<Xbf1Node> nodes)
    {
        nodes.Add(new()
        {
            Type = Xbf1NodeType.StartObject,
            Reference = metadata.Xbf1TypeReference(element.Name),
        });

        foreach (XAttribute attribute in element.Attributes().Where(value => !value.IsNamespaceDeclaration))
        {
            CompileAttribute(element, attribute, metadata, nodes);
        }

        var implicitNodes = new List<XNode>();
        foreach (XNode child in element.Nodes())
        {
            if (child is XElement propertyElement && IsPropertyElement(propertyElement))
            {
                CompileImplicitValues(element, implicitNodes, metadata, nodes);
                implicitNodes.Clear();
                CompilePropertyElement(element, propertyElement, metadata, nodes);
            }
            else if (child is XElement || child is XText text && !string.IsNullOrWhiteSpace(text.Value))
            {
                implicitNodes.Add(child);
            }
        }

        CompileImplicitValues(element, implicitNodes, metadata, nodes);

        nodes.Add(new Xbf1Node { Type = Xbf1NodeType.EndObject });
    }

    private static void CompileAttribute(XElement owner, XAttribute attribute, XbfMetadataBuilder metadata, List<Xbf1Node> nodes)
    {
        (XName declaringType, string propertyName) = attribute.Name.NamespaceName == XamlNamespaces.Xaml ? (attribute.Name, attribute.Name.LocalName) : XamlNameUtility.SplitPropertyName(owner.Name, attribute.Name);
        XbfPropertyFlags flags = XbfPropertyFlags.IsUnknown;
        if (attribute.Name.NamespaceName == XamlNamespaces.Xaml)
        {
            flags |= XbfPropertyFlags.IsMarkupDirective;
        }

        WritePropertyStart(metadata.Xbf1PropertyReference(declaringType, propertyName, flags), nodes);
        WriteAttributeValue(declaringType, propertyName, attribute, metadata, nodes);
        WritePropertyEnd(nodes);
    }

    private static void WriteAttributeValue(XName declaringType, string propertyName, XAttribute attribute, XbfMetadataBuilder metadata, List<Xbf1Node> nodes)
    {
        string value = attribute.Value;
        if (value.StartsWith("{}", StringComparison.Ordinal))
        {
            // Preserve the escape marker because the text writer cannot infer that a
            // persisted leading brace was literal rather than a markup extension.
            WriteText(value, metadata, nodes);
            return;
        }

        if (IsXamlNullExtension(attribute, value))
        {
            WriteValue(new Xbf1Value(Xbf1ValueType.None, null), nodes);
            return;
        }

        if (XamlMarkupExtensionParser.TryParse(value, out XamlMarkupExtension? extension))
        {
            XElement context = attribute.Parent ?? throw new InvalidDataException("A XAML attribute has no owning element.");
            WriteMarkupExtensionObject(context, extension!, metadata, nodes);
            return;
        }

        if (attribute.Name.NamespaceName == XamlNamespaces.Xaml)
        {
            if (propertyName == "ConnectionId" && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int connectionId))
            {
                WriteValue(new Xbf1Value(Xbf1ValueType.Signed, connectionId), nodes);
                return;
            }

            if (propertyName is "Class" or "Key" or "Name" or "Uid")
            {
                WriteValue(new Xbf1Value(Xbf1ValueType.String, value), nodes);
                return;
            }
        }

        if (metadata.TryGetXbf1PropertyMetadata(declaringType, propertyName, out XbfTrustedProperty property, out XbfTrustedType propertyType) && TryConvertValue(property, propertyType, value, out Xbf1Value converted))
        {
            WriteValue(converted, nodes);
            return;
        }

        WriteText(value, metadata, nodes);
    }

    private static bool IsXamlNullExtension(XAttribute attribute, string text)
    {
        if (!XamlMarkupExtensionParser.TryParse(text, out XamlMarkupExtension? extension) || extension is null || extension.Arguments.Count != 0)
        {
            return false;
        }

        int separator = extension.TypeName.IndexOf(':');
        if (separator <= 0 || extension.TypeName[(separator + 1)..] != "Null" || attribute.Parent is not XElement owner)
        {
            return false;
        }

        string prefix = extension.TypeName[..separator];
        return owner.GetNamespaceOfPrefix(prefix)?.NamespaceName == XamlNamespaces.Xaml;
    }

    private static void WriteMarkupExtensionObject(XElement context, XamlMarkupExtension extension, XbfMetadataBuilder metadata, List<Xbf1Node> nodes)
    {
        string extensionName = StripPrefix(extension.TypeName);
        if (extensionName == "Bind" && Prefix(extension.TypeName) == "x")
        {
            throw new InvalidDataException("x:Bind requires generated binding code and cannot be compiled as a standalone XBF markup extension.");
        }

        XName extensionType = MarkupExtensionType(context, extension.TypeName);
        nodes.Add(new()
        {
            Type = Xbf1NodeType.StartObject,
            Reference = metadata.Xbf1TypeReference(extensionType),
        });

        int positionalIndex = 0;
        foreach (XamlMarkupExtensionArgument argument in extension.Arguments)
        {
            string propertyName = argument.Name ?? PositionalProperty(extension.TypeName, positionalIndex++);
            WritePropertyStart(metadata.Xbf1PropertyReference(extensionType, propertyName), nodes);
            if (argument.Extension is not null)
            {
                WriteMarkupExtensionObject(context, argument.Extension, metadata, nodes);
            }
            else if (metadata.TryGetXbf1PropertyMetadata(extensionType, propertyName, out XbfTrustedProperty property, out XbfTrustedType propertyType) && TryConvertValue(property, propertyType, argument.Value, out Xbf1Value converted))
            {
                WriteValue(converted, nodes);
            }
            else
            {
                WriteText(argument.Value, metadata, nodes);
            }

            WritePropertyEnd(nodes);
        }

        nodes.Add(new Xbf1Node { Type = Xbf1NodeType.EndObject });
    }

    private static XName MarkupExtensionType(XElement context, string qualifiedName)
    {
        string prefix = Prefix(qualifiedName);
        string localName = StripPrefix(qualifiedName);
        XNamespace namespaceName = prefix.Length == 0 ? XamlNamespaces.Presentation : context.GetNamespaceOfPrefix(prefix) ?? throw new InvalidDataException($"Markup extension prefix '{prefix}' is not declared.");

        if (prefix.Length > 0 && prefix != "x" && !localName.EndsWith("Extension", StringComparison.Ordinal))
        {
            localName += "Extension";
        }

        return namespaceName + localName;
    }

    private static string PositionalProperty(string extensionType, int index)
    {
        if (index != 0)
        {
            return $"Argument{index + 1}";
        }

        return StripPrefix(extensionType) switch
        {
            "Binding" or "Bind" => "Path",
            "TemplateBinding" => "Property",
            "RelativeSource" => "Mode",
            "StaticResource" or "ThemeResource" or "CustomResource" => "ResourceKey",
            _ => "Value",
        };
    }

    private static string Prefix(string qualifiedName)
    {
        int separator = qualifiedName.IndexOf(':');
        return separator < 0 ? string.Empty : qualifiedName[..separator];
    }

    private static string StripPrefix(string qualifiedName)
    {
        int separator = qualifiedName.IndexOf(':');
        return separator < 0 ? qualifiedName : qualifiedName[(separator + 1)..];
    }

    private static bool TryConvertValue(XbfTrustedProperty property, XbfTrustedType propertyType, string text, out Xbf1Value value)
    {
        value = default;
        switch (propertyType.FullName)
        {
            case "Windows.Foundation.Boolean" when bool.TryParse(text, out bool boolean):
                value = boolean ? Xbf1Value.True : Xbf1Value.False;
                return true;
            case "Windows.Foundation.Int32" when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int signed):
                value = new Xbf1Value(Xbf1ValueType.Signed, signed);
                return true;
            case "Windows.Foundation.Double":
                if (property.FullName is "Windows.UI.Xaml.FrameworkElement.Width" or "Windows.UI.Xaml.FrameworkElement.Height" && TryParseLength(text, out float length))
                {
                    value = new Xbf1Value(Xbf1ValueType.LengthConverter, length);
                    return true;
                }

                if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float number))
                {
                    value = new Xbf1Value(Xbf1ValueType.Float, number);
                    return true;
                }

                return false;
            case "Windows.Foundation.Object":
            case "Windows.Foundation.String":
            case "Windows.Foundation.Uri":
                value = new Xbf1Value(Xbf1ValueType.String, text);
                return true;
            case "Windows.UI.Xaml.Media.Animation.KeyTime" when TryParseSeconds(text, out float keyTime):
                value = new Xbf1Value(Xbf1ValueType.KeyTime, keyTime);
                return true;
            case "Windows.UI.Xaml.Thickness" when TryParseThickness(text, out XbfThickness thickness):
                value = new Xbf1Value(Xbf1ValueType.Thickness, thickness);
                return true;
            case "Windows.UI.Xaml.GridLength" when TryParseGridLength(text, out XbfGridLength gridLength):
                value = new Xbf1Value(Xbf1ValueType.GridLength, gridLength);
                return true;
            case "Windows.UI.Color" when TryParseColor(text, out uint color):
                value = new Xbf1Value(Xbf1ValueType.Color, color);
                return true;
            case "Windows.UI.Xaml.Duration" when TryParseSeconds(text, out float duration):
                value = new Xbf1Value(Xbf1ValueType.Duration, duration);
                return true;
            default:
                return false;
        }
    }

    private static bool TryParseLength(string text, out float value)
    {
        if (text.Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            value = float.NaN;
            return true;
        }

        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseSeconds(string text, out float value)
    {
        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        if (TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out TimeSpan timeSpan) && timeSpan.TotalSeconds is >= float.MinValue and <= float.MaxValue)
        {
            value = (float)timeSpan.TotalSeconds;
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryParseThickness(string text, out XbfThickness value)
    {
        string[] components = text.Split([',', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        Span<float> parsed = stackalloc float[4];
        if (components.Length is not 1 and not 2 and not 4)
        {
            value = default;
            return false;
        }

        for (int index = 0; index < components.Length; index++)
        {
            if (!float.TryParse(components[index], NumberStyles.Float, CultureInfo.InvariantCulture, out parsed[index]))
            {
                value = default;
                return false;
            }
        }

        value = components.Length switch
        {
            1 => new XbfThickness(parsed[0], parsed[0], parsed[0], parsed[0]),
            2 => new XbfThickness(parsed[0], parsed[1], parsed[0], parsed[1]),
            _ => new XbfThickness(parsed[0], parsed[1], parsed[2], parsed[3]),
        };
        return true;
    }

    private static bool TryParseGridLength(string text, out XbfGridLength value)
    {
        string trimmed = text.Trim();
        if (trimmed.Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            value = new XbfGridLength(0, 1);
            return true;
        }

        if (trimmed.EndsWith('*'))
        {
            string coefficient = trimmed[..^1];
            if (coefficient.Length == 0)
            {
                value = new XbfGridLength(2, 1);
                return true;
            }

            if (float.TryParse(coefficient, NumberStyles.Float, CultureInfo.InvariantCulture, out float star))
            {
                value = new XbfGridLength(2, star);
                return true;
            }
        }
        else if (float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out float pixels))
        {
            value = new XbfGridLength(1, pixels);
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryParseColor(string text, out uint value)
    {
        string hex = text.Trim();
        if (hex.StartsWith('#'))
        {
            hex = hex[1..];
        }
        else
        {
            value = default;
            return false;
        }

        switch (hex.Length)
        {
            case 3:
                hex = $"FF{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
                break;
            case 4:
                hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}{hex[3]}{hex[3]}";
                break;
            case 6:
                hex = $"FF{hex}";
                break;
            case 8:
                break;
            default:
                value = default;
                return false;
        }

        return uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    private static void CompilePropertyElement(XElement owner, XElement propertyElement, XbfMetadataBuilder metadata, List<Xbf1Node> nodes)
    {
        (XName declaringType, string propertyName) = XamlNameUtility.SplitPropertyName(owner.Name, propertyElement.Name);
        WritePropertyValues(declaringType, propertyName, propertyElement.Nodes(), metadata, nodes);
    }

    private static void CompileImplicitValues(XElement owner, IReadOnlyCollection<XNode> values, XbfMetadataBuilder metadata, List<Xbf1Node> nodes)
    {
        if (values.Count == 0)
        {
            return;
        }

        bool isInitialization = values.All(value => value is XText);
        WritePropertyStart(isInitialization ? metadata.Xbf1ImplicitInitializationReference() : metadata.Xbf1ImplicitItemsReference(), nodes);
        WriteValues(values, metadata, nodes);
        WritePropertyEnd(nodes);
    }

    private static void WritePropertyValues(XName declaringType, string propertyName, IEnumerable<XNode> values, XbfMetadataBuilder metadata, List<Xbf1Node> nodes)
    {
        WritePropertyStart(metadata.Xbf1PropertyReference(declaringType, propertyName), nodes);
        bool isCollection = metadata.TryGetXbf1CollectionTypeReference(declaringType, propertyName, out Xbf1Reference collectionType);
        if (isCollection)
        {
            nodes.Add(new()
            {
                Type = Xbf1NodeType.StartObject,
                Reference = collectionType,
            });

            WritePropertyStart(metadata.Xbf1ImplicitItemsReference(), nodes);
        }

        WriteValues(values, metadata, nodes);

        if (isCollection)
        {
            WritePropertyEnd(nodes);
            nodes.Add(new Xbf1Node { Type = Xbf1NodeType.EndObject });
        }

        WritePropertyEnd(nodes);
    }

    private static void WriteValues(IEnumerable<XNode> values, XbfMetadataBuilder metadata, List<Xbf1Node> nodes)
    {
        foreach (XNode value in values)
        {
            switch (value)
            {
                case XElement element:
                    CompileElement(element, metadata, nodes);
                    break;
                case XText text when !string.IsNullOrWhiteSpace(text.Value):
                    WriteText(text.Value, metadata, nodes);
                    break;
            }
        }
    }

    private static void WritePropertyStart(Xbf1Reference property, List<Xbf1Node> nodes)
    {
        nodes.Add(new()
        {
            Type = Xbf1NodeType.StartProperty,
            Reference = property,
        });
    }

    private static void WritePropertyEnd(List<Xbf1Node> nodes)
    {
        nodes.Add(new() { Type = Xbf1NodeType.EndProperty });
    }

    private static void WriteText(string text, XbfMetadataBuilder metadata, List<Xbf1Node> nodes)
    {
        bool unique = ShouldPersistTextAsUnique(text);
        nodes.Add(new()
        {
            Type = Xbf1NodeType.Text,
            Reference = new Xbf1Reference(unique ? metadata.UniqueString(text) : metadata.String(text), unique ? Xbf1NodeFlags.IsStringValueAndUnique : Xbf1NodeFlags.None),
        });
    }

    private static bool ShouldPersistTextAsUnique(string text)
    {
        if (text.Length > 64)
        {
            return true;
        }

        int inspectedLength = Math.Min(text.Length, 3);
        for (int index = 0; index < inspectedLength; index++)
        {
            // The Windows 8.1 writer consults the ASCII CCharTypes numeric bit for
            // only the first three UTF-16 code units.
            if (text[index] is >= '0' and <= '9')
            {
                return true;
            }
        }

        return false;
    }

    private static void WriteValue(Xbf1Value value, List<Xbf1Node> nodes)
    {
        nodes.Add(new()
        {
            Type = Xbf1NodeType.Value,
            Value = value,
        });
    }

    private static bool IsPropertyElement(XElement element)
    {
        return element.Name.LocalName.Contains('.', StringComparison.Ordinal);
    }

    private static IEnumerable<XAttribute> NamespaceDeclarations(XElement root)
    {
        return root.Attributes().Where(value => value.IsNamespaceDeclaration);
    }
}
