using XbfKit.Compilation;
using XbfKit.IO;
using System.Xml.Linq;

namespace XbfKit;

/// <summary>Compiles human-readable XAML into XBF documents.</summary>
public static class XbfCompiler
{
    /// <summary>Compiles XAML using the Windows.UI.Xaml dialect.</summary>
    /// <param name="xaml">The XAML text to compile.</param>
    /// <param name="version">The XBF version to produce.</param>
    /// <returns>The compiled in-memory XBF document.</returns>
    public static XbfDocument Compile(string xaml, XbfVersion version)
    {
        return Compile(xaml, new XbfCompilationOptions { Version = version });
    }

    /// <summary>Compiles XAML using an explicit XBF version and framework dialect.</summary>
    /// <param name="xaml">The XAML text to compile.</param>
    /// <param name="version">The XBF version to produce.</param>
    /// <param name="dialect">The trusted framework type table to use.</param>
    /// <returns>The compiled in-memory XBF document.</returns>
    public static XbfDocument Compile(string xaml, XbfVersion version, XbfDialect dialect)
    {
        return Compile(xaml, new XbfCompilationOptions { Version = version, Dialect = dialect });
    }

    /// <summary>Compiles XAML using explicit format, dialect, and target-runtime options.</summary>
    /// <param name="xaml">The XAML text to compile.</param>
    /// <param name="options">The compilation settings.</param>
    /// <returns>The compiled in-memory XBF document.</returns>
    public static XbfDocument Compile(string xaml, XbfCompilationOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xaml);
        XbfRuntimeProfile profile = XbfRuntimeProfile.Create(options);

        XDocument document;
        try
        {
            document = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        }
        catch (Exception exception) when (exception is System.Xml.XmlException or ArgumentException)
        {
            throw new InvalidDataException("The input is not valid XML/XAML.", exception);
        }

        return options.Version.Major switch
        {
            1 => Xbf1Compiler.Compile(document, options.Version, profile),
            2 => Xbf2Compiler.Compile(document, options.Version, profile),
            _ => throw new NotSupportedException($"XBF version {options.Version} is not supported."),
        };
    }

    /// <summary>Compiles XAML and writes the resulting XBF using the Windows.UI.Xaml dialect.</summary>
    /// <param name="xaml">The XAML text to compile.</param>
    /// <param name="output">The writable destination stream.</param>
    /// <param name="version">The XBF version to produce.</param>
    public static void Compile(string xaml, Stream output, XbfVersion version)
    {
        ArgumentNullException.ThrowIfNull(output);
        Compile(xaml, output, new XbfCompilationOptions { Version = version });
    }

    /// <summary>Compiles XAML and writes the resulting XBF to a stream using an explicit dialect.</summary>
    /// <param name="xaml">The XAML text to compile.</param>
    /// <param name="output">The writable destination stream.</param>
    /// <param name="version">The XBF version to produce.</param>
    /// <param name="dialect">The trusted framework type table to use.</param>
    public static void Compile(string xaml, Stream output, XbfVersion version, XbfDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(output);
        Compile(xaml, output, new XbfCompilationOptions { Version = version, Dialect = dialect });
    }

    /// <summary>Compiles XAML and writes the resulting XBF using explicit target-runtime options.</summary>
    /// <param name="xaml">The XAML text to compile.</param>
    /// <param name="output">The writable destination stream.</param>
    /// <param name="options">The compilation settings.</param>
    public static void Compile(string xaml, Stream output, XbfCompilationOptions options)
    {
        ArgumentNullException.ThrowIfNull(output);
        XbfDocumentWriter.Write(Compile(xaml, options), output);
    }
}
