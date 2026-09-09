using Microsoft.VisualStudio.TestTools.UnitTesting;
using XbfKit.IO;

namespace XbfKit.Tests;

[TestClass]
public sealed class Xbf2CustomRuntimeDataCodecTests
{
    [TestMethod]
    public void EveryDeferredElementVersionRoundTrips()
    {
        foreach (Xbf2CustomRuntimeDataType type in new[] { Xbf2CustomRuntimeDataType.DeferredElementV1, Xbf2CustomRuntimeDataType.DeferredElementV2, Xbf2CustomRuntimeDataType.DeferredElementV3 })
        {
            var data = new Xbf2DeferredRuntimeData
            {
                Type = type,
                Name = Local(1),
                Realize = type == Xbf2CustomRuntimeDataType.DeferredElementV3,
            };
            if (type != Xbf2CustomRuntimeDataType.DeferredElementV1)
            {
                data.NonDeferredProperties.Add((Trusted(2), new Xbf2Constant(Xbf2ConstantType.Signed, 42)));
            }

            Xbf2DeferredRuntimeData result = RoundTrip(data);
            Assert.AreEqual(type, result.Type);
            Assert.AreEqual(data.Name, result.Name);
            Assert.AreEqual(type == Xbf2CustomRuntimeDataType.DeferredElementV3, result.Realize);
            Assert.AreEqual(type == Xbf2CustomRuntimeDataType.DeferredElementV1 ? 0 : 1, result.NonDeferredProperties.Count);
        }
    }

    [TestMethod]
    public void EveryStyleVersionRoundTrips()
    {
        foreach (Xbf2CustomRuntimeDataType type in new[] { Xbf2CustomRuntimeDataType.StyleV1, Xbf2CustomRuntimeDataType.StyleV2, Xbf2CustomRuntimeDataType.StyleV3 })
        {
            var data = new Xbf2StyleRuntimeData { Type = type };
            data.Setters.Add(new Xbf2StyleSetter
            {
                Flags = Xbf2StyleSetterFlags.IsPropertyResolved | Xbf2StyleSetterFlags.HasStringValue,
                Property = Trusted(3),
                StringValue = Local(4),
            });
            data.Setters.Add(new Xbf2StyleSetter
            {
                Flags = Xbf2StyleSetterFlags.HasTokenForSelf,
                Token = 29,
            });
            if (type == Xbf2CustomRuntimeDataType.StyleV3)
            {
                data.ConditionalObjects.Add(new Xbf2ConditionalObject(29, [new Xbf2Predicate(Trusted(5), Local(6))]));
            }

            Xbf2StyleRuntimeData result = RoundTrip(data);
            Assert.AreEqual(type, result.Type);
            Assert.AreEqual(2, result.Setters.Count);
            Assert.AreEqual(type == Xbf2CustomRuntimeDataType.StyleV3 ? 1 : 0, result.ConditionalObjects.Count);
        }
    }

    [TestMethod]
    public void EveryResourceDictionaryVersionRoundTrips()
    {
        foreach (Xbf2CustomRuntimeDataType type in new[] { Xbf2CustomRuntimeDataType.ResourceDictionaryV1, Xbf2CustomRuntimeDataType.ResourceDictionaryV2, Xbf2CustomRuntimeDataType.ResourceDictionaryV3, Xbf2CustomRuntimeDataType.ResourceDictionaryV4 })
        {
            var data = new Xbf2ResourceDictionaryRuntimeData { Type = type };
            bool packed = type == Xbf2CustomRuntimeDataType.ResourceDictionaryV4;
            data.Resources.Add(new Xbf2ResourceEntry(new Xbf2ResourceKey(Local(10), packed ? 0x20UL : null), [17]));
            data.ImplicitResources.Add(new Xbf2ResourceEntry(new Xbf2ResourceKey(Local(11), packed ? 0x21UL : null), [18]));
            data.ResourcesWithNames.Add(Local(12));
            if (type == Xbf2CustomRuntimeDataType.ResourceDictionaryV1)
            {
                data.LegacyImplicitDataTemplateKeys.Add(Local(13));
            }

            if (type is Xbf2CustomRuntimeDataType.ResourceDictionaryV1 or Xbf2CustomRuntimeDataType.ResourceDictionaryV2)
            {
                data.LegacyImplicitStyleKeys.Add(Local(14));
            }
            else
            {
                ulong? conditionalHash = packed ? 0x30UL : null;
                data.ConditionalResources.Add(new Xbf2ResourceEntry(new Xbf2ResourceKey(Local(15), conditionalHash), [20, 21]));
                data.ConditionalObjects.Add(new Xbf2ConditionalObject(20, [new Xbf2Predicate(Trusted(7), Local(16))]));
            }

            Xbf2ResourceDictionaryRuntimeData result = RoundTrip(data);
            Assert.AreEqual(type, result.Type);
            Assert.AreEqual(1, result.Resources.Count);
            Assert.AreEqual(1, result.ImplicitResources.Count);
            Assert.AreEqual(1, result.ResourcesWithNames.Count);
            Assert.AreEqual(type is Xbf2CustomRuntimeDataType.ResourceDictionaryV3 or Xbf2CustomRuntimeDataType.ResourceDictionaryV4 ? 1 : 0, result.ConditionalResources.Count);
        }
    }

    [TestMethod]
    public void EveryVisualStateVersionRoundTrips()
    {
        foreach (Xbf2CustomRuntimeDataType type in new[] { Xbf2CustomRuntimeDataType.VisualStateGroupCollectionV1, Xbf2CustomRuntimeDataType.VisualStateGroupCollectionV2, Xbf2CustomRuntimeDataType.VisualStateGroupCollectionV3, Xbf2CustomRuntimeDataType.VisualStateGroupCollectionV4, Xbf2CustomRuntimeDataType.VisualStateGroupCollectionV5 })
        {
            var data = new Xbf2VisualStateRuntimeData
            {
                Type = type,
                UnexpectedTokens = false,
                EntireCollectionToken = 80,
            };
            data.StateToGroupMap.Add(0);
            var state = new Xbf2VisualState
            {
                Name = Local(20),
                StoryboardToken = 30,
                HasStoryboard = true,
            };
            if (type != Xbf2CustomRuntimeDataType.VisualStateGroupCollectionV1)
            {
                state.DeferredSetterTokens.Add(31);
                state.StateTriggerValues.Add([1, 640]);
                if (type != Xbf2CustomRuntimeDataType.VisualStateGroupCollectionV2)
                {
                    state.ExtensibleTriggerTokens.Add(32);
                    state.TriggerCollectionTokens.Add(33);
                    if (type == Xbf2CustomRuntimeDataType.VisualStateGroupCollectionV5)
                    {
                        state.StaticResourceTriggerTokens.Add(34);
                    }
                }
            }

            data.States.Add(state);
            data.Groups.Add(new Xbf2VisualStateGroup(Local(21), true, 40));
            data.Transitions.Add(new Xbf2VisualTransition(Local(22), Local(23), 41));
            data.TransitionLookup.Add(new Xbf2TransitionLookupEntry(0, 0, 0));
            data.DefaultTransitionsByGroup.Add(0);
            if (type is Xbf2CustomRuntimeDataType.VisualStateGroupCollectionV4 or Xbf2CustomRuntimeDataType.VisualStateGroupCollectionV5)
            {
                data.SeenNameDirectives.Add(Local(24));
            }

            Xbf2VisualStateRuntimeData result = RoundTrip(data);
            Assert.AreEqual(type, result.Type);
            Assert.AreEqual(1, result.States.Count);
            Assert.AreEqual(1, result.Groups.Count);
            Assert.AreEqual(1, result.Transitions.Count);
            Assert.AreEqual(80u, result.EntireCollectionToken);
            Assert.AreEqual(type is Xbf2CustomRuntimeDataType.VisualStateGroupCollectionV4 or Xbf2CustomRuntimeDataType.VisualStateGroupCollectionV5 ? 1 : 0, result.SeenNameDirectives.Count);
        }
    }

    [TestMethod]
    public void RuntimeDataModelMustMatchItsSerializedType()
    {
        var data = new Xbf2DeferredRuntimeData
        {
            Type = Xbf2CustomRuntimeDataType.StyleV1,
            Name = Local(1),
        };
        TestUtilities.AssertThrows<InvalidDataException>(() => Encode(data));
    }

    private static T RoundTrip<T>(T data) where T : Xbf2CustomRuntimeData
    {
        Xbf2Instruction instruction = new Xbf2Substream { NodeBytes = Encode(data), LineBytes = [] }.Decode().Instructions.Single();
        T result = (T)(instruction.Segment?.RuntimeData ?? throw new AssertFailedException("Runtime data was not decoded."));
        CollectionAssert.AreEqual(Encode(data), Encode(result));
        return result;
    }

    private static byte[] Encode(Xbf2CustomRuntimeData data)
    {
        var instruction = new Xbf2Instruction
        {
            Opcode = Xbf2Opcode.SetCustomRuntimeData,
            Offset = 0,
            Segment = new Xbf2Segment
            {
                TargetSubstream = 1,
                RuntimeData = data,
            },
        };
        instruction.Segment.StaticResources.Add(Local(30));
        instruction.Segment.ThemeResources.Add(Local(31));
        return Xbf2InstructionEncoder.EncodeInstructions([instruction]);
    }

    private static Xbf2Reference Local(ushort id) => new(id, false);

    private static Xbf2Reference Trusted(ushort id) => new(id, true);
}
