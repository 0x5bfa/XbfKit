namespace XbfKit.Xaml;

/// <summary>Represents a parsed markup extension and its arguments.</summary>
/// <param name="TypeName">The qualified markup-extension type name.</param>
/// <param name="Arguments">The ordered positional and named arguments.</param>
internal sealed record XamlMarkupExtension(string TypeName, IReadOnlyList<XamlMarkupExtensionArgument> Arguments);

/// <summary>Represents one positional or named markup-extension argument.</summary>
/// <param name="Name">The optional named-argument property name.</param>
/// <param name="Value">The unescaped argument text.</param>
/// <param name="Extension">The parsed nested markup extension, when present.</param>
internal sealed record XamlMarkupExtensionArgument(string? Name, string Value, XamlMarkupExtension? Extension);

/// <summary>Parses the brace syntax used by XAML markup extensions.</summary>
internal static class XamlMarkupExtensionParser
{
    /// <summary>Attempts to parse a complete markup-extension expression.</summary>
    internal static bool TryParse(string text, out XamlMarkupExtension? extension)
    {
        extension = null;
        string value = text.Trim();
        if (value.StartsWith("{}", StringComparison.Ordinal) || value.Length < 3 || value[0] != '{' || value[^1] != '}')
        {
            return false;
        }

        string body = value[1..^1].Trim();
        int typeEnd = 0;
        while (typeEnd < body.Length && !char.IsWhiteSpace(body[typeEnd]) && body[typeEnd] != ',')
        {
            typeEnd++;
        }

        if (typeEnd == 0)
        {
            return false;
        }

        string typeName = body[..typeEnd];
        string argumentsText = body[typeEnd..].TrimStart(' ', '\t', ',');
        var arguments = new List<XamlMarkupExtensionArgument>();
        foreach (string item in SplitTopLevel(argumentsText))
        {
            string argument = item.Trim();
            if (argument.Length == 0)
            {
                continue;
            }

            int equals = FindTopLevelEquals(argument);
            string? name = equals < 0 ? null : argument[..equals].Trim();
            string argumentValue = equals < 0 ? argument : argument[(equals + 1)..].Trim();
            argumentValue = Unquote(argumentValue);
            TryParse(argumentValue, out XamlMarkupExtension? nested);
            arguments.Add(new XamlMarkupExtensionArgument(name, argumentValue, nested));
        }

        extension = new XamlMarkupExtension(typeName, arguments);
        return true;
    }

    private static IReadOnlyList<string> SplitTopLevel(string value)
    {
        var result = new List<string>();
        int start = 0;
        int braces = 0;
        char quote = '\0';
        for (int index = 0; index < value.Length; index++)
        {
            char current = value[index];
            if (quote != '\0')
            {
                if (current == quote && (index == 0 || value[index - 1] != '\\'))
                {
                    quote = '\0';
                }

                continue;
            }

            if (current is '\'' or '"')
            {
                quote = current;
            }
            else if (current == '{')
            {
                braces++;
            }
            else if (current == '}')
            {
                braces--;
            }
            else if (current == ',' && braces == 0)
            {
                result.Add(value[start..index]);
                start = index + 1;
            }
        }

        if (quote != '\0' || braces != 0)
        {
            throw new InvalidDataException("A XAML markup extension has unbalanced quotes or braces.");
        }

        result.Add(value[start..]);
        return result;
    }

    private static int FindTopLevelEquals(string value)
    {
        int braces = 0;
        char quote = '\0';
        for (int index = 0; index < value.Length; index++)
        {
            char current = value[index];
            if (quote != '\0')
            {
                if (current == quote && (index == 0 || value[index - 1] != '\\'))
                {
                    quote = '\0';
                }

                continue;
            }

            if (current is '\'' or '"')
            {
                quote = current;
            }
            else if (current == '{')
            {
                braces++;
            }
            else if (current == '}')
            {
                braces--;
            }
            else if (current == '=' && braces == 0)
            {
                return index;
            }
        }

        return -1;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }

        return value;
    }
}
