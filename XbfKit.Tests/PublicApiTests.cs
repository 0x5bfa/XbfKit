using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XbfKit.Tests;

[TestClass]
public sealed class PublicApiTests
{
    [TestMethod]
    public void CompileWriteReadAndDecompileUseTheStreamContracts()
    {
        const string xaml = "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" />";
        using var stream = new MemoryStream();
        XbfCompiler.Compile(xaml, stream, XbfVersion.Version2_1, XbfDialect.WUX);
        stream.Position = 0;
        string result = XbfDecompiler.Decompile(stream, XbfDialect.WUX);
        StringAssert.Contains(result, "<Grid");
    }

    [TestMethod]
    public void UnsupportedVersionIsRejected()
    {
        const string xaml = "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" />";
        TestUtilities.AssertThrows<NotSupportedException>(() => XbfCompiler.Compile(xaml, new XbfVersion(3, 0)));
    }

    [TestMethod]
    public void Xbf1CannotUseTheMuxDialect()
    {
        const string xaml = "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" />";
        TestUtilities.AssertThrows<ArgumentException>(() => XbfCompiler.Compile(xaml, XbfVersion.Version1, XbfDialect.MUX));
    }
}
