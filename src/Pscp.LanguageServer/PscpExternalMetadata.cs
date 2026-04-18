using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;

namespace Pscp.LanguageServer;

internal static class PscpExternalMetadata
{
    private static readonly StringComparer Comparer = StringComparer.Ordinal;

    private static readonly IReadOnlyDictionary<string, Type> KnownTypes =
        new Dictionary<string, Type>(Comparer)
        {
            ["Math"] = typeof(Math),
            ["MathF"] = typeof(MathF),
            ["Console"] = typeof(Console),
            ["Environment"] = typeof(Environment),
            ["Convert"] = typeof(Convert),
            ["GC"] = typeof(GC),
            ["String"] = typeof(string),
            ["StringComparer"] = typeof(StringComparer),
            ["Comparer"] = typeof(Comparer<>),
            ["File"] = typeof(File),
            ["Directory"] = typeof(Directory),
            ["Path"] = typeof(Path),
            ["Enumerable"] = typeof(Enumerable),
            ["BitOperations"] = typeof(BitOperations),
            ["BigInteger"] = typeof(BigInteger),
            ["Random"] = typeof(Random),
            ["Array"] = typeof(Array),
            ["List"] = typeof(List<>),
            ["LinkedList"] = typeof(LinkedList<>),
            ["Queue"] = typeof(Queue<>),
            ["Stack"] = typeof(Stack<>),
            ["HashSet"] = typeof(HashSet<>),
            ["Dictionary"] = typeof(Dictionary<,>),
            ["PriorityQueue"] = typeof(PriorityQueue<,>),
            ["SortedSet"] = typeof(SortedSet<>),
        };

    private static readonly IReadOnlyDictionary<string, string[]> NamespaceMembers =
        new Dictionary<string, string[]>(Comparer)
        {
            ["System"] =
            [
                "Math", "MathF", "Console", "Environment", "Convert", "GC", "StringComparer",
                "Random", "Array", "IO", "Collections", "Linq", "Numerics"
            ],
            ["System.IO"] = ["File", "Directory", "Path"],
            ["System.Collections"] = ["Generic"],
            ["System.Collections.Generic"] =
            [
                "List", "LinkedList", "Queue", "Stack", "HashSet", "Dictionary", "PriorityQueue", "SortedSet", "Comparer"
            ],
            ["System.Linq"] = ["Enumerable"],
            ["System.Numerics"] = ["BitOperations", "BigInteger"],
        };

    private static readonly IReadOnlyDictionary<string, string> NamespaceAliases =
        new Dictionary<string, string>(Comparer)
        {
            ["IO"] = "System.IO",
            ["Collections"] = "System.Collections",
            ["Generic"] = "System.Collections.Generic",
            ["Linq"] = "System.Linq",
            ["Numerics"] = "System.Numerics",
        };

    private static readonly IReadOnlyDictionary<string, PscpCompletionEntry> TopLevelEntries = CreateTopLevelEntries();
    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyDictionary<string, PscpCompletionEntry>>> StaticMemberEntries =
        new(CreateStaticMemberEntries);
    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyDictionary<string, PscpCompletionEntry>>> InstanceMemberEntries =
        new(CreateInstanceMemberEntries);

    public static IEnumerable<PscpCompletionEntry> GetTopLevelCompletions()
        => TopLevelEntries.Values;

    public static bool TryGetTopLevelEntry(string name, out PscpCompletionEntry? entry)
        => TopLevelEntries.TryGetValue(name, out entry);

    public static bool IsKnownNamespace(string name)
        => NamespaceMembers.ContainsKey(name);

    public static IEnumerable<PscpCompletionEntry> GetMemberCompletions(string receiverName, bool instanceContext)
    {
        if (TryGetNamespaceMembers(receiverName, out IReadOnlyDictionary<string, PscpCompletionEntry>? namespaceMembers))
        {
            return namespaceMembers!.Values;
        }

        string normalized = NormalizeTypeReceiver(receiverName);
        if (instanceContext)
        {
            if (InstanceMemberEntries.Value.TryGetValue(normalized, out IReadOnlyDictionary<string, PscpCompletionEntry>? members))
            {
                return members.Values;
            }
        }
        else if (StaticMemberEntries.Value.TryGetValue(normalized, out IReadOnlyDictionary<string, PscpCompletionEntry>? members))
        {
            return members.Values;
        }

        return Array.Empty<PscpCompletionEntry>();
    }

    public static bool TryGetMemberEntry(string receiverName, bool instanceContext, string memberName, out PscpCompletionEntry? entry)
    {
        foreach (PscpCompletionEntry candidate in GetMemberCompletions(receiverName, instanceContext))
        {
            if (string.Equals(candidate.Label, memberName, StringComparison.Ordinal))
            {
                entry = candidate;
                return true;
            }
        }

        entry = null;
        return false;
    }

    public static string NormalizeTypeReceiver(string receiverName)
    {
        string normalized = receiverName.Trim();
        if (normalized.EndsWith("?", StringComparison.Ordinal))
        {
            normalized = normalized[..^1];
        }

        if (normalized.EndsWith("[]", StringComparison.Ordinal))
        {
            return "Array";
        }

        int genericIndex = normalized.IndexOf('<');
        if (genericIndex >= 0)
        {
            normalized = normalized[..genericIndex];
        }

        int dotIndex = normalized.LastIndexOf('.');
        if (dotIndex >= 0)
        {
            normalized = normalized[(dotIndex + 1)..];
        }

        return normalized switch
        {
            "string" => "String",
            _ => normalized,
        };
    }

    private static bool TryGetNamespaceMembers(string receiverName, out IReadOnlyDictionary<string, PscpCompletionEntry>? entries)
    {
        if (!NamespaceMembers.TryGetValue(receiverName, out string[]? children))
        {
            entries = null;
            return false;
        }

        Dictionary<string, PscpCompletionEntry> results = new(Comparer);
        foreach (string child in children)
        {
            string qualifiedName = NamespaceAliases.TryGetValue(child, out string? aliasTarget)
                ? aliasTarget
                : $"{receiverName}.{child}";

            if (NamespaceMembers.ContainsKey(qualifiedName))
            {
                results[child] = CreateNamespaceEntry(child, qualifiedName);
                continue;
            }

            if (KnownTypes.TryGetValue(child, out Type? type))
            {
                results[child] = CreateTypeEntry(child, type);
            }
        }

        entries = results;
        return entries.Count > 0;
    }

    private static IReadOnlyDictionary<string, PscpCompletionEntry> CreateTopLevelEntries()
    {
        Dictionary<string, PscpCompletionEntry> entries = new(Comparer)
        {
            ["System"] = CreateNamespaceEntry("System", "System"),
        };

        foreach ((string key, Type type) in KnownTypes.OrderBy(pair => pair.Key, Comparer))
        {
            entries[key] = CreateTypeEntry(key, type);
        }

        return entries;
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, PscpCompletionEntry>> CreateStaticMemberEntries()
    {
        Dictionary<string, IReadOnlyDictionary<string, PscpCompletionEntry>> tables = new(Comparer);
        foreach ((string key, Type type) in KnownTypes)
        {
            tables[key] = CreateMemberEntries(type, BindingFlags.Public | BindingFlags.Static, instanceContext: false);
        }

        tables["Array"] = MergeFallbackEntries(tables["Array"], CreateArrayFallbackMembers(instanceContext: false));
        tables["Math"] = MergeFallbackEntries(tables["Math"], CreateMathFallbackMembers(useFloatVariants: false));
        tables["MathF"] = MergeFallbackEntries(tables["MathF"], CreateMathFallbackMembers(useFloatVariants: true));
        tables["Console"] = MergeFallbackEntries(tables["Console"], CreateConsoleFallbackMembers());
        tables["File"] = MergeFallbackEntries(tables["File"], CreateFileFallbackMembers());
        tables["Directory"] = MergeFallbackEntries(tables["Directory"], CreateDirectoryFallbackMembers());
        tables["Path"] = MergeFallbackEntries(tables["Path"], CreatePathFallbackMembers());
        return tables;
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, PscpCompletionEntry>> CreateInstanceMemberEntries()
    {
        Dictionary<string, IReadOnlyDictionary<string, PscpCompletionEntry>> tables = new(Comparer)
        {
            ["String"] = CreateMemberEntries(typeof(string), BindingFlags.Public | BindingFlags.Instance, instanceContext: true),
            ["Array"] = CreateMemberEntries(typeof(Array), BindingFlags.Public | BindingFlags.Instance, instanceContext: true),
            ["List"] = CreateMemberEntries(typeof(List<>), BindingFlags.Public | BindingFlags.Instance, instanceContext: true),
            ["LinkedList"] = CreateMemberEntries(typeof(LinkedList<>), BindingFlags.Public | BindingFlags.Instance, instanceContext: true),
            ["Queue"] = CreateMemberEntries(typeof(Queue<>), BindingFlags.Public | BindingFlags.Instance, instanceContext: true),
            ["Stack"] = CreateMemberEntries(typeof(Stack<>), BindingFlags.Public | BindingFlags.Instance, instanceContext: true),
            ["HashSet"] = CreateMemberEntries(typeof(HashSet<>), BindingFlags.Public | BindingFlags.Instance, instanceContext: true),
            ["Dictionary"] = CreateMemberEntries(typeof(Dictionary<,>), BindingFlags.Public | BindingFlags.Instance, instanceContext: true),
            ["PriorityQueue"] = CreateMemberEntries(typeof(PriorityQueue<,>), BindingFlags.Public | BindingFlags.Instance, instanceContext: true),
            ["SortedSet"] = CreateMemberEntries(typeof(SortedSet<>), BindingFlags.Public | BindingFlags.Instance, instanceContext: true),
        };

        tables["String"] = MergeFallbackEntries(tables["String"], CreateStringFallbackMembers());
        tables["Array"] = MergeFallbackEntries(tables["Array"], CreateArrayFallbackMembers(instanceContext: true));
        tables["List"] = MergeFallbackEntries(tables["List"], CreateListFallbackMembers());
        tables["LinkedList"] = MergeFallbackEntries(tables["LinkedList"], CreateLinkedListFallbackMembers());
        tables["Queue"] = MergeFallbackEntries(tables["Queue"], CreateQueueFallbackMembers());
        tables["Stack"] = MergeFallbackEntries(tables["Stack"], CreateStackFallbackMembers());
        tables["HashSet"] = MergeFallbackEntries(tables["HashSet"], CreateHashSetFallbackMembers());
        tables["Dictionary"] = MergeFallbackEntries(tables["Dictionary"], CreateDictionaryFallbackMembers());
        tables["PriorityQueue"] = MergeFallbackEntries(tables["PriorityQueue"], CreatePriorityQueueFallbackMembers());
        tables["SortedSet"] = MergeFallbackEntries(tables["SortedSet"], CreateSortedSetFallbackMembers());
        return tables;
    }

    private static IReadOnlyDictionary<string, PscpCompletionEntry> CreateMemberEntries(Type type, BindingFlags flags, bool instanceContext)
    {
        Dictionary<string, PscpCompletionEntry> entries = new(Comparer);

        foreach (PropertyInfo property in type.GetProperties(flags))
        {
            if (property.GetMethod?.IsSpecialName == true && property.GetMethod.DeclaringType == typeof(object))
            {
                continue;
            }

            entries.TryAdd(
                property.Name,
                new PscpCompletionEntry(
                    property.Name,
                    10,
                    $"{FormatTypeName(property.PropertyType)} {property.Name}",
                    BuildPropertyDocumentation(type, property),
                    null,
                    null,
                    property.Name));
        }

        foreach (FieldInfo field in type.GetFields(flags))
        {
            if (field.IsSpecialName)
            {
                continue;
            }

            entries.TryAdd(
                field.Name,
                new PscpCompletionEntry(
                    field.Name,
                    10,
                    $"{FormatTypeName(field.FieldType)} {field.Name}",
                    BuildFieldDocumentation(type, field),
                    null,
                    null,
                    field.Name));
        }

        foreach (IGrouping<string, MethodInfo> group in type.GetMethods(flags)
            .Where(method => !method.IsSpecialName)
            .Where(method => instanceContext || method.DeclaringType != typeof(object))
            .GroupBy(method => method.Name, Comparer))
        {
            PscpCompletionEntry entry = CreateMethodEntry(type, group.Key, group.OrderBy(method => method.GetParameters().Length).ToArray());
            entries.TryAdd(entry.Label, entry);
        }

        return entries;
    }

    private static PscpCompletionEntry CreateMethodEntry(Type owner, string name, IReadOnlyList<MethodInfo> methods)
    {
        MethodInfo representative = methods[0];
        string signature = FormatMethodSignature(representative);
        string detail = methods.Count == 1
            ? signature
            : $"{signature} (+{methods.Count - 1} overloads)";
        string documentation = BuildMethodDocumentation(owner, methods);
        string? insertText = BuildMethodInsertText(name, representative);
        int? insertTextFormat = insertText is null ? null : 2;
        return new PscpCompletionEntry(name, 2, detail, documentation, insertText, insertTextFormat, name);
    }

    private static string? BuildMethodInsertText(string name, MethodInfo method)
    {
        if (method.IsGenericMethodDefinition)
        {
            return null;
        }

        ParameterInfo[] parameters = method.GetParameters();
        if (parameters.Length == 0)
        {
            return $"{name}()";
        }

        if (parameters.Length > 4)
        {
            return $"{name}($1)";
        }

        string[] placeholders = parameters
            .Select((parameter, index) => $"${{{index + 1}:{parameter.Name ?? $"arg{index + 1}"}}}")
            .ToArray();
        return $"{name}({string.Join(", ", placeholders)})";
    }

    private static string BuildMethodDocumentation(Type owner, IReadOnlyList<MethodInfo> methods)
    {
        IEnumerable<string> lines = methods
            .Take(4)
            .Select(FormatMethodSignature);
        string header = $"```csharp\n{string.Join("\n", lines)}\n```";
        string summary = methods.Count == 1
            ? $"Public .NET member on `{owner.FullName}`."
            : $"Public .NET overload set on `{owner.FullName}`.";
        return header + "\n\n" + summary;
    }

    private static string BuildPropertyDocumentation(Type owner, PropertyInfo property)
        => $"```csharp\n{FormatTypeName(property.PropertyType)} {property.Name} {{ get; }}\n```\n\nPublic .NET member on `{owner.FullName}`.";

    private static string BuildFieldDocumentation(Type owner, FieldInfo field)
        => $"```csharp\n{FormatTypeName(field.FieldType)} {field.Name}\n```\n\nPublic .NET member on `{owner.FullName}`.";

    private static PscpCompletionEntry CreateNamespaceEntry(string label, string qualifiedName)
        => new(label, 9, "namespace", $"```csharp\nnamespace {qualifiedName}\n```\n\nPass-through .NET namespace.", null, null, label);

    private static PscpCompletionEntry CreateTypeEntry(string label, Type type)
        => new(label, 7, "type", BuildTypeDocumentation(type), null, null, label);

    private static string BuildTypeDocumentation(Type type)
        => $"```csharp\n{FormatTypeHeader(type)}\n```\n\nPass-through .NET type `{type.FullName}`.";

    private static string FormatTypeHeader(Type type)
    {
        string keyword = type.IsEnum
            ? "enum"
            : type.IsInterface
                ? "interface"
                : type.IsValueType && !type.IsPrimitive
                    ? "struct"
                    : "class";
        return $"{keyword} {FormatTypeName(type)}";
    }

    private static string FormatMethodSignature(MethodInfo method)
    {
        string genericSuffix = method.IsGenericMethodDefinition
            ? $"<{string.Join(", ", method.GetGenericArguments().Select(argument => argument.Name))}>"
            : string.Empty;
        string parameters = string.Join(", ", method.GetParameters().Select(FormatParameter));
        return $"{FormatTypeName(method.ReturnType)} {method.Name}{genericSuffix}({parameters})";
    }

    private static string FormatParameter(ParameterInfo parameter)
    {
        Type parameterType = parameter.ParameterType;
        string modifier = string.Empty;
        if (parameterType.IsByRef)
        {
            parameterType = parameterType.GetElementType() ?? parameterType;
            modifier = parameter.IsOut
                ? "out "
                : parameter.IsIn
                    ? "in "
                    : "ref ";
        }

        return $"{modifier}{FormatTypeName(parameterType)} {parameter.Name}";
    }

    private static string FormatTypeName(Type type)
    {
        if (type.IsByRef)
        {
            return FormatTypeName(type.GetElementType() ?? type);
        }

        if (type.IsArray)
        {
            return $"{FormatTypeName(type.GetElementType() ?? typeof(object))}[]";
        }

        if (type.IsGenericParameter)
        {
            return type.Name;
        }

        if (type == typeof(void))
        {
            return "void";
        }

        if (type == typeof(bool))
        {
            return "bool";
        }

        if (type == typeof(char))
        {
            return "char";
        }

        if (type == typeof(int))
        {
            return "int";
        }

        if (type == typeof(long))
        {
            return "long";
        }

        if (type == typeof(double))
        {
            return "double";
        }

        if (type == typeof(decimal))
        {
            return "decimal";
        }

        if (type == typeof(string))
        {
            return "string";
        }

        if (!type.IsGenericType)
        {
            return type.Name;
        }

        string baseName = type.Name;
        int tickIndex = baseName.IndexOf('`');
        if (tickIndex >= 0)
        {
            baseName = baseName[..tickIndex];
        }

        return $"{baseName}<{string.Join(", ", type.GetGenericArguments().Select(FormatTypeName))}>";
    }

    private static IReadOnlyDictionary<string, PscpCompletionEntry> MergeFallbackEntries(
        IReadOnlyDictionary<string, PscpCompletionEntry> primary,
        IReadOnlyDictionary<string, PscpCompletionEntry> fallback)
    {
        Dictionary<string, PscpCompletionEntry> merged = new(primary, Comparer);
        foreach ((string key, PscpCompletionEntry value) in fallback)
        {
            merged.TryAdd(key, value);
        }

        return merged;
    }

    private static IReadOnlyDictionary<string, PscpCompletionEntry> CreateMathFallbackMembers(bool useFloatVariants)
    {
        string number = useFloatVariants ? "float" : "double";
        return new Dictionary<string, PscpCompletionEntry>(Comparer)
        {
            ["Abs"] = Method("Abs", $"{number} Abs({number} value)", "Absolute value."),
            ["Sqrt"] = Method("Sqrt", $"{number} Sqrt({number} value)", "Square root."),
            ["Pow"] = Method("Pow", $"{number} Pow({number} value, {number} power)", "Power function."),
            ["Min"] = Method("Min", $"{number} Min({number} left, {number} right)", "Minimum of two values."),
            ["Max"] = Method("Max", $"{number} Max({number} left, {number} right)", "Maximum of two values."),
            ["Clamp"] = Method("Clamp", $"{number} Clamp({number} value, {number} min, {number} max)", "Clamp into an inclusive range."),
            ["Floor"] = Method("Floor", $"{number} Floor({number} value)", "Floor function."),
            ["Ceiling"] = Method("Ceiling", $"{number} Ceiling({number} value)", "Ceiling function."),
            ["Round"] = Method("Round", $"{number} Round({number} value)", "Round function."),
            ["PI"] = Property("PI", number, "Circle ratio constant."),
            ["E"] = Property("E", number, "Euler's number."),
        };
    }

    private static IReadOnlyDictionary<string, PscpCompletionEntry> CreateConsoleFallbackMembers()
        => new Dictionary<string, PscpCompletionEntry>(Comparer)
        {
            ["Write"] = Method("Write", "void Write(object value)", "Writes a value to standard output."),
            ["WriteLine"] = Method("WriteLine", "void WriteLine(object value)", "Writes a value followed by a newline."),
            ["ReadLine"] = Method("ReadLine", "string? ReadLine()", "Reads one line from standard input."),
            ["Out"] = Property("Out", "TextWriter", "Standard output writer."),
            ["Error"] = Property("Error", "TextWriter", "Standard error writer."),
        };

    private static IReadOnlyDictionary<string, PscpCompletionEntry> CreateFileFallbackMembers()
        => new Dictionary<string, PscpCompletionEntry>(Comparer)
        {
            ["Exists"] = Method("Exists", "bool Exists(string path)", "Checks whether a file exists."),
            ["ReadAllText"] = Method("ReadAllText", "string ReadAllText(string path)", "Reads the entire file as text."),
            ["WriteAllText"] = Method("WriteAllText", "void WriteAllText(string path, string contents)", "Writes text to a file."),
            ["ReadAllLines"] = Method("ReadAllLines", "string[] ReadAllLines(string path)", "Reads all file lines."),
            ["OpenRead"] = Method("OpenRead", "FileStream OpenRead(string path)", "Opens a file for reading."),
        };

    private static IReadOnlyDictionary<string, PscpCompletionEntry> CreateDirectoryFallbackMembers()
        => new Dictionary<string, PscpCompletionEntry>(Comparer)
        {
            ["Exists"] = Method("Exists", "bool Exists(string path)", "Checks whether a directory exists."),
            ["CreateDirectory"] = Method("CreateDirectory", "DirectoryInfo CreateDirectory(string path)", "Creates a directory."),
            ["GetFiles"] = Method("GetFiles", "string[] GetFiles(string path)", "Gets files in a directory."),
            ["GetDirectories"] = Method("GetDirectories", "string[] GetDirectories(string path)", "Gets child directories."),
        };

    private static IReadOnlyDictionary<string, PscpCompletionEntry> CreatePathFallbackMembers()
        => new Dictionary<string, PscpCompletionEntry>(Comparer)
        {
            ["Combine"] = Method("Combine", "string Combine(string left, string right)", "Combines path segments."),
            ["Join"] = Method("Join", "string Join(string left, string right)", "Joins path segments."),
            ["GetFileName"] = Method("GetFileName", "string? GetFileName(string path)", "Gets the file name."),
            ["GetDirectoryName"] = Method("GetDirectoryName", "string? GetDirectoryName(string path)", "Gets the directory name."),
            ["GetExtension"] = Method("GetExtension", "string GetExtension(string path)", "Gets the file extension."),
        };

    private static IReadOnlyDictionary<string, PscpCompletionEntry> CreateStringFallbackMembers()
        => new Dictionary<string, PscpCompletionEntry>(Comparer)
        {
            ["Length"] = Property("Length", "int", "String length."),
            ["Contains"] = Method("Contains", "bool Contains(string value)", "Checks whether the string contains a substring."),
            ["StartsWith"] = Method("StartsWith", "bool StartsWith(string value)", "Checks the prefix."),
            ["EndsWith"] = Method("EndsWith", "bool EndsWith(string value)", "Checks the suffix."),
            ["Split"] = Method("Split", "string[] Split(char separator)", "Splits the string."),
            ["Substring"] = Method("Substring", "string Substring(int startIndex)", "Returns a substring."),
            ["IndexOf"] = Method("IndexOf", "int IndexOf(string value)", "Finds the first index."),
            ["Replace"] = Method("Replace", "string Replace(string oldValue, string newValue)", "Replaces a substring."),
            ["Trim"] = Method("Trim", "string Trim()", "Trims whitespace."),
            ["ToCharArray"] = Method("ToCharArray", "char[] ToCharArray()", "Materializes characters."),
        };

    private static IReadOnlyDictionary<string, PscpCompletionEntry> CreateArrayFallbackMembers(bool instanceContext)
        => instanceContext
            ? new Dictionary<string, PscpCompletionEntry>(Comparer)
            {
                ["Length"] = Property("Length", "int", "Array length."),
                ["Rank"] = Property("Rank", "int", "Array rank."),
            }
            : new Dictionary<string, PscpCompletionEntry>(Comparer)
            {
                ["Copy"] = Method("Copy", "void Copy(Array sourceArray, Array destinationArray, int length)", "Copies array elements."),
                ["Fill"] = Method("Fill", "void Fill<T>(T[] array, T value)", "Fills an array with a value."),
                ["Reverse"] = Method("Reverse", "void Reverse(Array array)", "Reverses an array."),
                ["Sort"] = Method("Sort", "void Sort(Array array)", "Sorts an array."),
            };

    private static IReadOnlyDictionary<string, PscpCompletionEntry> CreateListFallbackMembers()
        => new Dictionary<string, PscpCompletionEntry>(Comparer)
        {
            ["Add"] = Method("Add", "void Add(T item)", "Adds one element."),
            ["AddRange"] = Method("AddRange", "void AddRange(IEnumerable<T> collection)", "Adds many elements."),
            ["Clear"] = Method("Clear", "void Clear()", "Removes all elements."),
            ["Contains"] = Method("Contains", "bool Contains(T item)", "Checks whether an item exists."),
            ["Count"] = Property("Count", "int", "Element count."),
            ["Remove"] = Method("Remove", "bool Remove(T item)", "Removes one element."),
            ["RemoveAt"] = Method("RemoveAt", "void RemoveAt(int index)", "Removes an element by index."),
            ["Reverse"] = Method("Reverse", "void Reverse()", "Reverses the list."),
            ["Sort"] = Method("Sort", "void Sort()", "Sorts the list."),
        };

    private static IReadOnlyDictionary<string, PscpCompletionEntry> CreateLinkedListFallbackMembers()
        => new Dictionary<string, PscpCompletionEntry>(Comparer)
        {
            ["AddFirst"] = Method("AddFirst", "LinkedListNode<T> AddFirst(T value)", "Adds one element at the front."),
            ["AddLast"] = Method("AddLast", "LinkedListNode<T> AddLast(T value)", "Adds one element at the back."),
            ["Clear"] = Method("Clear", "void Clear()", "Removes all elements."),
            ["Count"] = Property("Count", "int", "Element count."),
            ["First"] = Property("First", "LinkedListNode<T>?", "First node."),
            ["Last"] = Property("Last", "LinkedListNode<T>?", "Last node."),
            ["Remove"] = Method("Remove", "bool Remove(T value)", "Removes one element."),
        };

    private static IReadOnlyDictionary<string, PscpCompletionEntry> CreateQueueFallbackMembers()
        => new Dictionary<string, PscpCompletionEntry>(Comparer)
        {
            ["Clear"] = Method("Clear", "void Clear()", "Removes all elements."),
            ["Count"] = Property("Count", "int", "Element count."),
            ["Dequeue"] = Method("Dequeue", "T Dequeue()", "Removes and returns the front element."),
            ["Enqueue"] = Method("Enqueue", "void Enqueue(T item)", "Adds one element."),
            ["Peek"] = Method("Peek", "T Peek()", "Returns the front element."),
            ["TryDequeue"] = Method("TryDequeue", "bool TryDequeue(out T result)", "Attempts to dequeue one element."),
        };

    private static IReadOnlyDictionary<string, PscpCompletionEntry> CreateStackFallbackMembers()
        => new Dictionary<string, PscpCompletionEntry>(Comparer)
        {
            ["Clear"] = Method("Clear", "void Clear()", "Removes all elements."),
            ["Count"] = Property("Count", "int", "Element count."),
            ["Peek"] = Method("Peek", "T Peek()", "Returns the top element."),
            ["Pop"] = Method("Pop", "T Pop()", "Removes and returns the top element."),
            ["Push"] = Method("Push", "void Push(T item)", "Pushes one element."),
            ["TryPop"] = Method("TryPop", "bool TryPop(out T result)", "Attempts to pop one element."),
        };

    private static IReadOnlyDictionary<string, PscpCompletionEntry> CreateHashSetFallbackMembers()
        => new Dictionary<string, PscpCompletionEntry>(Comparer)
        {
            ["Add"] = Method("Add", "bool Add(T item)", "Adds one element."),
            ["Clear"] = Method("Clear", "void Clear()", "Removes all elements."),
            ["Contains"] = Method("Contains", "bool Contains(T item)", "Checks membership."),
            ["Count"] = Property("Count", "int", "Element count."),
            ["Remove"] = Method("Remove", "bool Remove(T item)", "Removes one element."),
        };

    private static IReadOnlyDictionary<string, PscpCompletionEntry> CreateDictionaryFallbackMembers()
        => new Dictionary<string, PscpCompletionEntry>(Comparer)
        {
            ["Add"] = Method("Add", "void Add(TKey key, TValue value)", "Adds one key-value pair."),
            ["Clear"] = Method("Clear", "void Clear()", "Removes all entries."),
            ["ContainsKey"] = Method("ContainsKey", "bool ContainsKey(TKey key)", "Checks key membership."),
            ["Count"] = Property("Count", "int", "Entry count."),
            ["Keys"] = Property("Keys", "Dictionary<TKey, TValue>.KeyCollection", "Key collection."),
            ["Remove"] = Method("Remove", "bool Remove(TKey key)", "Removes one key."),
            ["TryGetValue"] = Method("TryGetValue", "bool TryGetValue(TKey key, out TValue value)", "Attempts to get a value."),
            ["Values"] = Property("Values", "Dictionary<TKey, TValue>.ValueCollection", "Value collection."),
        };

    private static IReadOnlyDictionary<string, PscpCompletionEntry> CreatePriorityQueueFallbackMembers()
        => new Dictionary<string, PscpCompletionEntry>(Comparer)
        {
            ["Clear"] = Method("Clear", "void Clear()", "Removes all elements."),
            ["Count"] = Property("Count", "int", "Element count."),
            ["Dequeue"] = Method("Dequeue", "TElement Dequeue()", "Removes and returns the highest-priority element."),
            ["Enqueue"] = Method("Enqueue", "void Enqueue(TElement element, TPriority priority)", "Adds one element with priority."),
            ["Peek"] = Method("Peek", "TElement Peek()", "Returns the next element."),
            ["TryDequeue"] = Method("TryDequeue", "bool TryDequeue(out TElement element, out TPriority priority)", "Attempts to dequeue one element."),
        };

    private static IReadOnlyDictionary<string, PscpCompletionEntry> CreateSortedSetFallbackMembers()
        => new Dictionary<string, PscpCompletionEntry>(Comparer)
        {
            ["Add"] = Method("Add", "bool Add(T item)", "Adds one element."),
            ["Clear"] = Method("Clear", "void Clear()", "Removes all elements."),
            ["Contains"] = Method("Contains", "bool Contains(T item)", "Checks membership."),
            ["Count"] = Property("Count", "int", "Element count."),
            ["Max"] = Property("Max", "T?", "Largest element."),
            ["Min"] = Property("Min", "T?", "Smallest element."),
            ["Remove"] = Method("Remove", "bool Remove(T item)", "Removes one element."),
        };

    private static PscpCompletionEntry Method(string label, string detail, string summary)
        => new(label, 2, detail, $"```csharp\n{detail}\n```\n\n{summary}", BuildFallbackInsertText(label, detail), 2, label);

    private static PscpCompletionEntry Property(string label, string typeDisplay, string summary)
        => new(label, 10, $"{typeDisplay} {label}", $"```csharp\n{typeDisplay} {label}\n```\n\n{summary}", null, null, label);

    private static string BuildFallbackInsertText(string label, string detail)
    {
        int openParen = detail.IndexOf('(');
        int closeParen = detail.LastIndexOf(')');
        if (openParen < 0 || closeParen <= openParen)
        {
            return label;
        }

        string parameterList = detail[(openParen + 1)..closeParen].Trim();
        if (parameterList.Length == 0)
        {
            return $"{label}()";
        }

        int parameterCount = parameterList.Split(',').Length;
        string placeholders = string.Join(", ", Enumerable.Range(1, parameterCount).Select(index => $"${index}"));
        return $"{label}({placeholders})";
    }
}
