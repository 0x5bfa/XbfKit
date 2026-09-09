using XbfKit.Schema;
using XbfKit.Xaml;
using System.Globalization;

namespace XbfKit.Decompilation;

/// <summary>Interprets a linear XBF1 node stream as the shared XAML object model.</summary>
internal static class Xbf1Decompiler
{
    private static readonly IReadOnlyDictionary<string, string> MarkupExtensionDefaultProperties =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Binding"] = "Path",
            ["TemplateBinding"] = "Property",
            ["RelativeSource"] = "Mode",
            ["StaticResource"] = "ResourceKey",
            ["ThemeResource"] = "ResourceKey",
            ["CustomResource"] = "ResourceKey",
        };

    /// <summary>Decompiles an XBF1 document using the Windows.UI.Xaml trusted schema.</summary>
    internal static XamlObjectModel Decompile(XbfDocument document)
    {
        var nodes = (Xbf1NodeData)document.Nodes;
        var resolver = new XbfReferenceResolver(document.Metadata, XbfSchemaCatalog.Get(XbfDialect.WUX));
        var objectStack = new Stack<object>();
        var propertyStack = new Stack<PropertyScope>();
        var namespaces = new List<XamlNamespaceDeclaration>();
        XamlObjectModel? root = null;

        foreach (Xbf1Node node in nodes.Nodes)
        {
            switch (node.Type)
            {
                case Xbf1NodeType.Namespace:
                    namespaces.Add(new XamlNamespaceDeclaration(node.NamespacePrefix ?? string.Empty, resolver.XmlNamespace(RequiredReference(node).ObjectId)));
                    break;
                case Xbf1NodeType.StartObject:
                    {
                        Xbf1Reference reference = RequiredReference(node);
                        if (reference.Flags.HasFlag(Xbf1NodeFlags.IsRetrieved))
                        {
                            if (propertyStack.Count == 0)
                            {
                                throw new InvalidDataException("A retrieved v1 object has no active property.");
                            }

                            var collection = new XamlCollectionModel();
                            propertyStack.Peek().Values.Add(collection);
                            objectStack.Push(collection);
                            break;
                        }

                        var value = new XamlObjectModel(resolver.Type(reference));
                        if (root is null)
                        {
                            root = value;
                        }
                        else if (propertyStack.Count > 0)
                        {
                            propertyStack.Peek().Values.Add(value);
                        }
                        else
                        {
                            AddItem(objectStack.Peek(), value);
                        }

                        objectStack.Push(value);
                        break;
                    }
                case Xbf1NodeType.EndObject:
                    if (objectStack.Count == 0)
                    {
                        throw new InvalidDataException("The v1 object stack underflowed.");
                    }

                    objectStack.Pop();
                    break;
                case Xbf1NodeType.StartProperty:
                    {
                        XamlObjectModel? owner = objectStack.Count > 0 ? objectStack.Peek() as XamlObjectModel : null;
                        XamlPropertyName property = resolver.Property(RequiredReference(node), owner?.Type);
                        property = NormalizeDeferredProperty(owner?.Type, property);
                        if (objectStack.Count > 0 && objectStack.Peek() is XamlCollectionModel collection && property.IsImplicit)
                        {
                            propertyStack.Push(new PropertyScope(collection.Items));
                            break;
                        }

                        owner ??= CurrentObject(objectStack);
                        var member = new XamlMemberModel(property);
                        owner.Members.Add(member);
                        propertyStack.Push(new PropertyScope(member.Values));
                        break;
                    }
                case Xbf1NodeType.EndProperty:
                    if (propertyStack.Count == 0)
                    {
                        throw new InvalidDataException("The v1 property stack underflowed.");
                    }

                    NormalizeProperty(propertyStack.Pop());
                    break;
                case Xbf1NodeType.Text:
                    RequiredProperty(propertyStack).Values.Add(resolver.String(RequiredReference(node).ObjectId));
                    break;
                case Xbf1NodeType.Value:
                    RequiredProperty(propertyStack).Values.Add(FormatValue(node.Value ?? throw new InvalidDataException("A v1 value node has no value.")));
                    break;
                case Xbf1NodeType.EndOfStream:
                    goto Complete;
                case Xbf1NodeType.LineInfo:
                case Xbf1NodeType.LineInfoAbsolute:
                case Xbf1NodeType.EndOfAttributes:
                case Xbf1NodeType.None:
                    break;
                case Xbf1NodeType.StartConditionalScope:
                case Xbf1NodeType.EndConditionalScope:
                    throw new InvalidDataException("Conditional scopes are not valid in an XBF v1 node stream.");
            }
        }

    Complete:
        if (objectStack.Count != 0 || propertyStack.Count != 0)
        {
            throw new InvalidDataException("The v1 node stream ends with unclosed object or property scopes.");
        }

        if (root is null)
        {
            throw new InvalidDataException("The v1 node stream contains no root object.");
        }

        root.Namespaces.AddRange(namespaces);
        return root;
    }

    private static object? FormatValue(Xbf1Value value)
    {
        return value.Type switch
        {
            Xbf1ValueType.BoolFalse => false,
            Xbf1ValueType.BoolTrue => true,
            Xbf1ValueType.Float => value.Data,
            Xbf1ValueType.LengthConverter => value.Data is float length && float.IsNaN(length) ? "Auto" : value.Data,
            Xbf1ValueType.Signed or Xbf1ValueType.String => value.Data,
            Xbf1ValueType.KeyTime or Xbf1ValueType.Duration => FormatTimeSpan((float)(value.Data ?? 0f)),
            Xbf1ValueType.Thickness => value.Data is XbfThickness thickness ? string.Join(",", Format(thickness.Left), Format(thickness.Top), Format(thickness.Right), Format(thickness.Bottom)) : string.Empty,
            Xbf1ValueType.GridLength => value.Data is XbfGridLength gridLength ? FormatGridLength(gridLength) : string.Empty,
            Xbf1ValueType.Color => value.Data is uint color ? $"#{color:X8}" : "#00000000",
            _ => value.Data,
        };
    }

    private static string Format(float value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static string FormatTimeSpan(float seconds)
    {
        if (!float.IsFinite(seconds))
        {
            return Format(seconds);
        }

        double ticks = Math.Round((double)seconds * TimeSpan.TicksPerSecond);
        try
        {
            return TimeSpan.FromTicks(checked((long)ticks)).ToString("c", CultureInfo.InvariantCulture);
        }
        catch (OverflowException)
        {
            return Format(seconds);
        }
    }

    private static string FormatGridLength(XbfGridLength value)
    {
        return value.UnitType switch
        {
            0 => "Auto",
            1 => Format(value.Value),
            2 when value.Value == 1 => "*",
            2 => $"{Format(value.Value)}*",
            _ => Format(value.Value),
        };
    }

    private static Xbf1Reference RequiredReference(Xbf1Node node)
    {
        return node.Reference ?? throw new InvalidDataException($"A {node.Type} node has no reference.");
    }

    private static PropertyScope RequiredProperty(Stack<PropertyScope> properties)
    {
        return properties.Count > 0 ? properties.Peek() : throw new InvalidDataException("A v1 value appears outside a property.");
    }

    private static XamlObjectModel CurrentObject(Stack<object> values)
    {
        return values.OfType<XamlObjectModel>().FirstOrDefault() ?? throw new InvalidDataException("A v1 property appears outside an object.");
    }

    private static void AddItem(object target, object? value)
    {
        switch (target)
        {
            case XamlObjectModel xamlObject:
                xamlObject.Items.Add(value);
                break;
            case XamlCollectionModel collection:
                collection.Items.Add(value);
                break;
            default:
                throw new InvalidDataException("A v1 object cannot accept a child item.");
        }
    }

    private static XamlPropertyName NormalizeDeferredProperty(XamlTypeName? owner, XamlPropertyName property)
    {
        if (owner?.NamespaceUri != XamlNamespaces.Presentation || owner.Name != "VisualState")
        {
            return property;
        }

        return property.Name switch
        {
            "__DeferredStoryboard" => property with { Name = "Storyboard", DeclaringType = null },
            "__DeferredSetters" => property with { Name = "Setters", DeclaringType = null },
            _ => property,
        };
    }

    private static void NormalizeProperty(PropertyScope scope)
    {
        for (int index = 0; index < scope.Values.Count; index++)
        {
            if (scope.Values[index] is XamlObjectModel value && TryFormatMarkupExtension(value, out string? markup))
            {
                scope.Values[index] = markup;
            }
        }
    }

    private static bool TryFormatMarkupExtension(XamlObjectModel value, out string? markup)
    {
        markup = null;
        if (value.Type.NamespaceUri != XamlNamespaces.Presentation || !MarkupExtensionDefaultProperties.TryGetValue(value.Type.Name, out string? defaultProperty) || value.Items.Count != 0)
        {
            return false;
        }

        var arguments = new List<(string Name, string Value)>();
        foreach (XamlMemberModel member in value.Members)
        {
            if (member.Property.IsImplicit || member.Condition is not null || member.Values.Count != 1 || !TryFormatMarkupArgument(member.Values[0], out string? argument))
            {
                return false;
            }

            arguments.Add((StripPrefix(member.Property.Name), argument!));
        }

        var builder = new System.Text.StringBuilder();
        builder.Append('{').Append(value.Type.Name);
        if (arguments.Count == 1 && arguments[0].Name == defaultProperty)
        {
            builder.Append(' ').Append(arguments[0].Value);
        }
        else
        {
            for (int index = 0; index < arguments.Count; index++)
            {
                (string name, string argument) = arguments[index];
                builder.Append(index == 0 ? ' ' : ", ")
                    .Append(name)
                    .Append('=')
                    .Append(argument);
            }
        }

        builder.Append('}');
        markup = builder.ToString();
        return true;
    }

    private static bool TryFormatMarkupArgument(object? value, out string? result)
    {
        if (value is string markup && XamlMarkupExtensionParser.TryParse(markup, out _))
        {
            result = markup;
            return true;
        }

        if (value is XamlObjectModel nested)
        {
            return TryFormatMarkupExtension(nested, out result);
        }

        if (value is XamlCollectionModel or XamlConditionalValue)
        {
            result = null;
            return false;
        }

        string text = XamlValueFormatter.Format(value);
        bool requiresQuotes = text.Length == 0 || text != text.Trim() || text.IndexOfAny([',', '=', '{', '}', '\r', '\n']) >= 0;
        if (!requiresQuotes)
        {
            result = text;
            return true;
        }

        if (!text.Contains('\''))
        {
            result = $"'{text}'";
            return true;
        }

        if (!text.Contains('"'))
        {
            result = $"\"{text}\"";
            return true;
        }

        result = null;
        return false;
    }

    private static string StripPrefix(string name)
    {
        int separator = name.IndexOf(':');
        return separator < 0 ? name : name[(separator + 1)..];
    }

    private sealed record PropertyScope(List<object?> Values);
}
