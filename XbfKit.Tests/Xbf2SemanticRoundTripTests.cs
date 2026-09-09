using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XbfKit.Tests;

[TestClass]
public sealed class Xbf2SemanticRoundTripTests
{
    [TestMethod]
    public void RepresentativeFrameworkTreesAreStable()
    {
        string[] documents =
        [
            """
            <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <Grid.RowDefinitions><RowDefinition Height="Auto" /><RowDefinition Height="*" /></Grid.RowDefinitions>
                <TextBlock Grid.Row="1" Text="Hello" />
            </Grid>
            """,
            """
            <Paragraph xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <Run Text="Before" /><Hyperlink NavigateUri="https://example.com"><Run Text="Link" /></Hyperlink><Run Text="After" />
            </Paragraph>
            """,
            """
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" x:Key="ButtonStyle" TargetType="Button">
                <Setter Property="Opacity" Value="0.75" />
                <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button"><Border Background="{TemplateBinding Background}" /></ControlTemplate></Setter.Value></Setter>
            </Style>
            """,
            """
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" x:Key="Template">
                <StackPanel><TextBlock Text="Title" /><Button Content="Action" /></StackPanel>
            </DataTemplate>
            """,
            """
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Color x:Key="AccentColor">#FF112233</Color>
                <SolidColorBrush x:Key="AccentBrush" Color="{StaticResource AccentColor}" />
            </ResourceDictionary>
            """,
        ];

        foreach (string document in documents)
        {
            _ = TestUtilities.AssertSemanticRoundTrip(document);
        }
    }

    [TestMethod]
    public void BothXbf2ContainerVersionsRoundTrip()
    {
        const string xaml = """
            <Button xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Content="Hello" />
            """;
        foreach (XbfVersion version in new[] { XbfVersion.Version2, XbfVersion.Version2_1 })
        {
            XbfCompilationOptions options = TestUtilities.WuxOptions(version);
            XbfDocument document = TestUtilities.SerializeAndRead(TestUtilities.Compile(xaml, options));
            Assert.AreEqual(version, document.Version);
            _ = TestUtilities.AssertSemanticRoundTrip(xaml, options);
        }
    }

    [TestMethod]
    public void WuxRuntimeDataSelectionsArePresentInCompiledDocuments()
    {
        const string dictionary = """
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"><SolidColorBrush x:Key="Brush" Color="Red" /></ResourceDictionary>
            """;
        const string style = """
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button"><Setter Property="Opacity" Value="0.5" /></Style>
            """;
        foreach ((Version Target, Xbf2CustomRuntimeDataType Dictionary, Xbf2CustomRuntimeDataType Style) value in new[]
        {
            (new Version(10, 0, 10240, 0), Xbf2CustomRuntimeDataType.ResourceDictionaryV1, Xbf2CustomRuntimeDataType.StyleV1),
            (new Version(10, 0, 14393, 0), Xbf2CustomRuntimeDataType.ResourceDictionaryV2, Xbf2CustomRuntimeDataType.StyleV2),
            (new Version(10, 0, 15063, 0), Xbf2CustomRuntimeDataType.ResourceDictionaryV3, Xbf2CustomRuntimeDataType.StyleV2),
        })
        {
            XbfCompilationOptions options = TestUtilities.WuxOptions(target: value.Target);
            Assert.AreEqual(value.Dictionary, TestUtilities.RuntimeData<Xbf2ResourceDictionaryRuntimeData>(TestUtilities.Compile(dictionary, options)).Single().Type);
            Assert.AreEqual(value.Style, TestUtilities.RuntimeData<Xbf2StyleRuntimeData>(TestUtilities.Compile(style, options)).Single().Type);
        }
    }

    [TestMethod]
    public void MuxDocumentsUseTheMuxSchemaAndRemainStable()
    {
        const string xaml = """
            <Page xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:muxc="using:Microsoft.UI.Xaml.Controls">
                <muxc:NavigationView><TextBlock Text="MUX" /></muxc:NavigationView>
            </Page>
            """;
        var options = new XbfCompilationOptions
        {
            Version = XbfVersion.Version2_1,
            Dialect = XbfDialect.MUX,
        };
        string result = TestUtilities.AssertSemanticRoundTrip(xaml, options, XbfDialect.MUX);
        StringAssert.Contains(result, "using:Microsoft.UI.Xaml.Controls");
        StringAssert.Contains(result, "NavigationView");
    }

    [TestMethod]
    public void ResourceDependenciesAreRecordedOnDeferredSegments()
    {
        const string xaml = """
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <SolidColorBrush x:Key="StaticBrush" Color="Red" />
                <Style x:Key="Style" TargetType="Button">
                    <Setter Property="Background" Value="{StaticResource StaticBrush}" />
                    <Setter Property="Foreground" Value="{ThemeResource SystemControlForegroundBaseHighBrush}" />
                </Style>
            </ResourceDictionary>
            """;
        XbfDocument document = TestUtilities.Compile(xaml);
        Xbf2NodeData nodes = (Xbf2NodeData)document.Nodes;
        IReadOnlyList<Xbf2Segment> segments = [.. nodes.Substreams.SelectMany(stream => stream.Decode().Instructions).Select(instruction => instruction.Segment).OfType<Xbf2Segment>()];
        Assert.IsTrue(segments.Any(segment => segment.StaticResources.Count > 0));
        Assert.IsTrue(segments.Any(segment => segment.ThemeResources.Count > 0));
    }
}
