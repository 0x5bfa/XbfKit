using XbfKit.IO;
using XbfKit.Schema;
using XbfKit.Xaml;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace XbfKit.Compilation;

/// <summary>Compiles an XML XAML tree into XBF2 object-writer instructions and runtime data.</summary>
internal static class Xbf2Compiler
{
    // Object-writer sequences are based on:
    // https://github.com/microsoft/microsoft-ui-xaml/tree/main/dxaml/xcp/core/Parser
    // https://github.com/microsoft/microsoft-ui-xaml/tree/main/dxaml/xcp/tools/XbfParser/WidgetSpinner
    /// <summary>Compiles a parsed XAML document for the requested XBF2 version and dialect.</summary>
    internal static XbfDocument Compile(XDocument xaml, XbfVersion version, XbfRuntimeProfile profile)
    {
        XElement root = xaml.Root ?? throw new InvalidDataException("The XAML document has no root element.");
        if (root.Attributes().Any(value => IsXamlDirective(value, "Load") || IsXamlDirective(value, "DeferLoadStrategy")))
        {
            throw new InvalidDataException("x:Load and x:DeferLoadStrategy cannot be used on the XAML root element.");
        }

        if (!profile.SupportsConditionalXaml && ContainsConditionalXaml(root))
        {
            throw new InvalidDataException($"Conditional XAML requires WUX target 10.0.15063.0 or newer; the selected target is {profile.TargetRuntimeVersion}.");
        }

        var metadata = new XbfMetadataBuilder(profile);
        var instructions = new InstructionBuilder();

        WriteObject(root, metadata, instructions, isRoot: true);
        var nodeData = new Xbf2NodeData();
        foreach (InstructionBuilder substream in instructions.Substreams)
        {
            nodeData.Substreams.Add(Xbf2Substream.Encode(substream.Build()));
        }

        return new()
        {
            Version = version,
            Metadata = metadata.Metadata,
            Nodes = nodeData,
        };
    }

    private static void WriteObject(XElement element, XbfMetadataBuilder metadata, InstructionBuilder writer, bool isRoot = false, bool skipLoadDirective = false)
    {
        bool hasInitializationValue = TryGetInitializationValue(element, metadata, out string initializationValue);
        XAttribute[] declarations = [.. element.Attributes().Where(value => value.IsNamespaceDeclaration)];

        if (isRoot || declarations.Length > 0)
        {
            if (declarations.Length > 0)
            {
                WriteNamespace(writer, Xbf2Opcode.PushScopeAddNamespace, declarations[0], metadata);
                foreach (XAttribute declaration in declarations.Skip(1))
                {
                    WriteNamespace(writer, Xbf2Opcode.AddNamespace, declaration, metadata);
                }
            }
            else
            {
                writer.Add(Xbf2Opcode.PushScope, element);
            }

            WriteCreateTypeInstruction(writer, hasInitializationValue ? Xbf2Opcode.CreateTypeWithTypeConvertedConstantBeginInit : Xbf2Opcode.CreateTypeBeginInit, metadata.TypeReference(BaseName(element.Name)), hasInitializationValue ? SharedStringConstant(initializationValue, metadata) : null, element);
        }
        else
        {
            WriteCreateTypeInstruction(writer, hasInitializationValue ? Xbf2Opcode.PushScopeCreateTypeWithTypeConvertedConstantBeginInit : Xbf2Opcode.PushScopeCreateTypeBeginInit, metadata.TypeReference(BaseName(element.Name)), hasInitializationValue ? SharedStringConstant(initializationValue, metadata) : null, element);
        }

        foreach (XAttribute attribute in element.Attributes().Where(value => !value.IsNamespaceDeclaration))
        {
            if (skipLoadDirective && (IsXamlDirective(attribute, "Load") || IsXamlDirective(attribute, "DeferLoadStrategy")))
            {
                continue;
            }

            bool conditional = BeginCondition(attribute.Name, element, attribute, metadata, writer);
            WriteAttribute(element, attribute, metadata, writer);
            EndCondition(conditional, attribute, writer);
        }

        HashSet<XElement> customRuntimeElements = WriteCustomRuntimeContent(element, metadata, writer);
        foreach (XNode child in element.Nodes())
        {
            switch (child)
            {
                case XElement handled when customRuntimeElements.Contains(handled):
                    break;
                case XElement propertyElement when IsPropertyElement(propertyElement):
                    WritePropertyElement(element, propertyElement, metadata, writer);
                    break;
                case XElement value:
                    WriteImplicitElement(element, value, metadata, writer);
                    break;
                case XText text when !hasInitializationValue && !string.IsNullOrWhiteSpace(text.Value):
                    WriteImplicitText(element, text, metadata, writer);
                    break;
            }
        }

        writer.Add(Xbf2Opcode.EndInitPopScope, element);
    }

    private static void WriteAttribute(XElement owner, XAttribute attribute, XbfMetadataBuilder metadata, InstructionBuilder writer)
    {
        if (attribute.Name.NamespaceName == XamlNamespaces.Xaml)
        {
            switch (attribute.Name.LocalName)
            {
                case "Key":
                    return;
                case "Class":
                    writer.Add(Xbf2Opcode.CheckPeerType, attribute, text: attribute.Value);
                    return;
                case "Name":
                    WriteConstantInstruction(writer, Xbf2Opcode.SetName, SharedStringConstant(attribute.Value, metadata), attribute);
                    return;
                case "Uid":
                    WriteConstantInstruction(writer, Xbf2Opcode.GetResourcePropertyBag, SharedStringConstant(attribute.Value, metadata), attribute);
                    return;
                case "ConnectionId":
                    if (!int.TryParse(attribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int connectionID))
                    {
                        throw new InvalidDataException($"x:ConnectionId '{attribute.Value}' is not a signed 32-bit integer.");
                    }

                    WriteConstantInstruction(writer, Xbf2Opcode.SetConnectionId, new Xbf2Constant(Xbf2ConstantType.Signed, connectionID), attribute);
                    return;
                case "DeferLoadStrategy":
                case "Load":
                case "FieldModifier":
                case "ClassModifier":
                case "Space":
                    return;
            }
        }

        (XName declaringType, string propertyName) = XamlNameUtility.SplitPropertyName(owner.Name, BaseName(attribute.Name));
        declaringType = BaseName(declaringType);
        XbfPropertyFlags flags = XbfPropertyFlags.IsUnknown;
        if (attribute.Name.NamespaceName == XamlNamespaces.Xaml)
        {
            flags |= XbfPropertyFlags.IsMarkupDirective;
        }

        Xbf2Reference property = metadata.PropertyReference(declaringType, propertyName, flags);
        if (attribute.Value.StartsWith("{}", StringComparison.Ordinal))
        {
            WritePropertyConstant(writer, Xbf2Opcode.SetValueTypeConvertedConstant, property, SharedStringConstant(attribute.Value[2..], metadata), attribute);
            return;
        }

        if (XamlMarkupExtensionParser.TryParse(attribute.Value, out XamlMarkupExtension? extension))
        {
            WriteMarkupExtension(owner, property, extension!, attribute, metadata, writer);
            return;
        }

        WritePropertyConstant(writer, Xbf2Opcode.SetValueTypeConvertedConstant, property, SharedStringConstant(attribute.Value, metadata), attribute);
    }

    private static void WriteMarkupExtension(XElement context, Xbf2Reference property, XamlMarkupExtension extension, XObject source, XbfMetadataBuilder metadata, InstructionBuilder writer)
    {
        string extensionName = StripPrefix(extension.TypeName);
        switch (extensionName)
        {
            case "StaticResource":
                WritePropertyConstant(writer, Xbf2Opcode.SetValueFromStaticResource, property, SharedStringConstant(RequiredMarkupArgument(extension, "ResourceKey"), metadata), source);
                return;
            case "ThemeResource":
                WritePropertyConstant(writer, Xbf2Opcode.SetValueFromThemeResource, property, SharedStringConstant(RequiredMarkupArgument(extension, "ResourceKey"), metadata), source);
                return;
            case "TemplateBinding":
                string propertyName = RequiredMarkupArgument(extension, "Property");
                Xbf2Reference sourceProperty = TemplateBindingPropertyReference(context, propertyName, metadata);
                writer.Add(Xbf2Opcode.SetValueFromTemplateBinding, source, propertyReference: property, secondaryReference: sourceProperty);
                return;
            case "Null" when Prefix(extension.TypeName) == "x":
                WritePropertyConstant(writer, Xbf2Opcode.SetValueConstant, property, Xbf2Constant.NullString, source);
                return;
            case "Type" when Prefix(extension.TypeName) == "x":
                writer.Add(Xbf2Opcode.SetValueTypeConvertedResolvedType, source, propertyReference: property, typeReference: metadata.TypeReference(QualifiedTypeName(context, RequiredMarkupArgument(extension, "TypeName"))));
                return;
            case "Bind" when Prefix(extension.TypeName) == "x":
                throw new InvalidDataException("x:Bind requires generated binding code and cannot be compiled as a standalone XBF markup extension.");
        }

        WriteMarkupExtensionObject(context, extension, source, metadata, writer);
        writer.Add(Xbf2Opcode.SetValueFromMarkupExtension, source, propertyReference: property);
    }

    private static void WriteMarkupExtensionObject(XElement context, XamlMarkupExtension extension, XObject source, XbfMetadataBuilder metadata, InstructionBuilder writer)
    {
        XName extensionType = MarkupExtensionType(context, extension.TypeName);
        WriteTypeInstruction(writer, Xbf2Opcode.PushScopeCreateTypeBeginInit, metadata.TypeReference(extensionType), source);

        int positionalIndex = 0;
        foreach (XamlMarkupExtensionArgument argument in extension.Arguments)
        {
            string propertyName = argument.Name ?? PositionalProperty(extension.TypeName, positionalIndex++);
            Xbf2Reference property = metadata.PropertyReference(extensionType, propertyName);
            if (argument.Extension is not null)
            {
                WriteMarkupExtensionObject(context, argument.Extension, source, metadata, writer);
                writer.Add(Xbf2Opcode.SetValueFromMarkupExtension, source, propertyReference: property);
            }
            else
            {
                WritePropertyConstant(writer, Xbf2Opcode.SetValueTypeConvertedConstant, property, SharedStringConstant(argument.Value, metadata), source);
            }
        }

        writer.Add(Xbf2Opcode.EndInitProvideValuePopScope, source);
    }

    private static void WritePropertyElement(XElement owner, XElement propertyElement, XbfMetadataBuilder metadata, InstructionBuilder writer)
    {
        bool conditional = BeginCondition(propertyElement.Name, propertyElement, propertyElement, metadata, writer);
        (XName declaringType, string propertyName) = XamlNameUtility.SplitPropertyName(owner.Name, BaseName(propertyElement.Name));
        Xbf2Reference property = metadata.PropertyReference(BaseName(declaringType), propertyName);
        XElement[] childElements = [.. propertyElement.Elements()];
        string text = string.Concat(propertyElement.Nodes().OfType<XText>().Select(value => value.Value));
        XbfTrustedTypeFlags propertyTypeFlags = metadata.PropertyTypeFlags(BaseName(declaringType), propertyName);
        bool dictionaryProperty = propertyTypeFlags.HasFlag(XbfTrustedTypeFlags.IsDictionary) || propertyName is "Resources" or "ThemeDictionaries";
        bool collectionProperty = dictionaryProperty || propertyTypeFlags.HasFlag(XbfTrustedTypeFlags.IsCollection);

        if (propertyName == "VisualStateGroups" && declaringType.LocalName == "VisualStateManager")
        {
            WriteVisualStateRuntimeData(propertyElement, metadata, writer);
            EndCondition(conditional, propertyElement, writer);
            return;
        }

        if (childElements.Length == 0 && !collectionProperty)
        {
            string markupText = text.Trim();
            if (XamlMarkupExtensionParser.TryParse(markupText, out XamlMarkupExtension? extension))
            {
                WriteMarkupExtension(owner, property, extension!, propertyElement, metadata, writer);
                EndCondition(conditional, propertyElement, writer);
                return;
            }

            if (markupText.StartsWith("{}", StringComparison.Ordinal))
            {
                text = markupText[2..];
            }

            WritePropertyConstant(writer, Xbf2Opcode.SetValueTypeConvertedConstant, property, SharedStringConstant(text, metadata), propertyElement);
            EndCondition(conditional, propertyElement, writer);
            return;
        }

        if (childElements.Length == 1 && string.IsNullOrWhiteSpace(text) && !collectionProperty)
        {
            if (propertyName == "Template")
            {
                WriteDeferredObjectProperty(property, childElements[0], propertyElement, metadata, writer);
                EndCondition(conditional, propertyElement, writer);
                return;
            }

            WriteObjectWithAttachment(childElements[0], metadata, writer, () => writer.Add(Xbf2Opcode.SetValue, propertyElement, propertyReference: property));
            EndCondition(conditional, propertyElement, writer);
            return;
        }

        writer.Add(Xbf2Opcode.PushScopeGetValue, propertyElement, propertyReference: property);
        foreach (XNode node in propertyElement.Nodes())
        {
            switch (node)
            {
                case XElement child:
                    WriteObjectWithAttachment(child, metadata, writer, () => WriteCollectionAttachment(child, dictionaryProperty, metadata, writer));
                    break;
                case XText childText when !string.IsNullOrWhiteSpace(childText.Value):
                    WriteCollectionTextValue(owner, childText.Value, childText, metadata, writer);
                    break;
            }
        }

        writer.Add(Xbf2Opcode.PopScope, propertyElement);
        EndCondition(conditional, propertyElement, writer);
    }

    private static void WriteImplicitElement(XElement owner, XElement value, XbfMetadataBuilder metadata, InstructionBuilder writer)
    {
        if (owner.Name.LocalName == "ResourceDictionary")
        {
            WriteObjectWithAttachment(value, metadata, writer, () =>
            {
                XAttribute? key = value.Attribute(XName.Get("Key", XamlNamespaces.Xaml));
                if (key is not null)
                {
                    WriteConstantInstruction(writer, Xbf2Opcode.AddToDictionaryWithKey, SharedStringConstant(key.Value, metadata), key);
                }
                else
                {
                    writer.Add(Xbf2Opcode.AddToDictionary, value);
                }
            });
            return;
        }

        XName ownerName = BaseName(owner.Name);
        if (metadata.IsCollectionType(ownerName))
        {
            WriteObjectWithAttachment(value, metadata, writer, () => writer.Add(Xbf2Opcode.AddToCollection, value));
            return;
        }

        string propertyName = XbfMetadataBuilder.GetContentPropertyName(owner.Name);
        Xbf2Reference property = metadata.PropertyReference(ownerName, propertyName);

        if (propertyName == "Template")
        {
            WriteDeferredObjectProperty(property, value, value, metadata, writer);
            return;
        }

        if (propertyName == "Content")
        {
            WriteObjectWithAttachment(value, metadata, writer, () => writer.Add(Xbf2Opcode.SetValue, value, propertyReference: property));
        }
        else
        {
            XbfTrustedTypeFlags propertyTypeFlags = metadata.PropertyTypeFlags(ownerName, propertyName);
            bool dictionaryProperty = propertyTypeFlags.HasFlag(XbfTrustedTypeFlags.IsDictionary) || propertyName is "Resources" or "ThemeDictionaries";
            writer.Add(Xbf2Opcode.PushScopeGetValue, value, propertyReference: property);
            WriteObjectWithAttachment(value, metadata, writer, () => WriteCollectionAttachment(value, dictionaryProperty, metadata, writer));
            writer.Add(Xbf2Opcode.PopScope, value);
        }
    }

    private static void WriteImplicitText(XElement owner, XText text, XbfMetadataBuilder metadata, InstructionBuilder writer)
    {
        XName ownerName = BaseName(owner.Name);
        string propertyName = XbfMetadataBuilder.GetContentPropertyName(owner.Name);
        Xbf2Reference property = metadata.PropertyReference(ownerName, propertyName);
        if (propertyName != "Content")
        {
            writer.Add(Xbf2Opcode.PushScopeGetValue, text, propertyReference: property);
            WriteCollectionTextValue(owner, text.Value, text, metadata, writer);
            writer.Add(Xbf2Opcode.PopScope, text);
            return;
        }

        string markupText = text.Value.Trim();
        if (XamlMarkupExtensionParser.TryParse(markupText, out XamlMarkupExtension? extension))
        {
            WriteMarkupExtension(owner, property, extension!, text, metadata, writer);
            return;
        }

        string value = markupText.StartsWith("{}", StringComparison.Ordinal) ? markupText[2..] : text.Value;

        WritePropertyConstant(writer, Xbf2Opcode.SetValueTypeConvertedConstant, property, SharedStringConstant(value, metadata), text);
    }

    private static void WriteCollectionTextValue(XElement owner, string text, XText source, XbfMetadataBuilder metadata, InstructionBuilder writer)
    {
        string value = text.Trim();
        if (!XamlMarkupExtensionParser.TryParse(value, out XamlMarkupExtension? extension))
        {
            if (value.StartsWith("{}", StringComparison.Ordinal))
            {
                value = value[2..];
            }
            else
            {
                value = text;
            }

            WriteConstantInstruction(writer, Xbf2Opcode.PushConstant, SharedStringConstant(value, metadata), source);
            writer.Add(Xbf2Opcode.AddToCollection, source);
            return;
        }

        string extensionName = StripPrefix(extension!.TypeName);
        if (extensionName is "StaticResource" or "ThemeResource")
        {
            WriteConstantInstruction(writer, extensionName == "StaticResource" ? Xbf2Opcode.ProvideStaticResourceValue : Xbf2Opcode.ProvideThemeResourceValue, SharedStringConstant(RequiredMarkupArgument(extension, "ResourceKey"), metadata), source);
        }
        else if (extensionName == "Null" && Prefix(extension.TypeName) == "x")
        {
            WriteConstantInstruction(writer, Xbf2Opcode.PushConstant, Xbf2Constant.NullString, source);
        }
        else
        {
            if (extensionName == "Bind" && Prefix(extension.TypeName) == "x")
            {
                throw new InvalidDataException("x:Bind requires generated binding code and cannot be compiled as a standalone XBF markup extension.");
            }

            WriteMarkupExtensionObject(owner, extension, source, metadata, writer);
        }

        writer.Add(Xbf2Opcode.AddToCollection, source);
    }

    private static void WriteObjectWithAttachment(XElement element, XbfMetadataBuilder metadata, InstructionBuilder writer, Action writeAttachment)
    {
        bool conditional = BeginCondition(element.Name, element, element, metadata, writer);
        XAttribute? deferredDirective = element.Attributes().FirstOrDefault(value => IsXamlDirective(value, "Load") || IsXamlDirective(value, "DeferLoadStrategy"));
        if (deferredDirective is null)
        {
            WriteObject(element, metadata, writer);
        }
        else
        {
            WriteDeferredElement(element, deferredDirective, metadata, writer);
        }

        writeAttachment();
        EndCondition(conditional, element, writer);
    }

    private static void WriteCollectionAttachment(XElement value, bool dictionary, XbfMetadataBuilder metadata, InstructionBuilder writer)
    {
        if (!dictionary)
        {
            writer.Add(Xbf2Opcode.AddToCollection, value);
            return;
        }

        XAttribute? key = value.Attribute(XName.Get("Key", XamlNamespaces.Xaml));
        if (key is null)
        {
            writer.Add(Xbf2Opcode.AddToDictionary, value);
        }
        else
        {
            WriteConstantInstruction(writer, Xbf2Opcode.AddToDictionaryWithKey, SharedStringConstant(key.Value, metadata), key);
        }
    }

    private static HashSet<XElement> WriteCustomRuntimeContent(XElement element, XbfMetadataBuilder metadata, InstructionBuilder writer)
    {
        var handled = new HashSet<XElement>();
        string typeName = BaseName(element.Name).LocalName;
        if (typeName == "ResourceDictionary")
        {
            XElement[] resources = [.. element.Elements().Where(value => !IsPropertyElement(value))];
            if (resources.Length > 0)
            {
                WriteResourceDictionaryRuntimeData(resources, metadata, writer);
                handled.UnionWith(resources);
            }
        }
        else if (typeName == "Style")
        {
            XElement[] setterContainers = [.. element.Elements()
                .Where(value => IsPropertyElement(value) && XamlNameUtility.SplitPropertyName(element.Name, BaseName(value.Name)).PropertyName == "Setters")];
            XElement[] directSetters = [.. element.Elements().Where(value => !IsPropertyElement(value) && BaseName(value.Name).LocalName == "Setter")];
            if (setterContainers.Length > 0 || directSetters.Length > 0)
            {
                XElement[] setters = [.. setterContainers.SelectMany(value => value.Elements())
, .. directSetters];
                WriteStyleRuntimeData(setters, setterContainers, metadata, writer);
                handled.UnionWith(setterContainers);
                handled.UnionWith(directSetters);
            }
        }

        return handled;
    }

    private static void WriteResourceDictionaryRuntimeData(IReadOnlyList<XElement> resources, XbfMetadataBuilder metadata, InstructionBuilder writer)
    {
        InstructionBuilder deferredWriter = writer.CreateSubstream();
        var runtimeData = new Xbf2ResourceDictionaryRuntimeData
        {
            Type = metadata.RuntimeProfile.ResourceDictionaryRuntimeDataType,
        };

        foreach (XElement resource in resources)
        {
            uint token = deferredWriter.CurrentOffset;
            WriteObject(resource, metadata, deferredWriter);
            XAttribute? keyAttribute = resource.Attribute(XName.Get("Key", XamlNamespaces.Xaml));
            bool explicitKey = keyAttribute is not null;
            string key = keyAttribute?.Value ?? ImplicitResourceKey(resource);
            var entry = new Xbf2ResourceEntry(new Xbf2ResourceKey(Reference(metadata.String(key))), [token]);
            IReadOnlyList<Xbf2Predicate> predicates = PredicatesForElement(resource, metadata);
            if (predicates.Count == 0)
            {
                (explicitKey ? runtimeData.Resources : runtimeData.ImplicitResources).Add(entry);
            }
            else
            {
                (explicitKey ? runtimeData.ConditionalResources : runtimeData.ConditionalImplicitResources).Add(entry);
                runtimeData.ConditionalObjects.Add(new Xbf2ConditionalObject(token, predicates));
            }

            XAttribute? name = resource.Attribute(XName.Get("Name", XamlNamespaces.Xaml));
            if (name is not null)
            {
                runtimeData.ResourcesWithNames.Add(Reference(metadata.String(name.Value)));
            }
        }

        var segment = new Xbf2Segment
        {
            TargetSubstream = checked((uint)deferredWriter.Index),
            RuntimeData = runtimeData,
        };
        AddResourceDependencies(resources, metadata, segment);
        writer.Add(Xbf2Opcode.SetCustomRuntimeData, resources[0], segment: segment);
    }

    private static void WriteStyleRuntimeData(IReadOnlyList<XElement> setters, IReadOnlyList<XElement> setterContainers, XbfMetadataBuilder metadata, InstructionBuilder writer)
    {
        InstructionBuilder deferredWriter = writer.CreateSubstream();
        var runtimeData = new Xbf2StyleRuntimeData
        {
            Type = metadata.RuntimeProfile.StyleRuntimeDataType,
        };
        IReadOnlyList<Xbf2Predicate> containerPredicates = setterContainers
            .SelectMany(value => PredicatesForElement(value, metadata))
            .ToArray();
        foreach (XElement setter in setters)
        {
            uint token = deferredWriter.CurrentOffset;
            WriteObject(setter, metadata, deferredWriter);
            runtimeData.Setters.Add(new Xbf2StyleSetter
            {
                Flags = Xbf2StyleSetterFlags.HasTokenForSelf,
                Token = token,
            });

            Xbf2Predicate[] predicates = [.. containerPredicates
                .Concat(PredicatesForElement(setter, metadata))
                .Distinct()];
            if (predicates.Length > 0)
            {
                runtimeData.ConditionalObjects.Add(new Xbf2ConditionalObject(token, predicates));
            }
        }

        var segment = new Xbf2Segment
        {
            TargetSubstream = checked((uint)deferredWriter.Index),
            RuntimeData = runtimeData,
        };
        AddResourceDependencies(setters, metadata, segment);
        writer.Add(Xbf2Opcode.SetCustomRuntimeData, setterContainers.FirstOrDefault() ?? setters.First(), segment: segment);
    }

    private static void WriteVisualStateRuntimeData(XElement propertyElement, XbfMetadataBuilder metadata, InstructionBuilder writer)
    {
        InstructionBuilder deferredWriter = writer.CreateSubstream();
        uint collectionToken = deferredWriter.CurrentOffset;
        deferredWriter.Add(Xbf2Opcode.PushScopeGetValue, propertyElement, propertyReference: metadata.PropertyReference(XName.Get("VisualStateManager", XamlNamespaces.Presentation), "VisualStateGroups"));
        foreach (XElement group in propertyElement.Elements())
        {
            WriteObjectWithAttachment(group, metadata, deferredWriter, () => deferredWriter.Add(Xbf2Opcode.AddToCollection, group));
        }

        deferredWriter.Add(Xbf2Opcode.PopScope, propertyElement);
        var runtimeData = new Xbf2VisualStateRuntimeData
        {
            Type = metadata.RuntimeProfile.VisualStateRuntimeDataType,
            // MrmTool stores the complete collection, so direct WUX to fault it in
            // instead of consulting compact state tables that are not serialized.
            UnexpectedTokens = true,
            EntireCollectionToken = collectionToken,
        };
        var segment = new Xbf2Segment
        {
            TargetSubstream = checked((uint)deferredWriter.Index),
            RuntimeData = runtimeData,
        };
        AddResourceDependencies([propertyElement], metadata, segment);
        writer.Add(Xbf2Opcode.SetCustomRuntimeData, propertyElement, segment: segment);
    }

    private static IReadOnlyList<Xbf2Predicate> PredicatesForElement(XElement element, XbfMetadataBuilder metadata)
    {
        if (!TryParseCondition(element.Name.NamespaceName, element, out XName predicateType, out string arguments))
        {
            return [];
        }

        return
        [
            new Xbf2Predicate(metadata.TypeReference(predicateType), Reference(metadata.String(arguments))),
        ];
    }

    private static string ImplicitResourceKey(XElement resource)
    {
        XAttribute? typeKey = resource.Attributes().FirstOrDefault(value => !value.IsNamespaceDeclaration && value.Name.LocalName is "TargetType" or "DataType");
        return typeKey?.Value ?? BaseName(resource.Name).LocalName;
    }

    private static void AddResourceDependencies(IEnumerable<XElement> values, XbfMetadataBuilder metadata, Xbf2Segment segment)
    {
        var staticResources = new HashSet<string>(StringComparer.Ordinal);
        var themeResources = new HashSet<string>(StringComparer.Ordinal);
        foreach (XElement value in values)
        {
            foreach (XAttribute attribute in value.DescendantsAndSelf().Attributes()
                         .Where(attribute => !attribute.IsNamespaceDeclaration))
            {
                CollectResourceDependency(attribute.Value, staticResources, themeResources);
            }

            foreach (XText text in value.DescendantNodesAndSelf().OfType<XText>())
            {
                CollectResourceDependency(text.Value.Trim(), staticResources, themeResources);
            }
        }

        segment.StaticResources.AddRange(staticResources.Select(value => Reference(metadata.String(value))));
        segment.ThemeResources.AddRange(themeResources.Select(value => Reference(metadata.String(value))));
    }

    private static void CollectResourceDependency(string text, ISet<string> staticResources, ISet<string> themeResources)
    {
        if (!XamlMarkupExtensionParser.TryParse(text, out XamlMarkupExtension? extension) || extension is null)
        {
            return;
        }

        string extensionName = StripPrefix(extension.TypeName);
        if (extensionName is "StaticResource" or "ThemeResource")
        {
            string key = RequiredMarkupArgument(extension, "ResourceKey");
            (extensionName == "StaticResource" ? staticResources : themeResources).Add(key);
        }

        foreach (XamlMarkupExtensionArgument argument in extension.Arguments)
        {
            if (argument.Extension is not null)
            {
                CollectResourceDependency(FormatMarkupExtension(argument.Extension), staticResources, themeResources);
            }
        }
    }

    private static string FormatMarkupExtension(XamlMarkupExtension extension)
    {
        return "{" + extension.TypeName + " " + string.Join(", ", extension.Arguments.Select(argument => argument.Extension is null ? (argument.Name is null ? argument.Value : $"{argument.Name}={argument.Value}") : (argument.Name is null ? FormatMarkupExtension(argument.Extension) : $"{argument.Name}={FormatMarkupExtension(argument.Extension)}"))) + "}";
    }

    private static void WriteDeferredElement(XElement element, XAttribute deferredDirective, XbfMetadataBuilder metadata, InstructionBuilder writer)
    {
        WriteTypeInstruction(writer, Xbf2Opcode.PushScopeCreateTypeBeginInit, metadata.TypeReference(XName.Get("DeferredElement", XamlNamespaces.Presentation)), element);

        InstructionBuilder deferredWriter = writer.CreateSubstream();
        WriteObject(element, metadata, deferredWriter, skipLoadDirective: true);
        XAttribute? name = element.Attributes().FirstOrDefault(value => IsXamlDirective(value, "Name"));
        if (name is null || string.IsNullOrWhiteSpace(name.Value))
        {
            throw new InvalidDataException("An element using x:Load or x:DeferLoadStrategy must declare x:Name.");
        }

        bool realize = false;
        if (IsXamlDirective(deferredDirective, "Load"))
        {
            if (metadata.RuntimeProfile.DeferredElementRuntimeDataType != Xbf2CustomRuntimeDataType.DeferredElementV3)
            {
                throw new InvalidDataException($"x:Load requires WUX target 10.0.15063.0 or newer; the selected target is {metadata.RuntimeProfile.TargetRuntimeVersion}.");
            }

            if (!bool.TryParse(deferredDirective.Value, out realize))
            {
                throw new InvalidDataException($"x:Load '{deferredDirective.Value}' is not a Boolean value.");
            }
        }
        else if (!deferredDirective.Value.Equals("Lazy", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"x:DeferLoadStrategy '{deferredDirective.Value}' is unsupported; expected 'Lazy'.");
        }

        var runtimeData = new Xbf2DeferredRuntimeData
        {
            Type = metadata.RuntimeProfile.DeferredElementRuntimeDataType,
            Name = Reference(metadata.String(name.Value)),
            Realize = realize,
        };
        AddNonDeferredProperties(element, metadata, runtimeData);
        writer.Add(Xbf2Opcode.SetCustomRuntimeData, element, segment: new Xbf2Segment
        {
            TargetSubstream = checked((uint)deferredWriter.Index),
            RuntimeData = runtimeData,
        });
        writer.Add(Xbf2Opcode.EndInitPopScope, element);
    }

    private static void AddNonDeferredProperties(XElement element, XbfMetadataBuilder metadata, Xbf2DeferredRuntimeData runtimeData)
    {
        foreach (XAttribute attribute in element.Attributes().Where(value => !value.IsNamespaceDeclaration))
        {
            (XName declaringType, string propertyName) =
                XamlNameUtility.SplitPropertyName(element.Name, BaseName(attribute.Name));
            if (declaringType.LocalName != "RelativePanel" || !IsRelativePanelNonDeferredProperty(propertyName))
            {
                continue;
            }

            Xbf2Constant constant;
            if (propertyName.EndsWith("WithPanel", StringComparison.Ordinal))
            {
                if (!bool.TryParse(attribute.Value, out bool boolean))
                {
                    throw new InvalidDataException($"RelativePanel.{propertyName} on a deferred element must be a Boolean literal.");
                }

                constant = boolean ? Xbf2Constant.True : Xbf2Constant.False;
            }
            else
            {
                if (XamlMarkupExtensionParser.TryParse(attribute.Value, out _))
                {
                    throw new InvalidDataException($"RelativePanel.{propertyName} on a deferred element cannot use a markup extension.");
                }

                constant = SharedStringConstant(attribute.Value, metadata);
            }

            runtimeData.NonDeferredProperties.Add((metadata.PropertyReference(BaseName(declaringType), propertyName), constant));
        }
    }

    private static bool IsRelativePanelNonDeferredProperty(string propertyName)
    {
        return propertyName is "LeftOf" or "Above" or "RightOf" or "Below" or "AlignHorizontalCenterWith" or "AlignVerticalCenterWith" or "AlignLeftWith" or "AlignTopWith" or "AlignRightWith" or "AlignBottomWith" or "AlignLeftWithPanel" or "AlignTopWithPanel" or "AlignRightWithPanel" or "AlignBottomWithPanel" or "AlignHorizontalCenterWithPanel" or "AlignVerticalCenterWithPanel";
    }

    private static void WriteDeferredObjectProperty(Xbf2Reference property, XElement value, XObject source, XbfMetadataBuilder metadata, InstructionBuilder writer)
    {
        InstructionBuilder deferredWriter = writer.CreateSubstream();
        bool conditional = BeginCondition(value.Name, value, value, metadata, deferredWriter);
        WriteObject(value, metadata, deferredWriter);
        EndCondition(conditional, value, deferredWriter);
        writer.Add(Xbf2Opcode.SetDeferredProperty, source, propertyReference: property, segment: new Xbf2Segment
        {
            TargetSubstream = checked((uint)deferredWriter.Index),
        });
    }

    private static bool BeginCondition(XName name, XElement context, XObject source, XbfMetadataBuilder metadata, InstructionBuilder writer)
    {
        if (!TryParseCondition(name.NamespaceName, context, out XName predicateType, out string arguments))
        {
            return false;
        }

        writer.Add(Xbf2Opcode.BeginConditionalScope, source, predicate: new Xbf2Predicate(metadata.TypeReference(predicateType), Reference(metadata.String(arguments))));
        return true;
    }

    private static void EndCondition(bool conditional, XObject source, InstructionBuilder writer)
    {
        if (conditional)
        {
            writer.Add(Xbf2Opcode.EndConditionalScope, source);
        }
    }

    private static bool TryParseCondition(string namespaceUri, XElement context, out XName predicateType, out string arguments)
    {
        predicateType = default!;
        arguments = string.Empty;
        int question = namespaceUri.IndexOf('?');
        if (question < 0)
        {
            return false;
        }

        string expression = namespaceUri[(question + 1)..];
        int openingParenthesis = expression.IndexOf('(');
        if (openingParenthesis <= 0 || expression[^1] != ')')
        {
            throw new InvalidDataException($"Invalid conditional XAML namespace '{namespaceUri}'.");
        }

        string qualifiedName = expression[..openingParenthesis];
        arguments = expression[(openingParenthesis + 1)..^1];
        string prefix = Prefix(qualifiedName);
        string localName = StripPrefix(qualifiedName);
        XNamespace predicateNamespace;
        if (prefix.Length == 0)
        {
            predicateNamespace = XamlNamespaces.Presentation;
        }
        else
        {
            predicateNamespace = context.GetNamespaceOfPrefix(prefix) ?? throw new InvalidDataException($"Conditional XAML predicate prefix '{prefix}' is not declared.");
        }

        predicateType = predicateNamespace + localName;
        return true;
    }

    private static void WriteNamespace(InstructionBuilder writer, Xbf2Opcode opcode, XAttribute declaration, XbfMetadataBuilder metadata)
    {
        writer.Add(opcode, declaration, secondaryReference: Reference(metadata.XmlNamespace(declaration.Value)), text: declaration.Name.LocalName == "xmlns" ? string.Empty : declaration.Name.LocalName);
    }

    private static void WriteTypeInstruction(InstructionBuilder writer, Xbf2Opcode opcode, Xbf2Reference type, XObject source)
    {
        writer.Add(opcode, source, typeReference: type);
    }

    private static void WriteCreateTypeInstruction(InstructionBuilder writer, Xbf2Opcode opcode, Xbf2Reference type, Xbf2Constant? constant, XObject source)
    {
        writer.Add(opcode, source, typeReference: type, constant: constant);
    }

    private static void WriteConstantInstruction(InstructionBuilder writer, Xbf2Opcode opcode, Xbf2Constant constant, XObject source)
    {
        writer.Add(opcode, source, constant: constant);
    }

    private static void WritePropertyConstant(InstructionBuilder writer, Xbf2Opcode opcode, Xbf2Reference property, Xbf2Constant constant, XObject source)
    {
        writer.Add(opcode, source, propertyReference: property, constant: constant);
    }

    private static Xbf2Constant SharedStringConstant(string value, XbfMetadataBuilder metadata)
    {
        return new Xbf2Constant(Xbf2ConstantType.SharedString, Reference(metadata.String(value)));
    }

    private static Xbf2Reference TemplateBindingPropertyReference(XElement context, string propertyName, XbfMetadataBuilder metadata)
    {
        XElement? template = context.AncestorsAndSelf().FirstOrDefault(value => value.Name.LocalName is "ControlTemplate" or "DataTemplate");
        XAttribute? targetType = template?.Attributes().FirstOrDefault(value => !value.IsNamespaceDeclaration && value.Name.LocalName == "TargetType");
        if (template is not null && targetType is not null)
        {
            string qualifiedTypeName = targetType.Value;
            if (XamlMarkupExtensionParser.TryParse(targetType.Value, out XamlMarkupExtension? extension) && StripPrefix(extension!.TypeName) == "Type")
            {
                qualifiedTypeName = RequiredMarkupArgument(extension, "TypeName");
            }

            XName templateTargetType = QualifiedTypeName(template, qualifiedTypeName);
            return metadata.PropertyReference(templateTargetType, propertyName);
        }

        return metadata.PropertyReference(BaseName(context.Name), propertyName);
    }

    private static Xbf2Reference Reference(uint id)
    {
        if (id >= 0x8000)
        {
            throw new InvalidDataException("A generated XBF2 reference exceeds 15 bits.");
        }

        return new Xbf2Reference(checked((ushort)id), IsTrusted: false);
    }

    private static XName BaseName(XName name)
    {
        string namespaceUri = name.NamespaceName;
        int question = namespaceUri.IndexOf('?');
        return question < 0 ? name : XName.Get(name.LocalName, namespaceUri[..question]);
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

    private static XName QualifiedTypeName(XElement context, string qualifiedName)
    {
        string prefix = Prefix(qualifiedName);
        string localName = StripPrefix(qualifiedName);
        XNamespace namespaceName = prefix.Length == 0 ? context.GetDefaultNamespace() : context.GetNamespaceOfPrefix(prefix) ?? throw new InvalidDataException($"XAML type prefix '{prefix}' is not declared.");
        return BaseName(namespaceName + localName);
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
            "RelativeSource" => "Mode",
            "CustomResource" => "ResourceKey",
            _ => "Value",
        };
    }

    private static string RequiredMarkupArgument(XamlMarkupExtension extension, string namedProperty)
    {
        XamlMarkupExtensionArgument? argument = extension.Arguments.FirstOrDefault(value => value.Name is null || value.Name.Equals(namedProperty, StringComparison.Ordinal));
        return argument?.Value ?? throw new InvalidDataException($"Markup extension {extension.TypeName} requires {namedProperty}.");
    }

    private static string Prefix(string qualifiedName)
    {
        int colon = qualifiedName.IndexOf(':');
        return colon < 0 ? string.Empty : qualifiedName[..colon];
    }

    private static string StripPrefix(string qualifiedName)
    {
        int colon = qualifiedName.IndexOf(':');
        return colon < 0 ? qualifiedName : qualifiedName[(colon + 1)..];
    }

    private static bool IsPropertyElement(XElement element)
    {
        return element.Name.LocalName.Contains('.', StringComparison.Ordinal);
    }

    private static bool IsXamlDirective(XAttribute attribute, string name)
    {
        return attribute.Name.NamespaceName == XamlNamespaces.Xaml && attribute.Name.LocalName == name;
    }

    private static bool ContainsConditionalXaml(XElement root)
    {
        return root.DescendantsAndSelf().Any(element =>
            element.Name.NamespaceName.Contains('?', StringComparison.Ordinal) ||
            element.Attributes().Any(attribute => attribute.Name.NamespaceName.Contains('?', StringComparison.Ordinal)));
    }

    private static bool TryGetInitializationValue(XElement element, XbfMetadataBuilder metadata, out string value)
    {
        value = string.Empty;
        if (element.Elements().Any())
        {
            return false;
        }

        string text = string.Concat(element.Nodes().OfType<XText>().Select(node => node.Value)).Trim(' ', '\t', '\r', '\n');
        if (text.Length == 0)
        {
            return false;
        }

        XName name = BaseName(element.Name);
        if (name.NamespaceName != XamlNamespaces.Presentation || IsPresentationInitializationType(name.LocalName) || metadata.IsFrameworkTypeWithoutProperty(name, XamlFallbackSchema.GetContentPropertyName(name.LocalName)))
        {
            value = text.StartsWith("{}", StringComparison.Ordinal) ? text[2..] : text;
            return true;
        }

        return false;
    }

    private static bool IsPresentationInitializationType(string typeName)
    {
        return typeName is "Color" or "CornerRadius" or "Duration" or "FontWeight" or "FontFamily" or "GridLength" or "KeyTime" or "Matrix" or "Point" or "Rect" or "RepeatBehavior" or "Size" or "Thickness";
    }

    private sealed class InstructionBuilder
    {
        private readonly Xbf2DecodedSubstream _decoded = new();
        private readonly List<InstructionBuilder> _substreams;
        private int _nodeLength;
        private int _lastLine;
        private int _lastColumn;

        /// <summary>Creates a root instruction builder and its shared substream list.</summary>
        internal InstructionBuilder()
        {
            _substreams = [this];
        }

        private InstructionBuilder(List<InstructionBuilder> substreams)
        {
            _substreams = substreams;
            _substreams.Add(this);
        }

        /// <summary>Gets the builder's substream index.</summary>
        internal int Index
        {
            get
            {
                return _substreams.IndexOf(this);
            }
        }

        /// <summary>Gets all builders owned by the root instruction builder.</summary>
        internal IReadOnlyList<InstructionBuilder> Substreams
        {
            get
            {
                return _substreams;
            }
        }

        /// <summary>Gets the current encoded node-stream offset.</summary>
        internal uint CurrentOffset => checked((uint)_nodeLength);

        /// <summary>Creates and registers an additional instruction substream.</summary>
        internal InstructionBuilder CreateSubstream()
        {
            return new InstructionBuilder(_substreams);
        }

        /// <summary>Adds an instruction and records its estimated encoded offset and source location.</summary>
        internal void Add(Xbf2Opcode opcode, XObject? source = null, Xbf2Reference? typeReference = null, Xbf2Reference? propertyReference = null, Xbf2Reference? secondaryReference = null, Xbf2Constant? constant = null, string? text = null, Xbf2Segment? segment = null, Xbf2Predicate? predicate = null)
        {
            int offset = _nodeLength;
            AddLineRecord(source, offset);
            var instruction = new Xbf2Instruction
            {
                Opcode = opcode,
                Offset = offset,
                TypeReference = typeReference,
                PropertyReference = propertyReference,
                SecondaryReference = secondaryReference,
                Constant = constant,
                Text = text,
                Segment = segment,
                Predicate = predicate,
            };
            _decoded.Instructions.Add(instruction);
            _nodeLength = checked(_nodeLength + Xbf2InstructionEncoder.EncodeInstructions([instruction]).Length);
        }

        /// <summary>Builds the editable decoded substream represented by this builder.</summary>
        internal Xbf2DecodedSubstream Build()
        {
            var result = new Xbf2DecodedSubstream { NodeLength = _nodeLength };
            result.Instructions.AddRange(_decoded.Instructions);
            result.LineRecords.AddRange(_decoded.LineRecords);
            return result;
        }

        private void AddLineRecord(XObject? source, int offset)
        {
            if (source is not IXmlLineInfo lineInfo || !lineInfo.HasLineInfo())
            {
                return;
            }

            int line = lineInfo.LineNumber;
            int column = lineInfo.LinePosition;
            if (line == _lastLine && column == _lastColumn)
            {
                return;
            }

            _decoded.LineRecords.Add(new Xbf2LineRecord(checked((uint)offset), line, column));
            _lastLine = line;
            _lastColumn = column;
        }
    }
}
