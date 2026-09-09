using XbfKit.Decompilation;
using XbfKit.IO;
using XbfKit.Xaml;

namespace XbfKit;

/// <summary>Converts XBF documents into canonical, human-readable XAML.</summary>
/// <remarks>XBF2 does not identify whether trusted references target Windows.UI.Xaml or Microsoft.UI.Xaml, so callers must provide the dialect.</remarks>
public static class XbfDecompiler
{
    /// <summary>Decompiles XBF using an explicit framework dialect.</summary>
    /// <param name="stream">The source XBF stream.</param>
    /// <param name="dialect">The trusted framework type table to use.</param>
    /// <returns>Canonical XAML text.</returns>
    public static string Decompile(Stream stream, XbfDialect dialect)
    {
        return Decompile(stream, dialect, new XbfDecompilationOptions());
    }

    /// <summary>Decompiles XBF using an explicit framework dialect and output options.</summary>
    /// <param name="stream">The source XBF stream.</param>
    /// <param name="dialect">The trusted framework type table to use.</param>
    /// <param name="options">The source-level output transformations to apply.</param>
    /// <returns>Canonical XAML text.</returns>
    public static string Decompile(Stream stream, XbfDialect dialect, XbfDecompilationOptions options)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(options);
        XbfDocument document = XbfDocumentReader.Read(stream);
        return Decompile(document, dialect, options);
    }

    /// <summary>Decompiles an in-memory XBF document using an explicit framework dialect.</summary>
    /// <param name="document">The parsed XBF document.</param>
    /// <param name="dialect">The trusted framework type table to use.</param>
    /// <returns>Canonical XAML text.</returns>
    public static string Decompile(XbfDocument document, XbfDialect dialect)
    {
        return Decompile(document, dialect, new XbfDecompilationOptions());
    }

    /// <summary>Decompiles an in-memory XBF document using an explicit framework dialect and output options.</summary>
    /// <param name="document">The parsed XBF document.</param>
    /// <param name="dialect">The trusted framework type table to use.</param>
    /// <param name="options">The source-level output transformations to apply.</param>
    /// <returns>Canonical XAML text.</returns>
    public static string Decompile(XbfDocument document, XbfDialect dialect, XbfDecompilationOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        if (!document.Version.IsSupported)
        {
            throw new NotSupportedException($"XBF version {document.Version} is not supported.");
        }

        if (document.Version.Major switch
        {
            1 => dialect == XbfDialect.WUX,
            2 => dialect is XbfDialect.WUX or XbfDialect.MUX,
            _ => false,
        } is false)
        {
            throw new ArgumentException($"XBF version {document.Version} cannot use the {dialect} dialect.", nameof(dialect));
        }

        XamlObjectModel root = document.Version.Major switch
        {
            1 => Xbf1Decompiler.Decompile(document),
            2 => Xbf2Decompiler.Decompile(document, dialect),
            _ => throw new NotSupportedException($"XBF version {document.Version} is not supported."),
        };

        XamlObjectModelNormalizer.Normalize(root, options);
        return new XamlTextWriter(options).Write(root);
    }
}
