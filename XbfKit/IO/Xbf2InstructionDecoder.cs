namespace XbfKit.IO;

/// <summary>Decodes XBF2 object-writer instructions and line records.</summary>
public static class Xbf2InstructionDecoder
{
    /// <summary>Decodes an encoded XBF2 substream.</summary>
    /// <param name="substream">The encoded substream.</param>
    /// <returns>The editable instruction and line-record model.</returns>
    public static Xbf2DecodedSubstream Decode(Xbf2Substream substream)
    {
        ArgumentNullException.ThrowIfNull(substream);
        var result = new Xbf2DecodedSubstream { NodeLength = substream.NodeBytes.Length };
        var reader = new XbfBufferReader(substream.NodeBytes);

        while (reader.Remaining > 0)
        {
            int offset = reader.Position;
            Xbf2Opcode opcode = (Xbf2Opcode)reader.ReadByte();
            result.Instructions.Add(ReadInstruction(ref reader, opcode, offset));
        }

        DecodeLineStream(substream.LineBytes, substream.NodeBytes.Length, result.LineRecords);
        return result;
    }

    private static Xbf2Instruction ReadInstruction(ref XbfBufferReader reader, Xbf2Opcode opcode, int offset)
    {
        switch (opcode)
        {
            case Xbf2Opcode.PushScope:
            case Xbf2Opcode.PopScope:
            case Xbf2Opcode.AddToCollection:
            case Xbf2Opcode.AddToDictionary:
            case Xbf2Opcode.EndInitPopScope:
            case Xbf2Opcode.EndConditionalScope:
            case Xbf2Opcode.EndInitProvideValuePopScope:
                return New(opcode, offset);

            case Xbf2Opcode.AddNamespace:
            case Xbf2Opcode.PushScopeAddNamespace:
                return New(opcode, offset,
                    secondaryReference: ReadReference(ref reader, allowTrusted: false),
                    text: reader.ReadXbfString());

            case Xbf2Opcode.PushConstant:
            case Xbf2Opcode.AddToDictionaryWithKey:
            case Xbf2Opcode.SetConnectionId:
            case Xbf2Opcode.SetName:
            case Xbf2Opcode.GetResourcePropertyBag:
            case Xbf2Opcode.ProvideStaticResourceValue:
            case Xbf2Opcode.ProvideThemeResourceValue:
                return New(opcode, offset, constant: ReadConstant(ref reader));

            case Xbf2Opcode.CheckPeerType:
                return New(opcode, offset, text: reader.ReadXbfString());

            case Xbf2Opcode.SetValue:
            case Xbf2Opcode.PushScopeGetValue:
            case Xbf2Opcode.SetValueFromMarkupExtension:
                return New(opcode, offset, propertyReference: ReadReference(ref reader));

            case Xbf2Opcode.PushScopeCreateTypeBeginInit:
            case Xbf2Opcode.CreateTypeBeginInit:
                return New(opcode, offset, typeReference: ReadReference(ref reader));

            case Xbf2Opcode.PushScopeCreateTypeWithConstantBeginInit:
            case Xbf2Opcode.PushScopeCreateTypeWithTypeConvertedConstantBeginInit:
            case Xbf2Opcode.CreateTypeWithConstantBeginInit:
            case Xbf2Opcode.CreateTypeWithTypeConvertedConstantBeginInit:
                return New(opcode, offset,
                    typeReference: ReadReference(ref reader),
                    constant: ReadConstant(ref reader));

            case Xbf2Opcode.SetValueConstant:
            case Xbf2Opcode.SetValueTypeConvertedConstant:
            case Xbf2Opcode.SetValueFromStaticResource:
            case Xbf2Opcode.SetValueFromThemeResource:
                return New(opcode, offset,
                    propertyReference: ReadReference(ref reader),
                    constant: ReadConstant(ref reader));

            case Xbf2Opcode.SetValueTypeConvertedResolvedProperty:
            case Xbf2Opcode.SetValueFromTemplateBinding:
                return New(opcode, offset,
                    propertyReference: ReadReference(ref reader),
                    secondaryReference: ReadReference(ref reader));

            case Xbf2Opcode.SetValueTypeConvertedResolvedType:
                return New(opcode, offset,
                    propertyReference: ReadReference(ref reader),
                    typeReference: ReadReference(ref reader));

            case Xbf2Opcode.BeginConditionalScope:
                return New(opcode, offset, predicate: ReadPredicate(ref reader));

            case Xbf2Opcode.SetDeferredProperty:
                return New(opcode, offset,
                    propertyReference: ReadReference(ref reader),
                    segment: ReadSegment(ref reader, hasRuntimeData: false));

            case Xbf2Opcode.SetCustomRuntimeData:
                return New(opcode, offset, segment: ReadSegment(ref reader, hasRuntimeData: true));

            default:
                throw new InvalidDataException($"Unsupported or transient XBF v2 opcode 0x{(byte)opcode:X2} at node offset {offset}.");
        }
    }

    private static Xbf2Instruction New(
        Xbf2Opcode opcode,
        int offset,
        Xbf2Reference? typeReference = null,
        Xbf2Reference? propertyReference = null,
        Xbf2Reference? secondaryReference = null,
        Xbf2Constant? constant = null,
        string? text = null,
        Xbf2Segment? segment = null,
        Xbf2Predicate? predicate = null)
    {
        return new()
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
    }

    private static Xbf2Reference ReadReference(ref XbfBufferReader reader, bool allowTrusted = true)
    {
        Xbf2Reference reference = Xbf2Reference.FromEncoded(reader.ReadUInt16());
        if (!allowTrusted && reference.IsTrusted)
        {
            throw new InvalidDataException("A string or XML namespace reference has the trusted bit set.");
        }

        return reference;
    }

    private static Xbf2Constant ReadConstant(ref XbfBufferReader reader)
    {
        var type = (Xbf2ConstantType)reader.ReadByte();
        object? value = type switch
        {
            Xbf2ConstantType.BoolFalse => false,
            Xbf2ConstantType.BoolTrue => true,
            Xbf2ConstantType.Float => reader.ReadSingle(),
            Xbf2ConstantType.Signed => reader.ReadInt32(),
            Xbf2ConstantType.SharedString => ReadReference(ref reader, allowTrusted: false),
            Xbf2ConstantType.Thickness => new XbfThickness(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
            Xbf2ConstantType.GridLength => ReadGridLength(ref reader),
            Xbf2ConstantType.Color => reader.ReadUInt32(),
            Xbf2ConstantType.UniqueString => reader.ReadXbfString(),
            Xbf2ConstantType.NullString => null,
            Xbf2ConstantType.Enum => new Xbf2EnumValue(reader.ReadUInt16(), reader.ReadUInt32()),
            _ => throw new InvalidDataException($"Unknown XBF v2 constant type 0x{(byte)type:X2}."),
        };

        return new Xbf2Constant(type, value);
    }

    private static XbfGridLength ReadGridLength(ref XbfBufferReader reader)
    {
        byte unitType = reader.ReadByte();
        ReadOnlySpan<byte> padding = reader.ReadSpan(3);
        if (padding[0] != 0 || padding[1] != 0 || padding[2] != 0)
        {
            throw new InvalidDataException("An XBF GridLength has non-zero padding.");
        }

        return new XbfGridLength(unitType, reader.ReadSingle());
    }

    private static Xbf2Predicate ReadPredicate(ref XbfBufferReader reader)
    {
        return new(ReadReference(ref reader), ReadReference(ref reader, allowTrusted: false));
    }

    private static Xbf2Segment ReadSegment(ref XbfBufferReader reader, bool hasRuntimeData)
    {
        uint targetSubstream = reader.ReadVarUInt32();
        int staticCount = CheckedVectorCount(reader.ReadVarUInt32());
        int themeCount = CheckedVectorCount(reader.ReadVarUInt32());
        var staticResources = new List<Xbf2Reference>(staticCount);
        var themeResources = new List<Xbf2Reference>(themeCount);
        for (int index = 0; index < staticCount; index++)
        {
            staticResources.Add(ReadReference(ref reader, allowTrusted: false));
        }

        for (int index = 0; index < themeCount; index++)
        {
            themeResources.Add(ReadReference(ref reader, allowTrusted: false));
        }

        Xbf2CustomRuntimeData? runtimeData = hasRuntimeData ? ReadRuntimeData(ref reader) : null;
        var result = new Xbf2Segment
        {
            TargetSubstream = targetSubstream,
            RuntimeData = runtimeData,
        };
        result.StaticResources.AddRange(staticResources);
        result.ThemeResources.AddRange(themeResources);
        return result;
    }

    private static Xbf2CustomRuntimeData ReadRuntimeData(ref XbfBufferReader reader)
    {
        var type = (Xbf2CustomRuntimeDataType)reader.ReadVarUInt32();
        return type switch
        {
            Xbf2CustomRuntimeDataType.DeferredElementV1 or Xbf2CustomRuntimeDataType.DeferredElementV2 or Xbf2CustomRuntimeDataType.DeferredElementV3 => ReadDeferred(ref reader, type),
            Xbf2CustomRuntimeDataType.StyleV1 or Xbf2CustomRuntimeDataType.StyleV2 or Xbf2CustomRuntimeDataType.StyleV3 => ReadStyle(ref reader, type),
            Xbf2CustomRuntimeDataType.ResourceDictionaryV1 or Xbf2CustomRuntimeDataType.ResourceDictionaryV2 or Xbf2CustomRuntimeDataType.ResourceDictionaryV3 or Xbf2CustomRuntimeDataType.ResourceDictionaryV4 => ReadResourceDictionary(ref reader, type),
            Xbf2CustomRuntimeDataType.VisualStateGroupCollectionV1 or Xbf2CustomRuntimeDataType.VisualStateGroupCollectionV2 or Xbf2CustomRuntimeDataType.VisualStateGroupCollectionV3 or Xbf2CustomRuntimeDataType.VisualStateGroupCollectionV4 or Xbf2CustomRuntimeDataType.VisualStateGroupCollectionV5 => ReadVisualStates(ref reader, type),
            _ => throw new InvalidDataException($"Unknown custom runtime data type {type} ({(uint)type})."),
        };
    }

    private static Xbf2DeferredRuntimeData ReadDeferred(ref XbfBufferReader reader, Xbf2CustomRuntimeDataType type)
    {
        Xbf2Reference name = ReadReference(ref reader, allowTrusted: false);
        var properties = new List<(Xbf2Reference, Xbf2Constant)>();
        bool realize = false;
        if (type != Xbf2CustomRuntimeDataType.DeferredElementV1)
        {
            ReadVector(ref reader, (ref itemReader) => properties.Add((ReadReference(ref itemReader), ReadConstant(ref itemReader))));
            if (type == Xbf2CustomRuntimeDataType.DeferredElementV3)
            {
                realize = ReadRuntimeBool(ref reader);
            }
        }

        var result = new Xbf2DeferredRuntimeData { Type = type, Name = name, Realize = realize };
        result.NonDeferredProperties.AddRange(properties);
        return result;
    }

    private static Xbf2StyleRuntimeData ReadStyle(ref XbfBufferReader reader, Xbf2CustomRuntimeDataType type)
    {
        var result = new Xbf2StyleRuntimeData { Type = type };
        ReadVector(ref reader, (ref itemReader) => result.Setters.Add(ReadStyleSetter(ref itemReader, type)));
        if (type == Xbf2CustomRuntimeDataType.StyleV3)
        {
            ReadConditionalObjects(ref reader, result.ConditionalObjects);
        }

        return result;
    }

    private static Xbf2StyleSetter ReadStyleSetter(ref XbfBufferReader reader, Xbf2CustomRuntimeDataType type)
    {
        var flags = (Xbf2StyleSetterFlags)reader.ReadVarUInt32();
        Xbf2Reference? property = null;
        Xbf2Reference? propertyName = null;
        Xbf2Reference? declaringType = null;
        // Only StyleV3 omits the property when the token represents the whole setter.
        if (type != Xbf2CustomRuntimeDataType.StyleV3 || !flags.HasFlag(Xbf2StyleSetterFlags.HasTokenForSelf))
        {
            if (flags.HasFlag(Xbf2StyleSetterFlags.IsPropertyResolved))
            {
                property = ReadReference(ref reader);
            }
            else
            {
                propertyName = ReadReference(ref reader, allowTrusted: false);
                declaringType = ReadReference(ref reader);
            }
        }

        Xbf2Reference? stringValue = null;
        Xbf2Constant? constantValue = null;
        uint? token = null;
        if (flags.HasFlag(Xbf2StyleSetterFlags.HasStringValue))
        {
            stringValue = ReadReference(ref reader, allowTrusted: false);
        }
        else if (flags.HasFlag(Xbf2StyleSetterFlags.HasContainerValue))
        {
            constantValue = ReadConstant(ref reader);
        }
        else if ((flags & (Xbf2StyleSetterFlags.HasStaticResourceValue | Xbf2StyleSetterFlags.HasThemeResourceValue | Xbf2StyleSetterFlags.HasObjectValue | Xbf2StyleSetterFlags.HasTokenForSelf)) != 0)
        {
            token = reader.ReadVarUInt32();
        }

        return new Xbf2StyleSetter
        {
            Flags = flags,
            Property = property,
            PropertyName = propertyName,
            DeclaringType = declaringType,
            StringValue = stringValue,
            ConstantValue = constantValue,
            Token = token,
        };
    }

    private static Xbf2ResourceDictionaryRuntimeData ReadResourceDictionary(ref XbfBufferReader reader, Xbf2CustomRuntimeDataType type)
    {
        var result = new Xbf2ResourceDictionaryRuntimeData { Type = type };
        if (type == Xbf2CustomRuntimeDataType.ResourceDictionaryV4)
        {
            ReadResourceMapV4(ref reader, result.Resources, result.ImplicitResources);
            ReadSharedStringVector(ref reader, result.ResourcesWithNames);
            ReadResourceMapV4(ref reader, result.ConditionalResources, result.ConditionalImplicitResources, multipleTokens: true);
            ReadConditionalObjects(ref reader, result.ConditionalObjects);
            return result;
        }

        ReadResourceEntries(ref reader, result.Resources);
        ReadSharedStringVector(ref reader, result.ResourcesWithNames);
        ReadResourceEntries(ref reader, result.ImplicitResources);
        if (type == Xbf2CustomRuntimeDataType.ResourceDictionaryV1)
        {
            ReadSharedStringVector(ref reader, result.LegacyImplicitDataTemplateKeys);
        }

        if (type is Xbf2CustomRuntimeDataType.ResourceDictionaryV1 or Xbf2CustomRuntimeDataType.ResourceDictionaryV2)
        {
            ReadSharedStringVector(ref reader, result.LegacyImplicitStyleKeys);
        }
        else
        {
            ReadResourceEntries(ref reader, result.ConditionalResources, multipleTokens: true);
            ReadResourceEntries(ref reader, result.ConditionalImplicitResources, multipleTokens: true);
            ReadConditionalObjects(ref reader, result.ConditionalObjects);
        }

        return result;
    }

    private static void ReadResourceEntries(ref XbfBufferReader reader, List<Xbf2ResourceEntry> target, bool multipleTokens = false)
    {
        ReadVector(ref reader, (ref itemReader) =>
        {
            Xbf2Reference key = ReadReference(ref itemReader, allowTrusted: false);
            IReadOnlyList<uint> tokens = multipleTokens ? ReadUIntVector(ref itemReader) : new[] { itemReader.ReadVarUInt32() };
            target.Add(new Xbf2ResourceEntry(new Xbf2ResourceKey(key), tokens));
        });
    }

    private static void ReadResourceMapV4(ref XbfBufferReader reader, List<Xbf2ResourceEntry> explicitResources, List<Xbf2ResourceEntry> implicitResources, bool multipleTokens = false)
    {
        int count = CheckedVectorCount(reader.ReadVarUInt32());
        for (int index = 0; index < count; index++)
        {
            Xbf2Reference key = ReadReference(ref reader, allowTrusted: false);
            ulong hashAndIsKeyType = reader.ReadUInt64();
            IReadOnlyList<uint> tokens = multipleTokens ? ReadUIntVector(ref reader) : new[] { reader.ReadVarUInt32() };
            var entry = new Xbf2ResourceEntry(new Xbf2ResourceKey(key, hashAndIsKeyType), tokens);
            List<Xbf2ResourceEntry> target = (hashAndIsKeyType & 1) == 0 ? explicitResources : implicitResources;
            target.Add(entry);
        }
    }

    private static Xbf2VisualStateRuntimeData ReadVisualStates(ref XbfBufferReader reader, Xbf2CustomRuntimeDataType type)
    {
        var result = new Xbf2VisualStateRuntimeData { Type = type };
        result.StateToGroupMap.AddRange(ReadUIntVector(ref reader));
        ReadVector(ref reader, (ref itemReader) => result.States.Add(ReadVisualState(ref itemReader, type)));
        ReadVector(ref reader, (ref itemReader) => result.Groups.Add(new Xbf2VisualStateGroup(ReadReference(ref itemReader, allowTrusted: false), ReadRuntimeBool(ref itemReader), itemReader.ReadVarUInt32())));
        ReadVector(ref reader, (ref itemReader) => result.Transitions.Add(new Xbf2VisualTransition(ReadReference(ref itemReader, allowTrusted: false), ReadReference(ref itemReader, allowTrusted: false), itemReader.ReadVarUInt32())));

        bool unexpectedTokens = ReadRuntimeBool(ref reader);
        ReadVector(ref reader, (ref itemReader) => result.TransitionLookup.Add(new Xbf2TransitionLookupEntry(itemReader.ReadVarUInt32(), itemReader.ReadVarUInt32(), itemReader.ReadVarUInt32())));
        result.DefaultTransitionsByGroup.AddRange(ReadUIntVector(ref reader));
        uint entireCollectionToken = reader.ReadVarUInt32();
        if (type is Xbf2CustomRuntimeDataType.VisualStateGroupCollectionV4 or Xbf2CustomRuntimeDataType.VisualStateGroupCollectionV5)
        {
            ReadSharedStringVector(ref reader, result.SeenNameDirectives);
        }

        return CopyVisualStateScalars(result, unexpectedTokens, entireCollectionToken);
    }

    private static Xbf2VisualStateRuntimeData CopyVisualStateScalars(Xbf2VisualStateRuntimeData source, bool unexpectedTokens, uint entireCollectionToken)
    {
        var result = new Xbf2VisualStateRuntimeData
        {
            Type = source.Type,
            UnexpectedTokens = unexpectedTokens,
            EntireCollectionToken = entireCollectionToken,
        };
        result.StateToGroupMap.AddRange(source.StateToGroupMap);
        result.States.AddRange(source.States);
        result.Groups.AddRange(source.Groups);
        result.Transitions.AddRange(source.Transitions);
        result.TransitionLookup.AddRange(source.TransitionLookup);
        result.DefaultTransitionsByGroup.AddRange(source.DefaultTransitionsByGroup);
        result.SeenNameDirectives.AddRange(source.SeenNameDirectives);
        return result;
    }

    private static Xbf2VisualState ReadVisualState(ref XbfBufferReader reader, Xbf2CustomRuntimeDataType type)
    {
        var state = new Xbf2VisualState
        {
            Name = ReadReference(ref reader, allowTrusted: false),
            StoryboardToken = reader.ReadVarUInt32(),
            HasStoryboard = ReadRuntimeBool(ref reader),
        };

        if (type != Xbf2CustomRuntimeDataType.VisualStateGroupCollectionV1)
        {
            state.DeferredSetterTokens.AddRange(ReadUIntVector(ref reader));
            ReadVector(ref reader, (ref itemReader) => state.StateTriggerValues.Add(ReadUIntVector(ref itemReader)));
            if (type != Xbf2CustomRuntimeDataType.VisualStateGroupCollectionV2)
            {
                state.ExtensibleTriggerTokens.AddRange(ReadUIntVector(ref reader));
                state.TriggerCollectionTokens.AddRange(ReadUIntVector(ref reader));
                if (type == Xbf2CustomRuntimeDataType.VisualStateGroupCollectionV5)
                {
                    state.StaticResourceTriggerTokens.AddRange(ReadUIntVector(ref reader));
                }
            }
        }

        return state;
    }

    private static void ReadConditionalObjects(ref XbfBufferReader reader, List<Xbf2ConditionalObject> target)
    {
        ReadVector(ref reader, (ref itemReader) =>
        {
            uint token = itemReader.ReadVarUInt32();
            var predicates = new List<Xbf2Predicate>();
            ReadVector(ref itemReader, (ref predicateReader) => predicates.Add(ReadPredicate(ref predicateReader)));
            target.Add(new Xbf2ConditionalObject(token, predicates));
        });
    }

    private static void ReadSharedStringVector(ref XbfBufferReader reader, List<Xbf2Reference> target)
    {
        ReadVector(ref reader, (ref itemReader) => target.Add(ReadReference(ref itemReader, allowTrusted: false)));
    }

    private static List<uint> ReadUIntVector(ref XbfBufferReader reader)
    {
        var result = new List<uint>();
        ReadVector(ref reader, (ref itemReader) => result.Add(itemReader.ReadVarUInt32()));
        return result;
    }

    private delegate void ReadVectorItem(ref XbfBufferReader reader);

    private static void ReadVector(ref XbfBufferReader reader, ReadVectorItem readItem)
    {
        int count = CheckedVectorCount(reader.ReadVarUInt32());
        for (int index = 0; index < count; index++)
        {
            readItem(ref reader);
        }
    }

    private static int CheckedVectorCount(uint count)
    {
        if (count > 16_777_216)
        {
            throw new InvalidDataException($"A custom runtime vector count is unreasonable: {count}.");
        }

        return (int)count;
    }

    private static bool ReadRuntimeBool(ref XbfBufferReader reader)
    {
        return (Xbf2ConstantType)reader.ReadByte() switch
        {
            Xbf2ConstantType.BoolFalse => false,
            Xbf2ConstantType.BoolTrue => true,
            var value => throw new InvalidDataException($"Expected a runtime bool, found constant tag {value}."),
        };
    }

    private static void DecodeLineStream(ReadOnlySpan<byte> bytes, int nodeLength, List<Xbf2LineRecord> target)
    {
        var reader = new XbfBufferReader(bytes);
        uint offset = 0;
        int line = 0;
        int column = 0;
        while (reader.Remaining > 0)
        {
            offset = checked(offset + reader.ReadVarUInt32());
            line = checked(line + ZigZagDecode(reader.ReadVarUInt32()));
            column = checked(column + ZigZagDecode(reader.ReadVarUInt32()));
            if (offset > nodeLength)
            {
                throw new InvalidDataException("A line record points past its XBF v2 node substream.");
            }

            target.Add(new Xbf2LineRecord(offset, line, column));
        }
    }

    private static int ZigZagDecode(uint value)
    {
        return unchecked((int)((value >> 1) ^ (uint)-(int)(value & 1)));
    }
}
