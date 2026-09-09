namespace XbfKit.Xaml;

/// <summary>Contains namespace URIs defined by the XAML language and presentation schema.</summary>
internal static class XamlNamespaces
{
    /// <summary>The Windows XAML presentation namespace URI.</summary>
    internal const string Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    /// <summary>The XAML language namespace URI.</summary>
    internal const string Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>The legacy presentation-schema alias persisted by some XBF2 writers.</summary>
    internal const string LegacyPresentation = "http://schemas.microsoft.com/client/2007";
}

/// <summary>Identifies a XAML type by local name and namespace URI.</summary>
/// <param name="Name">The local type name.</param>
/// <param name="NamespaceUri">The XAML namespace URI.</param>
internal sealed record XamlTypeName(string Name, string NamespaceUri);

/// <summary>Identifies a XAML property and its directive, implicit, or attached traits.</summary>
/// <param name="Name">The local property name.</param>
/// <param name="DeclaringType">The explicit declaring type, when qualification is required.</param>
/// <param name="NamespaceUri">The explicit property namespace URI.</param>
/// <param name="IsDirective">Whether the property is a XAML language directive.</param>
/// <param name="IsImplicit">Whether the property is an implicit object-writer property.</param>
/// <param name="IsAttached">Whether the property uses attached-property syntax.</param>
internal sealed record XamlPropertyName(string Name, XamlTypeName? DeclaringType = null, string? NamespaceUri = null, bool IsDirective = false, bool IsImplicit = false, bool IsAttached = false);

/// <summary>Represents one XML namespace declaration.</summary>
/// <param name="Prefix">The namespace prefix.</param>
/// <param name="NamespaceUri">The namespace URI.</param>
internal sealed record XamlNamespaceDeclaration(string Prefix, string NamespaceUri);

/// <summary>Represents a conditional XAML predicate and its argument text.</summary>
/// <param name="PredicateType">The predicate type.</param>
/// <param name="Arguments">The predicate argument text.</param>
internal sealed record XamlCondition(XamlTypeName PredicateType, string Arguments);

/// <summary>Associates a XAML value with the condition under which it applies.</summary>
/// <param name="Value">The conditional value.</param>
/// <param name="Condition">The condition applied to the value.</param>
internal sealed record XamlConditionalValue(object? Value, XamlCondition Condition);
