namespace XbfKit.Xaml;

/// <summary>Provides fallback XAML language rules that are not encoded in an XBF stream.</summary>
internal static class XamlFallbackSchema
{
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
