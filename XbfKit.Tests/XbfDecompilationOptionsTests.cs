using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XbfKit.Tests;

[TestClass]
public sealed class XbfDecompilationOptionsTests
{
    [TestMethod]
    public void ConnectionIdentifiersCanBeIncludedOrRemoved()
    {
        const string xaml = """
            <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" x:ConnectionId="7" />
            """;
        XbfDocument document = TestUtilities.Compile(xaml);
        StringAssert.Contains(TestUtilities.Decompile(document), "x:ConnectionId=\"7\"");
        Assert.IsFalse(TestUtilities.Decompile(document, options: new XbfDecompilationOptions { IncludeConnectionIds = false }).Contains("x:ConnectionId", StringComparison.Ordinal));
    }

    [TestMethod]
    public void AttributeToleranceKeepsSmallAttributeSetsTogether()
    {
        const string xaml = """
            <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <Button Width="100" Content="Hello" />
            </Grid>
            """;
        string result = TestUtilities.Decompile(TestUtilities.Compile(xaml), options: new XbfDecompilationOptions { AttributesTolerance = 2 });
        string buttonLine = result.Split("\r\n", StringSplitOptions.None).Single(line => line.Contains("<Button", StringComparison.Ordinal));
        StringAssert.Contains(buttonLine, "Width=\"100\"");
        StringAssert.Contains(buttonLine, "Content=\"Hello\"");
    }

    [TestMethod]
    public void AttributeToleranceWrapsLargerAttributeSets()
    {
        const string xaml = """
            <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <Button Width="100" Height="40" Content="Hello" />
            </Grid>
            """;
        string result = TestUtilities.Decompile(TestUtilities.Compile(xaml), options: new XbfDecompilationOptions { AttributesTolerance = 2 });
        string[] lines = result.Split("\r\n", StringSplitOptions.None);
        int buttonLine = Array.FindIndex(lines, line => line.Contains("<Button", StringComparison.Ordinal));
        Assert.IsTrue(buttonLine >= 0);
        Assert.AreEqual("<Button", lines[buttonLine].Trim());
        Assert.IsTrue(lines.Skip(buttonLine + 1).Take(3).Any(line => line.Contains("Content=", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void RootLineBreakRuleOverridesAttributeTolerance()
    {
        const string xaml = """
            <Page xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Title="Title" />
            """;
        XbfDocument document = TestUtilities.Compile(xaml);
        string always = TestUtilities.Decompile(document, options: new XbfDecompilationOptions { RootElementLineBreakRule = XamlLineBreakRule.Always });
        string never = TestUtilities.Decompile(document, options: new XbfDecompilationOptions { RootElementLineBreakRule = XamlLineBreakRule.Never });
        Assert.AreEqual("<Page", always.Split("\r\n", StringSplitOptions.None)[0]);
        StringAssert.StartsWith(never, "<Page ");
    }

    [TestMethod]
    [DataRow(XamlThicknessSeparator.Comma, "Margin=\"1,2,3,4\"")]
    [DataRow(XamlThicknessSeparator.Space, "Margin=\"1 2 3 4\"")]
    [DataRow(XamlThicknessSeparator.None, "Margin=\"1 2 3 4\"")]
    public void ThicknessValuesUseTheSelectedSeparator(XamlThicknessSeparator separator, string expected)
    {
        const string xaml = """
            <Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Margin="1 2 3 4" />
            """;
        string result = TestUtilities.Decompile(TestUtilities.Compile(xaml), options: new XbfDecompilationOptions { ThicknessSeparator = separator });
        StringAssert.Contains(result, expected);
    }

    [TestMethod]
    public void SignificantNonAsciiCharactersUseXmlCharacterReferences()
    {
        const string xaml = """
            <Button xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Content="&#xE725;" />
            """;
        string result = TestUtilities.Decompile(TestUtilities.Compile(xaml));
        StringAssert.Contains(result, "&#xE725;");
    }

    [TestMethod]
    public void GeneratedTextUsesCrLfLineEndings()
    {
        const string xaml = """
            <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"><Button /></Grid>
            """;
        string result = TestUtilities.Decompile(TestUtilities.Compile(xaml));
        StringAssert.Contains(result, "\r\n");
        Assert.IsFalse(result.Replace("\r\n", string.Empty, StringComparison.Ordinal).Contains('\n'));
    }
}
