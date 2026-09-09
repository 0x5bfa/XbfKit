using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XbfKit.Tests;

[TestClass]
public sealed class Xbf2DeferredElementTests
{
    [TestMethod]
    public void LegacyTargetsUseDeferLoadStrategySyntax()
    {
        const string xaml = """
            <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Button x:Name="Deferred" x:DeferLoadStrategy="Lazy" Content="Hello" />
            </Grid>
            """;
        XbfCompilationOptions options = TestUtilities.WuxOptions(target: new Version(10, 0, 14393, 0));
        XbfDocument document = TestUtilities.Compile(xaml, options);
        Xbf2DeferredRuntimeData data = TestUtilities.RuntimeData<Xbf2DeferredRuntimeData>(document).Single();
        Assert.AreEqual(Xbf2CustomRuntimeDataType.DeferredElementV2, data.Type);
        string result = TestUtilities.AssertSemanticRoundTrip(xaml, options);
        StringAssert.Contains(result, "x:DeferLoadStrategy=\"Lazy\"");
        Assert.IsFalse(result.Contains("x:Load=", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ModernTargetsUseLoadSyntax(bool realize)
    {
        string xaml = $$"""
            <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Button x:Name="Deferred" x:Load="{{realize}}" Content="Hello" />
            </Grid>
            """;
        XbfCompilationOptions options = TestUtilities.WuxOptions(target: new Version(10, 0, 15063, 0));
        XbfDocument document = TestUtilities.Compile(xaml, options);
        Xbf2DeferredRuntimeData data = TestUtilities.RuntimeData<Xbf2DeferredRuntimeData>(document).Single();
        Assert.AreEqual(Xbf2CustomRuntimeDataType.DeferredElementV3, data.Type);
        Assert.AreEqual(realize, data.Realize);
        string result = TestUtilities.AssertSemanticRoundTrip(xaml, options);
        StringAssert.Contains(result, $"x:Load=\"{realize}\"");
    }

    [TestMethod]
    public void RelativePanelPropertiesRequiredBeforeRealizationAreSerialized()
    {
        const string xaml = """
            <RelativePanel xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Button x:Name="Deferred" x:DeferLoadStrategy="Lazy" RelativePanel.RightOf="Anchor" RelativePanel.AlignTopWithPanel="True" />
            </RelativePanel>
            """;
        XbfDocument document = TestUtilities.Compile(xaml, TestUtilities.WuxOptions(target: new Version(10, 0, 14393, 0)));
        Xbf2DeferredRuntimeData data = TestUtilities.RuntimeData<Xbf2DeferredRuntimeData>(document).Single();
        Assert.AreEqual(2, data.NonDeferredProperties.Count);
        string result = TestUtilities.Decompile(document);
        StringAssert.Contains(result, "RelativePanel.RightOf=\"Anchor\"");
        StringAssert.Contains(result, "RelativePanel.AlignTopWithPanel=\"True\"");
    }

    [TestMethod]
    public void DeferredElementsRequireAName()
    {
        const string xaml = """
            <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Button x:DeferLoadStrategy="Lazy" />
            </Grid>
            """;
        TestUtilities.AssertThrows<InvalidDataException>(() => TestUtilities.Compile(xaml));
    }

    [TestMethod]
    public void DeferredDirectivesCannotAppearOnTheRootObject()
    {
        const string xaml = """
            <Button xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" x:Name="Root" x:Load="False" />
            """;
        TestUtilities.AssertThrows<InvalidDataException>(() => TestUtilities.Compile(xaml));
    }

    [TestMethod]
    public void LoadSyntaxRequiresACompatibleTarget()
    {
        const string xaml = """
            <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Button x:Name="Deferred" x:Load="False" />
            </Grid>
            """;
        XbfCompilationOptions options = TestUtilities.WuxOptions(target: new Version(10, 0, 14393, 0));
        TestUtilities.AssertThrows<InvalidDataException>(() => TestUtilities.Compile(xaml, options));
    }

    [TestMethod]
    public void DeferLoadStrategyAcceptsOnlyLazy()
    {
        const string xaml = """
            <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Button x:Name="Deferred" x:DeferLoadStrategy="Eager" />
            </Grid>
            """;
        TestUtilities.AssertThrows<InvalidDataException>(() => TestUtilities.Compile(xaml));
    }
}
