using Microsoft.VisualStudio.TestTools.UnitTesting;
using XbfKit.IO;
using System.Buffers.Binary;

namespace XbfKit.Tests;

[TestClass]
public sealed class XbfMetadataAndValidationTests
{
    [TestMethod]
    public void MetadataTablesRoundTripWithoutChangingIdentifiers()
    {
        XbfMetadata metadata = CreateMetadata();
        XbfDocument decoded = TestUtilities.SerializeAndRead(new XbfDocument
        {
            Version = XbfVersion.Version2_1,
            Metadata = metadata,
            Nodes = NodeData(),
        });
        CollectionAssert.AreEqual(metadata.Strings, decoded.Metadata.Strings);
        CollectionAssert.AreEqual(metadata.Assemblies, decoded.Metadata.Assemblies);
        CollectionAssert.AreEqual(metadata.TypeNamespaces, decoded.Metadata.TypeNamespaces);
        CollectionAssert.AreEqual(metadata.Types, decoded.Metadata.Types);
        CollectionAssert.AreEqual(metadata.Properties, decoded.Metadata.Properties);
        CollectionAssert.AreEqual(metadata.XmlNamespaces, decoded.Metadata.XmlNamespaces);
    }

    [TestMethod]
    public void TrustedReferenceBitUsesOnlyTheHighBit()
    {
        foreach (ushort id in new ushort[] { 0, 1, 0x7FFF })
        {
            var local = new Xbf2Reference(id, false);
            var trusted = new Xbf2Reference(id, true);
            Assert.AreEqual(id, local.Encoded);
            Assert.AreEqual((ushort)(id | 0x8000), trusted.Encoded);
            Assert.AreEqual(local, Xbf2Reference.FromEncoded(local.Encoded));
            Assert.AreEqual(trusted, Xbf2Reference.FromEncoded(trusted.Encoded));
        }
    }

    [TestMethod]
    public void MetadataReferencesMustStayInsideTheirTables()
    {
        var metadata = new XbfMetadata();
        metadata.Assemblies.Add(new XbfAssemblyEntry(0, 0));
        byte[] bytes = TestUtilities.WriteDocument(new XbfDocument
        {
            Version = XbfVersion.Version2_1,
            Metadata = metadata,
            Nodes = NodeData(),
        });
        TestUtilities.AssertThrows<InvalidDataException>(() => XbfDocumentReader.Read(new MemoryStream(bytes)));
    }

    [TestMethod]
    public void HeaderMagicAndDeclaredLengthsAreValidated()
    {
        byte[] valid = MinimalDocumentBytes();
        byte[] badMagic = (byte[])valid.Clone();
        badMagic[0] = (byte)'Y';
        TestUtilities.AssertThrows<InvalidDataException>(() => XbfDocumentReader.Read(new MemoryStream(badMagic)));

        byte[] badLength = (byte[])valid.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(badLength.AsSpan(4, 4), uint.MaxValue);
        TestUtilities.AssertThrows<InvalidDataException>(() => XbfDocumentReader.Read(new MemoryStream(badLength)));

        TestUtilities.AssertThrows<EndOfStreamException>(() => XbfDocumentReader.Read(new MemoryStream(valid[..8])));
    }

    [TestMethod]
    public void UnsupportedVersionsAndInvalidTableOffsetsAreRejected()
    {
        byte[] unsupported = MinimalDocumentBytes();
        BinaryPrimitives.WriteUInt32LittleEndian(unsupported.AsSpan(12, 4), 3);
        TestUtilities.AssertThrows<NotSupportedException>(() => XbfDocumentReader.Read(new MemoryStream(unsupported)));

        byte[] invalidOffset = MinimalDocumentBytes();
        BinaryPrimitives.WriteUInt64LittleEndian(invalidOffset.AsSpan(20, 8), 0);
        TestUtilities.AssertThrows<InvalidDataException>(() => XbfDocumentReader.Read(new MemoryStream(invalidOffset)));
    }

    [TestMethod]
    public void DocumentShapeMustMatchItsVersion()
    {
        TestUtilities.AssertThrows<InvalidDataException>(() => TestUtilities.WriteDocument(new XbfDocument
        {
            Version = XbfVersion.Version2_1,
            Nodes = new Xbf2NodeData(),
        }));
        TestUtilities.AssertThrows<InvalidDataException>(() => TestUtilities.WriteDocument(new XbfDocument
        {
            Version = XbfVersion.Version1,
            Nodes = NodeData(),
        }));
        TestUtilities.AssertThrows<ArgumentException>(() => new XbfDocument
        {
            Version = XbfVersion.Version1,
            Hash = new byte[63],
            Nodes = new Xbf1NodeData(),
        });
    }

    [TestMethod]
    public void UnknownAndTruncatedNodeRecordsAreRejected()
    {
        TestUtilities.AssertThrows<InvalidDataException>(() => Xbf1NodeCodec.Decode([0xFF]));
        TestUtilities.AssertThrows<InvalidDataException>(() => new Xbf2Substream { NodeBytes = [0xFF], LineBytes = [] }.Decode());
        TestUtilities.AssertThrows<EndOfStreamException>(() => Xbf1NodeCodec.Decode([(byte)Xbf1NodeType.StartObject, 0x01]));
        TestUtilities.AssertThrows<EndOfStreamException>(() => new Xbf2Substream { NodeBytes = [(byte)Xbf2Opcode.SetValue, 0x01], LineBytes = [] }.Decode());
    }

    [TestMethod]
    public void VectorCountsAndReferenceKindsAreBounded()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.Unicode, leaveOpen: true))
        {
            writer.Write((byte)Xbf2Opcode.SetCustomRuntimeData);
            writer.WriteVarUInt32(1);
            writer.WriteVarUInt32(0);
            writer.WriteVarUInt32(0);
            writer.WriteVarUInt32((uint)Xbf2CustomRuntimeDataType.StyleV1);
            writer.WriteVarUInt32(16_777_217);
        }

        TestUtilities.AssertThrows<InvalidDataException>(() => new Xbf2Substream { NodeBytes = stream.ToArray(), LineBytes = [] }.Decode());
        var instruction = new Xbf2Instruction
        {
            Opcode = Xbf2Opcode.AddNamespace,
            Offset = 0,
            SecondaryReference = new Xbf2Reference(1, true),
            Text = "p",
        };
        TestUtilities.AssertThrows<InvalidDataException>(() => Xbf2InstructionEncoder.EncodeInstructions([instruction]));
    }

    [TestMethod]
    public void InstructionOffsetsMustBeUniqueBeforeLineTranslation()
    {
        var decoded = new Xbf2DecodedSubstream { NodeLength = 2 };
        decoded.Instructions.Add(new Xbf2Instruction { Opcode = Xbf2Opcode.PushScope, Offset = 0 });
        decoded.Instructions.Add(new Xbf2Instruction { Opcode = Xbf2Opcode.PopScope, Offset = 0 });
        TestUtilities.AssertThrows<InvalidDataException>(() => Xbf2InstructionEncoder.Encode(decoded));
    }

    private static XbfMetadata CreateMetadata()
    {
        var metadata = new XbfMetadata();
        metadata.Strings.AddRange(["Contoso", "Contoso.Controls", "Widget", "Value", "using:Contoso.Controls"]);
        metadata.Assemblies.Add(new XbfAssemblyEntry(1, 0));
        metadata.TypeNamespaces.Add(new XbfTypeNamespaceEntry(0, 1));
        metadata.Types.Add(new XbfTypeEntry(XbfTypeFlags.None, 0, 2));
        metadata.Properties.Add(new XbfPropertyEntry(XbfPropertyFlags.None, 0, 3));
        metadata.XmlNamespaces.Add(4);
        return metadata;
    }

    private static Xbf2NodeData NodeData()
    {
        var data = new Xbf2NodeData();
        data.Substreams.Add(new Xbf2Substream
        {
            NodeBytes = Xbf2InstructionEncoder.EncodeInstructions([new Xbf2Instruction { Opcode = Xbf2Opcode.PushScope, Offset = 0 }]),
            LineBytes = [],
        });
        return data;
    }

    private static byte[] MinimalDocumentBytes()
    {
        return TestUtilities.WriteDocument(new XbfDocument
        {
            Version = XbfVersion.Version2_1,
            Nodes = NodeData(),
        });
    }
}
