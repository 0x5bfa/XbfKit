using XbfKit.IO;
using XbfKit.Schema;
using XbfKit.Xaml;
using System.Globalization;

namespace XbfKit.Decompilation;

/// <summary>Interprets XBF2 object-writer instructions as the shared XAML object model.</summary>
internal sealed class Xbf2Decompiler
{
    private readonly XbfDocument _document;
    private readonly XbfDialect _dialect;
    private readonly XbfTrustedSchema _schema;
    private readonly Xbf2NodeData _nodeData;
    private readonly XbfReferenceResolver _resolver;
    private readonly Dictionary<int, Xbf2DecodedSubstream> _decoded = [];
    private readonly Stack<object> _objects = [];
    private readonly Stack<ScopeFrame> _scopes = [];
    private readonly Stack<ConditionFrame> _conditions = [];
    private object? _pendingConstant;
    private bool _hasPendingConstant;
    private XamlObjectModel? _root;
    private string? _rootClass;

    private Xbf2Decompiler(XbfDocument document, XbfDialect dialect)
    {
        _document = document;
        _dialect = dialect;
        _schema = XbfSchemaCatalog.Get(dialect);
        _nodeData = (Xbf2NodeData)document.Nodes;
        _resolver = new XbfReferenceResolver(document.Metadata, _schema);
    }

    private Xbf2Decompiler(Xbf2Decompiler context, object? additionalObject = null)
        : this(context._document, context._dialect)
    {
        foreach (object value in context._objects.Reverse())
        {
            _objects.Push(value);
        }

        foreach (ScopeFrame scope in context._scopes.Reverse())
        {
            _scopes.Push(scope.Clone());
        }

        foreach (ConditionFrame condition in context._conditions.Reverse())
        {
            _conditions.Push(condition.Clone());
        }

        if (additionalObject is not null)
        {
            _objects.Push(additionalObject);
        }
    }

    /// <summary>Decompiles an XBF2 document using the requested trusted XAML dialect.</summary>
    internal static XamlObjectModel Decompile(XbfDocument document, XbfDialect dialect)
    {
        var decompiler = new Xbf2Decompiler(document, dialect);
        object? result = decompiler.ProcessSubstream(0, 0, stopAfterRootScope: false);
        return result as XamlObjectModel ?? decompiler._root ?? throw new InvalidDataException("The XBF v2 root substream produced no XAML object.");
    }

    private object? ProcessSubstream(int substreamIndex, uint startOffset, bool stopAfterRootScope)
    {
        Xbf2DecodedSubstream decoded = GetSubstream(substreamIndex);
        int startIndex = decoded.Instructions.FindIndex(value => value.Offset == startOffset);
        if (startIndex < 0)
        {
            throw new InvalidDataException($"Substream {substreamIndex} has no instruction at byte offset {startOffset}.");
        }

        int initialScopeDepth = _scopes.Count;
        int initialConditionDepth = _conditions.Count;
        bool rootIsInterior = IsInteriorNode(decoded.Instructions[startIndex].Opcode);
        for (int index = startIndex; index < decoded.Instructions.Count; index++)
        {
            Xbf2Instruction instruction = decoded.Instructions[index];
            try
            {
                Execute(instruction);
            }
            catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException or OverflowException)
            {
                throw InstructionError(substreamIndex, decoded, instruction, exception);
            }

            if (stopAfterRootScope && (!rootIsInterior || _scopes.Count == initialScopeDepth && _conditions.Count == initialConditionDepth))
            {
                break;
            }
        }

        if (_root is not null)
        {
            if (!string.IsNullOrEmpty(_rootClass))
            {
                _root.SetValue(Directive("Class"), _rootClass);
            }

            return _root;
        }

        return _objects.Count > 0 ? _objects.Peek() : null;
    }

    private Xbf2DecodedSubstream GetSubstream(int index)
    {
        if ((uint)index >= _nodeData.Substreams.Count)
        {
            throw new InvalidDataException($"Substream index {index} is outside the substream table.");
        }

        if (!_decoded.TryGetValue(index, out Xbf2DecodedSubstream? decoded))
        {
            decoded = _nodeData.Substreams[index].Decode();
            _decoded[index] = decoded;
        }

        return decoded;
    }

    private void Execute(Xbf2Instruction instruction)
    {
        switch (instruction.Opcode)
        {
            case Xbf2Opcode.PushScope:
                PushScope(ScopeKind.NamespaceOnly);
                break;
            case Xbf2Opcode.PushScopeAddNamespace:
                PushScope(ScopeKind.NamespaceOnly);
                AddNamespace(instruction);
                break;
            case Xbf2Opcode.AddNamespace:
                AddNamespace(instruction);
                break;
            case Xbf2Opcode.PushScopeGetValue:
                PushGetValue(instruction.RequiredPropertyReference);
                break;
            case Xbf2Opcode.PushScopeCreateTypeBeginInit:
                PushScope(ScopeKind.CreatedObject);
                PushObject(instruction.RequiredTypeReference);
                break;
            case Xbf2Opcode.PushScopeCreateTypeWithConstantBeginInit:
            case Xbf2Opcode.PushScopeCreateTypeWithTypeConvertedConstantBeginInit:
                PushScope(ScopeKind.CreatedObject);
                PushInitializedObject(instruction.RequiredTypeReference, instruction.RequiredConstant);
                break;
            case Xbf2Opcode.CreateTypeBeginInit:
                PushObject(instruction.RequiredTypeReference);
                break;
            case Xbf2Opcode.CreateTypeWithConstantBeginInit:
            case Xbf2Opcode.CreateTypeWithTypeConvertedConstantBeginInit:
                PushInitializedObject(instruction.RequiredTypeReference, instruction.RequiredConstant);
                break;
            case Xbf2Opcode.EndInitPopScope:
            case Xbf2Opcode.EndInitProvideValuePopScope:
            case Xbf2Opcode.PopScope:
                PopScope();
                break;
            case Xbf2Opcode.SetValue:
            case Xbf2Opcode.SetValueFromMarkupExtension:
                SetPoppedValue(instruction.RequiredPropertyReference);
                break;
            case Xbf2Opcode.SetValueConstant:
            case Xbf2Opcode.SetValueTypeConvertedConstant:
                SetCurrentValue(ResolveProperty(instruction.RequiredPropertyReference), ConstantValue(instruction.RequiredConstant));
                break;
            case Xbf2Opcode.SetValueFromStaticResource:
                SetCurrentValue(ResolveProperty(instruction.RequiredPropertyReference), ResourceMarkup("StaticResource", instruction.RequiredConstant));
                break;
            case Xbf2Opcode.SetValueFromThemeResource:
                SetCurrentValue(ResolveProperty(instruction.RequiredPropertyReference), ResourceMarkup("ThemeResource", instruction.RequiredConstant));
                break;
            case Xbf2Opcode.SetValueFromTemplateBinding:
                SetCurrentValue(ResolveProperty(instruction.RequiredPropertyReference), $"{{TemplateBinding {ResolveProperty(instruction.RequiredSecondaryReference).Name}}}");
                break;
            case Xbf2Opcode.SetValueTypeConvertedResolvedProperty:
                SetCurrentValue(ResolveProperty(instruction.RequiredPropertyReference), ResolveProperty(instruction.RequiredSecondaryReference).Name);
                break;
            case Xbf2Opcode.SetValueTypeConvertedResolvedType:
                SetCurrentValue(ResolveProperty(instruction.RequiredPropertyReference), _resolver.Type(instruction.RequiredTypeReference).Name);
                break;
            case Xbf2Opcode.AddToCollection:
                AddToCollection();
                break;
            case Xbf2Opcode.AddToDictionary:
                AddToDictionary(key: null);
                break;
            case Xbf2Opcode.AddToDictionaryWithKey:
                AddToDictionary(ConstantValue(instruction.RequiredConstant));
                break;
            case Xbf2Opcode.PushConstant:
                PushConstant(instruction);
                break;
            case Xbf2Opcode.CheckPeerType:
                _rootClass = instruction.Text ?? string.Empty;
                break;
            case Xbf2Opcode.SetName:
                SetCurrentValue(Directive("Name"), ConstantValue(instruction.RequiredConstant));
                break;
            case Xbf2Opcode.SetConnectionId:
                SetCurrentValue(Directive("ConnectionId"), ConstantValue(instruction.RequiredConstant));
                break;
            case Xbf2Opcode.GetResourcePropertyBag:
                SetCurrentValue(Directive("Uid"), ConstantValue(instruction.RequiredConstant));
                break;
            case Xbf2Opcode.ProvideStaticResourceValue:
                PushResourceMarkupObject("StaticResource", instruction.RequiredConstant);
                break;
            case Xbf2Opcode.ProvideThemeResourceValue:
                PushResourceMarkupObject("ThemeResource", instruction.RequiredConstant);
                break;
            case Xbf2Opcode.SetDeferredProperty:
                SetDeferredProperty(instruction);
                break;
            case Xbf2Opcode.SetCustomRuntimeData:
                ApplyCustomRuntimeData(instruction);
                break;
            case Xbf2Opcode.BeginConditionalScope:
                BeginConditionalScope(instruction);
                break;
            case Xbf2Opcode.EndConditionalScope:
                EndConditionalScope();
                break;
            case Xbf2Opcode.ProvideValue:
            case Xbf2Opcode.CreateTypeWithConstant:
                break;
            default:
                throw new InvalidDataException($"XBF v2 opcode {instruction.Opcode} cannot be decompiled.");
        }
    }

    private void PushScope(ScopeKind kind)
    {
        _scopes.Push(new ScopeFrame(kind));
    }

    private void PushGetValue(Xbf2Reference reference)
    {
        XamlObjectModel owner = CurrentObject();
        XamlPropertyName property = ResolveProperty(reference);
        XamlMemberModel member = owner.GetOrAddMember(property, TakeCondition());
        XamlCollectionModel collection;
        if (member.Values.FirstOrDefault() is XamlCollectionModel existing)
        {
            collection = existing;
        }
        else
        {
            collection = new XamlCollectionModel();
            member.Values.Clear();
            member.Values.Add(collection);
        }

        PushScope(ScopeKind.GetValue);
        _objects.Push(collection);
    }

    private void PopScope()
    {
        if (_scopes.Count == 0)
        {
            throw new InvalidDataException("The XBF v2 scope stack underflowed.");
        }

        ScopeFrame frame = _scopes.Pop();
        XamlObjectModel? current = _objects.OfType<XamlObjectModel>().FirstOrDefault();
        current?.Namespaces.AddRange(frame.Namespaces);

        if (frame.Kind == ScopeKind.GetValue)
        {
            if (_objects.Count == 0 || _objects.Pop() is not XamlCollectionModel)
            {
                throw new InvalidDataException("A GetValue scope did not contain a collection.");
            }
        }
    }

    private void AddNamespace(Xbf2Instruction instruction)
    {
        if (_scopes.Count == 0)
        {
            throw new InvalidDataException("An XBF namespace declaration appears outside a scope.");
        }

        string namespaceUri = _resolver.XmlNamespace(instruction.SecondaryReference ?? throw new InvalidDataException("A namespace instruction has no namespace reference."));
        if (namespaceUri == XamlNamespaces.LegacyPresentation)
        {
            return;
        }

        _scopes.Peek().Namespaces.Add(new XamlNamespaceDeclaration(instruction.Text ?? string.Empty, namespaceUri));
    }

    private void PushObject(Xbf2Reference typeReference)
    {
        var value = new XamlObjectModel(_resolver.Type(typeReference));
        ApplyCondition(value);
        _root ??= value;
        _objects.Push(value);
    }

    private void PushInitializedObject(Xbf2Reference typeReference, Xbf2Constant constant)
    {
        var value = new XamlObjectModel(_resolver.Type(typeReference))
        {
            HasInitializationValue = true,
            InitializationValue = ConstantValue(constant),
        };
        ApplyCondition(value);
        _root ??= value;
        _objects.Push(value);
    }

    private void SetPoppedValue(Xbf2Reference propertyReference)
    {
        if (_objects.Count < 2)
        {
            throw new InvalidDataException("SetValue requires a value and an owning object.");
        }

        object value = _objects.Pop();
        SetCurrentValue(ResolveProperty(propertyReference), value);
    }

    private void SetCurrentValue(XamlPropertyName property, object? value)
    {
        XamlMemberModel member = CurrentObject().GetOrAddMember(property, TakeCondition());
        member.Values.Clear();
        member.Values.Add(value);
    }

    private void BeginConditionalScope(Xbf2Instruction instruction)
    {
        Xbf2Predicate predicate = instruction.Predicate ?? throw new InvalidDataException("A conditional-scope instruction has no predicate.");
        _conditions.Push(new ConditionFrame(ResolveCondition(predicate)));
    }

    private void EndConditionalScope()
    {
        if (_conditions.Count == 0)
        {
            throw new InvalidDataException("The XBF2 conditional-scope stack underflowed.");
        }

        _conditions.Pop();
    }

    private void ApplyCondition(XamlObjectModel value)
    {
        XamlCondition? condition = TakeCondition();
        if (condition is not null)
        {
            value.Condition = condition;
        }
    }

    private object? ApplyCondition(object? value)
    {
        if (value is XamlObjectModel xamlObject)
        {
            ApplyCondition(xamlObject);
            return value;
        }

        XamlCondition? condition = TakeCondition();
        return condition is null || value is XamlConditionalValue ? value : new XamlConditionalValue(value, condition);
    }

    private XamlCondition? TakeCondition()
    {
        foreach (ConditionFrame frame in _conditions)
        {
            if (!frame.Consumed)
            {
                frame.Consumed = true;
                return frame.Condition;
            }
        }

        return null;
    }

    private void AddToCollection()
    {
        object? value;
        if (_hasPendingConstant)
        {
            value = _pendingConstant;
            _pendingConstant = null;
            _hasPendingConstant = false;
        }
        else
        {
            if (_objects.Count < 2)
            {
                throw new InvalidDataException("AddToCollection requires a value and collection.");
            }

            value = _objects.Pop();
        }

        AddItem(_objects.Peek(), ApplyCondition(value));
    }

    private void AddToDictionary(object? key)
    {
        if (_objects.Count < 2)
        {
            throw new InvalidDataException("AddToDictionary requires a value and dictionary.");
        }

        object value = _objects.Pop();
        if (key is not null && value is XamlObjectModel xamlObject)
        {
            xamlObject.SetValue(Directive("Key"), key);
        }

        AddItem(_objects.Peek(), ApplyCondition(value));
    }

    private void PushConstant(Xbf2Instruction instruction)
    {
        object? value = ConstantValue(instruction.RequiredConstant);
        if (_objects.Count == 1 && _scopes.Count == 1 && value is string className)
        {
            _rootClass ??= className;
        }
        else
        {
            _pendingConstant = ApplyCondition(value);
            _hasPendingConstant = true;
        }
    }

    private void SetDeferredProperty(Xbf2Instruction instruction)
    {
        Xbf2Segment segment = instruction.Segment ?? throw new InvalidDataException("A deferred-property instruction has no segment.");
        ValidateSegmentResources(segment);
        object? value = Realize(segment.TargetSubstream, 0);
        SetCurrentValue(ResolveProperty(instruction.RequiredPropertyReference), value);
    }

    private void ApplyCustomRuntimeData(Xbf2Instruction instruction)
    {
        Xbf2Segment segment = instruction.Segment ?? throw new InvalidDataException("A custom-runtime instruction has no segment.");
        Xbf2CustomRuntimeData runtimeData = segment.RuntimeData ?? throw new InvalidDataException("A custom-runtime instruction has no runtime data.");
        ValidateSegmentResources(segment);

        switch (runtimeData)
        {
            case Xbf2DeferredRuntimeData deferred:
                {
                    if (_objects.Count > 0 && _objects.Peek() is XamlObjectModel)
                    {
                        _objects.Pop();
                    }

                    object? value = Realize(segment.TargetSubstream, 0);
                    if (value is XamlObjectModel xamlObject)
                    {
                        xamlObject.SetValue(Directive("Name"), _resolver.String(deferred.Name));
                        if (deferred.Type == Xbf2CustomRuntimeDataType.DeferredElementV3)
                        {
                            xamlObject.SetValue(Directive("Load"), deferred.Realize);
                        }
                        else
                        {
                            xamlObject.SetValue(Directive("DeferLoadStrategy"), "Lazy");
                        }

                        foreach ((Xbf2Reference property, Xbf2Constant constant) in deferred.NonDeferredProperties)
                        {
                            xamlObject.SetValue(ResolveProperty(property), ConstantValue(constant));
                        }
                    }

                    _objects.Push(value ?? string.Empty);
                    break;
                }
            case Xbf2ResourceDictionaryRuntimeData dictionary:
                ApplyResourceDictionary(segment.TargetSubstream, dictionary);
                break;
            case Xbf2VisualStateRuntimeData visualStates:
                {
                    Xbf2DecodedSubstream deferred = GetSubstream(checked((int)segment.TargetSubstream));
                    Xbf2Instruction? firstInstruction = deferred.Instructions.FirstOrDefault(value => value.Offset == visualStates.EntireCollectionToken);
                    object? collection = Realize(segment.TargetSubstream, visualStates.EntireCollectionToken);
                    if (firstInstruction?.Opcode == Xbf2Opcode.PushScopeGetValue)
                    {
                        break;
                    }

                    if (collection is XamlObjectModel collectionObject && collectionObject.Type.Name == "VisualStateGroupCollection")
                    {
                        var collectionItems = new XamlCollectionModel();
                        collectionItems.Items.AddRange(collectionObject.Items);
                        collection = collectionItems;
                    }

                    SetCurrentValue(new XamlPropertyName("VisualStateGroups", new XamlTypeName("VisualStateManager", XamlNamespaces.Presentation)), collection);
                    break;
                }
            case Xbf2StyleRuntimeData style:
                ApplyStyle(segment.TargetSubstream, style);
                break;
        }
    }

    private void ApplyResourceDictionary(uint substream, Xbf2ResourceDictionaryRuntimeData dictionary)
    {
        ApplyResourceEntries(substream, dictionary.Resources, dictionary.ConditionalObjects, hasExplicitKey: true);
        ApplyResourceEntries(substream, dictionary.ImplicitResources, dictionary.ConditionalObjects, hasExplicitKey: false);
        ApplyResourceEntries(substream, dictionary.ConditionalResources, dictionary.ConditionalObjects, hasExplicitKey: true);
        ApplyResourceEntries(substream, dictionary.ConditionalImplicitResources, dictionary.ConditionalObjects, hasExplicitKey: false);
    }

    private void ApplyResourceEntries(uint substream, IReadOnlyList<Xbf2ResourceEntry> entries, IReadOnlyList<Xbf2ConditionalObject> conditionalObjects, bool hasExplicitKey)
    {
        foreach (Xbf2ResourceEntry entry in entries)
        {
            foreach (uint token in entry.Tokens)
            {
                IReadOnlyList<Xbf2Predicate> predicates = PredicatesForToken(conditionalObjects, token);
                object? value = Realize(substream, token, predicates: predicates);
                if (hasExplicitKey && value is XamlObjectModel xamlObject)
                {
                    xamlObject.SetValue(Directive("Key"), _resolver.String(entry.Key.String));
                }

                AddItem(_objects.Peek(), value);
            }
        }
    }

    private void ApplyStyle(uint substream, Xbf2StyleRuntimeData style)
    {
        var setters = new XamlCollectionModel();
        foreach (Xbf2StyleSetter setter in style.Setters)
        {
            var setterObject = new XamlObjectModel(new XamlTypeName("Setter", XamlNamespaces.Presentation));
            string propertyName = setter.Property is Xbf2Reference property ? ResolveProperty(property).Name : setter.PropertyName is Xbf2Reference name ? _resolver.String(name) : string.Empty;
            setterObject.SetValue(new XamlPropertyName("Property", setterObject.Type), propertyName);

            object? value = null;
            bool hasDirectValue = false;
            if (setter.StringValue is Xbf2Reference stringValue)
            {
                value = _resolver.String(stringValue);
                hasDirectValue = true;
            }
            else if (setter.ConstantValue is Xbf2Constant constant)
            {
                value = ConstantValue(constant);
                hasDirectValue = true;
            }
            else if (setter.Token is uint token)
            {
                IReadOnlyList<Xbf2Predicate> predicates = PredicatesForToken(style.ConditionalObjects, token);
                if (setter.Flags.HasFlag(Xbf2StyleSetterFlags.HasTokenForSelf))
                {
                    setterObject = Realize(substream, token, predicates: predicates) as XamlObjectModel ?? setterObject;
                }
                else if (setter.Flags.HasFlag(Xbf2StyleSetterFlags.HasStaticResourceValue) || setter.Flags.HasFlag(Xbf2StyleSetterFlags.HasThemeResourceValue))
                {
                    _ = Realize(substream, token, setterObject, predicates);
                }
                else
                {
                    value = Realize(substream, token, predicates: predicates);
                    hasDirectValue = true;
                }
            }

            if (hasDirectValue)
            {
                setterObject.SetValue(new XamlPropertyName("Value", setterObject.Type), value);
            }

            setters.Items.Add(setterObject);
        }

        CurrentObject().SetValue(new XamlPropertyName("Setters", CurrentObject().Type), setters);
    }

    private object? Realize(uint substream, uint offset, object? additionalObject = null, IReadOnlyList<Xbf2Predicate>? predicates = null)
    {
        var nested = new Xbf2Decompiler(this, additionalObject);
        if (predicates is not null)
        {
            foreach (Xbf2Predicate predicate in predicates)
            {
                nested._conditions.Push(new ConditionFrame(nested.ResolveCondition(predicate)));
            }
        }

        return nested.ProcessSubstream(checked((int)substream), offset, stopAfterRootScope: true);
    }

    private XamlCondition ResolveCondition(Xbf2Predicate predicate)
    {
        return new XamlCondition(_resolver.Type(predicate.Type), _resolver.String(predicate.Arguments));
    }

    private void ValidateSegmentResources(Xbf2Segment segment)
    {
        foreach (Xbf2Reference reference in segment.StaticResources)
        {
            _ = _resolver.String(reference);
        }

        foreach (Xbf2Reference reference in segment.ThemeResources)
        {
            _ = _resolver.String(reference);
        }
    }

    private static InvalidDataException InstructionError(int substreamIndex, Xbf2DecodedSubstream decoded, Xbf2Instruction instruction, Exception innerException)
    {
        Xbf2LineRecord? lineRecord = null;
        foreach (Xbf2LineRecord candidate in decoded.LineRecords)
        {
            if (candidate.NodeOffset > instruction.Offset)
            {
                break;
            }

            lineRecord = candidate;
        }

        string location = lineRecord is Xbf2LineRecord line ? $", XAML line {line.Line}, column {line.Column}" : string.Empty;
        return new InvalidDataException($"Failed to decompile XBF2 substream {substreamIndex}, opcode {instruction.Opcode} at node offset {instruction.Offset}{location}: {innerException.Message}", innerException);
    }

    private static IReadOnlyList<Xbf2Predicate> PredicatesForToken(IReadOnlyList<Xbf2ConditionalObject> conditionalObjects, uint token)
    {
        foreach (Xbf2ConditionalObject conditionalObject in conditionalObjects)
        {
            if (conditionalObject.Token == token)
            {
                return conditionalObject.Predicates;
            }
        }

        return [];
    }

    private object? ConstantValue(Xbf2Constant constant)
    {
        return constant.Type switch
        {
            Xbf2ConstantType.BoolFalse => false,
            Xbf2ConstantType.BoolTrue => true,
            Xbf2ConstantType.Float or Xbf2ConstantType.Signed or Xbf2ConstantType.UniqueString => constant.Data,
            Xbf2ConstantType.SharedString => constant.Data is Xbf2Reference reference ? _resolver.String(reference) : string.Empty,
            Xbf2ConstantType.Thickness => constant.Data is XbfThickness thickness ? string.Join(",", Number(thickness.Left), Number(thickness.Top), Number(thickness.Right), Number(thickness.Bottom)) : string.Empty,
            Xbf2ConstantType.GridLength => constant.Data is XbfGridLength gridLength ? GridLength(gridLength) : string.Empty,
            Xbf2ConstantType.Color => constant.Data is uint color ? $"#{color:X8}" : "#00000000",
            Xbf2ConstantType.NullString => null,
            Xbf2ConstantType.Enum => constant.Data is Xbf2EnumValue enumValue ? _schema.FormatEnumValue(enumValue.StableTypeId, checked((int)enumValue.Value)) ?? enumValue.Value.ToString(CultureInfo.InvariantCulture) : string.Empty,
            _ => constant.Data,
        };
    }

    private string ResourceMarkup(string extension, Xbf2Constant constant)
    {
        return $"{{{extension} {XamlValueFormatter.Format(ConstantValue(constant))}}}";
    }

    private XamlObjectModel ResourceMarkupObject(string extension, Xbf2Constant constant)
    {
        var type = new XamlTypeName(extension, XamlNamespaces.Presentation);
        var value = new XamlObjectModel(type);
        value.SetValue(new XamlPropertyName("ResourceKey", type), ConstantValue(constant));
        return value;
    }

    private void PushResourceMarkupObject(string extension, Xbf2Constant constant)
    {
        XamlObjectModel value = ResourceMarkupObject(extension, constant);
        ApplyCondition(value);
        _objects.Push(value);
    }

    private XamlPropertyName ResolveProperty(Xbf2Reference reference)
    {
        return _resolver.Property(reference, CurrentObject().Type);
    }

    private XamlObjectModel CurrentObject()
    {
        return _objects.OfType<XamlObjectModel>().FirstOrDefault() ?? throw new InvalidDataException("An XBF v2 operation requires a current XAML object.");
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
                throw new InvalidDataException("The current XBF object cannot accept collection items.");
        }
    }

    private static XamlPropertyName Directive(string name)
    {
        return new(name, null, XamlNamespaces.Xaml, IsDirective: true);
    }

    private static bool IsInteriorNode(Xbf2Opcode opcode)
    {
        return opcode is Xbf2Opcode.PushScope or Xbf2Opcode.PushScopeAddNamespace or Xbf2Opcode.PushScopeGetValue or Xbf2Opcode.PushScopeCreateTypeBeginInit or Xbf2Opcode.PushScopeCreateTypeWithConstantBeginInit or Xbf2Opcode.PushScopeCreateTypeWithTypeConvertedConstantBeginInit or Xbf2Opcode.BeginConditionalScope;
    }

    private static string Number(float value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static string GridLength(XbfGridLength value)
    {
        return value.UnitType switch
        {
            0 => "Auto",
            1 => Number(value.Value),
            2 when value.Value == 1 => "*",
            2 => $"{Number(value.Value)}*",
            _ => Number(value.Value),
        };
    }

    private enum ScopeKind
    {
        NamespaceOnly,
        CreatedObject,
        GetValue,
    }

    private sealed class ScopeFrame
    {
        /// <summary>Initializes an object-writer scope frame.</summary>
        internal ScopeFrame(ScopeKind kind)
        {
            Kind = kind;
        }

        /// <summary>Gets the semantic kind of the scope.</summary>
        internal ScopeKind Kind { get; }

        /// <summary>Gets namespace declarations accumulated in the scope.</summary>
        internal List<XamlNamespaceDeclaration> Namespaces { get; } = [];

        /// <summary>Creates a copy for a nested deferred decompilation context.</summary>
        internal ScopeFrame Clone()
        {
            var result = new ScopeFrame(Kind);
            result.Namespaces.AddRange(Namespaces);
            return result;
        }
    }

    private sealed class ConditionFrame
    {
        /// <summary>Initializes a frame for a conditional XAML scope.</summary>
        internal ConditionFrame(XamlCondition condition)
        {
            Condition = condition;
        }

        /// <summary>Gets the condition associated with the frame.</summary>
        internal XamlCondition Condition { get; }

        /// <summary>Gets or sets whether the condition has been attached to an emitted value.</summary>
        internal bool Consumed { get; set; }

        /// <summary>Creates a copy for a nested deferred decompilation context.</summary>
        internal ConditionFrame Clone()
        {
            return new ConditionFrame(Condition) { Consumed = Consumed };
        }
    }
}
