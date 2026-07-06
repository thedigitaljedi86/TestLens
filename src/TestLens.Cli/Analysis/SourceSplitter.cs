using System.Text;

namespace TestLens.Analysis;

/// <summary>
/// Splits a source file into "active code" and "comment text" so test markers
/// can be counted separately in each. Handles line comments, block comments
/// and string literals (so a "//" inside a string is not treated as a comment).
/// </summary>
public static class SourceSplitter
{
    public static (string Code, string Comments) Split(string source, bool csharpVerbatimStrings)
    {
        var code = new StringBuilder(source.Length);
        var comments = new StringBuilder();
        int i = 0;

        while (i < source.Length)
        {
            char c = source[i];
            char next = i + 1 < source.Length ? source[i + 1] : '\0';

            if (c == '/' && next == '/')
            {
                int end = source.IndexOf('\n', i);
                if (end < 0) end = source.Length;
                comments.Append(source, i + 2, end - i - 2).Append('\n');
                i = end;
            }
            else if (c == '/' && next == '*')
            {
                int end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0) end = source.Length - 2;
                comments.Append(source, i + 2, end - i - 2).Append('\n');
                i = end + 2;
            }
            else if (csharpVerbatimStrings && c == '@' && next == '"')
            {
                // C# verbatim string: "" is an escaped quote.
                code.Append('"');
                i += 2;
                while (i < source.Length)
                {
                    if (source[i] == '"')
                    {
                        if (i + 1 < source.Length && source[i + 1] == '"') { i += 2; continue; }
                        i++;
                        break;
                    }
                    i++;
                }
                code.Append('"');
            }
            else if (c == '"' || c == '\'' || (!csharpVerbatimStrings && c == '`'))
            {
                char quote = c;
                code.Append(quote);
                i++;
                while (i < source.Length && source[i] != quote)
                {
                    if (source[i] == '\\') i++;
                    if (quote != '`' && i < source.Length && source[i] == '\n') break;
                    i++;
                }
                if (i < source.Length) { code.Append(quote); i++; }
            }
            else
            {
                code.Append(c);
                i++;
            }
        }

        return (code.ToString(), comments.ToString());
    }
}
