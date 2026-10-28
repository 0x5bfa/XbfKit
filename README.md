<h1 align="center">XbfKit</h1>
<p align="center">for decompiling and compiling versioned XBF documents.</p>

It supports XBF1 and XBF2/XBF2.1 documents, including the WUX and MUX
dialects where applicable. The library exposes a semantic XAML object model
so a document can be decompiled, edited, and recompiled.

XbfKit is used in a PRI file viewer, [MrmTool](https://github.com/ahmed605/MrmTool), by [@ahmed505](https://github.com/ahmed605).

## Usage

<a style="text-decoration:none" href="https://www.nuget.org/packages/XbfKit"><img src="https://img.shields.io/nuget/v/XbfKit" alt="NuGet badge" /></a>

```console
> dotnet add package XbfKit
```

---

For a real file-to-file conversion, use the overload that accepts
`XbfCompilationOptions` and the decompilation formatting options:

```csharp
using XbfKit;

const string inputPath = "Page.xaml";
const string xbfPath = "Page.xbf";
const string outputPath = "Page.decompiled.xaml";

using FileStream output = File.Create(xbfPath);
XbfCompiler.Compile(File.ReadAllText(inputPath), output, new XbfCompilationOptions());

using FileStream input = File.OpenRead(xbfPath);
var decompiled = XbfDecompiler.Decompile(input, XbfDialect.WUX, XbfDecompilationOptions());
File.WriteAllText(outputPath, decompiled);
```

To access the validated in-memory representation directly, use the reader and
writer in `XbfKit.IO`:

```csharp
using XbfKit;
using XbfKit.IO;

using FileStream input = File.OpenRead("Page.xbf");
var document = XbfDocumentReader.Read(input);

using FileStream output = File.Create("Page.copy.xbf");
XbfDocumentWriter.Write(document, output);
```

The supported format and dialect combinations are:

- XBF1 with `XbfDialect.WUX`
- XBF2 or XBF2.1 with `XbfDialect.WUX` or `XbfDialect.MUX`

`WUX` refers to `Windows.UI.Xaml`; `MUX` refers to `Microsoft.UI.Xaml`.
The dialect must be supplied when decompiling because XBF2 does not retain
enough information to distinguish those trusted framework schemas.
