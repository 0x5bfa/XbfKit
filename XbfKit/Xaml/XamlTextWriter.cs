using System.Text;
using System.Xml;

namespace XbfKit.Xaml;

/// <summary>Serializes the shared XAML object model as canonical, human-readable XML.</summary>
internal sealed class XamlTextWriter
{
    private readonly XbfDecompilationOptions _options;
    private readonly Dictionary<string, string> _prefixes = new(StringComparer.Ordinal);
    private readonly HashSet<XamlObjectModel> _writeStack = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<string> _thicknessAttributes;
    private int _generatedPrefix;

    /// <summary>Initializes a writer using the requested source formatting.</summary>
    internal XamlTextWriter(XbfDecompilationOptions options)
    {
        _options = options;
        _thicknessAttributes = ParseCommaSeparated(options.ThicknessAttributes);
    }

    /// <summary>Writes a complete XAML document rooted at the specified object.</summary>
    internal string Write(XamlObjectModel root)
    {
        EnsureRootNamespaces(root);
        using var output = new StringWriter(new StringBuilder(), System.Globalization.CultureInfo.InvariantCulture);
        using var writer = XmlWriter.Create(output, new XmlWriterSettings
        {
            Indent = true,
            IndentChars = _options.IndentWithTabs ? "\t" : new string(' ', Math.Max(_options.IndentSize, 1)),
            NewLineOnAttributes = false,
            OmitXmlDeclaration = true,
            NewLineChars = Environment.NewLine,
        });

        WriteObject(writer, root, isRoot: true);
        writer.Flush();
        return new XamlTextFormatter(_options).Format(output.ToString());
    }

    private void EnsureRootNamespaces(XamlObjectModel root)
    {
        foreach (XamlNamespaceDeclaration declaration in root.Namespaces)
        {
            _prefixes.TryAdd(declaration.NamespaceUri, declaration.Prefix);
        }

        if (!_prefixes.ContainsKey(root.Type.NamespaceUri))
        {
            string prefix = root.Type.NamespaceUri switch
            {
                XamlNamespaces.Presentation => string.Empty,
                XamlNamespaces.Xaml => "x",
                _ => NextPrefix(),
            };
            root.Namespaces.Insert(0, new XamlNamespaceDeclaration(prefix, root.Type.NamespaceUri));
            _prefixes[root.Type.NamespaceUri] = prefix;
        }

        if (UsesXamlNamespace(root, new(ReferenceEqualityComparer.Instance)) && !_prefixes.ContainsKey(XamlNamespaces.Xaml))
        {
            root.Namespaces.Add(new XamlNamespaceDeclaration("x", XamlNamespaces.Xaml));
            _prefixes[XamlNamespaces.Xaml] = "x";
        }

        CollectNamespaces(root, root, new(ReferenceEqualityComparer.Instance));
    }

    private void CollectNamespaces(XamlObjectModel current, XamlObjectModel root, HashSet<XamlObjectModel> visited)
    {
        if (!visited.Add(current))
        {
            return;
        }

        EnsureNamespace(current.Type.NamespaceUri, root);
        EnsureConditionNamespaces(current.Type.NamespaceUri, current.Condition, root);
        foreach (XamlMemberModel member in current.Members)
        {
            if (member.Property.IsDirective)
            {
                EnsureNamespace(XamlNamespaces.Xaml, root, "x");
            }
            else if (!string.IsNullOrEmpty(member.Property.NamespaceUri))
            {
                EnsureNamespace(member.Property.NamespaceUri!, root);
            }
            else if (member.Property.DeclaringType is not null)
            {
                EnsureNamespace(member.Property.DeclaringType.NamespaceUri, root);
            }

            string memberNamespace = MemberNamespace(member, current);
            EnsureConditionNamespaces(memberNamespace, member.Condition, root);

            foreach (object? value in member.Values)
            {
                CollectValueNamespaces(value, root, visited);
            }
        }

        foreach (object? item in current.Items)
        {
            CollectValueNamespaces(item, root, visited);
        }
    }

    private void CollectValueNamespaces(object? value, XamlObjectModel root, HashSet<XamlObjectModel> visited)
    {
        switch (value)
        {
            case XamlObjectModel child:
                CollectNamespaces(child, root, visited);
                break;
            case XamlCollectionModel collection:
                foreach (object? item in collection.Items)
                {
                    CollectValueNamespaces(item, root, visited);
                }
                break;
            case XamlConditionalValue conditional:
                EnsureConditionNamespaces(XamlNamespaces.Presentation, conditional.Condition, root);
                CollectValueNamespaces(conditional.Value, root, visited);
                break;
        }
    }

    private void EnsureConditionNamespaces(string baseNamespace, XamlCondition? condition, XamlObjectModel root)
    {
        if (condition is null)
        {
            return;
        }

        if (condition.PredicateType.NamespaceUri != XamlNamespaces.Presentation)
        {
            EnsureNamespace(condition.PredicateType.NamespaceUri, root);
        }

        EnsureNamespace(ConditionalNamespace(baseNamespace, condition), root);
    }

    private void EnsureNamespace(string namespaceUri, XamlObjectModel root, string? preferredPrefix = null)
    {
        if (namespaceUri.Length == 0)
        {
            return;
        }

        if (_prefixes.ContainsKey(namespaceUri))
        {
            return;
        }

        if (TryReuseConditionalPrefix(namespaceUri, root))
        {
            return;
        }

        string prefix = preferredPrefix ?? namespaceUri switch
        {
            XamlNamespaces.Presentation => string.Empty,
            XamlNamespaces.Xaml => "x",
            _ => NextPrefix(),
        };
        if (root.Namespaces.Any(value => value.Prefix == prefix && value.NamespaceUri != namespaceUri))
        {
            prefix = NextPrefix();
        }

        root.Namespaces.Add(new XamlNamespaceDeclaration(prefix, namespaceUri));
        _prefixes[namespaceUri] = prefix;
    }

    private bool TryReuseConditionalPrefix(string namespaceUri, XamlObjectModel root)
    {
        int question = namespaceUri.IndexOf('?');
        if (question <= 0 || question == namespaceUri.Length - 1)
        {
            return false;
        }

        string baseNamespace = namespaceUri[..question];
        string expression = namespaceUri[(question + 1)..];
        int bestIndex = -1;
        int bestScore = 0;
        for (int index = 0; index < root.Namespaces.Count; index++)
        {
            XamlNamespaceDeclaration declaration = root.Namespaces[index];
            if (declaration.Prefix.Length == 0 || declaration.NamespaceUri != baseNamespace || _prefixes.Values.Contains(declaration.Prefix, StringComparer.Ordinal))
            {
                continue;
            }

            int score = ConditionalPrefixScore(declaration.Prefix, expression);
            if (score > bestScore)
            {
                bestIndex = index;
                bestScore = score;
            }
        }

        if (bestIndex < 0)
        {
            return false;
        }

        string prefix = root.Namespaces[bestIndex].Prefix;
        root.Namespaces[bestIndex] = new XamlNamespaceDeclaration(prefix, namespaceUri);
        _prefixes[namespaceUri] = prefix;
        return true;
    }

    private static int ConditionalPrefixScore(string prefix, string expression)
    {
        int openingParenthesis = expression.IndexOf('(');
        string predicateName = openingParenthesis < 0 ? expression : expression[..openingParenthesis];
        int colon = predicateName.LastIndexOf(':');
        if (colon >= 0)
        {
            predicateName = predicateName[(colon + 1)..];
        }

        string normalizedPrefix = NormalizeIdentifier(prefix);
        string normalizedPredicate = NormalizeIdentifier(predicateName);
        bool predicateIsNegated = normalizedPredicate.Contains("not", StringComparison.Ordinal);
        bool prefixIsNegated = normalizedPrefix.Contains("not", StringComparison.Ordinal);
        if (predicateIsNegated != prefixIsNegated)
        {
            return 0;
        }

        int score = 0;
        foreach (string word in SplitIdentifierWords(predicateName))
        {
            string normalizedWord = NormalizeIdentifier(word);
            if (normalizedWord is "is" or "api" || normalizedWord.Length < 3)
            {
                continue;
            }

            if (normalizedPrefix.Contains(normalizedWord, StringComparison.Ordinal))
            {
                score += normalizedWord == "not" ? 8 : 4;
            }
        }

        string arguments = openingParenthesis >= 0 && expression.EndsWith(')') ? expression[(openingParenthesis + 1)..^1] : string.Empty;
        List<string> argumentNumbers = ExtractNumbers(arguments);
        List<string> prefixNumbers = ExtractNumbers(prefix);
        foreach (string number in argumentNumbers)
        {
            score += prefixNumbers.Contains(number, StringComparer.Ordinal) ? 8 : -2;
        }

        if (prefixNumbers.Count > 0 && argumentNumbers.Count > 0 && !prefixNumbers.Any(number => argumentNumbers.Contains(number, StringComparer.Ordinal)))
        {
            score -= 8;
        }

        return Math.Max(score, 0);
    }

    private static IEnumerable<string> SplitIdentifierWords(string value)
    {
        int start = 0;
        for (int index = 1; index < value.Length; index++)
        {
            if (char.IsUpper(value[index]) && !char.IsUpper(value[index - 1]))
            {
                yield return value[start..index];
                start = index;
            }
        }

        if (start < value.Length)
        {
            yield return value[start..];
        }
    }

    private static List<string> ExtractNumbers(string value)
    {
        var result = new List<string>();
        int start = -1;
        for (int index = 0; index <= value.Length; index++)
        {
            if (index < value.Length && char.IsDigit(value[index]))
            {
                start = start < 0 ? index : start;
            }
            else if (start >= 0)
            {
                result.Add(value[start..index]);
                start = -1;
            }
        }

        return result;
    }

    private static string NormalizeIdentifier(string value)
    {
        return new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }

    private void WriteObject(XmlWriter writer, XamlObjectModel value, bool isRoot = false)
    {
        if (!_writeStack.Add(value))
        {
            writer.WriteComment("Cyclic XBF object reference omitted.");
            return;
        }

        string namespaceUri = value.Condition is null ? value.Type.NamespaceUri : ConditionalNamespace(value.Type.NamespaceUri, value.Condition);
        string prefix = PrefixFor(namespaceUri);
        writer.WriteStartElement(prefix, XamlValueFormatter.SafeName(value.Type.Name), namespaceUri);

        if (isRoot)
        {
            foreach (XamlNamespaceDeclaration declaration in value.Namespaces.Distinct())
            {
                if (declaration.Prefix.Length == 0)
                {
                    writer.WriteAttributeString("xmlns", declaration.NamespaceUri);
                }
                else
                {
                    writer.WriteAttributeString("xmlns", declaration.Prefix, null, declaration.NamespaceUri);
                }
            }
        }

        var complexMembers = new List<XamlMemberModel>();
        Dictionary<(string NamespaceUri, string Name), int> attributeCounts = value.Members
            .Where(CanWriteAttribute)
            .GroupBy(member => AttributeKey(member, value))
            .ToDictionary(group => group.Key, group => group.Count());
        foreach (XamlMemberModel member in value.Members)
        {
            if (CanWriteAttribute(member) && attributeCounts[AttributeKey(member, value)] == 1)
            {
                WriteAttribute(writer, value, member);
            }
            else
            {
                complexMembers.Add(member);
            }
        }

        if (value.HasInitializationValue)
        {
            WriteText(writer, XamlValueFormatter.Format(value.InitializationValue));
        }

        foreach (XamlMemberModel member in complexMembers)
        {
            WriteMember(writer, value, member);
        }

        bool formatItemsAsElementSequence = CanFormatAsElementSequence(value.Items);
        foreach (object? item in value.Items)
        {
            WriteValue(writer, item, formatItemsAsElementSequence);
        }

        writer.WriteEndElement();
        _writeStack.Remove(value);
    }

    private static bool CanWriteAttribute(XamlMemberModel member)
    {
        return !member.Property.IsImplicit && member.Values.Count == 1 && member.Values[0] is not XamlObjectModel and not XamlCollectionModel and not XamlConditionalValue;
    }

    private void WriteAttribute(XmlWriter writer, XamlObjectModel owner, XamlMemberModel member)
    {
        XamlPropertyName property = member.Property;
        string value = FormatAttributeValue(property, XamlValueFormatter.Format(member.Values[0]));
        if (property.IsDirective)
        {
            WriteAttributeValue(writer, PrefixFor(XamlNamespaces.Xaml), XamlValueFormatter.SafeName(StripPrefix(property.Name)), XamlNamespaces.Xaml, value);
        }
        else if (member.Condition is not null)
        {
            string baseNamespace = MemberNamespace(member, owner);
            string conditionalNamespace = ConditionalNamespace(baseNamespace, member.Condition);
            WriteAttributeValue(writer, PrefixFor(conditionalNamespace), XamlValueFormatter.SafeName(StripPrefix(property.Name)), conditionalNamespace, value);
        }
        else if (!string.IsNullOrEmpty(property.NamespaceUri))
        {
            WriteAttributeValue(writer, PrefixFor(property.NamespaceUri!), XamlValueFormatter.SafeName(StripPrefix(property.Name)), property.NamespaceUri, value);
        }
        else if (property.DeclaringType is not null && (property.IsAttached || property.DeclaringType.Name != owner.Type.Name))
        {
            string localName = $"{property.DeclaringType.Name}.{StripPrefix(property.Name)}";
            string declaringPrefix = PrefixFor(property.DeclaringType.NamespaceUri);
            if (property.DeclaringType.NamespaceUri == owner.Type.NamespaceUri || declaringPrefix.Length == 0)
            {
                WriteAttributeValue(writer, null, XamlValueFormatter.SafeName(localName), null, value);
            }
            else
            {
                WriteAttributeValue(writer, declaringPrefix, XamlValueFormatter.SafeName(localName), property.DeclaringType.NamespaceUri, value);
            }
        }
        else
        {
            WriteAttributeValue(writer, null, XamlValueFormatter.SafeName(StripPrefix(property.Name)), null, value);
        }
    }

    private string FormatAttributeValue(XamlPropertyName property, string value)
    {
        if (_options.ThicknessSeparator == XamlThicknessSeparator.None ||
            !_thicknessAttributes.Contains(StripPrefix(property.Name)) ||
            value.StartsWith('{'))
        {
            return value;
        }

        string[] components = value.Split([',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (components.Length is not (1 or 2 or 4) || components.Any(component =>
            !double.TryParse(component, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _)))
        {
            return value;
        }

        string separator = _options.ThicknessSeparator == XamlThicknessSeparator.Comma ? "," : " ";
        return string.Join(separator, components);
    }

    private static HashSet<string> ParseCommaSeparated(string value)
    {
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static void WriteAttributeValue(XmlWriter writer, string? prefix, string localName, string? namespaceUri, string value)
    {
        writer.WriteStartAttribute(prefix, localName, namespaceUri);
        WriteText(writer, value);
        writer.WriteEndAttribute();
    }

    private (string NamespaceUri, string Name) AttributeKey(XamlMemberModel member, XamlObjectModel owner)
    {
        XamlPropertyName property = member.Property;
        if (property.IsDirective)
        {
            return (XamlNamespaces.Xaml, StripPrefix(property.Name));
        }

        if (member.Condition is not null)
        {
            return (ConditionalNamespace(MemberNamespace(member, owner), member.Condition), StripPrefix(property.Name));
        }

        if (!string.IsNullOrEmpty(property.NamespaceUri))
        {
            return (property.NamespaceUri!, StripPrefix(property.Name));
        }

        if (property.DeclaringType is not null && (property.IsAttached || property.DeclaringType.Name != owner.Type.Name))
        {
            string namespaceUri = property.DeclaringType.NamespaceUri == owner.Type.NamespaceUri || PrefixFor(property.DeclaringType.NamespaceUri).Length == 0
                ? string.Empty
                : property.DeclaringType.NamespaceUri;
            return (namespaceUri, $"{property.DeclaringType.Name}.{StripPrefix(property.Name)}");
        }

        return (string.Empty, StripPrefix(property.Name));
    }

    private void WriteMember(XmlWriter writer, XamlObjectModel owner, XamlMemberModel member)
    {
        bool formatValuesAsElementSequence = CanFormatAsElementSequence(member.Values);
        if (member.Property.IsImplicit)
        {
            foreach (object? value in member.Values)
            {
                WriteValue(writer, value, formatValuesAsElementSequence);
            }

            return;
        }

        XamlTypeName declaringType = member.Property.DeclaringType ?? owner.Type;
        string namespaceUri = member.Property.NamespaceUri ?? declaringType.NamespaceUri;
        if (member.Condition is not null)
        {
            namespaceUri = ConditionalNamespace(namespaceUri, member.Condition);
        }
        string localName = $"{declaringType.Name}.{StripPrefix(member.Property.Name)}";
        writer.WriteStartElement(PrefixFor(namespaceUri), XamlValueFormatter.SafeName(localName), namespaceUri);
        foreach (object? value in member.Values)
        {
            WriteValue(writer, value, formatValuesAsElementSequence);
        }

        writer.WriteEndElement();
    }

    private void WriteValue(XmlWriter writer, object? value, bool omitSeparatorWhitespace = false)
    {
        switch (value)
        {
            case string text when omitSeparatorWhitespace && string.IsNullOrWhiteSpace(text):
                break;
            case XamlObjectModel child:
                WriteObject(writer, child);
                break;
            case XamlCollectionModel collection:
                foreach (object? item in collection.Items)
                {
                    WriteValue(writer, item, omitSeparatorWhitespace);
                }
                break;
            case XamlConditionalValue conditional:
                WriteConditionalValue(writer, conditional);
                break;
            default:
                WriteText(writer, XamlValueFormatter.Format(value));
                break;
        }
    }

    private static void WriteText(XmlWriter writer, string value)
    {
        int copyStart = 0;
        for (int index = 0; index < value.Length;)
        {
            char highCharacter = value[index];
            int scalarValue;
            int characterCount;
            if (char.IsHighSurrogate(highCharacter) &&
                index + 1 < value.Length &&
                char.IsLowSurrogate(value[index + 1]))
            {
                scalarValue = char.ConvertToUtf32(highCharacter, value[index + 1]);
                characterCount = 2;
            }
            else
            {
                scalarValue = highCharacter;
                characterCount = 1;
            }

            if (!IsPrivateUseCharacter(scalarValue))
            {
                index += characterCount;
                continue;
            }

            if (index > copyStart)
            {
                writer.WriteString(value[copyStart..index]);
            }

            if (characterCount == 1)
            {
                writer.WriteCharEntity(highCharacter);
            }
            else
            {
                writer.WriteSurrogateCharEntity(value[index + 1], highCharacter);
            }

            index += characterCount;
            copyStart = index;
        }

        if (copyStart < value.Length)
        {
            writer.WriteString(value[copyStart..]);
        }
    }

    private static bool IsPrivateUseCharacter(int value)
    {
        return value is >= 0xE000 and <= 0xF8FF or
            >= 0xF0000 and <= 0xFFFFD or
            >= 0x100000 and <= 0x10FFFD;
    }

    private static bool CanFormatAsElementSequence(IEnumerable<object?> values)
    {
        bool hasElement = false;
        foreach (object? value in values)
        {
            if (!IsElementSequenceValue(value, ref hasElement))
            {
                return false;
            }
        }

        return hasElement;
    }

    private static bool IsElementSequenceValue(object? value, ref bool hasElement)
    {
        switch (value)
        {
            case XamlObjectModel:
                hasElement = true;
                return true;
            case XamlConditionalValue { Value: XamlObjectModel }:
                hasElement = true;
                return true;
            case XamlCollectionModel collection:
                foreach (object? item in collection.Items)
                {
                    if (!IsElementSequenceValue(item, ref hasElement))
                    {
                        return false;
                    }
                }

                return true;
            case string text:
                return string.IsNullOrWhiteSpace(text);
            default:
                return false;
        }
    }

    private void WriteConditionalValue(XmlWriter writer, XamlConditionalValue conditional)
    {
        if (conditional.Value is XamlObjectModel xamlObject)
        {
            XamlCondition? previous = xamlObject.Condition;
            xamlObject.Condition = conditional.Condition;
            WriteObject(writer, xamlObject);
            xamlObject.Condition = previous;
            return;
        }

        writer.WriteComment($"Conditional XBF value: {conditional.Condition.PredicateType.Name}({conditional.Condition.Arguments})");
        WriteValue(writer, conditional.Value);
    }

    private string PrefixFor(string namespaceUri)
    {
        return _prefixes.TryGetValue(namespaceUri, out string? prefix) ? prefix : string.Empty;
    }

    private string NextPrefix()
    {
        string prefix;
        do
        {
            prefix = $"p{++_generatedPrefix}";
        }
        while (_prefixes.Values.Contains(prefix, StringComparer.Ordinal));
        return prefix;
    }

    private static string StripPrefix(string name)
    {
        int colon = name.IndexOf(':');
        return colon >= 0 ? name[(colon + 1)..] : name;
    }

    private static string MemberNamespace(XamlMemberModel member, XamlObjectModel owner)
    {
        return member.Property.NamespaceUri ?? member.Property.DeclaringType?.NamespaceUri ?? owner.Type.NamespaceUri;
    }

    private string ConditionalNamespace(string baseNamespace, XamlCondition condition)
    {
        string predicateName = condition.PredicateType.Name;
        if (condition.PredicateType.NamespaceUri != XamlNamespaces.Presentation)
        {
            string predicatePrefix = PrefixFor(condition.PredicateType.NamespaceUri);
            predicateName = $"{predicatePrefix}:{predicateName}";
        }

        return $"{baseNamespace}?{predicateName}({condition.Arguments})";
    }

    private static bool UsesXamlNamespace(XamlObjectModel value, HashSet<XamlObjectModel> visited)
    {
        if (!visited.Add(value))
        {
            return false;
        }

        if (value.Type.NamespaceUri == XamlNamespaces.Xaml || value.Members.Any(member => member.Property.IsDirective))
        {
            return true;
        }

        foreach (object? item in value.Members.SelectMany(member => member.Values).Concat(value.Items))
        {
            if (ValueUsesXamlNamespace(item, visited))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ValueUsesXamlNamespace(object? value, HashSet<XamlObjectModel> visited)
    {
        switch (value)
        {
            case XamlObjectModel child:
                return UsesXamlNamespace(child, visited);
            case XamlCollectionModel collection:
                foreach (object? item in collection.Items)
                {
                    if (ValueUsesXamlNamespace(item, visited))
                    {
                        return true;
                    }
                }

                return false;
            case XamlConditionalValue conditional:
                return ValueUsesXamlNamespace(conditional.Value, visited);
            default:
                return false;
        }
    }
}
