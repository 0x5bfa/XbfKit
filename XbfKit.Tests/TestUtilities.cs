using Microsoft.VisualStudio.TestTools.UnitTesting;
using XbfKit.IO;

namespace XbfKit.Tests;

internal static class TestUtilities
{
    internal const string PresentationNamespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    internal const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    internal static XbfCompilationOptions WuxOptions(XbfVersion? version = null, Version? target = null)
    {
        return new XbfCompilationOptions
        {
            Version = version ?? XbfVersion.Version2_1,
            Dialect = XbfDialect.WUX,
            TargetRuntimeVersion = target,
        };
    }

    internal static XbfDocument Compile(string body, XbfCompilationOptions? options = null)
    {
        return XbfCompiler.Compile(body, options ?? WuxOptions());
    }

    internal static XbfDocument SerializeAndRead(XbfDocument document)
    {
        using var stream = new MemoryStream();
        XbfDocumentWriter.Write(document, stream);
        stream.Position = 0;
        return XbfDocumentReader.Read(stream);
    }

    internal static string Decompile(XbfDocument document, XbfDialect dialect = XbfDialect.WUX, XbfDecompilationOptions? options = null)
    {
        return XbfDecompiler.Decompile(document, dialect, options ?? new XbfDecompilationOptions());
    }

    internal static string AssertSemanticRoundTrip(string xaml, XbfCompilationOptions? options = null, XbfDialect dialect = XbfDialect.WUX)
    {
        XbfDocument firstDocument = SerializeAndRead(Compile(xaml, options));
        string first = Decompile(firstDocument, dialect);
        XbfDocument secondDocument = SerializeAndRead(Compile(first, options));
        string second = Decompile(secondDocument, dialect);
        Assert.AreEqual(first, second);
        return first;
    }

    internal static IReadOnlyList<T> RuntimeData<T>(XbfDocument document) where T : Xbf2CustomRuntimeData
    {
        var nodeData = document.Nodes as Xbf2NodeData ?? throw new AssertFailedException("The document does not contain XBF2 node data.");
        return [.. nodeData.Substreams
            .SelectMany(stream => stream.Decode().Instructions)
            .Select(instruction => instruction.Segment?.RuntimeData)
            .OfType<T>()];
    }

    internal static T AssertThrows<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T exception)
        {
            return exception;
        }

        throw new AssertFailedException($"Expected {typeof(T).Name} to be thrown.");
    }

    internal static byte[] WriteDocument(XbfDocument document)
    {
        using var stream = new MemoryStream();
        XbfDocumentWriter.Write(document, stream);
        return stream.ToArray();
    }
}
