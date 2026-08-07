using System.Text;

namespace NutManager.Infrastructure.NutProtocol;

internal static class NutProtocolTokenizer
{
    public static IReadOnlyList<string> Tokenize(string line)
    {
        var tokens = new List<string>();
        var index = 0;

        while (index < line.Length)
        {
            SkipWhitespace(line, ref index);
            if (index >= line.Length)
            {
                break;
            }

            tokens.Add(line[index] == '"'
                ? ReadQuotedToken(line, ref index)
                : ReadSimpleToken(line, ref index));
        }

        return tokens;
    }

    public static string QuoteArgument(string value)
    {
        if (value.IndexOfAny(['\r', '\n']) >= 0)
        {
            throw new ArgumentException("The NUT argument must not contain a line break.", nameof(value));
        }

        return $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private static void SkipWhitespace(string line, ref int index)
    {
        while (index < line.Length && char.IsWhiteSpace(line[index]))
        {
            index++;
        }
    }

    private static string ReadSimpleToken(string line, ref int index)
    {
        var start = index;
        while (index < line.Length && !char.IsWhiteSpace(line[index]))
        {
            if (line[index] == '"')
            {
                throw new NutProtocolException("Unexpected quote in a simple NUT token.");
            }

            index++;
        }

        return line[start..index];
    }

    private static string ReadQuotedToken(string line, ref int index)
    {
        var value = new StringBuilder();
        index++;

        while (index < line.Length)
        {
            var character = line[index++];
            if (character == '"')
            {
                if (index < line.Length && !char.IsWhiteSpace(line[index]))
                {
                    throw new NutProtocolException("Unexpected text after a quoted NUT token.");
                }

                return value.ToString();
            }

            if (character != '\\')
            {
                value.Append(character);
                continue;
            }

            if (index >= line.Length)
            {
                throw new NutProtocolException("Unterminated escape sequence in a NUT token.");
            }

            var escaped = line[index++];
            if (escaped is not ('"' or '\\'))
            {
                throw new NutProtocolException("Invalid escape sequence in a NUT token.");
            }

            value.Append(escaped);
        }

        throw new NutProtocolException("Unterminated quoted NUT token.");
    }
}
