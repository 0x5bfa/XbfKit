using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XbfKit.Tests;

[TestClass]
public sealed class Xbf2CompilerSemanticTests
{
    [TestMethod]
    [DataRow("10.0.10240.0")]
    [DataRow("10.0.14393.0")]
    public void LegacyStyleSetterTokensIncludeTheirPropertyMetadata(string target)
    {
        string[] documents =
        [
            """
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button">
                <Setter Property="Opacity" Value="0.5" />
            </Style>
            """,
            """
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="{x:Type Button}">
                <Setter Property="Opacity" Value="0.5" />
            </Style>
            """,
            """
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button">
                <Setter Property="Grid.Row" Value="1" />
            </Style>
            """,
            """
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:local="using:Contoso" TargetType="local:Widget">
                <Setter Property="CustomValue" Value="42" />
            </Style>
            """,
        ];
        string[] propertyNames = ["Opacity", "Opacity", "Row", "CustomValue"];
        XbfCompilationOptions options = TestUtilities.WuxOptions(target: Version.Parse(target));
        for (int index = 0; index < documents.Length; index++)
        {
            XbfDocument document = TestUtilities.Compile(documents[index], options);
            Xbf2StyleSetter setter = TestUtilities.RuntimeData<Xbf2StyleRuntimeData>(document).Single().Setters.Single();
            Assert.IsTrue(setter.Flags.HasFlag(Xbf2StyleSetterFlags.HasTokenForSelf));
            Assert.IsNotNull(setter.PropertyName);
            Assert.IsNotNull(setter.DeclaringType);
            Assert.AreEqual(propertyNames[index], document.Metadata.Strings[setter.PropertyName.Value.ObjectId]);
            _ = TestUtilities.AssertSemanticRoundTrip(documents[index], options);
        }
    }

    [TestMethod]
    public void DictionaryValuedMembersPreserveExplicitKeys()
    {
        string[] documents =
        [
            """
            <local:PageBase xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:local="using:Contoso">
                <local:PageBase.Resources>
                    <Style x:Key="ProgressStyle" TargetType="ProgressBar" />
                </local:PageBase.Resources>
            </local:PageBase>
            """,
            """
            <Application xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <x:String x:Key="ApplicationName">MrmTool</x:String>
            </Application>
            """,
            """
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <ResourceDictionary.ThemeDictionaries>
                    <ResourceDictionary x:Key="Dark">
                        <SolidColorBrush x:Key="ForegroundBrush" Color="White" />
                    </ResourceDictionary>
                </ResourceDictionary.ThemeDictionaries>
            </ResourceDictionary>
            """,
        ];

        string[] keys = ["ProgressStyle", "ApplicationName", "Dark"];
        for (int index = 0; index < documents.Length; index++)
        {
            string result = TestUtilities.AssertSemanticRoundTrip(documents[index]);
            StringAssert.Contains(result, $"x:Key=\"{keys[index]}\"");
        }
    }

    [TestMethod]
    public void ResourceDictionariesPreserveExplicitAndImplicitKeys()
    {
        const string xaml = """
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <SolidColorBrush x:Key="AccentBrush" Color="#FF123456" />
                <Style TargetType="Button">
                    <Setter Property="Opacity" Value="0.5" />
                </Style>
                <DataTemplate x:Key="ItemTemplate">
                    <TextBlock Text="Item" />
                </DataTemplate>
            </ResourceDictionary>
            """;
        string result = TestUtilities.AssertSemanticRoundTrip(xaml);
        StringAssert.Contains(result, "x:Key=\"AccentBrush\"");
        StringAssert.Contains(result, "TargetType=\"Button\"");
        StringAssert.Contains(result, "x:Key=\"ItemTemplate\"");
    }

    [TestMethod]
    public void ContentAndAttachedPropertiesRetainTheirXamlShape()
    {
        const string xaml = """
            <UserControl xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <Grid>
                    <TextBlock Grid.Row="1" Text="Content" />
                </Grid>
            </UserControl>
            """;
        string result = TestUtilities.AssertSemanticRoundTrip(xaml);
        Assert.IsFalse(result.Contains("UserControl.Content", StringComparison.Ordinal));
        StringAssert.Contains(result, "Grid.Row=\"1\"");
        StringAssert.Contains(result, "<Grid>");
    }

    [TestMethod]
    public void InitializationTextPreservesCharactersThatAreNotXmlWhitespace()
    {
        const string xaml = """
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <x:String x:Key="Nbsp">&#xA0;</x:String>
                <x:String x:Key="Emoji">&#x1F600;</x:String>
                <x:String x:Key="PrivateUse">&#xE725;</x:String>
            </ResourceDictionary>
            """;
        string result = TestUtilities.AssertSemanticRoundTrip(xaml);
        Assert.IsFalse(result.Contains("<x:String x:Key=\"Nbsp\" />", StringComparison.Ordinal));
        Assert.IsFalse(result.Contains("<x:String x:Key=\"Emoji\" />", StringComparison.Ordinal));
        Assert.IsFalse(result.Contains("<x:String x:Key=\"PrivateUse\" />", StringComparison.Ordinal));
    }

    [TestMethod]
    public void XmlWhitespaceAroundInitializationTextIsNormalized()
    {
        const string xaml = """
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <x:String x:Key="Value">
                    text
                </x:String>
            </ResourceDictionary>
            """;
        string result = TestUtilities.AssertSemanticRoundTrip(xaml);
        StringAssert.Contains(result, ">text</x:String>");
    }

    [TestMethod]
    public void BuiltInMarkupExtensionsRetainTheirSemanticForm()
    {
        const string xaml = """
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
                <Border
                    Background="{ThemeResource AccentBrush}"
                    BorderBrush="{StaticResource BorderBrush}"
                    Tag="{x:Null}"
                    Width="{TemplateBinding Width}" />
            </ControlTemplate>
            """;
        string result = TestUtilities.AssertSemanticRoundTrip(xaml);
        StringAssert.Contains(result, "{ThemeResource AccentBrush}");
        StringAssert.Contains(result, "{StaticResource BorderBrush}");
        StringAssert.Contains(result, "{x:Null}");
        StringAssert.Contains(result, "{TemplateBinding Width}");
    }

    [TestMethod]
    public void TypeAndCustomMarkupExtensionsRoundTrip()
    {
        const string xaml = """
            <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:local="using:Contoso">
                <Grid.Resources>
                    <local:Holder x:Key="Holder" Value="{x:Type Button}" Other="{local:Extension Value={StaticResource Key}}" />
                </Grid.Resources>
            </Grid>
            """;
        _ = TestUtilities.AssertSemanticRoundTrip(xaml);
    }

    [TestMethod]
    public void BindMarkupRequiresGeneratedCode()
    {
        const string xaml = """
            <TextBlock xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Text="{x:Bind Name}" />
            """;
        InvalidDataException exception = TestUtilities.AssertThrows<InvalidDataException>(() => TestUtilities.Compile(xaml));
        StringAssert.Contains(exception.Message, "generated binding code");
    }

    [TestMethod]
    public void ConditionalNamespacesRoundTripOnSupportingTargets()
    {
        const string xaml = """
            <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                  xmlns:p="http://schemas.microsoft.com/winfx/2006/xaml/presentation?IsApiContractPresent(Windows.Foundation.UniversalApiContract,5)"
                  xmlns:q="http://schemas.microsoft.com/winfx/2006/xaml/presentation?IsApiContractNotPresent(Windows.Foundation.UniversalApiContract,7)">
                <p:Grid>
                    <q:Button Content="Conditional" />
                </p:Grid>
            </Grid>
            """;
        XbfCompilationOptions options = TestUtilities.WuxOptions(target: new Version(10, 0, 15063, 0));
        string result = TestUtilities.AssertSemanticRoundTrip(xaml, options);
        StringAssert.Contains(result, "IsApiContractPresent");
        StringAssert.Contains(result, "IsApiContractNotPresent");
    }

    [TestMethod]
    public void ConditionalNamespacesAreRejectedByUnsupportedTargets()
    {
        const string xaml = """
            <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                  xmlns:p="http://schemas.microsoft.com/winfx/2006/xaml/presentation?IsApiContractPresent(Windows.Foundation.UniversalApiContract,5)">
                <p:Button />
            </Grid>
            """;
        XbfCompilationOptions options = TestUtilities.WuxOptions(target: new Version(10, 0, 14393, 0));
        TestUtilities.AssertThrows<InvalidDataException>(() => TestUtilities.Compile(xaml, options));
    }

    [TestMethod]
    public void UnknownTypesAndPropertiesUseDynamicMetadata()
    {
        const string xaml = """
            <local:Widget xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:local="using:Contoso" CustomValue="42">
                <local:Widget.Items>
                    <Button Content="One" />
                    <Button Content="Two" />
                </local:Widget.Items>
            </local:Widget>
            """;
        XbfDocument document = TestUtilities.Compile(xaml);
        Assert.IsTrue(document.Metadata.Types.Any(type => type.Flags.HasFlag(XbfTypeFlags.IsUnknown)));
        Assert.IsTrue(document.Metadata.Properties.Any(property => property.Flags.HasFlag(XbfPropertyFlags.IsUnknown)));
        _ = TestUtilities.AssertSemanticRoundTrip(xaml);
    }
}
