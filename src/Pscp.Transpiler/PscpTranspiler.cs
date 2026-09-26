namespace Pscp.Transpiler;

// Lexing, parsing, binding and semantic analysis of one source, without C# generation.
public sealed record PscpFrontEndResult(
    IReadOnlyList<Token> Tokens,
    PscpProgram Program,
    SyntaxSpans Spans,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool Success => Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
}

public static class PscpTranspiler
{
    public static TranspilationResult Transpile(string source, TranspilationOptions? options = null)
    {
        options ??= new TranspilationOptions();
        FrontEnd frontEnd = RunFrontEnd(source);
        CSharpEmitter emitter = new(options, frontEnd.Semantic);
        string csharp = emitter.Emit(frontEnd.Program);
        return new TranspilationResult(source, frontEnd.Program, csharp, frontEnd.Diagnostics);
    }

    // Diagnostics without generating C#. Used by `pscp check`, `pscp test` and the language server, which
    // analyze on every edit and never need the emitted code.
    public static IReadOnlyList<Diagnostic> Check(string source)
        => RunFrontEnd(source).Diagnostics;

    public static PscpFrontEndResult Analyze(string source)
    {
        FrontEnd frontEnd = RunFrontEnd(source);
        return new PscpFrontEndResult(frontEnd.Tokens, frontEnd.Program, frontEnd.Spans, frontEnd.Diagnostics);
    }

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

        SemanticAnalysisResult? semantic = null;
        if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            BindingResult binding = PscpBinder.Bind(program, parser.Spans);
            program = binding.Program;
            diagnostics.AddRange(binding.Diagnostics);
            semantic = PscpSemanticAnalyzer.Analyze(program, parser.Spans, binding.ConstantsUsedByTypes);
            diagnostics.AddRange(semantic.Diagnostics);
        }

        diagnostics.Sort((left, right) => left.Span.Start.CompareTo(right.Span.Start));
        return new FrontEnd(tokens, program, parser.Spans, semantic, Deduplicate(diagnostics));
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
