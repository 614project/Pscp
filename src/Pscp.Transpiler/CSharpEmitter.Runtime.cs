using System.Text.RegularExpressions;

namespace Pscp.Transpiler;

// Runtime helpers appended to the generated program. Each helper class is a template whose members are kept
// only when the program, or another kept member, refers to them by name (spec §31).
internal sealed partial class CSharpEmitter
{
    private static readonly Regex RuntimeIdentifierPattern = new(@"[A-Za-z_][A-Za-z0-9_]*", RegexOptions.CultureInvariant);

    partial void EmitRuntimeHelpers()
    {
        string programText = _writer.ToString();
        bool verbose = _options.HelperEmission == HelperEmissionMode.Verbose;
        List<string> blocks = [];
        string usedText = programText;

        void Add(string block)
        {
            blocks.Add(block);
            usedText += "\n" + block;
        }

        if (verbose || programText.Contains("__PscpArray.", StringComparison.Ordinal))
        {
            Add(PruneRuntimeClass(ArrayHelpersTemplate, name => verbose || programText.Contains("__PscpArray." + name, StringComparison.Ordinal)));
        }

        if (verbose || programText.Contains("__PscpCollection.", StringComparison.Ordinal))
        {
            Add(CollectionHelpersTemplate);
        }

        if (verbose || programText.Contains("__PscpThunk.run(", StringComparison.Ordinal))
        {
            Add(ThunkHelpersTemplate);
        }

        if (_emitStdin || verbose)
        {
            Add(PruneRuntimeClass(StdinTemplate, name => verbose || programText.Contains($"{PscpBinder.StdinName}.{name}(", StringComparison.Ordinal)));
        }

        if (_emitStdout || verbose)
        {
            Add(PruneRuntimeClass(StdoutTemplate, name => verbose || programText.Contains($"{PscpBinder.StdoutName}.{name}(", StringComparison.Ordinal)));
        }

        // The sequence helpers are extension methods, so a member call such as `xs.sort()` also uses them.
        string sequenceText = usedText;
        HashSet<string> sequenceHelpers = GetRuntimeClassMemberNames(SequenceHelpersTemplate);
        if (verbose || sequenceHelpers.Any(name => UsesSequenceHelper(sequenceText, name)))
        {
            Add(PruneRuntimeClass(SequenceHelpersTemplate, name => verbose || UsesSequenceHelper(sequenceText, name)));
        }

        string renderText = usedText;
        if (verbose || renderText.Contains("__PscpRender.", StringComparison.Ordinal))
        {
            Add(PruneRuntimeClass(RenderTemplate, name => verbose || renderText.Contains("__PscpRender." + name, StringComparison.Ordinal)));
        }

        if (verbose || usedText.Contains("__PscpOrder", StringComparison.Ordinal))
        {
            Add(OrderTemplate);
        }

        for (int i = 0; i < blocks.Count; i++)
        {
            if (i > 0)
            {
                _writer.WriteLine();
            }

            WriteRuntimeBlock(blocks[i]);
        }
    }

    private static bool UsesSequenceHelper(string text, string name)
        => text.Contains("__PscpSeq." + name + "(", StringComparison.Ordinal)
            || text.Contains("." + name + "(", StringComparison.Ordinal);

    // The class header, the kept members and the closing brace. Members are separated at brace depth 1; a member
    // is kept when `isRoot` accepts its name or a kept member refers to it (constructors and fields included).
    private static string PruneRuntimeClass(string template, Func<string, bool> isRoot)
    {
        (string header, List<(string Name, string Text)> members, string footer) = SplitRuntimeClass(template);
        HashSet<string> names = members.Select(member => member.Name).Where(name => name.Length > 0).ToHashSet(StringComparer.Ordinal);
        string className = Regex.Match(header, @"(?:class|struct)\s+([A-Za-z_][A-Za-z0-9_]*)").Groups[1].Value;
        HashSet<string> kept = new(StringComparer.Ordinal);
        Queue<string> pending = new();
        foreach (string name in names)
        {
            if (name == className || isRoot(name))
            {
                kept.Add(name);
                pending.Enqueue(name);
            }
        }

        while (pending.Count > 0)
        {
            string current = pending.Dequeue();
            foreach ((string name, string text) in members)
            {
                if (name != current)
                {
                    continue;
                }

                foreach (Match identifier in RuntimeIdentifierPattern.Matches(StripRuntimeLiterals(text)))
                {
                    if (names.Contains(identifier.Value) && kept.Add(identifier.Value))
                    {
                        pending.Enqueue(identifier.Value);
                    }
                }
            }
        }

        System.Text.StringBuilder builder = new();
        builder.Append(header);
        foreach ((string name, string text) in members)
        {
            if (name.Length == 0 || kept.Contains(name))
            {
                builder.Append(text);
            }
        }

        builder.Append(footer);
        return builder.ToString();
    }

    private static HashSet<string> GetRuntimeClassMemberNames(string template)
        => SplitRuntimeClass(template).Members.Select(member => member.Name).Where(name => name.Length > 0).ToHashSet(StringComparer.Ordinal);

    private static (string Header, List<(string Name, string Text)> Members, string Footer) SplitRuntimeClass(string template)
    {
        string[] lines = template.Replace("\r", string.Empty).Split('\n');
        System.Text.StringBuilder header = new();
        int index = 0;
        for (; index < lines.Length; index++)
        {
            header.Append(lines[index]).Append('\n');
            if (lines[index].Trim() == "{")
            {
                index++;
                break;
            }
        }

        List<(string Name, string Text)> members = [];
        System.Text.StringBuilder footer = new();
        while (index < lines.Length)
        {
            string trimmed = lines[index].Trim();
            if (trimmed.Length == 0)
            {
                index++;
                continue;
            }

            if (trimmed == "}")
            {
                for (; index < lines.Length; index++)
                {
                    footer.Append(lines[index]).Append('\n');
                }

                break;
            }

            System.Text.StringBuilder member = new();
            int depth = 0;
            bool opened = false;
            int start = index;
            while (index < lines.Length)
            {
                string line = lines[index];
                member.Append(line).Append('\n');
                string code = line.TrimStart().StartsWith("#", StringComparison.Ordinal) ? string.Empty : StripRuntimeLiterals(line);
                foreach (char ch in code)
                {
                    if (ch == '{')
                    {
                        depth++;
                        opened = true;
                    }
                    else if (ch == '}')
                    {
                        depth--;
                    }
                }

                index++;
                string lineTrimmed = StripRuntimeLiterals(line).TrimEnd();
                if (depth == 0 && (opened ? lineTrimmed.EndsWith('}') || lineTrimmed.EndsWith(';') : lineTrimmed.EndsWith(';')))
                {
                    break;
                }
            }

            // Leading comments belong to the member that follows them.
            int signature = start;
            while (signature < index - 1 && lines[signature].TrimStart().StartsWith("//", StringComparison.Ordinal))
            {
                signature++;
            }

            members.Add((GetRuntimeMemberName(lines[signature].Trim()), member.ToString()));
        }

        return (header.ToString(), members, footer.ToString());
    }

    private static string StripRuntimeLiterals(string text)
    {
        System.Text.StringBuilder builder = new();
        bool inString = false;
        bool inChar = false;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (inString || inChar)
            {
                if (ch == '\\')
                {
                    i++;
                }
                else if ((inString && ch == '"') || (inChar && ch == '\''))
                {
                    inString = false;
                    inChar = false;
                }

                continue;
            }

            if (ch == '"')
            {
                inString = true;
                continue;
            }

            if (ch == '\'')
            {
                inChar = true;
                continue;
            }

            if (ch == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n')
                {
                    i++;
                }

                builder.Append('\n');
                continue;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    // `public static T[] sort<T>(...)` → `sort`; `private int _length;` → `_length`; `public Foo()` → `Foo`.
    private static string GetRuntimeMemberName(string signature)
    {
        if (signature.StartsWith("//", StringComparison.Ordinal) || signature.StartsWith("#", StringComparison.Ordinal) || signature.StartsWith("[", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        int paren = signature.IndexOf('(');
        int equals = signature.IndexOf(" = ", StringComparison.Ordinal);
        int semicolon = signature.IndexOf(';');
        int end = paren >= 0 && (equals < 0 || paren < equals) ? paren : equals >= 0 ? equals : semicolon >= 0 ? semicolon : signature.Length;
        string beforeEnd = signature[..end].TrimEnd();
        int genericDepth = 0;
        int nameEnd = beforeEnd.Length;
        for (int i = beforeEnd.Length - 1; i >= 0; i--)
        {
            char ch = beforeEnd[i];
            if (ch == '>')
            {
                genericDepth++;
            }
            else if (ch == '<')
            {
                genericDepth--;
                if (genericDepth == 0)
                {
                    nameEnd = i;
                }
            }
            else if (genericDepth == 0 && !(char.IsLetterOrDigit(ch) || ch == '_'))
            {
                return beforeEnd[(i + 1)..nameEnd];
            }
        }

        return beforeEnd[..nameEnd];
    }

    private const string ArrayHelpersTemplate =
        """
        public static class __PscpArray
        {
            public static T[] zero<T>(int n) => new T[n];

            public static T[] fillNew<T>(int n) where T : new()
            {
                T[] result = new T[n];
                for (int i = 0; i < n; i++)
                {
                    result[i] = new T();
                }

                return result;
            }

            public static T[][] jagged<T>(int n, int m)
            {
                T[][] result = new T[n][];
                for (int i = 0; i < n; i++)
                {
                    result[i] = new T[m];
                }

                return result;
            }
        }
        """;

    private const string CollectionHelpersTemplate =
        """
        public static class __PscpCollection
        {
            public static void enqueue<TElement, TPriority>(PriorityQueue<TElement, TPriority> queue, (TElement, TPriority) entry) => queue.Enqueue(entry.Item1, entry.Item2);
            public static bool tryAdd<TKey, TValue>(Dictionary<TKey, TValue> dictionary, (TKey, TValue) entry) where TKey : notnull => dictionary.TryAdd(entry.Item1, entry.Item2);
        }
        """;

    private const string ThunkHelpersTemplate =
        """
        public static class __PscpThunk
        {
            public static T run<T>(Func<T> thunk) => thunk();
        }
        """;

    // PSCP default order (spec §25.1): strings compare ordinally, tuples element by element, other types use
    // `Comparer<T>.Default` (numbers, `char`, `bool`, `IComparable<T>` including `operator<=>`).
    private const string OrderTemplate =
        """
        public static class __PscpOrder<T>
        {
            public static readonly IComparer<T> Asc = Create();
            public static readonly IComparer<T> Desc = Comparer<T>.Create((left, right) => Asc.Compare(right, left));

            private static IComparer<T> Create()
            {
                if (typeof(T) == typeof(string)) return (IComparer<T>)(object)StringComparer.Ordinal;
                if (__PscpOrderCore.NeedsOrdinal(typeof(T))) return Comparer<T>.Create(static (left, right) => __PscpOrderCore.Instance.Compare(left, right));
                return Comparer<T>.Default;
            }
        }

        public sealed class __PscpOrderCore : System.Collections.IComparer
        {
            public static readonly __PscpOrderCore Instance = new();

            public static bool NeedsOrdinal(Type type)
            {
                if (type == typeof(string)) return true;
                Type? underlying = Nullable.GetUnderlyingType(type);
                if (underlying is not null) return NeedsOrdinal(underlying);
                return type.IsGenericType
                    && (type.FullName ?? string.Empty).StartsWith("System.ValueTuple`", StringComparison.Ordinal)
                    && Array.Exists(type.GetGenericArguments(), NeedsOrdinal);
            }

            public int Compare(object? left, object? right)
            {
                if (left is string leftText && right is string rightText) return string.CompareOrdinal(leftText, rightText);
                if (left is System.Collections.IStructuralComparable structural && right is not null) return structural.CompareTo(right, this);
                return System.Collections.Comparer.Default.Compare(left, right);
            }
        }
        """;
}
