using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XbfKit.Tests;

[TestClass]
public sealed class Xbf2VisualStateTests
{
    private const string VisualStateDocument = """
        <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
            <VisualStateManager.VisualStateGroups>
                <VisualStateGroup x:Name="CommonStates">
                    <VisualStateGroup.Transitions>
                        <VisualTransition From="Normal" To="PointerOver" GeneratedDuration="0:0:0.2">
                            <Storyboard>
                                <DoubleAnimation Storyboard.TargetName="Target" Storyboard.TargetProperty="Opacity" To="0.5" />
                            </Storyboard>
                        </VisualTransition>
                    </VisualStateGroup.Transitions>
                    <VisualState x:Name="Normal" />
                    <VisualState x:Name="PointerOver">
                        <VisualState.StateTriggers>
                            <AdaptiveTrigger MinWindowWidth="640" />
                        </VisualState.StateTriggers>
                        <VisualState.Setters>
                            <Setter Target="Target.Opacity" Value="0.5" />
                        </VisualState.Setters>
                        <Storyboard>
                            <DoubleAnimation Storyboard.TargetName="Target" Storyboard.TargetProperty="Opacity" To="0.5" />
                        </Storyboard>
                    </VisualState>
                </VisualStateGroup>
            </VisualStateManager.VisualStateGroups>
            <Border x:Name="Target" />
        </Grid>
        """;

    [TestMethod]
    public void CompleteCollectionsUseTheSupportedWuxFallbackContract()
    {
        XbfDocument document = TestUtilities.Compile(VisualStateDocument);
        Xbf2NodeData nodeData = (Xbf2NodeData)document.Nodes;
        Xbf2Instruction instruction = nodeData.Substreams
            .SelectMany(stream => stream.Decode().Instructions)
            .Single(value => value.Segment?.RuntimeData is Xbf2VisualStateRuntimeData);
        Xbf2VisualStateRuntimeData data = (Xbf2VisualStateRuntimeData)instruction.Segment!.RuntimeData!;
        Assert.IsTrue(data.UnexpectedTokens);
        Assert.AreEqual(0, data.States.Count);
        Assert.AreEqual(0, data.Groups.Count);
        Assert.AreEqual(0, data.Transitions.Count);
        Xbf2DecodedSubstream deferred = nodeData.Substreams[checked((int)instruction.Segment.TargetSubstream)].Decode();
        Assert.IsTrue(deferred.Instructions.Any(value => value.Offset == data.EntireCollectionToken));
        Assert.AreEqual(Xbf2Opcode.PushScopeGetValue, deferred.Instructions.Single(value => value.Offset == data.EntireCollectionToken).Opcode);
    }

    [TestMethod]
    public void StatesSettersStoryboardsTriggersAndTransitionsRoundTripTogether()
    {
        string result = TestUtilities.AssertSemanticRoundTrip(VisualStateDocument);
        StringAssert.Contains(result, "x:Name=\"CommonStates\"");
        StringAssert.Contains(result, "x:Name=\"PointerOver\"");
        StringAssert.Contains(result, "<VisualTransition");
        StringAssert.Contains(result, "<AdaptiveTrigger");
        StringAssert.Contains(result, "<VisualState.Setters>");
        StringAssert.Contains(result, "<Storyboard>");
    }

    [TestMethod]
    public void MultipleGroupsRetainOrderAndNames()
    {
        const string xaml = """
            <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <VisualStateManager.VisualStateGroups>
                    <VisualStateGroup x:Name="First"><VisualState x:Name="One" /></VisualStateGroup>
                    <VisualStateGroup x:Name="Second"><VisualState x:Name="Two" /></VisualStateGroup>
                </VisualStateManager.VisualStateGroups>
            </Grid>
            """;
        string result = TestUtilities.AssertSemanticRoundTrip(xaml);
        int first = result.IndexOf("x:Name=\"First\"", StringComparison.Ordinal);
        int second = result.IndexOf("x:Name=\"Second\"", StringComparison.Ordinal);
        Assert.IsTrue(first >= 0);
        Assert.IsTrue(second > first);
    }

    [TestMethod]
    public void ConditionalStateContentUsesTheFullCollectionPath()
    {
        const string xaml = """
            <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                  xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                  xmlns:p="http://schemas.microsoft.com/winfx/2006/xaml/presentation?IsApiContractPresent(Windows.Foundation.UniversalApiContract,5)">
                <VisualStateManager.VisualStateGroups>
                    <VisualStateGroup x:Name="CommonStates">
                        <p:VisualState x:Name="Conditional" />
                    </VisualStateGroup>
                </VisualStateManager.VisualStateGroups>
            </Grid>
            """;
        XbfCompilationOptions options = TestUtilities.WuxOptions(target: new Version(10, 0, 15063, 0));
        XbfDocument document = TestUtilities.Compile(xaml, options);
        Assert.IsTrue(TestUtilities.RuntimeData<Xbf2VisualStateRuntimeData>(document).Single().UnexpectedTokens);
        string result = TestUtilities.AssertSemanticRoundTrip(xaml, options);
        StringAssert.Contains(result, "IsApiContractPresent");
        StringAssert.Contains(result, "x:Name=\"Conditional\"");
    }
}
