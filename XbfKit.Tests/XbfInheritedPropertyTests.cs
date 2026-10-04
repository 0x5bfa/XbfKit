using Microsoft.VisualStudio.TestTools.UnitTesting;
using XbfKit.Xaml;

namespace XbfKit.Tests;

[TestClass]
public sealed class XbfInheritedPropertyTests
{
    private const string AnimationNamespace = "using:CommunityToolkit.WinUI.Animations";

    [TestMethod]
    [DataRow(XbfDialect.WUX, 1, 0)]
    [DataRow(XbfDialect.WUX, 2, 0)]
    [DataRow(XbfDialect.WUX, 2, 1)]
    [DataRow(XbfDialect.MUX, 2, 0)]
    [DataRow(XbfDialect.MUX, 2, 1)]
    public void InheritedAnimationMembersUseInstanceSyntax(XbfDialect dialect, int major, int minor)
    {
        const string xaml = """
            <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:animations="using:CommunityToolkit.WinUI.Animations">
                <animations:Implicit.ShowAnimations>
                    <animations:OpacityAnimation Delay="0:0:0.1" Duration="0:0:0.3" EasingType="Cubic" EasingMode="EaseOut" Repeat="1" DelayBehavior="SetInitialValueBeforeDelay" From="0" To="1">
                        <animations:OpacityAnimation.KeyFrames>
                            <animations:ScalarKeyFrame Value="0.25" />
                            <animations:ScalarKeyFrame Value="0.75" />
                        </animations:OpacityAnimation.KeyFrames>
                    </animations:OpacityAnimation>
                    <animations:ScaleAnimation Duration="0:0:0.3" From="0.5" To="1" />
                    <animations:TranslationAnimation Duration="0:0:0.3" From="0" To="1" />
                    <animations:OffsetAnimation Duration="0:0:0.3" From="0" To="1" />
                </animations:Implicit.ShowAnimations>
                <animations:Implicit.HideAnimations>
                    <animations:OpacityAnimation Duration="0:0:0.2" From="1" To="0" />
                    <animations:ScaleAnimation Duration="0:0:0.2" From="1" To="0.5" />
                </animations:Implicit.HideAnimations>
            </Grid>
            """;
        var options = new XbfCompilationOptions { Dialect = dialect, Version = new XbfVersion((uint)major, (uint)minor) };
        XbfDocument document = TestUtilities.Compile(xaml, options);
        string expected = TestUtilities.Decompile(document, dialect);
        UseNativeAnimationDeclaringTypes(document.Metadata);

        string result = TestUtilities.Decompile(TestUtilities.SerializeAndRead(document), dialect);
        Assert.AreEqual(expected, result);
        StringAssert.Contains(result, "<animations:Implicit.ShowAnimations>");
        StringAssert.Contains(result, "<animations:Implicit.HideAnimations>");
        StringAssert.Contains(result, "<animations:OpacityAnimation.KeyFrames>");
        Assert.IsFalse(result.Contains("Animation_x0060_", StringComparison.Ordinal));
        Assert.IsFalse(result.Contains("Animation.Duration=", StringComparison.Ordinal));
        _ = TestUtilities.AssertSemanticRoundTrip(result, options, dialect);
    }

    [TestMethod]
    public void UnknownSameNamespaceAttachedPropertiesKeepTheirDeclaringTypes()
    {
        const string xaml = """
            <animations:OpacityAnimation xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:animations="using:CommunityToolkit.WinUI.Animations" xmlns:local="using:Contoso"
                Duration="0:0:0.3" From="0" To="1"
                animations:Implicit.Animations="{StaticResource SharedAnimation}"
                animations:Animation.CustomAttached="42" local:Extensions.From="other" />
            """;
        XbfDocument document = TestUtilities.Compile(xaml);
        string expected = TestUtilities.Decompile(document);
        UseNativeAnimationDeclaringTypes(document.Metadata);
        string result = TestUtilities.Decompile(TestUtilities.SerializeAndRead(document));
        Assert.AreEqual(expected, result);
        StringAssert.Contains(result, "Implicit.Animations=");
        StringAssert.Contains(result, "Animation.CustomAttached=\"42\"");
        StringAssert.Contains(result, "local:Extensions.From=\"other\"");
    }

    [TestMethod]
    [DataRow("using:Contoso", "OpacityAnimation", "Animation", "Duration")]
    [DataRow(AnimationNamespace, "Widget", "Animation", "Duration")]
    [DataRow(AnimationNamespace, "CustomAnimation", "Animation`2<String, Double>", "From")]
    [DataRow(AnimationNamespace, "OpacityAnimation", "Implicit", "ShowAnimations")]
    public void UnverifiedTypeRelationshipsAreNotTreatedAsInheritance(string namespaceUri, string owner, string declaringType, string property)
    {
        Assert.IsFalse(XamlFallbackSchema.IsKnownInheritedProperty(
            new XamlTypeName(owner, namespaceUri), new XamlTypeName(declaringType, namespaceUri), property));
    }

    private static void UseNativeAnimationDeclaringTypes(XbfMetadata metadata)
    {
        for (int index = 0; index < metadata.Properties.Count; index++)
        {
            XbfPropertyEntry property = metadata.Properties[index];
            XbfTypeEntry type = metadata.Types[(int)property.DeclaringTypeId];
            string ownerName = metadata.Strings[(int)type.NameStringId];
            if (ownerName is not ("OpacityAnimation" or "ScaleAnimation" or "TranslationAnimation" or "OffsetAnimation"))
            {
                continue;
            }

            string propertyName = metadata.Strings[(int)property.NameStringId];
            string declaringType = propertyName is "Delay" or "Duration" or "EasingType" or "EasingMode" or "Repeat" or "DelayBehavior" ? "Animation"
                : ownerName == "OpacityAnimation" ? "Animation`2<System.Nullable`1<Double>, Double>"
                    : "Animation`2<String, System.Numerics.Vector3>";
            uint nameId = (uint)metadata.Strings.Count;
            metadata.Strings.Add(declaringType);
            uint typeId = (uint)metadata.Types.Count;
            metadata.Types.Add(type with { NameStringId = nameId });
            metadata.Properties[index] = property with { DeclaringTypeId = typeId };
        }
    }
}
