using System.Xml.Linq;

namespace XbfKit.Xaml;

/// <summary>Provides small namespace-aware name helpers shared by XAML compilers.</summary>
internal static class XamlNameUtility
{
    /// <summary>Removes a conditional-XAML query suffix from a name's namespace URI.</summary>
    internal static XName WithoutCondition(XName name)
    {
        string namespaceUri = name.NamespaceName;
        int question = namespaceUri.IndexOf('?');
        return question < 0 ? name : XName.Get(name.LocalName, namespaceUri[..question]);
    }

    /// <summary>Splits an attached or qualified property name into its declaring type and member name.</summary>
    internal static (XName DeclaringType, string PropertyName) SplitPropertyName(XName ownerType, XName propertyName)
    {
        int separator = propertyName.LocalName.IndexOf('.');
        if (separator <= 0 || separator == propertyName.LocalName.Length - 1)
        {
            return (ownerType, propertyName.LocalName);
        }

        string ownerName = propertyName.LocalName[..separator];
        string memberName = propertyName.LocalName[(separator + 1)..];
        XNamespace declaringNamespace = propertyName.NamespaceName.Length == 0 ? ownerType.Namespace : propertyName.Namespace;
        return (declaringNamespace + ownerName, memberName);
    }

    /// <summary>Gets the prefix portion of a qualified XAML name.</summary>
    internal static string Prefix(string qualifiedName)
    {
        int separator = qualifiedName.IndexOf(':');
        return separator < 0 ? string.Empty : qualifiedName[..separator];
    }

    /// <summary>Gets the local-name portion of a qualified XAML name.</summary>
    internal static string StripPrefix(string qualifiedName)
    {
        int separator = qualifiedName.IndexOf(':');
        return separator < 0 ? qualifiedName : qualifiedName[(separator + 1)..];
    }
}
