using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XbfKit.Tests;

[TestClass]
public sealed class Xbf1CodecAndSemanticTests
{
    [TestMethod]
    public void EveryNodeRepresentationRoundTripsThroughTheDocumentCodec()
    {
        var data = new Xbf1NodeData();
        data.Nodes.AddRange(
        [
            new Xbf1Node { Type = Xbf1NodeType.None },
            new Xbf1Node { Type = Xbf1NodeType.StartObject, Reference = new Xbf1Reference(1, Xbf1NodeFlags.IsTrustedXbfIndex) },
            new Xbf1Node { Type = Xbf1NodeType.StartProperty, Reference = new Xbf1Reference(2, Xbf1NodeFlags.IsRetrieved) },
            new Xbf1Node { Type = Xbf1NodeType.Text, Reference = new Xbf1Reference(3, Xbf1NodeFlags.IsStringValueAndUnique) },
            new Xbf1Node { Type = Xbf1NodeType.Namespace, Reference = new Xbf1Reference(4, Xbf1NodeFlags.IsUnknown), NamespacePrefix = "local" },
            new Xbf1Node { Type = Xbf1NodeType.EndOfAttributes },
            new Xbf1Node { Type = Xbf1NodeType.StartConditionalScope },
            new Xbf1Node { Type = Xbf1NodeType.EndConditionalScope },
            new Xbf1Node { Type = Xbf1NodeType.LineInfo, LineDelta = -2, ColumnDelta = 9 },
            new Xbf1Node { Type = Xbf1NodeType.LineInfoAbsolute, AbsoluteLine = 120, AbsoluteColumn = 33 },
            new Xbf1Node { Type = Xbf1NodeType.EndProperty },
            new Xbf1Node { Type = Xbf1NodeType.EndObject },
            new Xbf1Node { Type = Xbf1NodeType.EndOfStream },
        ]);
        XbfDocument document = Document(data);
        byte[] first = TestUtilities.WriteDocument(document);
        XbfDocument decoded;
        using (var stream = new MemoryStream(first))
        {
            decoded = IO.XbfDocumentReader.Read(stream);
        }

        CollectionAssert.AreEqual(first, TestUtilities.WriteDocument(decoded));
        Assert.AreEqual(data.Nodes.Count, ((Xbf1NodeData)decoded.Nodes).Nodes.Count);
    }

    [TestMethod]
    public void EveryValueRepresentationRoundTripsThroughTheDocumentCodec()
    {
        Xbf1Value[] values =
        [
            new(Xbf1ValueType.None, null),
            Xbf1Value.False,
            Xbf1Value.True,
            new(Xbf1ValueType.Float, 1.25f),
            new(Xbf1ValueType.Signed, -42),
            new(Xbf1ValueType.String, "Text \U0001F600"),
            new(Xbf1ValueType.KeyTime, 1.5f),
            new(Xbf1ValueType.Thickness, new XbfThickness(1, 2, 3, 4)),
            new(Xbf1ValueType.LengthConverter, 25.5f),
            new(Xbf1ValueType.GridLength, new XbfGridLength(1, 2.5f)),
            new(Xbf1ValueType.Color, 0xFFAABBCCu),
            new(Xbf1ValueType.Duration, 2.75f),
        ];
        var data = new Xbf1NodeData();
        foreach (Xbf1Value value in values)
        {
            data.Nodes.Add(new Xbf1Node { Type = Xbf1NodeType.Value, Value = value });
        }

        byte[] first = TestUtilities.WriteDocument(Document(data));
        using var stream = new MemoryStream(first);
        XbfDocument decoded = IO.XbfDocumentReader.Read(stream);
        CollectionAssert.AreEqual(first, TestUtilities.WriteDocument(decoded));
    }

    [TestMethod]
    public void FrameworkValuesAndContentPropertiesRemainStable()
    {
        const string xaml = """
            <UserControl xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Grid>
                    <Button Grid.Row="1" HorizontalAlignment="Center" Margin="1,2,3,4" Content="Hello" />
                </Grid>
            </UserControl>
            """;
        string result = TestUtilities.AssertSemanticRoundTrip(xaml, TestUtilities.WuxOptions(XbfVersion.Version1));
        Assert.IsFalse(result.Contains("UserControl.Content", StringComparison.Ordinal));
        StringAssert.Contains(result, "Grid.Row=\"1\"");
        StringAssert.Contains(result, "HorizontalAlignment=\"Center\"");
    }

    [TestMethod]
    public void TimeValuesRetainTheirCanonicalText()
    {
        const string xaml = """
            <Storyboard xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Duration="00:00:02">
                <ObjectAnimationUsingKeyFrames>
                    <DiscreteObjectKeyFrame KeyTime="00:00:01" Value="Visible" />
                </ObjectAnimationUsingKeyFrames>
            </Storyboard>
            """;
        string result = TestUtilities.AssertSemanticRoundTrip(xaml, TestUtilities.WuxOptions(XbfVersion.Version1));
        StringAssert.Contains(result, "Duration=\"00:00:02\"");
        StringAssert.Contains(result, "KeyTime=\"00:00:01\"");
    }

    [TestMethod]
    public void ObjectFormResourceExtensionsPreserveTheirMeaning()
    {
        const string xaml = """
            <DiscreteObjectKeyFrame xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <DiscreteObjectKeyFrame.Value>
                    <ThemeResource ResourceKey="AccentBrush" />
                </DiscreteObjectKeyFrame.Value>
            </DiscreteObjectKeyFrame>
            """;
        string result = TestUtilities.AssertSemanticRoundTrip(xaml, TestUtilities.WuxOptions(XbfVersion.Version1));
        StringAssert.Contains(result, "Value=\"{ThemeResource AccentBrush}\"");

        XbfDocument recompiled = TestUtilities.Compile(result, TestUtilities.WuxOptions(XbfVersion.Version1));
        StringAssert.Contains(TestUtilities.Decompile(recompiled), "Value=\"{ThemeResource AccentBrush}\"");
    }

    [TestMethod]
    public void ConnectionIdentifiersFollowDecompilationOptions()
    {
        const string xaml = """
            <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" x:ConnectionId="9" />
            """;
        XbfDocument document = TestUtilities.Compile(xaml, TestUtilities.WuxOptions(XbfVersion.Version1));
        StringAssert.Contains(TestUtilities.Decompile(document), "x:ConnectionId=\"9\"");
        Assert.IsFalse(TestUtilities.Decompile(document, options: new XbfDecompilationOptions { IncludeConnectionIds = false }).Contains("x:ConnectionId", StringComparison.Ordinal));
    }

    [TestMethod]
    public void InvalidNodePayloadsAreRejected()
    {
        var missingReference = new Xbf1NodeData();
        missingReference.Nodes.Add(new Xbf1Node { Type = Xbf1NodeType.StartObject });
        TestUtilities.AssertThrows<InvalidDataException>(() => TestUtilities.WriteDocument(Document(missingReference)));

        var invalidValue = new Xbf1NodeData();
        invalidValue.Nodes.Add(new Xbf1Node { Type = Xbf1NodeType.Value, Value = new Xbf1Value(Xbf1ValueType.Float, "not a float") });
        TestUtilities.AssertThrows<InvalidDataException>(() => TestUtilities.WriteDocument(Document(invalidValue)));
    }

    private static XbfDocument Document(Xbf1NodeData data)
    {
        return new XbfDocument
        {
            Version = XbfVersion.Version1,
            Nodes = data,
        };
    }
}
