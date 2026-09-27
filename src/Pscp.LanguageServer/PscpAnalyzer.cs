using System.Text;
using System.Text.RegularExpressions;
using Pscp.Transpiler;

namespace Pscp.LanguageServer;

internal sealed partial class PscpAnalyzer
{
    private static readonly Regex IdentifierPattern = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);
    private static readonly Regex SizedArrayPattern = new(@"\[[^\[\]]+\]", RegexOptions.Compiled);

    public PscpAnalysisResult Analyze(DocumentSnapshot snapshot)
    {
        // Diagnostics come from the same front end as `pscp check`, so the editor and the CLI agree on every
        // judgement (guide §2). The token walk below only builds symbols, references and classifications.
        PscpFrontEndResult frontEnd = RunFrontEnd(snapshot.Text);
        AnalyzerState state = new(snapshot, frontEnd.Tokens);
        foreach (Diagnostic diagnostic in frontEnd.Diagnostics)
        {
            state.AddDiagnostic(diagnostic.EffectiveCode, diagnostic.Message, diagnostic.Span, ToServerSeverity(diagnostic.Severity), relatedSpan: diagnostic.RelatedSpan);
        }

        Scope globalScope = state.CreateScope(null, 0, snapshot.Text.Length);
        InitializeIntrinsics(state, globalScope);
        AnalyzeStatements(state, globalScope, 0, frontEnd.Tokens.Count - 1, null, topLevel: true, loopDepth: 0);
        AddDefaultTokenClassifications(state);
        AddRewriteHovers(state);
        AddDeprecationMarks(state, frontEnd.Diagnostics);
        PscpAnalysisResult result = state.Build();
        result.FrontEnd = frontEnd;
        return result;
    }

    // Guide §7.2 and §11.3: a deprecated name carries the `deprecated` modifier and says what replaces it
    // (spec appendix C). The judgement itself is the front end's PSCP2900 warning.
    private static void AddDeprecationMarks(AnalyzerState state, IReadOnlyList<Diagnostic> diagnostics)
    {
        foreach (Diagnostic diagnostic in diagnostics)
        {
            if (diagnostic.EffectiveCode != DiagnosticCodes.Deprecated)
            {
                continue;
            }

            for (int i = 0; i < state.Tokens.Count; i++)
            {
                Token token = state.Tokens[i];
                if (token.Position != diagnostic.Span.Start || token.Kind != TokenKind.Identifier)
                {
                    continue;
                }

                state.MarkToken(i, state.ClassificationOf(i) ?? "function", ["deprecated"]);
                string replacement = PscpCodeActions.GetReplacement(token.Text) is { } name ? $"Use `{name}` instead." : diagnostic.Message;
                state.SetHover(i, $"```pscp\n{token.Text}\n```\n\nDeprecated: it is removed in v0.8. {replacement}\n\n[Spec appendix C]({DiagnosticCodes.SpecUrl}#{Uri.EscapeDataString("부록-c-폐기-예정-목록")})");
                break;
            }
        }
    }

    // Guide §7.2: a data-structure rewrite operator shows the .NET method it calls and that method's return
    // type. The receiver's static type is only known once the statement walk has bound the names, so the
    // hover is filled in here (spec §26.2).
    private static void AddRewriteHovers(AnalyzerState state)
    {
        for (int i = 0; i < state.Tokens.Count; i++)
        {
            TokenKind kind = state.Tokens[i].Kind;
            if (kind is not (TokenKind.PlusEqual or TokenKind.MinusEqual or TokenKind.Tilde or TokenKind.MinusMinus))
            {
                continue;
            }

            // `+=` and `-=` follow the receiver; `~` and prefix `--` precede it.
            int receiverIndex = kind is TokenKind.PlusEqual or TokenKind.MinusEqual
                ? PreviousNonTriviaIndex(state.Tokens, i - 1)
                : NextNonTriviaIndex(state.Tokens, i + 1);
            if (receiverIndex < 0 || state.Tokens[receiverIndex].Kind != TokenKind.Identifier)
            {
                continue;
            }

            if (state.TryGetSymbolAt(receiverIndex, out PscpServerSymbol? symbol)
                && PscpOperatorHovers.ForRewrite(kind, symbol!.TypeDisplay) is { } hover)
            {
                state.SetHover(i, hover);
                state.MarkToken(i, "operator", "rewrite");
            }
        }
    }

    private static int PreviousNonTriviaIndex(IReadOnlyList<Token> tokens, int index)
    {
        while (index >= 0 && tokens[index].Kind is TokenKind.NewLine or TokenKind.Semicolon)
        {
            index--;
        }

        return index;
    }

    private static int NextNonTriviaIndex(IReadOnlyList<Token> tokens, int index)
    {
        while (index < tokens.Count && tokens[index].Kind is TokenKind.NewLine or TokenKind.Semicolon)
        {
            index++;
        }

        return index < tokens.Count ? index : -1;
    }

    // An internal failure of the binder or the semantic pass must not take the editor features down with it:
    // the lexer and parser results are still reported.
    private static PscpFrontEndResult RunFrontEnd(string text)
    {
        try
        {
            return PscpTranspiler.Analyze(text);
        }
        catch (Exception)
        {
            Lexer lexer = new(text);
            IReadOnlyList<Token> tokens = lexer.Lex();
            Parser parser = new(tokens);
            PscpProgram program = parser.ParseProgram();
            return new PscpFrontEndResult(tokens, program, parser.Spans, [.. lexer.Diagnostics, .. parser.Diagnostics]);
        }
    }

    private static ServerDiagnosticSeverity ToServerSeverity(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Error => ServerDiagnosticSeverity.Error,
        DiagnosticSeverity.Warning => ServerDiagnosticSeverity.Warning,
        _ => ServerDiagnosticSeverity.Information,
    };

    private void InitializeIntrinsics(AnalyzerState state, Scope globalScope)
    {
        foreach ((string name, PscpCompletionEntry completion) in PscpIntrinsics.Globals)
        {
            state.AddSyntheticSymbol(
                globalScope,
                name,
                PscpServerSymbolKind.Intrinsic,
                typeDisplay: completion.Detail,
                documentation: completion.Documentation);
        }
    }

    private void AnalyzeStatements(
        AnalyzerState state,
        Scope scope,
        int start,
        int endExclusive,
        FunctionContext? functionContext,
        bool topLevel,
        int loopDepth)
    {
        int index = start;
        while (index < endExclusive)
        {
            index = SkipSeparators(state.Tokens, index, endExclusive);
            if (index >= endExclusive)
            {
                break;
            }

            Token token = state.Tokens[index];
            switch (token.Kind)
            {
                case TokenKind.OpenBrace:
                {
                    int close = FindMatching(state.Tokens, index, TokenKind.OpenBrace, TokenKind.CloseBrace, endExclusive);
                    Scope blockScope = state.CreateScope(scope, token.Position, close >= 0 ? EndOf(state.Tokens[close]) : scope.EndOffset);
                    AnalyzeStatements(state, blockScope, index + 1, close < 0 ? endExclusive : close, functionContext, topLevel: false, loopDepth);
                    index = close < 0 ? endExclusive : close + 1;
                    break;
                }
                case TokenKind.Break:
                case TokenKind.Continue:
                    state.MarkToken(index, "keyword");
                    index = FindStatementEnd(state.Tokens, index, endExclusive);
                    break;
                case TokenKind.Return:
                    state.MarkToken(index, "keyword");
                    AnalyzeExpression(state, scope, index + 1, FindStatementEnd(state.Tokens, index, endExclusive), functionContext, loopDepth);
                    index = FindStatementEnd(state.Tokens, index, endExclusive);
                    break;
                case TokenKind.If:
                    index = AnalyzeIfStatement(state, scope, index, endExclusive, functionContext, loopDepth);
                    break;
                case TokenKind.While:
                    index = AnalyzeWhileStatement(state, scope, index, endExclusive, functionContext, loopDepth);
                    break;
                case TokenKind.For:
                    index = AnalyzeForStatement(state, scope, index, endExclusive, functionContext, loopDepth);
                    break;
                case TokenKind.Equal:
                case TokenKind.PlusEqual:
                {
                    state.MarkToken(index, "operator", "shorthand");
                    int outputEnd = FindStatementEnd(state.Tokens, index, endExclusive);
                    state.SetHover(index, BuildOutputShorthandHover(
                        token.Kind == TokenKind.Equal,
                        InferExpressionType(state, scope, index + 1, outputEnd)));
                    AnalyzeExpression(state, scope, index + 1, outputEnd, functionContext, loopDepth);
                    index = outputEnd;
                    break;
                }
                default:
                    if (TryAnalyzeTypeDeclaration(state, scope, ref index, endExclusive, topLevel))
                    {
                        break;
                    }

                    if (topLevel && TryAnalyzeFunction(state, scope, ref index, endExclusive))
                    {
                        break;
                    }

                    if (TryAnalyzeDeclaration(state, scope, ref index, endExclusive, topLevel))
                    {
                        break;
                    }

                    index = AnalyzeGeneralStatement(state, scope, index, endExclusive, functionContext, loopDepth);
                    break;
            }
        }
    }

    private bool TryAnalyzeTypeDeclaration(AnalyzerState state, Scope scope, ref int index, int endExclusive, bool topLevel)
    {
        int saved = index;
        int cursor = index;
        while (cursor < endExclusive && state.Tokens[cursor].Kind == TokenKind.Identifier && IsTypeModifier(state.Tokens[cursor].Text))
        {
            state.MarkToken(cursor, "modifier");
            cursor++;
        }

        bool isRecord = false;
        if (cursor < endExclusive && state.Tokens[cursor].Kind == TokenKind.Record)
        {
            isRecord = true;
            state.MarkToken(cursor, "keyword");
            cursor++;
        }

        if (cursor < endExclusive && state.Tokens[cursor].Kind is TokenKind.Class or TokenKind.Struct)
        {
            state.MarkToken(cursor, "keyword");
            cursor++;
        }
        else if (!isRecord)
        {
            index = saved;
            return false;
        }

        if (!IsIdentifier(state.Tokens, cursor))
        {
            index = saved;
            return false;
        }

        int nameIndex = cursor++;
        string typeName = state.Tokens[nameIndex].Text;
        int primaryOpenParen = -1;
        List<ParameterInfo> primaryParameters = [];
        if (cursor < endExclusive && state.Tokens[cursor].Kind == TokenKind.OpenParen)
        {
            primaryOpenParen = cursor;
            cursor++;
            if (!TryReadParameters(state, ref cursor, out primaryParameters))
            {
                index = saved;
                return false;
            }
        }

        int openBraceIndex = FindTopLevelToken(state.Tokens, cursor, endExclusive, TokenKind.OpenBrace);
        int closeBraceIndex = -1;
        int declarationEnd;
        if (openBraceIndex >= 0)
        {
            closeBraceIndex = FindMatching(state.Tokens, openBraceIndex, TokenKind.OpenBrace, TokenKind.CloseBrace, endExclusive);
            declarationEnd = closeBraceIndex < 0 ? endExclusive : closeBraceIndex + 1;
        }
        else
        {
            declarationEnd = FindStatementEnd(state.Tokens, saved, endExclusive);
        }

        TextSpan declarationSpan = new(state.Tokens[saved].Position, Math.Max(0, EndOf(state.Tokens[Math.Max(declarationEnd - 1, saved)]) - state.Tokens[saved].Position));
        PscpServerSymbol typeSymbol = state.AddSymbol(
            scope,
            typeName,
            PscpServerSymbolKind.Type,
            state.Tokens[nameIndex].Span,
            state.Tokens[nameIndex].Span,
            new ScopeSpan(state.Tokens[nameIndex].Position, scope.EndOffset),
            typeName,
            isMutable: false,
            isIntrinsic: false,
            containerSymbolId: null,
            documentation: $"type `{typeName}`");
        state.MarkToken(nameIndex, "type", "declaration");
        state.AddReference(typeSymbol, nameIndex, isDeclaration: true, isWrite: false);

        if (topLevel)
        {
            state.AddDocumentSymbol(new PscpDocumentSymbol(
                typeName,
                23,
                declarationSpan,
                state.Tokens[nameIndex].Span,
                Array.Empty<PscpDocumentSymbol>()));
        }

        Scope typeScope = state.CreateScope(scope, state.Tokens[nameIndex].Position, declarationSpan.End);
        foreach (ParameterInfo parameter in primaryParameters)
        {
            foreach (int typeToken in parameter.TypeTokenIndices)
            {
                state.MarkToken(typeToken, "type");
            }

            if (parameter.IsDiscard)
            {
                state.MarkToken(parameter.NameTokenIndex, "variable", "declaration");
                continue;
            }

            PscpServerSymbol memberSymbol = state.AddSymbol(
                typeScope,
                parameter.Name,
                PscpServerSymbolKind.Property,
                state.Tokens[parameter.NameTokenIndex].Span,
                state.Tokens[parameter.NameTokenIndex].Span,
                new ScopeSpan(state.Tokens[parameter.NameTokenIndex].Position, declarationSpan.End),
                parameter.TypeDisplay,
                isMutable: false,
                isIntrinsic: false,
                containerSymbolId: typeSymbol.Id,
                documentation: $"record member `{typeName}.{parameter.Name}: {parameter.TypeDisplay}`");
            state.MarkToken(parameter.NameTokenIndex, "property", "declaration");
            state.AddReference(memberSymbol, parameter.NameTokenIndex, isDeclaration: true, isWrite: false);
            state.AddTypeMember(typeName, memberSymbol);
        }

        if (primaryOpenParen >= 0)
        {
            state.MarkToken(primaryOpenParen, "operator");
        }

        index = declarationEnd;
        return true;
    }

    private bool TryAnalyzeFunction(AnalyzerState state, Scope scope, ref int index, int endExclusive)
    {
        int saved = index;
        bool isRecursive = state.Match(ref index, TokenKind.Rec, out int recIndex);
        if (isRecursive)
        {
            state.MarkToken(recIndex, "keyword");
        }

        if (!TryReadType(state, ref index, allowSizedArrays: false, out string returnType, out List<int> typeTokens))
        {
            index = saved;
            return false;
        }

        if (!IsIdentifier(state.Tokens, index))
        {
            index = saved;
            return false;
        }

        int nameIndex = index++;
        if (!state.Match(ref index, TokenKind.OpenParen, out _))
        {
            index = saved;
            return false;
        }

        if (!TryReadParameters(state, ref index, out List<ParameterInfo> parameters))
        {
            index = saved;
            return false;
        }

        if (!state.Match(ref index, TokenKind.OpenBrace, out int openBraceIndex))
        {
            index = saved;
            return false;
        }

        int closeBraceIndex = FindMatching(state.Tokens, openBraceIndex, TokenKind.OpenBrace, TokenKind.CloseBrace, endExclusive);
        if (closeBraceIndex < 0)
        {
            closeBraceIndex = endExclusive - 1;
        }

        foreach (int typeToken in typeTokens)
        {
            state.MarkToken(typeToken, "type");
        }

        Token nameToken = state.Tokens[nameIndex];
        PscpServerSymbol functionSymbol = state.AddSymbol(
            scope,
            nameToken.Text,
            PscpServerSymbolKind.Function,
            new TextSpan(nameToken.Position, nameToken.Text.Length),
            new TextSpan(nameToken.Position, nameToken.Text.Length),
            new ScopeSpan(0, state.Snapshot.Text.Length),
            returnType,
            isMutable: false,
            isIntrinsic: false,
            containerSymbolId: null,
            documentation: $"```pscp\n{returnType} {nameToken.Text}({string.Join(", ", parameters.Select(p => $"{p.TypeDisplay} {p.Name}"))})\n```");

        state.MarkToken(nameIndex, "function", "declaration");
        state.AddDocumentSymbol(new PscpDocumentSymbol(
            nameToken.Text,
            12,
            new TextSpan(state.Tokens[saved].Position, Math.Max(0, EndOf(state.Tokens[closeBraceIndex]) - state.Tokens[saved].Position)),
            new TextSpan(nameToken.Position, nameToken.Text.Length),
            Array.Empty<PscpDocumentSymbol>()));

        Scope functionScope = state.CreateScope(scope, state.Tokens[openBraceIndex].Position, EndOf(state.Tokens[closeBraceIndex]));
        FunctionContext functionContext = new(functionSymbol, isRecursive, returnType);

        foreach (ParameterInfo parameter in parameters)
        {
            foreach (int typeToken in parameter.TypeTokenIndices)
            {
                state.MarkToken(typeToken, "type");
            }

            if (parameter.IsDiscard)
            {
                state.MarkToken(parameter.NameTokenIndex, "variable", "declaration");
                continue;
            }

            PscpServerSymbol paramSymbol = state.AddSymbol(
                functionScope,
                parameter.Name,
                PscpServerSymbolKind.Parameter,
                state.Tokens[parameter.NameTokenIndex].Span,
                state.Tokens[parameter.NameTokenIndex].Span,
                new ScopeSpan(state.Tokens[parameter.NameTokenIndex].Position, EndOf(state.Tokens[closeBraceIndex])),
                parameter.TypeDisplay,
                isMutable: false,
                isIntrinsic: false,
                containerSymbolId: functionSymbol.Id,
                documentation: $"parameter `{parameter.Name}: {parameter.TypeDisplay}`");
            state.MarkToken(parameter.NameTokenIndex, "parameter", "declaration");
            state.AddReference(paramSymbol, parameter.NameTokenIndex, isDeclaration: true, isWrite: false);
        }

        state.Signatures[functionSymbol.Name] = new PscpSignatureEntry(
            $"{returnType} {functionSymbol.Name}({string.Join(", ", parameters.Select(p => $"{p.TypeDisplay} {p.Name}"))})",
            parameters.Select(p => p.Name).ToArray(),
            "User-defined function.");

        AnalyzeStatements(state, functionScope, openBraceIndex + 1, closeBraceIndex, functionContext, topLevel: false, loopDepth: 0);
        index = closeBraceIndex + 1;
        return true;
    }

    private bool TryAnalyzeDeclaration(AnalyzerState state, Scope scope, ref int index, int endExclusive, bool topLevel)
    {
        int saved = index;
        bool isMutable;
        string? explicitType = null;
        List<int> typeTokens = [];

        string declarationKeyword;
        if (state.Match(ref index, TokenKind.Let, out int letIndex))
        {
            isMutable = false;
            declarationKeyword = "let";
            state.MarkToken(letIndex, "keyword");
        }
        else if (state.Match(ref index, TokenKind.Var, out int varIndex))
        {
            isMutable = true;
            declarationKeyword = "var";
            state.MarkToken(varIndex, "keyword");
        }
        else
        {
            bool sawMut = state.Match(ref index, TokenKind.Mut, out int mutIndex);
            declarationKeyword = sawMut ? "mut" : string.Empty;
            if (sawMut)
            {
                state.MarkToken(mutIndex, "keyword");
            }

            if (!TryReadType(state, ref index, allowSizedArrays: true, out explicitType, out typeTokens))
            {
                index = saved;
                return false;
            }

            if (index < endExclusive && state.Tokens[index].Kind == TokenKind.OpenParen)
            {
                index = saved;
                return false;
            }

            isMutable = sawMut;
        }

        if (!TryReadBindingTargets(state, ref index, out List<BindingTargetInfo> bindings))
        {
            index = saved;
            return false;
        }

        foreach (int typeToken in typeTokens)
        {
            state.MarkToken(typeToken, "type");
        }

        int statementEnd = FindStatementEnd(state.Tokens, saved, endExclusive);
        int equalIndex = FindToken(state.Tokens, index, statementEnd, TokenKind.Equal);
        bool isInputShorthand = equalIndex >= 0 && IsTerminatorToken(state.Tokens, equalIndex + 1, statementEnd);

        string? inferredType = explicitType;
        if (explicitType is null && equalIndex >= 0 && !isInputShorthand)
        {
            inferredType = InferExpressionType(state, scope, equalIndex + 1, statementEnd);
        }

        foreach (BindingTargetInfo binding in bindings)
        {
            if (binding.IsDiscard)
            {
                state.MarkToken(binding.TokenIndex, "variable", "declaration");
                continue;
            }

            PscpServerSymbol symbol = state.AddSymbol(
                scope,
                binding.Name,
                PscpServerSymbolKind.Local,
                state.Tokens[binding.TokenIndex].Span,
                state.Tokens[binding.TokenIndex].Span,
                new ScopeSpan(state.Tokens[binding.TokenIndex].Position, scope.EndOffset),
                explicitType ?? inferredType,
                isMutable,
                isIntrinsic: false,
                containerSymbolId: null,
                documentation: BuildSymbolDocumentation(
                    binding.Name,
                    explicitType ?? inferredType,
                    isMutable,
                    isInputShorthand,
                    declarationKeyword,
                    explicitType,
                    topLevel,
                    equalIndex >= 0 && !isInputShorthand && IsCompileTimeConstant(state, equalIndex + 1, statementEnd)));
            state.MarkToken(binding.TokenIndex, "variable", isMutable ? new[] { "declaration", "mutable" } : new[] { "declaration", "readonly" });
            state.AddReference(symbol, binding.TokenIndex, isDeclaration: true, isWrite: false);
            if (explicitType is null && !string.IsNullOrWhiteSpace(inferredType))
            {
                TextSpan span = state.Tokens[binding.TokenIndex].Span;
                state.AddInlayHint(span, $": {inferredType}");
            }

            if (topLevel)
            {
                state.AddDocumentSymbol(new PscpDocumentSymbol(
                    binding.Name,
                    13,
                    new TextSpan(state.Tokens[saved].Position, Math.Max(0, EndOf(state.Tokens[Math.Max(statementEnd - 1, saved)]) - state.Tokens[saved].Position)),
                    state.Tokens[binding.TokenIndex].Span,
                    Array.Empty<PscpDocumentSymbol>()));
            }
        }

        if (equalIndex >= 0)
        {
            state.MarkToken(equalIndex, "operator", isInputShorthand ? ["shorthand"] : Array.Empty<string>());
            if (isInputShorthand)
            {
                state.SetHover(equalIndex, BuildInputShorthandHover(snapshotText(state, saved, statementEnd), explicitType, bindings.Count));
            }
            else
            {
                AnalyzeExpression(state, scope, equalIndex + 1, statementEnd, functionContext: null, loopDepth: 0);
            }
        }

        index = statementEnd;
        return true;
    }

    private int AnalyzeGeneralStatement(AnalyzerState state, Scope scope, int index, int endExclusive, FunctionContext? functionContext, int loopDepth)
    {
        int statementEnd = FindStatementEnd(state.Tokens, index, endExclusive);
        int arrowIndex = FindTopLevelToken(state.Tokens, index, statementEnd, TokenKind.Arrow);
        if (arrowIndex >= 0)
        {
            return AnalyzeFastFor(state, scope, index, statementEnd, arrowIndex, functionContext, loopDepth);
        }

        int assignmentIndex = FindTopLevelAssignment(state.Tokens, index, statementEnd);
        if (assignmentIndex >= 0)
        {
            AnalyzeAssignment(state, scope, index, assignmentIndex, statementEnd, functionContext, loopDepth);
            return statementEnd;
        }

        AnalyzeExpression(state, scope, index, statementEnd, functionContext, loopDepth);
        return statementEnd;
    }

    private int AnalyzeIfStatement(AnalyzerState state, Scope scope, int index, int endExclusive, FunctionContext? functionContext, int loopDepth)
    {
        state.MarkToken(index, "keyword");
        int statementEnd = FindStatementEnd(state.Tokens, index, endExclusive);
        int thenIndex = FindTopLevelToken(state.Tokens, index + 1, statementEnd, TokenKind.Then);
        int openBraceIndex = FindTopLevelToken(state.Tokens, index + 1, endExclusive, TokenKind.OpenBrace);

        if (thenIndex >= 0 && (openBraceIndex < 0 || thenIndex < openBraceIndex))
        {
            AnalyzeExpression(state, scope, index, statementEnd, functionContext, loopDepth);
            return statementEnd;
        }

        if (openBraceIndex < 0)
        {
            AnalyzeExpression(state, scope, index + 1, statementEnd, functionContext, loopDepth);
            return statementEnd;
        }

        AnalyzeExpression(state, scope, index + 1, openBraceIndex, functionContext, loopDepth);
        int closeBraceIndex = FindMatching(state.Tokens, openBraceIndex, TokenKind.OpenBrace, TokenKind.CloseBrace, endExclusive);
        Scope thenScope = state.CreateScope(scope, state.Tokens[openBraceIndex].Position, closeBraceIndex < 0 ? scope.EndOffset : EndOf(state.Tokens[closeBraceIndex]));
        AnalyzeStatements(state, thenScope, openBraceIndex + 1, closeBraceIndex < 0 ? endExclusive : closeBraceIndex, functionContext, topLevel: false, loopDepth);

        int next = closeBraceIndex < 0 ? endExclusive : closeBraceIndex + 1;
        next = SkipSeparators(state.Tokens, next, endExclusive);
        if (next < endExclusive && state.Tokens[next].Kind == TokenKind.Else)
        {
            state.MarkToken(next, "keyword");
            next++;
            next = SkipSeparators(state.Tokens, next, endExclusive);
            if (next < endExclusive && state.Tokens[next].Kind == TokenKind.OpenBrace)
            {
                int elseClose = FindMatching(state.Tokens, next, TokenKind.OpenBrace, TokenKind.CloseBrace, endExclusive);
                Scope elseScope = state.CreateScope(scope, state.Tokens[next].Position, elseClose < 0 ? scope.EndOffset : EndOf(state.Tokens[elseClose]));
                AnalyzeStatements(state, elseScope, next + 1, elseClose < 0 ? endExclusive : elseClose, functionContext, topLevel: false, loopDepth);
                return elseClose < 0 ? endExclusive : elseClose + 1;
            }

            int elseEnd = FindStatementEnd(state.Tokens, next, endExclusive);
            AnalyzeExpression(state, scope, next, elseEnd, functionContext, loopDepth);
            return elseEnd;
        }

        return next;
    }

    private int AnalyzeWhileStatement(AnalyzerState state, Scope scope, int index, int endExclusive, FunctionContext? functionContext, int loopDepth)
    {
        state.MarkToken(index, "keyword");
        int doIndex = FindTopLevelToken(state.Tokens, index + 1, endExclusive, TokenKind.Do);
        int openBraceIndex = FindTopLevelToken(state.Tokens, index + 1, endExclusive, TokenKind.OpenBrace);
        if (doIndex >= 0 && (openBraceIndex < 0 || doIndex < openBraceIndex))
        {
            AnalyzeExpression(state, scope, index + 1, doIndex, functionContext, loopDepth);
            Scope bodyScope = state.CreateScope(scope, state.Tokens[doIndex].Position, scope.EndOffset);
            int bodyEnd = FindStatementEnd(state.Tokens, doIndex + 1, endExclusive);
            AnalyzeGeneralStatement(state, bodyScope, doIndex + 1, bodyEnd, functionContext, loopDepth + 1);
            return bodyEnd;
        }

        if (openBraceIndex >= 0)
        {
            AnalyzeExpression(state, scope, index + 1, openBraceIndex, functionContext, loopDepth);
            int closeBrace = FindMatching(state.Tokens, openBraceIndex, TokenKind.OpenBrace, TokenKind.CloseBrace, endExclusive);
            Scope bodyScope = state.CreateScope(scope, state.Tokens[openBraceIndex].Position, closeBrace < 0 ? scope.EndOffset : EndOf(state.Tokens[closeBrace]));
            AnalyzeStatements(state, bodyScope, openBraceIndex + 1, closeBrace < 0 ? endExclusive : closeBrace, functionContext, topLevel: false, loopDepth + 1);
            return closeBrace < 0 ? endExclusive : closeBrace + 1;
        }

        int end = FindStatementEnd(state.Tokens, index, endExclusive);
        AnalyzeExpression(state, scope, index + 1, end, functionContext, loopDepth);
        return end;
    }

    private int AnalyzeForStatement(AnalyzerState state, Scope scope, int index, int endExclusive, FunctionContext? functionContext, int loopDepth)
    {
        state.MarkToken(index, "keyword");
        int cursor = index + 1;
        if (!IsIdentifier(state.Tokens, cursor))
        {
            return FindStatementEnd(state.Tokens, index, endExclusive);
        }

        int bindingIndex = cursor++;
        state.MarkToken(bindingIndex, "variable", "declaration");
        if (!state.Match(ref cursor, TokenKind.In, out int inIndex))
        {
            return FindStatementEnd(state.Tokens, index, endExclusive);
        }

        state.MarkToken(inIndex, "keyword");
        int doIndex = FindTopLevelToken(state.Tokens, cursor, endExclusive, TokenKind.Do);
        int openBraceIndex = FindTopLevelToken(state.Tokens, cursor, endExclusive, TokenKind.OpenBrace);
        int sourceEnd = doIndex >= 0 && (openBraceIndex < 0 || doIndex < openBraceIndex) ? doIndex : openBraceIndex;
        if (sourceEnd < 0)
        {
            sourceEnd = FindStatementEnd(state.Tokens, index, endExclusive);
        }

        AnalyzeExpression(state, scope, cursor, sourceEnd, functionContext, loopDepth);

        int scopeEnd = scope.EndOffset;
        if (doIndex >= 0 && (openBraceIndex < 0 || doIndex < openBraceIndex))
        {
            scopeEnd = EndOf(state.Tokens[Math.Max(FindStatementEnd(state.Tokens, doIndex + 1, endExclusive) - 1, doIndex)]);
        }
        else if (openBraceIndex >= 0)
        {
            int close = FindMatching(state.Tokens, openBraceIndex, TokenKind.OpenBrace, TokenKind.CloseBrace, endExclusive);
            if (close >= 0)
            {
                scopeEnd = EndOf(state.Tokens[close]);
            }
        }

        Scope bodyScope = state.CreateScope(scope, state.Tokens[bindingIndex].Position, scopeEnd);
        if (state.Tokens[bindingIndex].Text != "_")
        {
            string? itemType = InferLoopVariableType(state, scope, cursor, sourceEnd);
            PscpServerSymbol loopSymbol = state.AddSymbol(
                bodyScope,
                state.Tokens[bindingIndex].Text,
                PscpServerSymbolKind.LoopVariable,
                state.Tokens[bindingIndex].Span,
                state.Tokens[bindingIndex].Span,
                new ScopeSpan(state.Tokens[bindingIndex].Position, scopeEnd),
                itemType,
                isMutable: false,
                isIntrinsic: false,
                containerSymbolId: null,
                documentation: $"loop variable `{state.Tokens[bindingIndex].Text}`");
            state.AddReference(loopSymbol, bindingIndex, isDeclaration: true, isWrite: false);
        }

        if (doIndex >= 0 && (openBraceIndex < 0 || doIndex < openBraceIndex))
        {
            return AnalyzeGeneralStatement(state, bodyScope, doIndex + 1, FindStatementEnd(state.Tokens, doIndex + 1, endExclusive), functionContext, loopDepth + 1);
        }

        if (openBraceIndex >= 0)
        {
            int close = FindMatching(state.Tokens, openBraceIndex, TokenKind.OpenBrace, TokenKind.CloseBrace, endExclusive);
            AnalyzeStatements(state, bodyScope, openBraceIndex + 1, close < 0 ? endExclusive : close, functionContext, topLevel: false, loopDepth + 1);
            return close < 0 ? endExclusive : close + 1;
        }

        return FindStatementEnd(state.Tokens, index, endExclusive);
    }

    private int AnalyzeFastFor(AnalyzerState state, Scope scope, int start, int statementEnd, int arrowIndex, FunctionContext? functionContext, int loopDepth)
    {
        AnalyzeExpression(state, scope, start, arrowIndex, functionContext, loopDepth);

        int cursor = arrowIndex + 1;
        if (!IsIdentifier(state.Tokens, cursor))
        {
            return statementEnd;
        }

        int firstBinding = cursor++;
        int? secondBinding = null;
        if (cursor < statementEnd && state.Tokens[cursor].Kind == TokenKind.Comma && IsIdentifier(state.Tokens, cursor + 1))
        {
            secondBinding = cursor + 1;
            cursor += 2;
        }

        int bodyStart = cursor;
        bool doForm = cursor < statementEnd && state.Tokens[cursor].Kind == TokenKind.Do;
        if (doForm)
        {
            bodyStart++;
        }

        int scopeEnd = scope.EndOffset;
        if (bodyStart < statementEnd && state.Tokens[bodyStart].Kind == TokenKind.OpenBrace)
        {
            int close = FindMatching(state.Tokens, bodyStart, TokenKind.OpenBrace, TokenKind.CloseBrace, statementEnd);
            if (close >= 0)
            {
                scopeEnd = EndOf(state.Tokens[close]);
            }
        }

        string? itemType = InferLoopVariableType(state, scope, start, arrowIndex);
        Scope bodyScope = state.CreateScope(scope, state.Tokens[firstBinding].Position, scopeEnd);
        AddFastForBinding(state, bodyScope, firstBinding, secondBinding is not null, isIndex: secondBinding is not null, typeDisplay: secondBinding is null ? itemType : "int");
        if (secondBinding is not null)
        {
            AddFastForBinding(state, bodyScope, secondBinding.Value, hasSecond: true, isIndex: false, typeDisplay: itemType);
        }

        if (bodyStart < statementEnd && state.Tokens[bodyStart].Kind == TokenKind.OpenBrace)
        {
            int close = FindMatching(state.Tokens, bodyStart, TokenKind.OpenBrace, TokenKind.CloseBrace, statementEnd);
            AnalyzeStatements(state, bodyScope, bodyStart + 1, close < 0 ? statementEnd : close, functionContext, topLevel: false, loopDepth + 1);
            return close < 0 ? statementEnd : close + 1;
        }

        AnalyzeExpression(state, bodyScope, bodyStart, statementEnd, functionContext, loopDepth + 1);
        return statementEnd;
    }

    private void AddFastForBinding(AnalyzerState state, Scope scope, int tokenIndex, bool hasSecond, bool isIndex, string? typeDisplay = null)
    {
        state.MarkToken(tokenIndex, "variable", "declaration");
        if (state.Tokens[tokenIndex].Text == "_")
        {
            return;
        }

        string? type = typeDisplay ?? (isIndex ? "int" : null);
        PscpServerSymbol symbol = state.AddSymbol(
            scope,
            state.Tokens[tokenIndex].Text,
            PscpServerSymbolKind.LoopVariable,
            state.Tokens[tokenIndex].Span,
            state.Tokens[tokenIndex].Span,
            new ScopeSpan(state.Tokens[tokenIndex].Position, scope.EndOffset),
            type,
            isMutable: false,
            isIntrinsic: false,
            containerSymbolId: null,
            documentation: hasSecond && isIndex
                ? $"fast-iteration index `{state.Tokens[tokenIndex].Text}`"
                : $"fast-iteration variable `{state.Tokens[tokenIndex].Text}`");
        state.AddReference(symbol, tokenIndex, isDeclaration: true, isWrite: false);
    }

    private void AnalyzeAssignment(AnalyzerState state, Scope scope, int start, int assignmentIndex, int statementEnd, FunctionContext? functionContext, int loopDepth)
    {
        state.MarkToken(assignmentIndex, "operator");
        AnalyzeAssignmentTarget(state, scope, start, assignmentIndex, state.Tokens[assignmentIndex].Kind, functionContext, loopDepth);
        AnalyzeExpression(state, scope, assignmentIndex + 1, statementEnd, functionContext, loopDepth);
    }

    private void AnalyzeAssignmentTarget(AnalyzerState state, Scope scope, int start, int endExclusive, TokenKind assignmentKind, FunctionContext? functionContext, int loopDepth)
    {
        start = SkipSeparators(state.Tokens, start, endExclusive);
        while (endExclusive > start && IsTerminatorToken(state.Tokens, endExclusive - 1, endExclusive))
        {
            endExclusive--;
        }

        if (TryAnalyzeSimpleAssignmentTargets(state, scope, start, endExclusive, assignmentKind))
        {
            return;
        }

        AnalyzeExpression(state, scope, start, endExclusive, functionContext, loopDepth);
    }

    private bool TryAnalyzeSimpleAssignmentTargets(AnalyzerState state, Scope scope, int start, int endExclusive, TokenKind assignmentKind)
    {
        if (start >= endExclusive)
        {
            return true;
        }

        if (IsIdentifier(state.Tokens, start) && start + 1 == endExclusive)
        {
            AnalyzeAssignableBinding(state, scope, start, assignmentKind);
            return true;
        }

        if (state.Tokens[start].Kind != TokenKind.OpenParen)
        {
            return false;
        }

        int close = FindMatching(state.Tokens, start, TokenKind.OpenParen, TokenKind.CloseParen, endExclusive);
        if (close != endExclusive - 1)
        {
            return false;
        }

        int elementStart = start + 1;
        bool sawElement = false;
        while (elementStart < close)
        {
            elementStart = SkipSeparators(state.Tokens, elementStart, close);
            if (elementStart >= close)
            {
                break;
            }

            int comma = FindTopLevelToken(state.Tokens, elementStart, close, TokenKind.Comma);
            int elementEnd = comma >= 0 ? comma : close;
            if (!TryAnalyzeSimpleAssignmentTargets(state, scope, elementStart, elementEnd, assignmentKind))
            {
                return false;
            }

            sawElement = true;
            elementStart = comma >= 0 ? comma + 1 : close;
        }

        return sawElement;
    }

    private void AnalyzeAssignableBinding(AnalyzerState state, Scope scope, int tokenIndex, TokenKind assignmentKind)
    {
        if (state.Tokens[tokenIndex].Text == "_")
        {
            state.MarkToken(tokenIndex, "variable");
            return;
        }

        if (!state.TryResolve(scope, state.Tokens[tokenIndex].Text, state.Tokens[tokenIndex].Position, out PscpServerSymbol? symbol))
        {
            return;
        }

        state.AddReference(symbol!, tokenIndex, isDeclaration: false, isWrite: true);
    }

    private string? InferLoopVariableType(AnalyzerState state, Scope scope, int start, int endExclusive)
    {
        string? sourceType = InferExpressionType(state, scope, start, endExclusive);
        if (string.IsNullOrWhiteSpace(sourceType))
        {
            return null;
        }

        if (sourceType.EndsWith("[]", StringComparison.Ordinal))
        {
            return sourceType[..^2];
        }

        return sourceType switch
        {
            "IEnumerable<int>" => "int",
            "IEnumerable<long>" => "long",
            _ => null,
        };
    }

    // Guide §7.2: the output shorthand shows `write` or `writeln` and the rendering rule that the value's
    // static type selects (spec §18.3).
    private static string BuildOutputShorthandHover(bool isWrite, string? valueType)
    {
        string method = isWrite ? "write" : "writeln";
        string newline = isWrite ? "without a trailing newline" : "followed by a newline";
        List<string> lines =
        [
            $"```pscp\n{(isWrite ? "=" : "+=")} expr\n```",
            $"Output shorthand: it writes the rendered value {newline}.",
        ];

        if (RenderingRule(valueType) is { } rule)
        {
            lines.Add($"`{SizedArrayPattern.Replace(valueType!, "[]")}` renders as {rule}.");
        }

        lines.Add($"→ C#: `__pscp_stdout.{method}(expr)`");
        lines.Add($"[Spec §18.3 렌더링 규칙]({DiagnosticCodes.SpecUrl}#{Uri.EscapeDataString("183-렌더링-규칙")})");
        return string.Join("\n\n", lines);
    }

    // Spec §18.3. Only the shapes the analyzer can name from a static type are described.
    private static string? RenderingRule(string? valueType)
    {
        if (string.IsNullOrWhiteSpace(valueType))
        {
            return null;
        }

        // A declared array carries its size expressions (`char[n][n]`); the rendering rule depends only on
        // the element type.
        valueType = SizedArrayPattern.Replace(valueType!, "[]");

        if (valueType is "int" or "long" or "short" or "byte" or "sbyte" or "uint" or "ulong" or "ushort" or "decimal")
        {
            return "a decimal number";
        }

        if (valueType is "double" or "float")
        {
            return "the shortest round-trippable decimal form, never in exponent notation";
        }

        if (valueType is "bool")
        {
            return "`true` or `false`, lowercase";
        }

        if (valueType is "char" or "string")
        {
            return "the characters themselves";
        }

        if (valueType.StartsWith('(') || valueType.StartsWith("KeyValuePair", StringComparison.Ordinal))
        {
            return "its elements joined by one space";
        }

        string? element = CollectionElementType(valueType);
        if (element is null)
        {
            return null;
        }

        if (element is "char")
        {
            return "its characters joined with no separator";
        }

        return CollectionElementType(element) is not null || element.StartsWith('(')
            ? "one element per line, each element by the rules above"
            : "its elements joined by one space";
    }

    private static string? CollectionElementType(string type)
    {
        if (type.EndsWith("[]", StringComparison.Ordinal))
        {
            return type[..^2];
        }

        foreach (string name in (string[])["List<", "IEnumerable<", "HashSet<", "SortedSet<", "Queue<", "Stack<", "LinkedList<"])
        {
            if (type.StartsWith(name, StringComparison.Ordinal) && type.EndsWith('>'))
            {
                return type[name.Length..^1];
            }
        }

        return null;
    }

    // Guide §7.2: the input shorthand `=` shows how each element is read, how many are read, and the
    // element-by-element lowering (spec §17.1).
    private static string BuildInputShorthandHover(string shape, string? declaredType, int bindingCount)
    {
        string element = declaredType is null ? "value" : declaredType.Split('[')[0].TrimEnd('?');
        string reader = element switch
        {
            "int" => "readInt()",
            "long" => "readLong()",
            "double" => "readDouble()",
            "decimal" => "readDecimal()",
            "bool" => "readBool()",
            "string" => "readString()   // one whitespace-delimited token",
            "char" => "readChar()   // one character, whitespace skipped",
            _ => "read the element",
        };

        // `int[n][m] grid =` reads `n × m` elements in row-major order; `int a, b =` reads one per name.
        int dimensions = declaredType is null ? 0 : declaredType.Count(character => character == '[');
        string count = dimensions switch
        {
            0 when bindingCount > 1 => $"{bindingCount} values, one per name, in declaration order",
            0 => "one value",
            1 => "one value per element, in order",
            _ => $"one value per element in {dimensions} dimensions, in row-major order",
        };

        return $"```pscp\n{shape}\n```\n\nInput shorthand: it reads {count} from `stdin`. A `char` element is read character by character, so a grid reads the same whether or not its rows carry spaces.\n\n→ C#: `__pscp_stdin.{reader}` per element\n\n[Spec §17.1 선언 기반 입력 shorthand]({DiagnosticCodes.SpecUrl}#{Uri.EscapeDataString("171-선언-기반-입력-shorthand")})";
    }

    // Guide §7.2: a binding shows its declaration form, its type, whether it is immutable, and what the
    // binding lowers to (spec §9.6).
    private static string BuildSymbolDocumentation(
        string name,
        string? typeDisplay,
        bool isMutable,
        bool isInputShorthand,
        string declarationKeyword = "",
        string? explicitType = null,
        bool topLevel = false,
        bool isCompileTimeConstant = false)
    {
        string prefix = declarationKeyword.Length > 0 ? declarationKeyword + " " : string.Empty;
        string shape = explicitType is null
            ? $"{prefix}{name}{(typeDisplay is null ? string.Empty : $": {typeDisplay}")}"
            : $"{prefix}{explicitType} {name}";

        List<string> lines =
        [
            $"```pscp\n{shape}\n```",
            isMutable ? "Mutable binding." : "Immutable binding.",
        ];

        if (isInputShorthand)
        {
            lines.Add("Declared by the input shorthand: the value is read from `stdin`.");
        }

        // Spec §9.6: an immutable binding whose initializer is a compile-time constant becomes `const`, and a
        // `const` binding is the only one a type declaration can see (spec §9.5). Everything else stays a
        // local of the generated entry-point method.
        string type = typeDisplay ?? "var";
        if (!isMutable && isCompileTimeConstant)
        {
            lines.Add($"→ C#: `const {type} {name} = ...;`");
            lines.Add("A compile-time constant, so it is visible inside a type declaration as well.");
        }
        else
        {
            lines.Add($"→ C#: `{type} {name} = ...;`{(topLevel ? "  (a local of the generated entry point)" : string.Empty)}");
        }

        lines.Add($"[Spec §9.6 const / readonly lowering]({DiagnosticCodes.SpecUrl}#{Uri.EscapeDataString("96-const--readonly-lowering")})");
        return string.Join("\n\n", lines);
    }

    // Spec §9.6: only a literal-and-operator expression is a compile-time constant.
    private static bool IsCompileTimeConstant(AnalyzerState state, int start, int endExclusive)
    {
        bool sawValue = false;
        for (int i = start; i < endExclusive; i++)
        {
            switch (state.Tokens[i].Kind)
            {
                case TokenKind.IntegerLiteral:
                case TokenKind.FloatLiteral:
                case TokenKind.StringLiteral:
                case TokenKind.CharLiteral:
                case TokenKind.True:
                case TokenKind.False:
                    sawValue = true;
                    break;
                case TokenKind.Plus:
                case TokenKind.Minus:
                case TokenKind.Star:
                case TokenKind.Slash:
                case TokenKind.Percent:
                case TokenKind.OpenParen:
                case TokenKind.CloseParen:
                case TokenKind.NewLine:
                    break;
                default:
                    return false;
            }
        }

        return sawValue;
    }

    private static bool IsTypeModifier(string text)
        => text is "public"
            or "private"
            or "protected"
            or "internal"
            or "readonly"
            or "partial"
            or "sealed"
            or "abstract"
            or "static"
            or "unsafe";

    private static string snapshotText(AnalyzerState state, int startTokenIndex, int endTokenIndexExclusive)
    {
        int end = Math.Min(endTokenIndexExclusive, state.Tokens.Count);
        StringBuilder text = new();
        for (int i = startTokenIndex; i < end; i++)
        {
            Token token = state.Tokens[i];
            if (token.Text == "\n")
            {
                text.Append('\n');
                continue;
            }

            text.Append(token.Text);
            // `char[n][n] board` must not come out as `char [n ][n ]board`: a bracket, a separator or a
            // member access binds tight to what precedes it.
            TokenKind next = i + 1 < end ? state.Tokens[i + 1].Kind : TokenKind.EndOfFile;
            bool tightNext = next is TokenKind.CloseBracket or TokenKind.CloseParen
                or TokenKind.OpenBracket or TokenKind.OpenParen or TokenKind.Comma or TokenKind.Dot
                or TokenKind.Semicolon or TokenKind.LessThan or TokenKind.GreaterThan or TokenKind.EndOfFile;
            // `char[n][n] board`: a closing bracket still needs a space before the name that follows it.
            bool closingBeforeName = token.Kind is TokenKind.CloseBracket or TokenKind.CloseParen or TokenKind.GreaterThan
                && next is TokenKind.Identifier;
            if (closingBeforeName || (NeedsTrailingSpace(token.Kind) && !tightNext))
            {
                text.Append(' ');
            }
        }

        return text.ToString().Trim();
    }

    private static bool NeedsTrailingSpace(TokenKind kind)
        => kind is TokenKind.Identifier or TokenKind.IntegerLiteral or TokenKind.FloatLiteral or TokenKind.StringLiteral or TokenKind.CharLiteral
            or TokenKind.Let or TokenKind.Var or TokenKind.Mut or TokenKind.Rec or TokenKind.If or TokenKind.Then or TokenKind.Else
            or TokenKind.For or TokenKind.In or TokenKind.Do or TokenKind.While or TokenKind.Return or TokenKind.And or TokenKind.Or or TokenKind.Xor or TokenKind.Not;

    private sealed record ParameterInfo(string Name, bool IsDiscard, string TypeDisplay, int NameTokenIndex, IReadOnlyList<int> TypeTokenIndices);

    private sealed record BindingTargetInfo(string Name, bool IsDiscard, int TokenIndex);

    private sealed record FunctionContext(PscpServerSymbol Symbol, bool IsRecursive, string ReturnType);
}
