namespace XbfKit;

/// <summary>Controls optional transformations applied while converting XBF to human-readable XAML.</summary>
public sealed class XbfDecompilationOptions
{
    /// <summary>Gets or initializes whether generated <c>x:ConnectionId</c> directives are retained.</summary>
    public bool IncludeConnectionIds { get; init; } = true;

    /// <summary>Gets or initializes the number of spaces represented by one indentation level.</summary>
    public int IndentSize { get; init; } = 4;

    /// <summary>Gets or initializes whether indentation levels use tabs instead of spaces.</summary>
    public bool IndentWithTabs { get; init; }

    /// <summary>Gets or initializes the maximum number of attributes kept on an unbroken start tag.</summary>
    public int AttributesTolerance { get; init; } = 2;

    /// <summary>Gets or initializes whether the first attribute remains beside the element name when attributes wrap.</summary>
    public bool KeepFirstAttributeOnSameLine { get; init; }

    /// <summary>Gets or initializes the maximum attribute characters on a wrapped line, or zero for no limit.</summary>
    public int MaxAttributeCharactersPerLine { get; init; }

    /// <summary>Gets or initializes the maximum attributes on each wrapped line, or zero for no limit.</summary>
    public int MaxAttributesPerLine { get; init; } = 1;

    /// <summary>Gets or initializes the comma-separated element names whose attributes are never wrapped.</summary>
    public string NewlineExemptionElements { get; init; } =
        "RadialGradientBrush, GradientStop, LinearGradientBrush, ScaleTransform, SkewTransform, " +
        "RotateTransform, TranslateTransform, Trigger, Condition, Setter";

    /// <summary>Gets or initializes whether changes between attribute-order groups force a new line.</summary>
    public bool SeparateByGroups { get; init; }

    /// <summary>Gets or initializes the additional indentation applied to wrapped attributes, or zero for one indentation level.</summary>
    public int AttributeIndentation { get; init; }

    /// <summary>Gets or initializes how additional attribute indentation is represented when tabs are enabled.</summary>
    public XamlAttributeIndentationStyle AttributeIndentationStyle { get; init; } = XamlAttributeIndentationStyle.Spaces;

    /// <summary>Gets or initializes whether attributes are ordered using <see cref="AttributeOrderingRuleGroups"/>.</summary>
    public bool EnableAttributeReordering { get; init; } = true;

    /// <summary>Gets or initializes the ordered, comma-separated wildcard rules used to group attributes.</summary>
    public IReadOnlyList<string> AttributeOrderingRuleGroups { get; init; } =
    [
        "x:Class",
        "xmlns, xmlns:x",
        "xmlns:*",
        "x:Key, Key, x:Name, Name, x:Uid, Uid, Title",
        "Grid.Row, Grid.RowSpan, Grid.Column, Grid.ColumnSpan, Canvas.Left, Canvas.Top, Canvas.Right, Canvas.Bottom",
        "Width, Height, MinWidth, MinHeight, MaxWidth, MaxHeight",
        "Margin, Padding, HorizontalAlignment, VerticalAlignment, HorizontalContentAlignment, VerticalContentAlignment, Panel.ZIndex",
        "*:*, *",
        "PageSource, PageIndex, Offset, Color, TargetName, Property, Value, StartPoint, EndPoint",
        "mc:Ignorable, d:IsDataSource, d:LayoutOverrides, d:IsStaticText",
        "Storyboard.*, From, To, Duration",
    ];

    /// <summary>Gets or initializes comma-separated wildcard rules for attributes kept beside the element name.</summary>
    public string FirstLineAttributes { get; init; } = string.Empty;

    /// <summary>Gets or initializes whether attributes with the same rule rank are ordered by name.</summary>
    public bool OrderAttributesByName { get; init; } = true;

    /// <summary>Gets or initializes whether a multiline start tag places its ending bracket on a separate line.</summary>
    public bool PutEndingBracketOnNewLine { get; init; }

    /// <summary>Gets or initializes whether empty elements use self-closing tags.</summary>
    public bool RemoveEndingTagOfEmptyElement { get; init; } = true;

    /// <summary>Gets or initializes whether a space is written before the slash of a self-closing tag.</summary>
    public bool SpaceBeforeClosingSlash { get; init; } = true;

    /// <summary>Gets or initializes the attribute wrapping rule used by the document root element.</summary>
    public XamlLineBreakRule RootElementLineBreakRule { get; init; }

    /// <summary>Gets or initializes whether markup-extension attributes are isolated when a start tag wraps.</summary>
    public bool FormatMarkupExtension { get; init; } = true;

    /// <summary>Gets or initializes the comma-separated markup extensions exempt from isolated-line formatting.</summary>
    public string NoNewLineMarkupExtensions { get; init; } = "x:Bind, Binding";

    /// <summary>Gets or initializes the separator used to normalize recognized thickness values.</summary>
    public XamlThicknessSeparator ThicknessSeparator { get; init; } = XamlThicknessSeparator.Comma;

    /// <summary>Gets or initializes the comma-separated attributes whose numeric thickness values are normalized.</summary>
    public string ThicknessAttributes { get; init; } = "Margin, Padding, BorderThickness, ThumbnailClipMargin";

    /// <summary>Gets or initializes the number of spaces placed inside generated XAML comments.</summary>
    public int CommentPadding { get; init; } = 2;
}

/// <summary>Specifies how wrapped attribute indentation combines tabs and spaces.</summary>
public enum XamlAttributeIndentationStyle
{
    /// <summary>Converts complete indentation-width groups into tabs.</summary>
    Mixed,

    /// <summary>Uses tabs for element depth and spaces for additional indentation.</summary>
    Spaces,
}

/// <summary>Specifies whether a start tag must wrap its attributes.</summary>
public enum XamlLineBreakRule
{
    /// <summary>Uses the normal attribute-tolerance rules.</summary>
    Default,

    /// <summary>Always wraps attributes when at least one attribute is present.</summary>
    Always,

    /// <summary>Never wraps attributes.</summary>
    Never,
}

/// <summary>Specifies how numeric thickness components are separated.</summary>
public enum XamlThicknessSeparator
{
    /// <summary>Leaves thickness values unchanged.</summary>
    None,

    /// <summary>Separates thickness components with spaces.</summary>
    Space,

    /// <summary>Separates thickness components with commas.</summary>
    Comma,
}
