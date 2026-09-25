namespace Pscp.Transpiler;

public static class PscpTranspiler
{
    public static TranspilationResult Transpile(string source, TranspilationOptions? options = null)
    {
        options ??= new TranspilationOptions();

        Lexer lexer = new(source);
        IReadOnlyList<Token> tokens = lexer.Lex();

        Parser parser = new(tokens);
        PscpProgram program = parser.ParseProgram();

        List<Diagnostic> diagnostics = [];
        diagnostics.AddRange(lexer.Diagnostics);
        diagnostics.AddRange(parser.Diagnostics);

        SemanticAnalysisResult? semantic = null;
        if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            semantic = PscpSemanticAnalyzer.Analyze(tokens, program);
            diagnostics.AddRange(semantic.Diagnostics);
        }

        CSharpEmitter emitter = new(options, semantic);
        string csharp = emitter.Emit(program);

        return new TranspilationResult(source, program, csharp, diagnostics);
    }

    // Lexer, parser, and semantic diagnostics without generating C#. Used by `pscp check` and the language
    // server, which analyze on every edit and never need the emitted code.
    public static IReadOnlyList<Diagnostic> Check(string source)
    {
        Lexer lexer = new(source);
        IReadOnlyList<Token> tokens = lexer.Lex();
        Parser parser = new(tokens);
        PscpProgram program = parser.ParseProgram();

        List<Diagnostic> diagnostics = [.. lexer.Diagnostics, .. parser.Diagnostics];
        if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.AddRange(AnalyzeSemantics(tokens, program));
        }

        return diagnostics;
    }

    // Semantic diagnostics for an already lexed and parsed program.
    public static IReadOnlyList<Diagnostic> AnalyzeSemantics(IReadOnlyList<Token> tokens, PscpProgram program)
        => PscpSemanticAnalyzer.Analyze(tokens, program).Diagnostics;
}

