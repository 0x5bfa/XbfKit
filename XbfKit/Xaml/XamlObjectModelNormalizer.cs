namespace XbfKit.Xaml;

/// <summary>Applies source-level XAML conventions that are not preserved directly by XBF object-writer data.</summary>
internal static class XamlObjectModelNormalizer
{
    /// <summary>Normalizes an object graph for human-readable XAML output.</summary>
    internal static void Normalize(XamlObjectModel root, XbfDecompilationOptions options)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(options);
        NormalizeObject(root, options, new HashSet<XamlObjectModel>(ReferenceEqualityComparer.Instance));
    }

    private static void NormalizeObject(XamlObjectModel value, XbfDecompilationOptions options, HashSet<XamlObjectModel> visited)
    {
        if (!visited.Add(value))
        {
            return;
        }

        if (!options.IncludeConnectionIds)
        {
            value.Members.RemoveAll(IsConnectionId);
        }

        foreach (XamlMemberModel member in value.Members)
        {
            foreach (object? memberValue in member.Values)
            {
                NormalizeValue(memberValue, options, visited);
            }
        }

        foreach (object? item in value.Items)
        {
            NormalizeValue(item, options, visited);
        }

        CollapseContentProperty(value);
    }

    private static void NormalizeValue(object? value, XbfDecompilationOptions options, HashSet<XamlObjectModel> visited)
    {
        switch (value)
        {
            case XamlObjectModel child:
                NormalizeObject(child, options, visited);
                break;
            case XamlCollectionModel collection:
                foreach (object? item in collection.Items)
                {
                    NormalizeValue(item, options, visited);
                }

                break;
            case XamlConditionalValue conditional:
                NormalizeValue(conditional.Value, options, visited);
                break;
        }
    }

    private static void CollapseContentProperty(XamlObjectModel owner)
    {
        string contentPropertyName = XamlFallbackSchema.GetContentPropertyName(owner.Type.Name);
        for (int index = owner.Members.Count - 1; index >= 0; index--)
        {
            XamlMemberModel member = owner.Members[index];
            if (member.Property.IsImplicit || member.Condition is not null || member.Property.Name != contentPropertyName || !TryGetContentValues(member.Values, out List<object?> values))
            {
                continue;
            }

            owner.Members.RemoveAt(index);
            owner.Items.AddRange(values);
        }
    }

    private static bool TryGetContentValues(IReadOnlyList<object?> source, out List<object?> values)
    {
        values = [];
        foreach (object? value in source)
        {
            switch (value)
            {
                case XamlObjectModel:
                case XamlConditionalValue { Value: XamlObjectModel }:
                    values.Add(value);
                    break;
                case XamlCollectionModel collection:
                    values.AddRange(collection.Items);
                    break;
                default:
                    values = [];
                    return false;
            }
        }

        return values.Count > 0;
    }

    private static bool IsConnectionId(XamlMemberModel member)
    {
        return member.Property.IsDirective && StripPrefix(member.Property.Name) == "ConnectionId";
    }

    private static string StripPrefix(string name)
    {
        int separator = name.IndexOf(':');
        return separator < 0 ? name : name[(separator + 1)..];
    }
}
