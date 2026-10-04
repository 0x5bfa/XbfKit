using Microsoft.VisualStudio.TestTools.UnitTesting;
using XbfKit.Decompilation;
using XbfKit.IO;
using XbfKit.Xaml;

namespace XbfKit.Tests;

[TestClass]
public sealed class Xbf2DecompilerConstantTests
{
    [TestMethod]
    [DataRow(XbfDialect.WUX)]
    [DataRow(XbfDialect.MUX)]
    public void UnusedConstantsDoNotReplaceCollectionChildren(XbfDialect dialect)
    {
        string[] documents =
        [
            """
            <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Button x:Name="First" />
                <Button x:Name="Second" />
            </Grid>
            """,
            """
            <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Button x:Name="First" Content="One" />
                <Button x:Name="Second" Content="Two" />
            </Grid>
            """,
            """
            <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Grid x:Name="First"><Button x:Name="Nested" Content="One" /></Grid>
                <Button x:Name="Second" Content="Two" />
            </Grid>
            """,
        ];

        foreach (string xaml in documents)
        {
            XbfDocument document = TestUtilities.Compile(xaml, new XbfCompilationOptions { Dialect = dialect });
            string expected = TestUtilities.Decompile(document, dialect);
            Xbf2NodeData nodes = (Xbf2NodeData)document.Nodes;
            var instructions = new List<Xbf2Instruction>();
            foreach (Xbf2Instruction instruction in nodes.Substreams[0].Decode().Instructions)
            {
                instructions.Add(instruction);
                if (instruction.Opcode == Xbf2Opcode.SetName)
                {
                    // Native XBF can retain a value after dropping its directive assignment.
                    instructions.Add(new Xbf2Instruction
                    {
                        Opcode = Xbf2Opcode.PushConstant,
                        Offset = 0,
                        Constant = new Xbf2Constant(Xbf2ConstantType.UniqueString, "public"),
                    });
                }
            }

            nodes.Substreams[0] = new Xbf2Substream { NodeBytes = Xbf2InstructionEncoder.EncodeInstructions(instructions) };
            Assert.AreEqual(expected, TestUtilities.Decompile(TestUtilities.SerializeAndRead(document), dialect));
        }
    }

    [TestMethod]
    [DataRow(XbfDialect.WUX)]
    [DataRow(XbfDialect.MUX)]
    public void ConstantsImmediatelyAddedToCollectionsArePreserved(XbfDialect dialect)
    {
        const string xaml = """
            <ComboBox xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <ComboBox.Items>First</ComboBox.Items>
            </ComboBox>
            """;
        XbfDocument document = TestUtilities.Compile(xaml, new XbfCompilationOptions { Dialect = dialect });
        Xbf2NodeData nodes = (Xbf2NodeData)document.Nodes;
        List<Xbf2Instruction> instructions = nodes.Substreams[0].Decode().Instructions;
        int index = instructions.FindIndex(value => value.Opcode == Xbf2Opcode.AddToCollection) + 1;
        Xbf2Constant[] constants = [Xbf2Constant.NullString, Xbf2Constant.True, new(Xbf2ConstantType.Signed, 42)];
        foreach (Xbf2Constant constant in constants)
        {
            instructions.Insert(index++, new Xbf2Instruction { Opcode = Xbf2Opcode.PushConstant, Offset = 0, Constant = constant });
            instructions.Insert(index++, new Xbf2Instruction { Opcode = Xbf2Opcode.AddToCollection, Offset = 0 });
        }

        nodes.Substreams[0] = new Xbf2Substream { NodeBytes = Xbf2InstructionEncoder.EncodeInstructions(instructions) };
        XamlObjectModel root = Xbf2Decompiler.Decompile(TestUtilities.SerializeAndRead(document), dialect);
        XamlCollectionModel collection = (XamlCollectionModel)root.Members.Single(member => member.Property.Name == "Items").Values.Single()!;
        CollectionAssert.AreEqual(new object?[] { "First", null, true, 42 }, collection.Items);
    }
}
