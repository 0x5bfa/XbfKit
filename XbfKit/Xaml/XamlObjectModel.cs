namespace XbfKit.Xaml;

/// <summary>Represents an object, its members, items, namespaces, and condition in canonical XAML.</summary>
internal sealed class XamlObjectModel
{
    /// <summary>Initializes an object model for the specified XAML type.</summary>
    internal XamlObjectModel(XamlTypeName type)
    {
        Type = type;
    }

    /// <summary>Gets the object's XAML type.</summary>
    internal XamlTypeName Type { get; }

    /// <summary>Gets or sets the condition applied to the object.</summary>
    internal XamlCondition? Condition { get; set; }

    /// <summary>Gets or sets whether an x:Initialization value is present.</summary>
    internal bool HasInitializationValue { get; set; }

    /// <summary>Gets or sets the x:Initialization value.</summary>
    internal object? InitializationValue { get; set; }

    /// <summary>Gets namespace declarations attached to the object.</summary>
    internal List<XamlNamespaceDeclaration> Namespaces { get; } = [];

    /// <summary>Gets explicitly assigned members.</summary>
    internal List<XamlMemberModel> Members { get; } = [];

    /// <summary>Gets implicit content or collection items.</summary>
    internal List<object?> Items { get; } = [];

    /// <summary>Gets an existing matching member or appends a new member.</summary>
    internal XamlMemberModel GetOrAddMember(XamlPropertyName property, XamlCondition? condition = null)
    {
        XamlMemberModel? existing = Members.LastOrDefault(value => value.Property == property && value.Condition == condition);
        if (existing is not null)
        {
            return existing;
        }

        var member = new XamlMemberModel(property) { Condition = condition };
        Members.Add(member);
        return member;
    }

    /// <summary>Replaces a member's values with one value.</summary>
    internal void SetValue(XamlPropertyName property, object? value)
    {
        XamlMemberModel member = GetOrAddMember(property);
        member.Values.Clear();
        member.Values.Add(value);
    }
}

/// <summary>Represents one XAML member assignment and its ordered values.</summary>
internal sealed class XamlMemberModel
{
    /// <summary>Initializes a member model for the specified property.</summary>
    internal XamlMemberModel(XamlPropertyName property)
    {
        Property = property;
    }

    /// <summary>Gets the assigned property.</summary>
    internal XamlPropertyName Property { get; }

    /// <summary>Gets or sets the condition applied to this member.</summary>
    internal XamlCondition? Condition { get; set; }

    /// <summary>Gets the ordered values assigned to the member.</summary>
    internal List<object?> Values { get; } = [];
}

/// <summary>Represents an intermediate collection value.</summary>
internal sealed class XamlCollectionModel
{
    /// <summary>Gets the ordered collection items.</summary>
    internal List<object?> Items { get; } = [];
}
