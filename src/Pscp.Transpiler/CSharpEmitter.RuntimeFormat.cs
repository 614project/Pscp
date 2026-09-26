namespace Pscp.Transpiler;

// Layout of the generated runtime helper blocks (compact or pretty).
internal sealed partial class CSharpEmitter
{
    private void WriteRuntimeBlock(string text)
    {
        string formatted = _options.Pretty
            ? FormatRuntimeBlockPretty(text)
            : FormatRuntimeBlockCompact(text);

        foreach (string line in formatted.Replace("\r", string.Empty).Split('\n'))
        {
            _writer.WriteLine(line);
        }
    }

    private static string FormatRuntimeBlockPretty(string text)
    {
        string[] lines = NormalizeRuntimeBlockLines(text);
        List<string> output = [];

        foreach (string rawLine in lines)
        {
            string line = rawLine.TrimEnd();
            string trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                if (output.Count > 0 && output[^1].Length > 0)
                {
                    output.Add(string.Empty);
                }

                continue;
            }

            if (trimmed == "{" && TryAttachOpeningBrace(output))
            {
                continue;
            }

            output.Add(line);
        }

        while (output.Count > 0 && output[0].Length == 0)
        {
            output.RemoveAt(0);
        }

        while (output.Count > 0 && output[^1].Length == 0)
        {
            output.RemoveAt(output.Count - 1);
        }

        return string.Join("\n", output);
    }

    private static bool TryAttachOpeningBrace(List<string> output)
    {
        for (int i = output.Count - 1; i >= 0; i--)
        {
            string previous = output[i].TrimEnd();
            if (previous.Length == 0)
            {
                continue;
            }

            string trimmed = previous.TrimStart();
            if (trimmed.StartsWith("#", StringComparison.Ordinal)
                || previous.EndsWith("{", StringComparison.Ordinal)
                || previous.EndsWith(";", StringComparison.Ordinal)
                || previous.EndsWith(",", StringComparison.Ordinal))
            {
                return false;
            }

            output[i] = previous + " {";
            return true;
        }

        return false;
    }

    private static string FormatRuntimeBlockCompact(string text)
    {
        string[] lines = NormalizeRuntimeBlockLines(text);
        System.Text.StringBuilder output = new();
        System.Text.StringBuilder codeLine = new();

        void FlushCodeLine()
        {
            if (codeLine.Length == 0)
            {
                return;
            }

            output.AppendLine(codeLine.ToString());
            codeLine.Clear();
        }

        foreach (string rawLine in lines)
        {
            string trimmed = rawLine.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (IsPreprocessorDirective(trimmed))
            {
                FlushCodeLine();
                output.AppendLine(trimmed);
                continue;
            }

            string segment = MinifyRuntimeCodeLine(trimmed);
            if (segment.Length == 0)
            {
                continue;
            }

            if (NeedsMinifiedSpace(codeLine, segment[0]))
            {
                codeLine.Append(' ');
            }

            codeLine.Append(segment);
        }

        FlushCodeLine();
        return output.ToString().TrimEnd('\n');
    }

    private static string[] NormalizeRuntimeBlockLines(string text)
        => text.Replace("\r", string.Empty).Trim('\n').Split('\n');

    private static bool IsPreprocessorDirective(string trimmedLine)
        => trimmedLine.StartsWith("#", StringComparison.Ordinal);

    private static string MinifyRuntimeCodeLine(string line)
    {
        System.Text.StringBuilder builder = new(line.Length);
        bool pendingSpace = false;
        bool inString = false;
        bool inVerbatimString = false;
        bool inChar = false;
        bool escaped = false;

        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];

            if (inString)
            {
                builder.Append(ch);
                if (inVerbatimString)
                {
                    if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        builder.Append(line[++i]);
                    }
                    else if (ch == '"')
                    {
                        inString = false;
                        inVerbatimString = false;
                    }
                }
                else if (escaped)
                {
                    escaped = false;
                }
                else if (ch == '\\')
                {
                    escaped = true;
                }
                else if (ch == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (inChar)
            {
                builder.Append(ch);
                if (escaped)
                {
                    escaped = false;
                }
                else if (ch == '\\')
                {
                    escaped = true;
                }
                else if (ch == '\'')
                {
                    inChar = false;
                }

                continue;
            }

            if (ch == '/' && i + 1 < line.Length && line[i + 1] == '/')
            {
                break;
            }

            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace && NeedsMinifiedSpace(builder, ch))
            {
                builder.Append(' ');
            }

            pendingSpace = false;
            builder.Append(ch);

            if (ch == '"')
            {
                inString = true;
                inVerbatimString = builder.Length >= 2 && builder[^2] == '@';
                escaped = false;
            }
            else if (ch == '\'')
            {
                inChar = true;
                escaped = false;
            }
        }

        return builder.ToString();
    }

    private static bool NeedsMinifiedSpace(char left, char right)
        => IsIdentifierPart(left) && IsIdentifierPart(right)
            || IsIdentifierPart(right) && left is '>' or ']' or ')' or '?';

    private static bool NeedsMinifiedSpace(System.Text.StringBuilder builder, char right)
    {
        if (builder.Length == 0)
        {
            return false;
        }

        char left = builder[^1];
        if (left == '>' && builder.Length >= 2 && builder[^2] == '=')
        {
            return false;
        }

        if (left == '?' && builder.Length >= 2 && builder[^2] == '?')
        {
            return false;
        }

        return NeedsMinifiedSpace(left, right);
    }

    private static bool IsIdentifierPart(char ch)
        => char.IsLetterOrDigit(ch) || ch == '_' || ch == '@';

}
