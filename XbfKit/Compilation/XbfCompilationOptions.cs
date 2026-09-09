namespace XbfKit;

/// <summary>Controls the XBF container, XAML dialect, and target runtime used during compilation.</summary>
public sealed class XbfCompilationOptions
{
    /// <summary>Gets the XBF file-format version to produce.</summary>
    public XbfVersion Version { get; init; } = XbfVersion.Version2_1;

    /// <summary>Gets the XAML framework whose trusted schema is used by the generated document.</summary>
    public XbfDialect Dialect { get; init; } = XbfDialect.WUX;

    /// <summary>
    /// Gets the Windows.UI.Xaml runtime version that must be able to read the generated WUX document.
    /// </summary>
    /// <remarks>
    /// This option applies only to <see cref="XbfDialect.WUX"/>. When omitted, XBF1 targets Windows 8.1
    /// and XBF2 targets the latest stable WUX schema represented by this library (Windows 10 version 1903).
    /// </remarks>
    public Version? TargetRuntimeVersion { get; init; }
}
