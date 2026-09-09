using Microsoft.VisualStudio.TestTools.UnitTesting;
using XbfKit.IO;

namespace XbfKit.Tests;

[TestClass]
public sealed class Xbf2InstructionCodecTests
{
    [TestMethod]
    public void EveryPersistedOpcodeRoundTripsThroughTheBinaryCodec()
    {
        IReadOnlyList<Xbf2Instruction> fixtures = CreatePersistedInstructionFixtures();
        Assert.AreEqual(36, fixtures.Count);
        CollectionAssert.AreEquivalent(
            Enum.GetValues<Xbf2Opcode>().Where(IsPersisted).Cast<object>().ToArray(),
            fixtures.Select(instruction => instruction.Opcode).Cast<object>().ToArray());

        foreach (Xbf2Instruction fixture in fixtures)
        {
            byte[] encoded = Xbf2InstructionEncoder.EncodeInstructions([fixture]);
            var substream = new Xbf2Substream { NodeBytes = encoded, LineBytes = [] };
            Xbf2Instruction decoded = AssertExactlyOne(substream.Decode().Instructions, fixture.Opcode.ToString());
            byte[] reencoded = Xbf2InstructionEncoder.EncodeInstructions([decoded]);
            CollectionAssert.AreEqual(encoded, reencoded, fixture.Opcode.ToString());
        }
    }

    [TestMethod]
    public void EveryConstantRepresentationRoundTrips()
    {
        Xbf2Constant[] constants =
        [
            Xbf2Constant.False,
            Xbf2Constant.True,
            new(Xbf2ConstantType.Float, -12.5f),
            new(Xbf2ConstantType.Signed, -123456),
            new(Xbf2ConstantType.SharedString, new Xbf2Reference(17, false)),
            new(Xbf2ConstantType.Thickness, new XbfThickness(1, 2, 3, 4)),
            new(Xbf2ConstantType.GridLength, new XbfGridLength(2, 3.5f)),
            new(Xbf2ConstantType.Color, 0xFFA1B2C3u),
            new(Xbf2ConstantType.UniqueString, "unique \U0001F600"),
            Xbf2Constant.NullString,
            new(Xbf2ConstantType.Enum, new Xbf2EnumValue(42, 0xDEADBEEFu)),
        ];

        foreach (Xbf2Constant constant in constants)
        {
            var instruction = New(Xbf2Opcode.PushConstant, constant: constant);
            byte[] encoded = Xbf2InstructionEncoder.EncodeInstructions([instruction]);
            Xbf2Instruction decoded = AssertExactlyOne(new Xbf2Substream { NodeBytes = encoded, LineBytes = [] }.Decode().Instructions, constant.Type.ToString());
            CollectionAssert.AreEqual(encoded, Xbf2InstructionEncoder.EncodeInstructions([decoded]), constant.Type.ToString());
        }
    }

    [TestMethod]
    public void LineRecordsFollowInstructionsWhenEncodedLengthsChange()
    {
        var decoded = new Xbf2DecodedSubstream { NodeLength = 4 };
        decoded.Instructions.Add(New(Xbf2Opcode.PushScope, offset: 0));
        decoded.Instructions.Add(New(Xbf2Opcode.SetName, offset: 1, constant: new Xbf2Constant(Xbf2ConstantType.UniqueString, "a longer name")));
        decoded.Instructions.Add(New(Xbf2Opcode.PopScope, offset: 3));
        decoded.LineRecords.Add(new Xbf2LineRecord(0, 10, 2));
        decoded.LineRecords.Add(new Xbf2LineRecord(1, 11, 4));
        decoded.LineRecords.Add(new Xbf2LineRecord(3, 12, 1));

        Xbf2DecodedSubstream result = Xbf2InstructionEncoder.Encode(decoded).Decode();
        Assert.AreEqual(3, result.LineRecords.Count);
        Assert.AreEqual(10, result.LineRecords[0].Line);
        Assert.AreEqual(11, result.LineRecords[1].Line);
        Assert.AreEqual(12, result.LineRecords[2].Line);
        Assert.AreEqual((uint)result.Instructions[1].Offset, result.LineRecords[1].NodeOffset);
        Assert.AreEqual((uint)result.Instructions[2].Offset, result.LineRecords[2].NodeOffset);
    }

    [TestMethod]
    public void NonPersistedOpcodesAreRejectedByTheBinaryCodec()
    {
        Xbf2Opcode[] nonPersisted = [Xbf2Opcode.None, Xbf2Opcode.PushResolvedType, Xbf2Opcode.PushResolvedProperty, Xbf2Opcode.SetResourceDictionaryItems, Xbf2Opcode.EndOfStream, .. Enum.GetValues<Xbf2Opcode>().Where(opcode => (byte)opcode >= 128)];
        foreach (Xbf2Opcode opcode in nonPersisted.Distinct())
        {
            TestUtilities.AssertThrows<InvalidDataException>(() => Xbf2InstructionEncoder.EncodeInstructions([New(opcode)]));
            TestUtilities.AssertThrows<InvalidDataException>(() => new Xbf2Substream { NodeBytes = [(byte)opcode], LineBytes = [] }.Decode());
        }
    }

    [TestMethod]
    public void MissingOpcodeOperandsAreRejected()
    {
        TestUtilities.AssertThrows<InvalidDataException>(() => Xbf2InstructionEncoder.EncodeInstructions([New(Xbf2Opcode.SetValue)]));
        TestUtilities.AssertThrows<InvalidDataException>(() => Xbf2InstructionEncoder.EncodeInstructions([New(Xbf2Opcode.PushConstant)]));
        TestUtilities.AssertThrows<InvalidDataException>(() => Xbf2InstructionEncoder.EncodeInstructions([New(Xbf2Opcode.SetDeferredProperty, property: new Xbf2Reference(1, true))]));
    }

    [TestMethod]
    public void TruncatedInstructionIsRejected()
    {
        byte[] bytes = Xbf2InstructionEncoder.EncodeInstructions([New(Xbf2Opcode.SetName, constant: new Xbf2Constant(Xbf2ConstantType.UniqueString, "value"))]);
        Array.Resize(ref bytes, bytes.Length - 1);
        TestUtilities.AssertThrows<EndOfStreamException>(() => new Xbf2Substream { NodeBytes = bytes, LineBytes = [] }.Decode());
    }

    private static IReadOnlyList<Xbf2Instruction> CreatePersistedInstructionFixtures()
    {
        var local = new Xbf2Reference(3, false);
        var trusted = new Xbf2Reference(5, true);
        var text = new Xbf2Constant(Xbf2ConstantType.UniqueString, "value");
        var deferredSegment = new Xbf2Segment { TargetSubstream = 2 };
        deferredSegment.StaticResources.Add(new Xbf2Reference(7, false));
        deferredSegment.ThemeResources.Add(new Xbf2Reference(8, false));
        var runtimeSegment = new Xbf2Segment
        {
            TargetSubstream = 1,
            RuntimeData = new Xbf2DeferredRuntimeData
            {
                Type = Xbf2CustomRuntimeDataType.DeferredElementV3,
                Name = local,
                Realize = false,
            },
        };

        return
        [
            New(Xbf2Opcode.PushScope),
            New(Xbf2Opcode.PopScope),
            New(Xbf2Opcode.AddNamespace, secondary: local, inlineText: "p"),
            New(Xbf2Opcode.PushConstant, constant: text),
            New(Xbf2Opcode.SetValue, property: trusted),
            New(Xbf2Opcode.AddToCollection),
            New(Xbf2Opcode.AddToDictionary),
            New(Xbf2Opcode.AddToDictionaryWithKey, constant: text),
            New(Xbf2Opcode.CheckPeerType, inlineText: "Contoso.Page"),
            New(Xbf2Opcode.SetConnectionId, constant: new Xbf2Constant(Xbf2ConstantType.Signed, 12)),
            New(Xbf2Opcode.SetName, constant: text),
            New(Xbf2Opcode.GetResourcePropertyBag, constant: text),
            New(Xbf2Opcode.SetCustomRuntimeData, segment: runtimeSegment),
            New(Xbf2Opcode.SetDeferredProperty, property: trusted, segment: deferredSegment),
            New(Xbf2Opcode.PushScopeAddNamespace, secondary: local, inlineText: "q"),
            New(Xbf2Opcode.PushScopeGetValue, property: trusted),
            New(Xbf2Opcode.PushScopeCreateTypeBeginInit, type: trusted),
            New(Xbf2Opcode.PushScopeCreateTypeWithConstantBeginInit, type: trusted, constant: text),
            New(Xbf2Opcode.PushScopeCreateTypeWithTypeConvertedConstantBeginInit, type: trusted, constant: text),
            New(Xbf2Opcode.CreateTypeBeginInit, type: trusted),
            New(Xbf2Opcode.CreateTypeWithConstantBeginInit, type: trusted, constant: text),
            New(Xbf2Opcode.CreateTypeWithTypeConvertedConstantBeginInit, type: trusted, constant: text),
            New(Xbf2Opcode.SetValueConstant, property: trusted, constant: text),
            New(Xbf2Opcode.SetValueTypeConvertedConstant, property: trusted, constant: text),
            New(Xbf2Opcode.SetValueTypeConvertedResolvedProperty, property: trusted, secondary: trusted),
            New(Xbf2Opcode.SetValueTypeConvertedResolvedType, type: trusted, property: trusted),
            New(Xbf2Opcode.SetValueFromStaticResource, property: trusted, constant: text),
            New(Xbf2Opcode.SetValueFromTemplateBinding, property: trusted, secondary: trusted),
            New(Xbf2Opcode.SetValueFromMarkupExtension, property: trusted),
            New(Xbf2Opcode.EndInitPopScope),
            New(Xbf2Opcode.ProvideStaticResourceValue, constant: text),
            New(Xbf2Opcode.ProvideThemeResourceValue, constant: text),
            New(Xbf2Opcode.SetValueFromThemeResource, property: trusted, constant: text),
            New(Xbf2Opcode.BeginConditionalScope, predicate: new Xbf2Predicate(trusted, local)),
            New(Xbf2Opcode.EndConditionalScope),
            New(Xbf2Opcode.EndInitProvideValuePopScope),
        ];
    }

    private static bool IsPersisted(Xbf2Opcode opcode)
    {
        byte value = (byte)opcode;
        return value is >= 1 and <= 40 && value is not 5 and not 6 and not 16 and not 37;
    }

    private static Xbf2Instruction New(Xbf2Opcode opcode, int offset = 0, Xbf2Reference? type = null, Xbf2Reference? property = null, Xbf2Reference? secondary = null, Xbf2Constant? constant = null, string? inlineText = null, Xbf2Segment? segment = null, Xbf2Predicate? predicate = null)
    {
        return new Xbf2Instruction
        {
            Opcode = opcode,
            Offset = offset,
            TypeReference = type,
            PropertyReference = property,
            SecondaryReference = secondary,
            Constant = constant,
            Text = inlineText,
            Segment = segment,
            Predicate = predicate,
        };
    }

    private static T AssertExactlyOne<T>(IReadOnlyList<T> values, string message)
    {
        Assert.AreEqual(1, values.Count, message);
        return values[0];
    }
}
