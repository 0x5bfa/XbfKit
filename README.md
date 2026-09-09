<h1 align="center">XbfKit</h1>
<p align="center">for decompiling and compiling versioned XBF documents.</p>


It supports XBF1 and XBF2/XBF2.1 documents, including the WUX and MUX
dialects where applicable. The library exposes a semantic XAML object model
so a document can be decompiled, edited, and recompiled.

## Usage

<a style="text-decoration:none" href="https://www.nuget.org/packages/XbfKit"><img src="https://img.shields.io/nuget/v/XbfKit" alt="NuGet badge" /></a>

```console
> dotnet add package XbfKit
```

---

```csharp
using XbfKit;

const string xaml = "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" />";

using var xbf = new MemoryStream();
XbfCompiler.Compile(xaml, xbf, XbfVersion.Version2_1, XbfDialect.WUX);
xbf.Position = 0;

string decompiled = XbfDecompiler.Decompile(xbf, XbfDialect.WUX);
```

For a real file-to-file conversion, use the overload that accepts
`XbfCompilationOptions` and the decompilation formatting options:

```csharp
using XbfKit;

const string inputPath = "Page.xaml";
const string xbfPath = "Page.xbf";
const string outputPath = "Page.decompiled.xaml";

var compilationOptions = new XbfCompilationOptions
{
    Version = XbfVersion.Version2_1,
    Dialect = XbfDialect.WUX,
};

using (FileStream output = File.Create(xbfPath))
{
    XbfCompiler.Compile(File.ReadAllText(inputPath), output, compilationOptions);
}

var decompilationOptions = new XbfDecompilationOptions
{
    IndentSize = 2,
    IncludeConnectionIds = false,
    MaxAttributesPerLine = 2,
};

using FileStream input = File.OpenRead(xbfPath);
string decompiled = XbfDecompiler.Decompile(input, XbfDialect.WUX, decompilationOptions);
File.WriteAllText(outputPath, decompiled);
```

To access the validated in-memory representation directly, use the reader and
writer in `XbfKit.IO`:

```csharp
using XbfKit;
using XbfKit.IO;

XbfDocument document;
using (FileStream input = File.OpenRead("Page.xbf"))
{
    document = XbfDocumentReader.Read(input);
}

// Inspect or edit document.Metadata and document.Nodes here.

using FileStream output = File.Create("Page.copy.xbf");
XbfDocumentWriter.Write(document, output);
```

The supported format and dialect combinations are:

- XBF1 with `XbfDialect.WUX`
- XBF2 or XBF2.1 with `XbfDialect.WUX` or `XbfDialect.MUX`

`WUX` refers to `Windows.UI.Xaml`; `MUX` refers to `Microsoft.UI.Xaml`.
The dialect must be supplied when decompiling because XBF2 does not retain
enough information to distinguish those trusted framework schemas.
