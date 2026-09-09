using System.Text;

namespace XbfKit.Xaml;

/// <summary>Applies configurable, source-only layout rules to serialized XAML.</summary>
internal sealed class XamlTextFormatter
{
    private readonly XbfDecompilationOptions _options;
    private readonly HashSet<string> _newlineExemptions;
    private readonly HashSet<string> _noNewLineMarkupExtensions;
    private readonly List<string[]> _orderingGroups;
    private readonly string[] _firstLineAttributeRules;

    /// <summary>Initializes a formatter from decompilation options.</summary>
    internal XamlTextFormatter(XbfDecompilationOptions options)
    {
        _options = options;
        _newlineExemptions = ParseCommaSeparated(options.NewlineExemptionElements);
        _noNewLineMarkupExtensions = ParseCommaSeparated(options.NoNewLineMarkupExtensions);
        _orderingGroups = [.. options.AttributeOrderingRuleGroups.Select(group => SplitCommaSeparated(group)).Where(group => group.Length > 0)];
        _firstLineAttributeRules = SplitCommaSeparated(options.FirstLineAttributes);
    }

    /// <summary>Formats a complete serialized XAML document.</summary>
    internal string Format(string xaml)
    {
        var result = new StringBuilder(xaml.Length);
        int copyStart = 0;
        int searchStart = 0;
        bool foundRoot = false;
        while (searchStart < xaml.Length)
        {
            int tagStart = xaml.IndexOf('<', searchStart);
            if (tagStart < 0)
            {
                break;
            }

            int tagEnd = FindTagEnd(xaml, tagStart + 1);
            if (tagEnd < 0)
            {
                break;
            }

            if (IsStartTag(xaml, tagStart, tagEnd))
            {
                result.Append(xaml, copyStart, tagStart - copyStart);
                AppendStartTag(result, xaml.AsSpan(tagStart, tagEnd - tagStart + 1), IndentationAt(xaml, tagStart), isRoot: !foundRoot);
                copyStart = tagEnd + 1;
                foundRoot = true;
            }

            searchStart = tagEnd + 1;
        }

        result.Append(xaml, copyStart, xaml.Length - copyStart);
        return FormatComments(result.ToString());
    }

    private void AppendStartTag(StringBuilder result, ReadOnlySpan<char> tag, string indentation, bool isRoot)
    {
        TagInfo info = ParseTag(tag);
        List<AttributeInfo> attributes = OrderAttributes(info.Attributes);
        bool wrap = ShouldWrap(info.ElementName, attributes.Count, isRoot);
        if (!wrap)
        {
            AppendElementAndAttributes(result, info.ElementName, attributes);
            AppendTagEnding(result, info.ElementName, info.IsSelfClosing, onSeparateLine: false, indentation);
            return;
        }

        var firstLineAttributes = new List<AttributeInfo>();
        var remainingAttributes = new List<AttributeInfo>();
        foreach (AttributeInfo attribute in attributes)
        {
            if (_options.EnableAttributeReordering && MatchesAny(_firstLineAttributeRules, attribute.Name))
            {
                firstLineAttributes.Add(attribute);
            }
            else
            {
                remainingAttributes.Add(attribute);
            }
        }

        if (_options.KeepFirstAttributeOnSameLine && firstLineAttributes.Count == 0 && remainingAttributes.Count > 0)
        {
            firstLineAttributes.Add(remainingAttributes[0]);
            remainingAttributes.RemoveAt(0);
        }

        AppendElementAndAttributes(result, info.ElementName, firstLineAttributes);
        string attributeIndentation = GetAttributeIndentation(indentation);
        foreach (List<AttributeInfo> line in BuildAttributeLines(remainingAttributes))
        {
            result.Append(Environment.NewLine);
            result.Append(attributeIndentation);
            for (int index = 0; index < line.Count; index++)
            {
                if (index > 0)
                {
                    result.Append(' ');
                }

                result.Append(line[index].RawText);
            }
        }

        bool onSeparateLine = _options.PutEndingBracketOnNewLine && remainingAttributes.Count > 0;
        AppendTagEnding(result, info.ElementName, info.IsSelfClosing, onSeparateLine, indentation);
    }

    private static void AppendElementAndAttributes(StringBuilder result, string elementName, IReadOnlyList<AttributeInfo> attributes)
    {
        result.Append('<');
        result.Append(elementName);
        foreach (AttributeInfo attribute in attributes)
        {
            result.Append(' ');
            result.Append(attribute.RawText);
        }
    }

    private void AppendTagEnding(StringBuilder result, string elementName, bool isSelfClosing, bool onSeparateLine, string indentation)
    {
        if (onSeparateLine)
        {
            result.Append(Environment.NewLine);
            result.Append(indentation);
        }

        if (!isSelfClosing)
        {
            result.Append('>');
            return;
        }

        if (_options.RemoveEndingTagOfEmptyElement)
        {
            if (!onSeparateLine && _options.SpaceBeforeClosingSlash)
            {
                result.Append(' ');
            }

            result.Append("/>");
            return;
        }

        result.Append('>');
        result.Append("</");
        result.Append(elementName);
        result.Append('>');
    }

    private List<AttributeInfo> OrderAttributes(IReadOnlyList<AttributeInfo> attributes)
    {
        if (!_options.EnableAttributeReordering || attributes.Count < 2)
        {
            return [.. attributes];
        }

        return [.. attributes
            .Select(attribute => (Attribute: attribute, Rank: GetOrderingRank(attribute.Name)))
            .OrderBy(value => value.Rank.Group)
            .ThenBy(value => value.Rank.Rule)
            .ThenBy(value => _options.OrderAttributesByName ? value.Attribute.Name : string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.Attribute.OriginalIndex)
            .Select(value => value.Attribute)];
    }

    private List<List<AttributeInfo>> BuildAttributeLines(IReadOnlyList<AttributeInfo> attributes)
    {
        var lines = new List<List<AttributeInfo>>();
        var currentLine = new List<AttributeInfo>();
        int currentCharacters = 0;
        int previousGroup = -1;
        bool previousWasIsolated = false;
        foreach (AttributeInfo attribute in attributes)
        {
            int group = GetOrderingRank(attribute.Name).Group;
            bool isolated = IsIsolatedMarkupExtension(attribute.Value);
            bool exceedsCount = _options.MaxAttributesPerLine > 0 && currentLine.Count >= _options.MaxAttributesPerLine;
            bool exceedsCharacters = _options.MaxAttributeCharactersPerLine > 0 && currentLine.Count > 0 &&
                currentCharacters + 1 + attribute.RawText.Length > _options.MaxAttributeCharactersPerLine;
            bool changesGroup = _options.SeparateByGroups && currentLine.Count > 0 && group != previousGroup;
            if (currentLine.Count > 0 && (exceedsCount || exceedsCharacters || changesGroup || isolated || previousWasIsolated))
            {
                lines.Add(currentLine);
                currentLine = [];
                currentCharacters = 0;
            }

            currentLine.Add(attribute);
            currentCharacters += (currentLine.Count > 1 ? 1 : 0) + attribute.RawText.Length;
            previousGroup = group;
            previousWasIsolated = isolated;
        }

        if (currentLine.Count > 0)
        {
            lines.Add(currentLine);
        }

        return lines;
    }

    private bool ShouldWrap(string elementName, int attributeCount, bool isRoot)
    {
        if (attributeCount == 0)
        {
            return false;
        }

        if (isRoot)
        {
            if (_options.RootElementLineBreakRule == XamlLineBreakRule.Always)
            {
                return true;
            }

            if (_options.RootElementLineBreakRule == XamlLineBreakRule.Never)
            {
                return false;
            }
        }

        string localName = StripPrefix(elementName);
        if (_newlineExemptions.Contains(localName))
        {
            return false;
        }

        return _options.AttributesTolerance < 1 || attributeCount > _options.AttributesTolerance;
    }

    private string GetAttributeIndentation(string elementIndentation)
    {
        int indentSize = Math.Max(_options.IndentSize, 1);
        int additionalSpaces = _options.AttributeIndentation > 0 ? _options.AttributeIndentation : indentSize;
        if (!_options.IndentWithTabs)
        {
            return elementIndentation + new string(' ', additionalSpaces);
        }

        if (_options.AttributeIndentationStyle == XamlAttributeIndentationStyle.Mixed)
        {
            return elementIndentation + new string('\t', additionalSpaces / indentSize) + new string(' ', additionalSpaces % indentSize);
        }

        return elementIndentation + new string(' ', additionalSpaces);
    }

    private bool IsIsolatedMarkupExtension(string value)
    {
        if (!_options.FormatMarkupExtension || value.Length < 2 || value[0] != '{' || value.StartsWith("{}", StringComparison.Ordinal))
        {
            return false;
        }

        int start = 1;
        while (start < value.Length && char.IsWhiteSpace(value[start]))
        {
            start++;
        }

        int end = start;
        while (end < value.Length && !char.IsWhiteSpace(value[end]) && value[end] is not ',' and not '}')
        {
            end++;
        }

        return end > start && !_noNewLineMarkupExtensions.Contains(value[start..end]);
    }

    private (int Group, int Rule) GetOrderingRank(string attributeName)
    {
        for (int groupIndex = 0; groupIndex < _orderingGroups.Count; groupIndex++)
        {
            string[] group = _orderingGroups[groupIndex];
            for (int ruleIndex = 0; ruleIndex < group.Length; ruleIndex++)
            {
                if (!group[ruleIndex].Contains('*') && string.Equals(group[ruleIndex], attributeName, StringComparison.OrdinalIgnoreCase))
                {
                    return (groupIndex, ruleIndex);
                }
            }
        }

        (int Group, int Rule, int Specificity)? best = null;
        for (int groupIndex = 0; groupIndex < _orderingGroups.Count; groupIndex++)
        {
            string[] group = _orderingGroups[groupIndex];
            for (int ruleIndex = 0; ruleIndex < group.Length; ruleIndex++)
            {
                string rule = group[ruleIndex];
                if (!rule.Contains('*') || !WildcardMatch(rule, attributeName))
                {
                    continue;
                }

                int specificity = rule.Count(character => character != '*');
                if (best is null || specificity > best.Value.Specificity)
                {
                    best = (groupIndex, ruleIndex, specificity);
                }
            }
        }

        return best is { } match ? (match.Group, match.Rule) : (int.MaxValue, int.MaxValue);
    }

    private string FormatComments(string xaml)
    {
        int padding = Math.Max(_options.CommentPadding, 0);
        string spaces = new(' ', padding);
        var result = new StringBuilder(xaml.Length);
        int copyStart = 0;
        while (copyStart < xaml.Length)
        {
            int start = xaml.IndexOf("<!--", copyStart, StringComparison.Ordinal);
            if (start < 0)
            {
                break;
            }

            int end = xaml.IndexOf("-->", start + 4, StringComparison.Ordinal);
            if (end < 0)
            {
                break;
            }

            result.Append(xaml, copyStart, start - copyStart);
            result.Append("<!--");
            result.Append(spaces);
            result.Append(xaml.AsSpan(start + 4, end - start - 4).Trim());
            result.Append(spaces);
            result.Append("-->");
            copyStart = end + 3;
        }

        result.Append(xaml, copyStart, xaml.Length - copyStart);
        return result.ToString();
    }

    private static TagInfo ParseTag(ReadOnlySpan<char> tag)
    {
        int index = 1;
        int nameStart = index;
        while (index < tag.Length && !char.IsWhiteSpace(tag[index]) && tag[index] is not '/' and not '>')
        {
            index++;
        }

        string elementName = tag[nameStart..index].ToString();
        var attributes = new List<AttributeInfo>();
        while (index < tag.Length)
        {
            while (index < tag.Length && char.IsWhiteSpace(tag[index]))
            {
                index++;
            }

            if (index >= tag.Length || tag[index] is '/' or '>')
            {
                break;
            }

            int attributeStart = index;
            while (index < tag.Length && !char.IsWhiteSpace(tag[index]) && tag[index] is not '=' and not '/' and not '>')
            {
                index++;
            }

            string name = tag[attributeStart..index].ToString();
            while (index < tag.Length && char.IsWhiteSpace(tag[index]))
            {
                index++;
            }

            if (index >= tag.Length || tag[index] != '=')
            {
                break;
            }

            index++;
            while (index < tag.Length && char.IsWhiteSpace(tag[index]))
            {
                index++;
            }

            if (index >= tag.Length || tag[index] is not ('\'' or '"'))
            {
                break;
            }

            char quote = tag[index++];
            int valueStart = index;
            while (index < tag.Length && tag[index] != quote)
            {
                index++;
            }

            if (index >= tag.Length)
            {
                break;
            }

            string value = tag[valueStart..index].ToString();
            index++;
            attributes.Add(new AttributeInfo(name, value, tag[attributeStart..index].ToString(), attributes.Count));
        }

        int closingIndex = tag.Length - 2;
        while (closingIndex >= 0 && char.IsWhiteSpace(tag[closingIndex]))
        {
            closingIndex--;
        }

        return new TagInfo(elementName, attributes, closingIndex >= 0 && tag[closingIndex] == '/');
    }

    private static int FindTagEnd(string xaml, int start)
    {
        char quote = '\0';
        for (int index = start; index < xaml.Length; index++)
        {
            char current = xaml[index];
            if (quote != '\0')
            {
                if (current == quote)
                {
                    quote = '\0';
                }
            }
            else if (current is '\'' or '"')
            {
                quote = current;
            }
            else if (current == '>')
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsStartTag(string xaml, int start, int end)
    {
        return end > start + 1 && xaml[start + 1] is not '/' and not '!' and not '?';
    }

    private static string IndentationAt(string xaml, int position)
    {
        int lineStart = position > 0 ? xaml.LastIndexOf('\n', position - 1) + 1 : 0;
        int index = lineStart;
        while (index < position && xaml[index] is ' ' or '\t')
        {
            index++;
        }

        return index == position ? xaml[lineStart..position] : string.Empty;
    }

    private static HashSet<string> ParseCommaSeparated(string value)
    {
        return SplitCommaSeparated(value).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string[] SplitCommaSeparated(string value)
    {
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool MatchesAny(IEnumerable<string> patterns, string value)
    {
        return patterns.Any(pattern => WildcardMatch(pattern, value));
    }

    private static bool WildcardMatch(string pattern, string value)
    {
        int patternIndex = 0;
        int valueIndex = 0;
        int starIndex = -1;
        int starValueIndex = -1;
        while (valueIndex < value.Length)
        {
            if (patternIndex < pattern.Length && pattern[patternIndex] != '*' &&
                char.ToUpperInvariant(pattern[patternIndex]) == char.ToUpperInvariant(value[valueIndex]))
            {
                patternIndex++;
                valueIndex++;
            }
            else if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                starIndex = patternIndex++;
                starValueIndex = valueIndex;
            }
            else if (starIndex >= 0)
            {
                patternIndex = starIndex + 1;
                valueIndex = ++starValueIndex;
            }
            else
            {
                return false;
            }
        }

        while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
        {
            patternIndex++;
        }

        return patternIndex == pattern.Length;
    }

    private static string StripPrefix(string name)
    {
        int separator = name.IndexOf(':');
        return separator < 0 ? name : name[(separator + 1)..];
    }

    private sealed record AttributeInfo(string Name, string Value, string RawText, int OriginalIndex);

    private sealed record TagInfo(string ElementName, List<AttributeInfo> Attributes, bool IsSelfClosing);
}
