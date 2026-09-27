namespace Pscp.Transpiler;

// Lexing, parsing, binding and semantic analysis of one source, without C# generation.
public sealed record PscpFrontEndResult(
    IReadOnlyList<Token> Tokens,
    PscpProgram Program,
    SyntaxSpans Spans,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool Success => Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);

    internal SemanticAnalysisResult? Semantic { get; init; }
}

public static class PscpTranspiler
{
    public static TranspilationResult Transpile(string source, TranspilationOptions? options = null)
    {
        options ??= new TranspilationOptions();
        FrontEnd frontEnd = RunFrontEnd(source);
        string csharp;
        try
        {
            csharp = new CSharpEmitter(options, frontEnd.Semantic).Emit(frontEnd.Program);
        }
        catch (Exception) when (frontEnd.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            // A program with errors is not compiled; its C# is only informative.
            csharp = string.Empty;
        }

        return new TranspilationResult(source, frontEnd.Program, csharp, frontEnd.Diagnostics);
    }

    // Diagnostics without generating C#. Used by `pscp check`, `pscp test` and the language server, which
    // analyze on every edit and never need the emitted code.
    public static IReadOnlyList<Diagnostic> Check(string source)
        => RunFrontEnd(source).Diagnostics;

    public static PscpFrontEndResult Analyze(string source)
    {
        FrontEnd frontEnd = RunFrontEnd(source);
        return new PscpFrontEndResult(frontEnd.Tokens, frontEnd.Program, frontEnd.Spans, frontEnd.Diagnostics) { Semantic = frontEnd.Semantic };
    }

    // C# for an analyzed source without analyzing it again (the language server's generated C# preview), or null
    // when the source has errors.
    public static string? Generate(PscpFrontEndResult frontEnd, TranspilationOptions? options = null)
        => frontEnd.Success
            ? new CSharpEmitter(options ?? new TranspilationOptions(), frontEnd.Semantic).Emit(frontEnd.Program)
            : null;

    private sealed record FrontEnd(
        IReadOnlyList<Token> Tokens,
        PscpProgram Program,
        SyntaxSpans Spans,
        SemanticAnalysisResult? Semantic,
        IReadOnlyList<Diagnostic> Diagnostics);

    private static FrontEnd RunFrontEnd(string source)
    {
        Lexer lexer = new(source);
        IReadOnlyList<Token> tokens = lexer.Lex();
        Parser parser = new(tokens);
        PscpProgram program = parser.ParseProgram();

        // Diagnostics without a specific code take the code of the stage that reported them.
        List<Diagnostic> diagnostics =
        [
            .. lexer.Diagnostics.Select(diagnostic => diagnostic.Code is null ? diagnostic with { Code = DiagnosticCodes.Lexical } : diagnostic),
            .. parser.Diagnostics.Select(diagnostic => diagnostic.Code is null ? diagnostic with { Code = DiagnosticCodes.Syntax } : diagnostic),
        ];

        // Guide §6.4: a syntax error does not hide the semantic diagnostics of the rest of the file. Semantic
        // diagnostics on the lines of a syntax error are dropped, since they are usually its consequences.
        SemanticAnalysisResult? semantic = null;
        Diagnostic[] syntaxErrors = diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        try
        {
            BindingResult binding = PscpBinder.Bind(program, parser.Spans);
            program = binding.Program;
            SemanticAnalysisResult analysis = PscpSemanticAnalyzer.Analyze(program, parser.Spans, binding.ConstantsUsedByTypes);
            IEnumerable<Diagnostic> semanticDiagnostics = binding.Diagnostics.Concat(analysis.Diagnostics);
            if (syntaxErrors.Length > 0)
            {
                int[] lineStarts = GetLineStarts(source);
                HashSet<int> brokenLines = syntaxErrors.Select(error => LineOf(lineStarts, error.Span.Start)).ToHashSet();
                semanticDiagnostics = semanticDiagnostics.Where(diagnostic => !brokenLines.Contains(LineOf(lineStarts, diagnostic.Span.Start)));
            }
            else
            {
                semantic = analysis;
            }

            diagnostics.AddRange(semanticDiagnostics);
        }
        catch (Exception) when (syntaxErrors.Length > 0)
        {
            // A tree broken by syntax errors may not analyze; its syntax errors are still reported.
        }

        diagnostics.Sort((left, right) => left.Span.Start.CompareTo(right.Span.Start));
        return new FrontEnd(tokens, program, parser.Spans, semantic, Deduplicate(diagnostics));
    }

    private static int[] GetLineStarts(string source)
    {
        List<int> starts = [0];
        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] == '\n')
            {
                starts.Add(i + 1);
            }
        }

        return starts.ToArray();
    }

    private static int LineOf(int[] lineStarts, int offset)
    {
        int index = Array.BinarySearch(lineStarts, offset);
        return index >= 0 ? index : ~index - 1;
    }

    // The same message at the same place is reported once.
    private static IReadOnlyList<Diagnostic> Deduplicate(List<Diagnostic> diagnostics)
    {
        HashSet<(int, int, string, string?)> seen = [];
        List<Diagnostic> unique = [];
        foreach (Diagnostic diagnostic in diagnostics)
        {
            if (seen.Add((diagnostic.Span.Start, diagnostic.Span.Length, diagnostic.Message, diagnostic.Code)))
            {
                unique.Add(diagnostic);
            }
        }

        return unique;
    }
}
