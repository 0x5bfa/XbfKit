using Microsoft.VisualStudio.TestTools.UnitTesting;
using XbfKit.Compilation;

namespace XbfKit.Tests;

[TestClass]
public sealed class XbfRuntimeProfileTests
{
    [TestMethod]
    [DataRow("10.0.10240.0", 798, 1749, Xbf2CustomRuntimeDataType.ResourceDictionaryV1, Xbf2CustomRuntimeDataType.StyleV1, Xbf2CustomRuntimeDataType.DeferredElementV2, false)]
    [DataRow("10.0.10586.0", 807, 1769, Xbf2CustomRuntimeDataType.ResourceDictionaryV1, Xbf2CustomRuntimeDataType.StyleV1, Xbf2CustomRuntimeDataType.DeferredElementV2, false)]
    [DataRow("10.0.14393.0", 840, 1879, Xbf2CustomRuntimeDataType.ResourceDictionaryV2, Xbf2CustomRuntimeDataType.StyleV2, Xbf2CustomRuntimeDataType.DeferredElementV2, false)]
    [DataRow("10.0.15063.0", 882, 1972, Xbf2CustomRuntimeDataType.ResourceDictionaryV3, Xbf2CustomRuntimeDataType.StyleV2, Xbf2CustomRuntimeDataType.DeferredElementV3, true)]
    [DataRow("10.0.16299.0", 901, 2013, Xbf2CustomRuntimeDataType.ResourceDictionaryV3, Xbf2CustomRuntimeDataType.StyleV2, Xbf2CustomRuntimeDataType.DeferredElementV3, true)]
    [DataRow("10.0.17134.0", 941, 2135, Xbf2CustomRuntimeDataType.ResourceDictionaryV3, Xbf2CustomRuntimeDataType.StyleV2, Xbf2CustomRuntimeDataType.DeferredElementV3, true)]
    [DataRow("10.0.17763.0", 970, 2357, Xbf2CustomRuntimeDataType.ResourceDictionaryV3, Xbf2CustomRuntimeDataType.StyleV2, Xbf2CustomRuntimeDataType.DeferredElementV3, true)]
    [DataRow("10.0.18362.0", 978, 2456, Xbf2CustomRuntimeDataType.ResourceDictionaryV3, Xbf2CustomRuntimeDataType.StyleV2, Xbf2CustomRuntimeDataType.DeferredElementV3, true)]
    public void WuxProfilesFollowTargetRuntimeBoundaries(string version, int typeCount, int propertyCount, Xbf2CustomRuntimeDataType dictionaryType, Xbf2CustomRuntimeDataType styleType, Xbf2CustomRuntimeDataType deferredType, bool conditional)
    {
        XbfRuntimeProfile profile = XbfRuntimeProfile.Create(TestUtilities.WuxOptions(target: Version.Parse(version)));
        Assert.AreEqual(typeCount, profile.StableTypeCount);
        Assert.AreEqual(propertyCount, profile.StablePropertyCount);
        Assert.AreEqual(dictionaryType, profile.ResourceDictionaryRuntimeDataType);
        Assert.AreEqual(styleType, profile.StyleRuntimeDataType);
        Assert.AreEqual(deferredType, profile.DeferredElementRuntimeDataType);
        Assert.AreEqual(Xbf2CustomRuntimeDataType.VisualStateGroupCollectionV5, profile.VisualStateRuntimeDataType);
        Assert.AreEqual(conditional, profile.SupportsConditionalXaml);
        Assert.IsTrue(profile.SupportsStableType((ushort)(typeCount - 1)));
        Assert.IsFalse(profile.SupportsStableType((ushort)typeCount));
        Assert.IsTrue(profile.SupportsStableProperty((ushort)(propertyCount - 1)));
        Assert.IsFalse(profile.SupportsStableProperty((ushort)propertyCount));
    }

    [TestMethod]
    public void DefaultProfilesSelectTheDocumentEra()
    {
        XbfRuntimeProfile xbf1 = XbfRuntimeProfile.Create(TestUtilities.WuxOptions(XbfVersion.Version1));
        XbfRuntimeProfile xbf2 = XbfRuntimeProfile.Create(TestUtilities.WuxOptions());
        Assert.AreEqual(new Version(6, 3, 9600, 0), xbf1.TargetRuntimeVersion);
        Assert.AreEqual(new Version(10, 0, 18362, 0), xbf2.TargetRuntimeVersion);
    }

    [TestMethod]
    public void MuxProfileUsesMuxRuntimeDataAndUnboundedStableReferences()
    {
        XbfRuntimeProfile profile = XbfRuntimeProfile.Create(new XbfCompilationOptions
        {
            Version = XbfVersion.Version2_1,
            Dialect = XbfDialect.MUX,
        });
        Assert.AreEqual(XbfDialect.MUX, profile.Dialect);
        Assert.IsNull(profile.TargetRuntimeVersion);
        Assert.AreEqual(ushort.MaxValue + 1, profile.StableTypeCount);
        Assert.AreEqual(ushort.MaxValue + 1, profile.StablePropertyCount);
        Assert.AreEqual(Xbf2CustomRuntimeDataType.ResourceDictionaryV3, profile.ResourceDictionaryRuntimeDataType);
        Assert.AreEqual(Xbf2CustomRuntimeDataType.StyleV3, profile.StyleRuntimeDataType);
        Assert.IsTrue(profile.SupportsConditionalXaml);
    }

    [TestMethod]
    public void FileFormatAndTargetRuntimeMustBelongToTheSameEra()
    {
        TestUtilities.AssertThrows<ArgumentException>(() => XbfRuntimeProfile.Create(TestUtilities.WuxOptions(XbfVersion.Version1, new Version(10, 0, 10240, 0))));
        TestUtilities.AssertThrows<ArgumentException>(() => XbfRuntimeProfile.Create(TestUtilities.WuxOptions(XbfVersion.Version2_1, new Version(6, 3, 9600, 0))));
        TestUtilities.AssertThrows<ArgumentException>(() => XbfRuntimeProfile.Create(new XbfCompilationOptions
        {
            Version = XbfVersion.Version2_1,
            Dialect = XbfDialect.MUX,
            TargetRuntimeVersion = new Version(10, 0, 18362, 0),
        }));
    }

    [TestMethod]
    public void RuntimeVersionMustContainFourUnsignedComponents()
    {
        TestUtilities.AssertThrows<ArgumentOutOfRangeException>(() => XbfRuntimeProfile.Create(TestUtilities.WuxOptions(target: new Version(10, 0))));
        TestUtilities.AssertThrows<ArgumentOutOfRangeException>(() => XbfRuntimeProfile.Create(TestUtilities.WuxOptions(target: new Version(70000, 0, 0, 0))));
    }
}
