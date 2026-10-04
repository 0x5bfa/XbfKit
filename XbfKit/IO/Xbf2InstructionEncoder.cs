using System.Text;

namespace XbfKit.IO;

/// <summary>
/// Encodes the persisted XBF2 object-writer instruction format.
/// </summary>
public static class Xbf2InstructionEncoder
{
    /// <summary>Encodes instructions and line records into an XBF2 substream.</summary>
    /// <param name="decoded">The decoded substream.</param>
    /// <returns>The encoded XBF2 substream.</returns>
    public static Xbf2Substream Encode(Xbf2DecodedSubstream decoded)
    {
        ArgumentNullException.ThrowIfNull(decoded);

        using var nodeStream = new MemoryStream();
        using var writer = new BinaryWriter(nodeStream, Encoding.Unicode, leaveOpen: true);
        var translatedOffsets = new List<OffsetTranslation>();
        var seenOffsets = new HashSet<int>();
        foreach (Xbf2Instruction instruction in decoded.Instructions)
        {
            if (!seenOffsets.Add(instruction.Offset))
            {
                throw new InvalidDataException($"Multiple XBF2 instructions have offset {instruction.Offset}.");
            }

            uint newStart = checked((uint)nodeStream.Position);
            WriteInstruction(writer, instruction);
            translatedOffsets.Add(new OffsetTranslation(instruction.Offset, newStart, checked((uint)nodeStream.Position)));
        }

        byte[] nodeBytes = nodeStream.ToArray();
        byte[] lineBytes = EncodeLineRecords(decoded.LineRecords, translatedOffsets, decoded.NodeLength, nodeBytes.Length);
        return new Xbf2Substream
        {
            NodeBytes = nodeBytes,
            LineBytes = lineBytes,
        };
    }

    /// <summary>Encodes an instruction sequence without line records.</summary>
    /// <param name="instructions">The instructions to encode.</param>
    /// <returns>The encoded node bytes.</returns>
    public static byte[] EncodeInstructions(IEnumerable<Xbf2Instruction> instructions)
    {
        ArgumentNullException.ThrowIfNull(instructions);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Unicode, leaveOpen: true);
        foreach (Xbf2Instruction instruction in instructions)
        {
            WriteInstruction(writer, instruction);
        }

        return stream.ToArray();
    }

    /// <summary>Encodes line records for an instruction stream of the specified byte length.</summary>
    /// <param name="records">The line records to encode.</param>
    /// <param name="nodeLength">The encoded instruction-stream length.</param>
    /// <returns>The encoded line-record bytes.</returns>
    public static byte[] EncodeLineRecords(IEnumerable<Xbf2LineRecord> records, int nodeLength)
    {
        ArgumentNullException.ThrowIfNull(records);
        return EncodeLineRecords(records, translatedOffsets: null, oldNodeLength: nodeLength, nodeLength);
    }

    private static void WriteInstruction(BinaryWriter writer, Xbf2Instruction instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        writer.Write((byte)instruction.Opcode);
        switch (instruction.Opcode)
        {
            case Xbf2Opcode.PushScope:
            case Xbf2Opcode.PopScope:
            case Xbf2Opcode.AddToCollection:
            case Xbf2Opcode.AddToDictionary:
            case Xbf2Opcode.EndInitPopScope:
            case Xbf2Opcode.EndConditionalScope:
            case Xbf2Opcode.EndInitProvideValuePopScope:
                return;

            case Xbf2Opcode.AddNamespace:
            case Xbf2Opcode.PushScopeAddNamespace:
                WriteReference(writer, instruction.RequiredSecondaryReference, allowTrusted: false);
                writer.WriteXbfString(instruction.Text ?? throw Missing(instruction, "namespace prefix"));
                return;

            case Xbf2Opcode.PushConstant:
            case Xbf2Opcode.AddToDictionaryWithKey:
            case Xbf2Opcode.SetConnectionId:
            case Xbf2Opcode.SetName:
            case Xbf2Opcode.GetResourcePropertyBag:
            case Xbf2Opcode.ProvideStaticResourceValue:
            case Xbf2Opcode.ProvideThemeResourceValue:
                WriteConstant(writer, instruction.RequiredConstant);
                return;

            case Xbf2Opcode.CheckPeerType:
                writer.WriteXbfString(instruction.Text ?? throw Missing(instruction, "type name"));
                return;

            case Xbf2Opcode.SetValue:
            case Xbf2Opcode.PushScopeGetValue:
            case Xbf2Opcode.SetValueFromMarkupExtension:
                WriteReference(writer, instruction.RequiredPropertyReference);
                return;

            case Xbf2Opcode.PushScopeCreateTypeBeginInit:
            case Xbf2Opcode.CreateTypeBeginInit:
                WriteReference(writer, instruction.RequiredTypeReference);
                return;

            case Xbf2Opcode.PushScopeCreateTypeWithConstantBeginInit:
            case Xbf2Opcode.PushScopeCreateTypeWithTypeConvertedConstantBeginInit:
            case Xbf2Opcode.CreateTypeWithConstantBeginInit:
            case Xbf2Opcode.CreateTypeWithTypeConvertedConstantBeginInit:
                WriteReference(writer, instruction.RequiredTypeReference);
                WriteConstant(writer, instruction.RequiredConstant);
                return;

            case Xbf2Opcode.SetValueConstant:
            case Xbf2Opcode.SetValueTypeConvertedConstant:
            case Xbf2Opcode.SetValueFromStaticResource:
            case Xbf2Opcode.SetValueFromThemeResource:
                WriteReference(writer, instruction.RequiredPropertyReference);
                WriteConstant(writer, instruction.RequiredConstant);
                return;

            case Xbf2Opcode.SetValueTypeConvertedResolvedProperty:
            case Xbf2Opcode.SetValueFromTemplateBinding:
                WriteReference(writer, instruction.RequiredPropertyReference);
                WriteReference(writer, instruction.RequiredSecondaryReference);
                return;

            case Xbf2Opcode.SetValueTypeConvertedResolvedType:
                WriteReference(writer, instruction.RequiredPropertyReference);
                WriteReference(writer, instruction.RequiredTypeReference);
                return;

            case Xbf2Opcode.BeginConditionalScope:
                WritePredicate(writer, instruction.Predicate ?? throw Missing(instruction, "predicate"));
                return;

            case Xbf2Opcode.SetDeferredProperty:
                WriteReference(writer, instruction.RequiredPropertyReference);
                WriteSegment(writer, instruction.Segment ?? throw Missing(instruction, "segment"), hasRuntimeData: false);
                return;

            case Xbf2Opcode.SetCustomRuntimeData:
                WriteSegment(writer, instruction.Segment ?? throw Missing(instruction, "segment"), hasRuntimeData: true);
                return;

            default:
                throw new InvalidDataException($"Unsupported or transient XBF2 opcode 0x{(byte)instruction.Opcode:X2} cannot be encoded.");
        }
    }

    private static void WriteConstant(BinaryWriter writer, Xbf2Constant constant)
    {
        writer.Write((byte)constant.Type);
        switch (constant.Type)
        {
            case Xbf2ConstantType.BoolFalse:
                RequireData(constant, false);
                return;
            case Xbf2ConstantType.BoolTrue:
                RequireData(constant, true);
                return;
            case Xbf2ConstantType.Float:
                writer.Write(RequireData<float>(constant));
                return;
            case Xbf2ConstantType.Signed:
                writer.Write(RequireData<int>(constant));
                return;
            case Xbf2ConstantType.SharedString:
                WriteReference(writer, RequireData<Xbf2Reference>(constant), allowTrusted: false);
                return;
            case Xbf2ConstantType.Thickness:
                XbfThickness thickness = RequireData<XbfThickness>(constant);
                writer.Write(thickness.Left);
                writer.Write(thickness.Top);
                writer.Write(thickness.Right);
                writer.Write(thickness.Bottom);
                return;
            case Xbf2ConstantType.GridLength:
                XbfGridLength gridLength = RequireData<XbfGridLength>(constant);
                writer.Write(gridLength.UnitType);
                writer.Write(new byte[3]);
                writer.Write(gridLength.Value);
                return;
            case Xbf2ConstantType.Color:
                writer.Write(RequireData<uint>(constant));
                return;
            case Xbf2ConstantType.UniqueString:
                writer.WriteXbfString(RequireData<string>(constant));
                return;
            case Xbf2ConstantType.NullString:
                if (constant.Data is not null)
                {
                    throw new InvalidDataException("An XBF2 null-string constant has non-null data.");
                }

                return;
            case Xbf2ConstantType.Enum:
                Xbf2EnumValue enumValue = RequireData<Xbf2EnumValue>(constant);
                writer.Write(enumValue.StableTypeId);
                writer.Write(enumValue.Value);
                return;
            default:
                throw new InvalidDataException($"Unknown XBF2 constant type 0x{(byte)constant.Type:X2}.");
        }
    }

    private static void WriteSegment(BinaryWriter writer, Xbf2Segment segment, bool hasRuntimeData)
    {
        writer.WriteVarUInt32(segment.TargetSubstream);
        WriteCount(writer, segment.StaticResources.Count);
        WriteCount(writer, segment.ThemeResources.Count);
        foreach (Xbf2Reference reference in segment.StaticResources)
        {
            WriteReference(writer, reference, allowTrusted: false);
        }

        foreach (Xbf2Reference reference in segment.ThemeResources)
        {
            WriteReference(writer, reference, allowTrusted: false);
        }

        if (hasRuntimeData)
        {
            WriteRuntimeData(writer, segment.RuntimeData ?? throw new InvalidDataException("An XBF2 custom-runtime segment has no runtime data."));
        }
        else if (segment.RuntimeData is not null)
        {
            throw new InvalidDataException("An XBF2 deferred-property segment unexpectedly contains custom runtime data.");
        }
    }

    private static void WriteRuntimeData(BinaryWriter writer, Xbf2CustomRuntimeData runtimeData)
    {
        writer.WriteVarUInt32((uint)runtimeData.Type);
        switch (runtimeData)
        {
            case Xbf2DeferredRuntimeData deferred:
                WriteDeferred(writer, deferred);
                return;
            case Xbf2StyleRuntimeData style:
                WriteStyle(writer, style);
                return;
            case Xbf2ResourceDictionaryRuntimeData dictionary:
                WriteResourceDictionary(writer, dictionary);
                return;
            case Xbf2VisualStateRuntimeData visualStates:
                WriteVisualStates(writer, visualStates);
                return;
            default:
                throw new InvalidDataException($"Unsupported XBF2 custom runtime data model {runtimeData.GetType().Name}.");
        }
    }

    private static void WriteDeferred(BinaryWriter writer, Xbf2DeferredRuntimeData deferred)
    {
        RequireRuntimeType(deferred.Type, Xbf2CustomRuntimeDataType.DeferredElementV1, Xbf2CustomRuntimeDataType.DeferredElementV2, Xbf2CustomRuntimeDataType.DeferredElementV3);
        WriteReference(writer, deferred.Name, allowTrusted: false);
        if (deferred.Type != Xbf2CustomRuntimeDataType.DeferredElementV1)
        {
            WriteVector(writer, deferred.NonDeferredProperties, (valueWriter, value) =>
            {
                WriteReference(valueWriter, value.Property);
                WriteConstant(valueWriter, value.Value);
            });
            if (deferred.Type == Xbf2CustomRuntimeDataType.DeferredElementV3)
            {
                WriteRuntimeBool(writer, deferred.Realize);
            }
        }
    }

    private static void WriteStyle(BinaryWriter writer, Xbf2StyleRuntimeData style)
    {
        RequireRuntimeType(style.Type, Xbf2CustomRuntimeDataType.StyleV1, Xbf2CustomRuntimeDataType.StyleV2, Xbf2CustomRuntimeDataType.StyleV3);
        WriteVector(writer, style.Setters, (itemWriter, setter) => WriteStyleSetter(itemWriter, setter, style.Type));
        if (style.Type == Xbf2CustomRuntimeDataType.StyleV3)
        {
            WriteConditionalObjects(writer, style.ConditionalObjects);
        }
    }

    private static void WriteStyleSetter(BinaryWriter writer, Xbf2StyleSetter setter, Xbf2CustomRuntimeDataType type)
    {
        writer.WriteVarUInt32((uint)setter.Flags);
        if (type != Xbf2CustomRuntimeDataType.StyleV3 || !setter.Flags.HasFlag(Xbf2StyleSetterFlags.HasTokenForSelf))
        {
            if (setter.Flags.HasFlag(Xbf2StyleSetterFlags.IsPropertyResolved))
            {
                WriteReference(writer, setter.Property ?? throw new InvalidDataException("A resolved XBF2 style setter has no property."));
            }
            else
            {
                WriteReference(writer, setter.PropertyName ?? throw new InvalidDataException("An unresolved XBF2 style setter has no property name."), allowTrusted: false);
                WriteReference(writer, setter.DeclaringType ?? throw new InvalidDataException("An unresolved XBF2 style setter has no declaring type."));
            }
        }

        if (setter.Flags.HasFlag(Xbf2StyleSetterFlags.HasStringValue))
        {
            WriteReference(writer, setter.StringValue ?? throw new InvalidDataException("An XBF2 string style setter has no string value."), allowTrusted: false);
        }
        else if (setter.Flags.HasFlag(Xbf2StyleSetterFlags.HasContainerValue))
        {
            WriteConstant(writer, setter.ConstantValue ?? throw new InvalidDataException("An XBF2 container style setter has no constant value."));
        }
        else if ((setter.Flags & TokenStyleSetterFlags) != 0)
        {
            writer.WriteVarUInt32(setter.Token ?? throw new InvalidDataException("An XBF2 deferred style setter has no token."));
        }
    }

    private static void WriteResourceDictionary(BinaryWriter writer, Xbf2ResourceDictionaryRuntimeData dictionary)
    {
        RequireRuntimeType(dictionary.Type, Xbf2CustomRuntimeDataType.ResourceDictionaryV1, Xbf2CustomRuntimeDataType.ResourceDictionaryV2, Xbf2CustomRuntimeDataType.ResourceDictionaryV3, Xbf2CustomRuntimeDataType.ResourceDictionaryV4);
        if (dictionary.Type == Xbf2CustomRuntimeDataType.ResourceDictionaryV4)
        {
            var resources = dictionary.Resources.Concat(dictionary.ImplicitResources).ToArray();
            WriteResourceMapV4(writer, resources);
            WriteSharedStringVector(writer, dictionary.ResourcesWithNames);
            var conditional = dictionary.ConditionalResources.Concat(dictionary.ConditionalImplicitResources).ToArray();
            WriteResourceMapV4(writer, conditional, multipleTokens: true);
            WriteConditionalObjects(writer, dictionary.ConditionalObjects);
            return;
        }

        WriteResourceEntries(writer, dictionary.Resources);
        WriteSharedStringVector(writer, dictionary.ResourcesWithNames);
        WriteResourceEntries(writer, dictionary.ImplicitResources);
        if (dictionary.Type == Xbf2CustomRuntimeDataType.ResourceDictionaryV1)
        {
            WriteSharedStringVector(writer, dictionary.LegacyImplicitDataTemplateKeys);
        }

        if (dictionary.Type is Xbf2CustomRuntimeDataType.ResourceDictionaryV1 or Xbf2CustomRuntimeDataType.ResourceDictionaryV2)
        {
            WriteSharedStringVector(writer, dictionary.LegacyImplicitStyleKeys);
        }
        else
        {
            WriteResourceEntries(writer, dictionary.ConditionalResources, multipleTokens: true);
            WriteResourceEntries(writer, dictionary.ConditionalImplicitResources, multipleTokens: true);
            WriteConditionalObjects(writer, dictionary.ConditionalObjects);
        }
    }

    private static void WriteResourceEntries(BinaryWriter writer, IReadOnlyCollection<Xbf2ResourceEntry> entries, bool multipleTokens = false)
    {
        WriteVector(writer, entries, (entryWriter, entry) =>
        {
            WriteReference(entryWriter, entry.Key.String, allowTrusted: false);
            WriteTokens(entryWriter, entry.Tokens, multipleTokens);
        });
    }

    private static void WriteResourceMapV4(BinaryWriter writer, IReadOnlyCollection<Xbf2ResourceEntry> entries, bool multipleTokens = false)
    {
        WriteCount(writer, entries.Count);
        foreach (Xbf2ResourceEntry entry in entries)
        {
            WriteReference(writer, entry.Key.String, allowTrusted: false);
            writer.Write(entry.Key.HashAndIsKeyType ?? throw new InvalidDataException("An XBF2 resource dictionary v4 key has no hash/type field."));
            WriteTokens(writer, entry.Tokens, multipleTokens);
        }
    }

    private static void WriteTokens(BinaryWriter writer, IReadOnlyList<uint> tokens, bool multipleTokens)
    {
        if (multipleTokens)
        {
            WriteUIntVector(writer, tokens);
            return;
        }

        if (tokens.Count != 1)
        {
            throw new InvalidDataException("A non-conditional XBF2 resource entry must contain exactly one token.");
        }

        writer.WriteVarUInt32(tokens[0]);
    }

    private static void WriteVisualStates(BinaryWriter writer, Xbf2VisualStateRuntimeData visualStates)
    {
        RequireRuntimeType(visualStates.Type, Xbf2CustomRuntimeDataType.VisualStateGroupCollectionV1, Xbf2CustomRuntimeDataType.VisualStateGroupCollectionV2, Xbf2CustomRuntimeDataType.VisualStateGroupCollectionV3, Xbf2CustomRuntimeDataType.VisualStateGroupCollectionV4, Xbf2CustomRuntimeDataType.VisualStateGroupCollectionV5);
        WriteUIntVector(writer, visualStates.StateToGroupMap);
        WriteVector(writer, visualStates.States, (stateWriter, state) => WriteVisualState(stateWriter, state, visualStates.Type));
        WriteVector(writer, visualStates.Groups, (groupWriter, group) =>
        {
            WriteReference(groupWriter, group.Name, allowTrusted: false);
            WriteRuntimeBool(groupWriter, group.HasDynamicTimelines);
            groupWriter.WriteVarUInt32(group.DeferredSelfToken);
        });
        WriteVector(writer, visualStates.Transitions, (transitionWriter, transition) =>
        {
            WriteReference(transitionWriter, transition.ToState, allowTrusted: false);
            WriteReference(transitionWriter, transition.FromState, allowTrusted: false);
            transitionWriter.WriteVarUInt32(transition.DeferredSelfToken);
        });
        WriteRuntimeBool(writer, visualStates.UnexpectedTokens);
        WriteVector(writer, visualStates.TransitionLookup, (lookupWriter, entry) =>
        {
            lookupWriter.WriteVarUInt32(entry.FromIndex);
            lookupWriter.WriteVarUInt32(entry.ToIndex);
            lookupWriter.WriteVarUInt32(entry.TransitionIndex);
        });
        WriteUIntVector(writer, visualStates.DefaultTransitionsByGroup);
        writer.WriteVarUInt32(visualStates.EntireCollectionToken);
        if (visualStates.Type is Xbf2CustomRuntimeDataType.VisualStateGroupCollectionV4 or Xbf2CustomRuntimeDataType.VisualStateGroupCollectionV5)
        {
            WriteSharedStringVector(writer, visualStates.SeenNameDirectives);
        }
    }

    private static void WriteVisualState(BinaryWriter writer, Xbf2VisualState state, Xbf2CustomRuntimeDataType type)
    {
        WriteReference(writer, state.Name, allowTrusted: false);
        writer.WriteVarUInt32(state.StoryboardToken);
        WriteRuntimeBool(writer, state.HasStoryboard);
        if (type != Xbf2CustomRuntimeDataType.VisualStateGroupCollectionV1)
        {
            WriteUIntVector(writer, state.DeferredSetterTokens);
            WriteVector(writer, state.StateTriggerValues, WriteUIntVector);
            if (type != Xbf2CustomRuntimeDataType.VisualStateGroupCollectionV2)
            {
                WriteUIntVector(writer, state.ExtensibleTriggerTokens);
                WriteUIntVector(writer, state.TriggerCollectionTokens);
                if (type == Xbf2CustomRuntimeDataType.VisualStateGroupCollectionV5)
                {
                    WriteUIntVector(writer, state.StaticResourceTriggerTokens);
                }
            }
        }
    }

    private static void WriteConditionalObjects(BinaryWriter writer, IReadOnlyCollection<Xbf2ConditionalObject> objects)
    {
        WriteVector(writer, objects, (objectWriter, value) =>
        {
            objectWriter.WriteVarUInt32(value.Token);
            WriteVector(objectWriter, value.Predicates, WritePredicate);
        });
    }

    private static void WritePredicate(BinaryWriter writer, Xbf2Predicate predicate)
    {
        WriteReference(writer, predicate.Type);
        WriteReference(writer, predicate.Arguments, allowTrusted: false);
    }

    private static void WriteSharedStringVector(BinaryWriter writer, IReadOnlyCollection<Xbf2Reference> values)
    {
        WriteVector(writer, values, (valueWriter, value) => WriteReference(valueWriter, value, allowTrusted: false));
    }

    private static void WriteUIntVector(BinaryWriter writer, IReadOnlyCollection<uint> values)
    {
        WriteVector(writer, values, (valueWriter, value) => valueWriter.WriteVarUInt32(value));
    }

    private static void WriteVector<T>(BinaryWriter writer, IReadOnlyCollection<T> values, Action<BinaryWriter, T> writeValue)
    {
        WriteCount(writer, values.Count);
        foreach (T value in values)
        {
            writeValue(writer, value);
        }
    }

    private static void WriteCount(BinaryWriter writer, int count)
    {
        writer.WriteVarUInt32(checked((uint)count));
    }

    private static void WriteRuntimeBool(BinaryWriter writer, bool value)
    {
        writer.Write((byte)(value ? Xbf2ConstantType.BoolTrue : Xbf2ConstantType.BoolFalse));
    }

    private static void WriteReference(BinaryWriter writer, Xbf2Reference reference, bool allowTrusted = true)
    {
        if (!allowTrusted && reference.IsTrusted)
        {
            throw new InvalidDataException("An XBF2 string or XML namespace reference has the trusted bit set.");
        }

        writer.Write(reference.Encoded);
    }

    private static byte[] EncodeLineRecords(IEnumerable<Xbf2LineRecord> records, IReadOnlyList<OffsetTranslation>? translatedOffsets, int oldNodeLength, int nodeLength)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Unicode, leaveOpen: true);
        uint previousOffset = 0;
        int previousLine = 0;
        int previousColumn = 0;
        foreach (Xbf2LineRecord record in records)
        {
            uint offset = record.NodeOffset;
            if (translatedOffsets is not null)
            {
                offset = TranslateOffset(record.NodeOffset, translatedOffsets, oldNodeLength, nodeLength);
            }

            if (offset < previousOffset || offset > nodeLength)
            {
                throw new InvalidDataException($"An XBF2 line record offset {offset} is not monotonic or is past the node stream.");
            }

            writer.WriteVarUInt32(offset - previousOffset);
            writer.WriteVarUInt32(ZigZagEncode(checked(record.Line - previousLine)));
            writer.WriteVarUInt32(ZigZagEncode(checked(record.Column - previousColumn)));
            previousOffset = offset;
            previousLine = record.Line;
            previousColumn = record.Column;
        }

        return stream.ToArray();
    }

    private static uint TranslateOffset(uint oldOffset, IReadOnlyList<OffsetTranslation> translations, int oldNodeLength, int newNodeLength)
    {
        if (oldOffset > oldNodeLength)
        {
            throw new InvalidDataException($"An XBF2 line record offset {oldOffset} is past the old node stream length {oldNodeLength}.");
        }

        if (oldOffset == oldNodeLength)
        {
            return checked((uint)newNodeLength);
        }

        for (int index = translations.Count - 1; index >= 0; index--)
        {
            OffsetTranslation current = translations[index];
            if (oldOffset < current.OldStart)
            {
                continue;
            }

            int oldEnd = index + 1 < translations.Count ? translations[index + 1].OldStart : oldNodeLength;
            if (oldOffset > oldEnd)
            {
                break;
            }

            uint relativeOffset = oldOffset - checked((uint)current.OldStart);
            uint newLength = current.NewEnd - current.NewStart;
            return current.NewStart + Math.Min(relativeOffset, newLength);
        }

        throw new InvalidDataException($"An XBF2 line record at old node offset {oldOffset} is not inside the node stream.");
    }

    private static uint ZigZagEncode(int value)
    {
        return unchecked((uint)((value << 1) ^ (value >> 31)));
    }

    private static T RequireData<T>(Xbf2Constant constant)
    {
        if (constant.Data is not T value)
        {
            throw new InvalidDataException($"XBF2 constant {constant.Type} does not contain {typeof(T).Name} data.");
        }

        return value;
    }

    private static void RequireData(Xbf2Constant constant, bool expected)
    {
        if (constant.Data is not bool value || value != expected)
        {
            throw new InvalidDataException($"XBF2 constant {constant.Type} has an inconsistent boolean value.");
        }
    }

    private static void RequireRuntimeType(Xbf2CustomRuntimeDataType actual, params Xbf2CustomRuntimeDataType[] expected)
    {
        if (!expected.Contains(actual))
        {
            throw new InvalidDataException($"The custom runtime data model does not match type {actual}.");
        }
    }

    private static InvalidDataException Missing(Xbf2Instruction instruction, string field)
    {
        return new InvalidDataException($"XBF2 opcode {instruction.Opcode} has no {field}.");
    }

    private const Xbf2StyleSetterFlags TokenStyleSetterFlags =
        Xbf2StyleSetterFlags.HasStaticResourceValue | Xbf2StyleSetterFlags.HasThemeResourceValue | Xbf2StyleSetterFlags.HasObjectValue | Xbf2StyleSetterFlags.HasTokenForSelf;

    private readonly record struct OffsetTranslation(int OldStart, uint NewStart, uint NewEnd);
}
