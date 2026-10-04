namespace XbfKit.Xaml;

/// <summary>Provides fallback XAML language rules that are not encoded in an XBF stream.</summary>
internal static class XamlFallbackSchema
{
    /// <summary>Identifies inherited instance properties from a known external XAML schema.</summary>
    internal static bool IsKnownInheritedProperty(XamlTypeName currentType, XamlTypeName declaringType, string propertyName)
    {
        // XBF records the declaring type but does not retain its inheritance or
        // custom attached-property metadata. Use verified schema information;
        // a shared namespace alone cannot distinguish inheritance from attachment.
        // https://github.com/CommunityToolkit/Windows/tree/main/components/Animations/src/Xaml
        if (currentType.NamespaceUri != "using:CommunityToolkit.WinUI.Animations" ||
            declaringType.NamespaceUri != currentType.NamespaceUri ||
            currentType.Name is not ("OpacityAnimation" or "ScaleAnimation" or "TranslationAnimation" or "OffsetAnimation"))
        {
            return false;
        }

        if (declaringType.Name == "Animation")
        {
            return propertyName is "Delay" or "Duration" or "EasingType" or "EasingMode" or "Repeat" or "DelayBehavior";
        }

        return declaringType.Name.StartsWith("Animation`2<", StringComparison.Ordinal) &&
            propertyName is "From" or "To" or "KeyFrames";
    }

    /// <summary>Gets a conventional content-property name when schema metadata is unavailable.</summary>
    internal static string GetContentPropertyName(string typeName)
    {
        return typeName switch
        {
            "ResourceDictionary" or "Application" => "Resources",
            "Grid" or "Canvas" or "RelativePanel" or "Storyboard" or "TransformGroup" or "GeometryGroup" => "Children",
            "ListView" or "GridView" or "ComboBox" or "ListBox" or "MenuFlyout" or "MenuBar" => "Items",
            "TextBlock" or "Span" or "Bold" or "Italic" or "Underline" or "Paragraph" or "Hyperlink" => "Inlines",
            "Border" or "Viewbox" or "Popup" or "InlineUIContainer" or "BlockUIContainer" => "Child",
            "ControlTemplate" or "DataTemplate" or "ItemsPanelTemplate" => "Template",
            "Run" => "Text",
            "RichTextBlock" => "Blocks",
            "Style" => "Setters",
            "VisualStateGroup" => "States",
            "VisualState" => "Storyboard",
            "PathGeometry" => "Figures",
            "PathFigure" => "Segments",
            _ when typeName.EndsWith("Panel", StringComparison.Ordinal) => "Children",
            _ when typeName.EndsWith("ItemsControl", StringComparison.Ordinal) => "Items",
            _ when typeName.EndsWith("GradientBrush", StringComparison.Ordinal) => "GradientStops",
            _ => "Content",
        };
    }
}
