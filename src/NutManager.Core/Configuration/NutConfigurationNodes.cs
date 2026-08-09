namespace NutManager.Core.Configuration;

public abstract class NutConfigurationNode
{
    protected NutConfigurationNode(string rawText, string lineEnding)
    {
        RawText = rawText;
        LineEnding = lineEnding;
    }

    public string RawText { get; }

    public string LineEnding { get; }

    public bool IsModified { get; protected set; }

    internal string Serialize() => (IsModified ? RenderModifiedText() : RawText) + LineEnding;

    protected virtual string RenderModifiedText() => RawText;
}

public sealed class NutRawNode : NutConfigurationNode
{
    internal NutRawNode(string rawText, string lineEnding)
        : base(rawText, lineEnding)
    {
    }
}

public sealed class NutSectionNode : NutConfigurationNode
{
    internal NutSectionNode(string rawText, string lineEnding, string name)
        : base(rawText, lineEnding)
    {
        Name = name;
    }

    public string Name { get; }
}

public sealed class NutConfigurationAssignmentNode : NutConfigurationNode
{
    private readonly string _beforeValue;
    private readonly string _trailingWhitespace;
    private readonly char? _quoteCharacter;

    internal NutConfigurationAssignmentNode(
        string rawText,
        string lineEnding,
        string name,
        string rawValue,
        string beforeValue,
        string trailingWhitespace,
        char? quoteCharacter,
        string? sectionName,
        bool isSensitive)
        : base(rawText, lineEnding)
    {
        Name = name;
        RawValue = rawValue;
        Value = DecodeValue(rawValue, quoteCharacter);
        _beforeValue = beforeValue;
        _trailingWhitespace = trailingWhitespace;
        _quoteCharacter = quoteCharacter;
        SectionName = sectionName;
        IsSensitive = isSensitive;
    }

    public string Name { get; }

    /// <summary>
    /// Gets the lexical value token currently represented by this assignment, including
    /// any outer quote delimiters and escape sequences.
    /// </summary>
    public string RawValue { get; private set; }

    /// <summary>
    /// Gets the editable value without outer quote delimiters. For quoted values,
    /// backslash escapes for a backslash or the matching quote delimiter are decoded.
    /// </summary>
    public string Value { get; private set; }

    public string? SectionName { get; }

    public bool IsSensitive { get; }

    /// <summary>
    /// Replaces only this assignment's value in memory. Existing indentation,
    /// spacing around the equals sign, trailing whitespace, and quote style remain intact.
    /// </summary>
    public void SetValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (string.Equals(Value, value, StringComparison.Ordinal))
        {
            return;
        }

        Value = value;
        RawValue = RenderValue(value);
        IsModified = true;
    }

    protected override string RenderModifiedText() => _beforeValue + RawValue + _trailingWhitespace;

    private static string DecodeValue(string rawValue, char? quoteCharacter)
    {
        if (quoteCharacter is not { } quote)
        {
            return rawValue;
        }

        var value = rawValue[1..^1];
        var decoded = new System.Text.StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '\\' && index + 1 < value.Length &&
                (value[index + 1] == '\\' || value[index + 1] == quote))
            {
                decoded.Append(value[++index]);
                continue;
            }

            decoded.Append(value[index]);
        }

        return decoded.ToString();
    }

    private string RenderValue(string value)
    {
        if (_quoteCharacter is not { } quote)
        {
            return value;
        }

        var escaped = value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace(quote.ToString(), $"\\{quote}", StringComparison.Ordinal);
        return $"{quote}{escaped}{quote}";
    }
}

public sealed class NutConfigurationDirectiveNode : NutConfigurationNode
{
    private readonly string _prefix;
    private readonly string _separator;
    private readonly string _trailingWhitespace;

    internal NutConfigurationDirectiveNode(
        string rawText,
        string lineEnding,
        string name,
        string arguments,
        string prefix,
        string separator,
        string trailingWhitespace,
        string? sectionName,
        bool isSensitive)
        : base(rawText, lineEnding)
    {
        Name = name;
        Arguments = arguments;
        _prefix = prefix;
        _separator = separator;
        _trailingWhitespace = trailingWhitespace;
        SectionName = sectionName;
        IsSensitive = isSensitive;
    }

    public string Name { get; }

    public string Arguments { get; private set; }

    public string? SectionName { get; }

    public bool IsSensitive { get; }

    /// <summary>
    /// Replaces only this directive's argument text in memory while retaining its leading spacing.
    /// </summary>
    public void SetArguments(string arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        Arguments = arguments;
        IsModified = true;
    }

    protected override string RenderModifiedText()
    {
        var separator = _separator.Length == 0 && Arguments.Length > 0 ? " " : _separator;
        return _prefix + Name + separator + Arguments + _trailingWhitespace;
    }
}
