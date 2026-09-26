namespace Pscp.Transpiler;

// The PSCP default order (spec §25.1). .NET compares strings by culture, so strings and tuples that contain
// strings need an ordinal comparer; numbers, `char`, `bool` and types with `operator<=>` (IComparable<T>) keep
// the .NET default comparer.
internal sealed partial class CSharpEmitter
{
    private enum OrderKind
    {
        // Built-in numeric types and `char`: C# comparison operators apply.
        Numeric,
        // `bool` and declared types: `Comparer<T>.Default` is the default order.
        Default,
        String,
        // Tuples with strings, type parameters and unknown types: `__PscpOrder<T>` decides at run time.
        Runtime,
    }

    private OrderKind GetOrderKind(TypeSyntax? type)
        => type switch
        {
            NamedTypeSyntax { TypeArguments.Count: 0, Name: "int" or "long" or "short" or "byte" or "sbyte" or "uint" or "ulong" or "ushort" or "double" or "float" or "decimal" or "char" } => OrderKind.Numeric,
            NamedTypeSyntax { TypeArguments.Count: 0, Name: "bool" } => OrderKind.Default,
            NamedTypeSyntax { TypeArguments.Count: 0, Name: "string" or "String" or "System.String" } => OrderKind.String,
            NamedTypeSyntax { TypeArguments.Count: 0 } named when _declaredTypeNames.Contains(named.Name) => OrderKind.Default,
            NullableTypeSyntax { InnerType: var inner } when GetOrderKind(inner) is OrderKind.Numeric or OrderKind.Default => OrderKind.Default,
            TupleTypeSyntax tuple when tuple.Elements.All(element => GetOrderKind(element) is OrderKind.Numeric or OrderKind.Default) => OrderKind.Default,
            _ => OrderKind.Runtime,
        };

    // An `IComparer<T>` for the default order.
    private string EmitDefaultOrderComparer(TypeSyntax type)
        => GetOrderKind(type) switch
        {
            OrderKind.String => "StringComparer.Ordinal",
            OrderKind.Runtime => $"__PscpOrder<{EmitType(type)}>.Asc",
            _ => $"Comparer<{EmitType(type)}>.Default",
        };

    // `left <=> right` in the default order.
    private string EmitDefaultOrderCompare(string left, string right, TypeSyntax type)
        => GetOrderKind(type) switch
        {
            OrderKind.String => $"string.CompareOrdinal({left}, {right})",
            OrderKind.Runtime => $"__PscpOrder<{EmitType(type)}>.Asc.Compare({left}, {right})",
            _ => $"Comparer<{EmitType(type)}>.Default.Compare({left}, {right})",
        };

    // `left < right` (or `>`) in the default order.
    private string EmitPreferredComparison(string left, string right, TypeSyntax type, bool preferLower)
    {
        string op = preferLower ? "<" : ">";
        return GetOrderKind(type) == OrderKind.Numeric
            ? $"{left} {op} {right}"
            : $"{EmitDefaultOrderCompare(left, right, type)} {op} 0";
    }

    // The comparer an auto-constructed ordered collection needs (§9.3, §25.4), or null when `new()` already
    // uses the default order.
    private string? EmitAutoConstructComparer(NamedTypeSyntax type)
    {
        TypeSyntax? key = type.Name switch
        {
            "SortedSet" or "System.Collections.Generic.SortedSet" when type.TypeArguments.Count == 1 => type.TypeArguments[0],
            "SortedDictionary" or "System.Collections.Generic.SortedDictionary" when type.TypeArguments.Count == 2 => type.TypeArguments[0],
            "PriorityQueue" or "System.Collections.Generic.PriorityQueue" when type.TypeArguments.Count == 2 => type.TypeArguments[1],
            _ => null,
        };

        return key is not null && GetOrderKind(key) is OrderKind.String or OrderKind.Runtime ? EmitDefaultOrderComparer(key) : null;
    }

    private string EmitAutoConstruction(NamedTypeSyntax type, bool targetTyped)
    {
        string arguments = EmitAutoConstructComparer(type) ?? string.Empty;
        return targetTyped ? $"new({arguments})" : $"new {EmitType(type)}({arguments})";
    }
}
