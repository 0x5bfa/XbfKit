namespace XbfKit;

/// <summary>Selects the XAML framework whose trusted schema is used to interpret an XBF document.</summary>
public enum XbfDialect
{
    /// <summary>For System XAML (Windows.UI.Xaml).</summary>
    WUX,

    /// <summary>For WinAppSDK XAML (Microsoft.UI.Xaml).</summary>
    MUX,
}
