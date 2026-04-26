using System.Text.RegularExpressions;
using Pscp.Transpiler;

namespace Pscp.LanguageServer;

internal sealed partial class PscpAnalyzer
{
    private static readonly Regex IdentifierPattern = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    public PscpAnalysisResult Analyze(DocumentSnapshot snapshot)
    {
        Lexer lexer = new(snapshot.Text);
        IReadOnlyList<Token> tokens = lexer.Lex();
        Parser parser = new(tokens);
        _ = parser.ParseProgram();

        AnalyzerState state = new(snapshot, tokens);
        foreach (Diagnostic diagnostic in lexer.Diagnostics)
        {
            state.AddDiagnostic("PSCP1000", diagnostic.Message, diagnostic.Span, ToServerSeverity(diagnostic.Severity));
        }

        foreach (Diagnostic diagnostic in parser.Diagnostics)
        {
            state.AddDiagnostic("PSCP1001", diagnostic.Message, diagnostic.Span, ToServerSeverity(diagnostic.Severity));
        }

        Scope globalScope = state.CreateScope(null, 0, snapshot.Text.Length);
        InitializeIntrinsics(state, globalScope);
        AnalyzeStatements(state, globalScope, 0, tokens.Count - 1, null, topLevel: true, loopDepth: 0);
        if (!lexer.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            && !parser.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            AddTranspilerWarnings(state, snapshot.Text);
        }

        AddDefaultTokenClassifications(state);
        return state.Build();
    }

    private static void AddTranspilerWarnings(AnalyzerState state, string text)
    {
        TranspilationResult result;
        try
        {
            result = PscpTranspiler.Transpile(text);
        }
        catch
        {
            return;
        }

        foreach (Diagnostic diagnostic in result.Diagnostics)
        {
            if (diagnostic.Severity == DiagnosticSeverity.Error)
            {
                continue;
            }

            state.AddDiagnostic("PSCP3001", diagnostic.Message, diagnostic.Span, ServerDiagnosticSeverity.Warning);
        }
    }

    private static ServerDiagnosticSeverity ToServerSeverity(DiagnosticSeverity severity)
        => severity == DiagnosticSeverity.Warning ? ServerDiagnosticSeverity.Warning : ServerDiagnosticSeverity.Error;

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
                    if (loopDepth == 0)
                    {
                        state.AddDiagnostic("PSCP2008", $"`{token.Text}` is valid only inside a loop.", token.Span, ServerDiagnosticSeverity.Error);
                    }

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
                    state.MarkToken(index, "operator");
                    state.SetHover(index, token.Kind == TokenKind.Equal
                        ? "```pscp\n= expr\n```\n\nWrites the rendered value without a trailing newline."
                        : "```pscp\n+= expr\n```\n\nWrites the rendered value followed by a newline.");
                    AnalyzeExpression(state, scope, index + 1, FindStatementEnd(state.Tokens, index, endExclusive), functionContext, loopDepth);
                    index = FindStatementEnd(state.Tokens, index, endExclusive);
                    break;
                default:
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

        if (state.Match(ref index, TokenKind.Let, out int letIndex))
        {
            isMutable = false;
            state.MarkToken(letIndex, "keyword");
        }
        else if (state.Match(ref index, TokenKind.Var, out int varIndex))
        {
            isMutable = true;
            state.MarkToken(varIndex, "keyword");
        }
        else
        {
            bool sawMut = state.Match(ref index, TokenKind.Mut, out int mutIndex);
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
                documentation: BuildSymbolDocumentation(binding.Name, explicitType ?? inferredType, isMutable, isInputShorthand));
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
            state.MarkToken(equalIndex, "operator");
            if (isInputShorthand)
            {
                state.SetHover(equalIndex, $"```pscp\n{snapshotText(state, saved, statementEnd)}\n```\n\nDeclaration-based input shorthand. This lowers to the corresponding `stdin.*` helper.");
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

        Scope bodyScope = state.CreateScope(scope, state.Tokens[firstBinding].Position, scopeEnd);
        AddFastForBinding(state, bodyScope, firstBinding, secondBinding is not null, isIndex: secondBinding is not null);
        if (secondBinding is not null)
        {
            AddFastForBinding(state, bodyScope, secondBinding.Value, hasSecond: true, isIndex: false);
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

    private void AddFastForBinding(AnalyzerState state, Scope scope, int tokenIndex, bool hasSecond, bool isIndex)
    {
        state.MarkToken(tokenIndex, "variable", "declaration");
        if (state.Tokens[tokenIndex].Text == "_")
        {
            return;
        }

        string? type = isIndex ? "int" : null;
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
        if (!symbol!.IsMutable
            && symbol.Kind is not PscpServerSymbolKind.Function and not PscpServerSymbolKind.Intrinsic
            && !IsKnownCollectionMutationAssignment(symbol.TypeDisplay, assignmentKind))
        {
            state.AddDiagnostic("PSCP2007", $"Cannot assign to immutable binding `{symbol.Name}`.", state.Tokens[tokenIndex].Span, ServerDiagnosticSeverity.Error, symbol.Id);
        }
    }

    private static bool IsKnownCollectionMutationAssignment(string? typeDisplay, TokenKind assignmentKind)
    {
        if (assignmentKind is not (TokenKind.PlusEqual or TokenKind.MinusEqual)
            || string.IsNullOrWhiteSpace(typeDisplay))
        {
            return false;
        }

        string normalized = PscpExternalMetadata.NormalizeTypeReceiver(typeDisplay!);
        return normalized is "List"
            or "LinkedList"
            or "Queue"
            or "Stack"
            or "HashSet"
            or "SortedSet"
            or "Dictionary"
            or "PriorityQueue";
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

    private static string BuildSymbolDocumentation(string name, string? typeDisplay, bool isMutable, bool isInputShorthand)
    {
        List<string> lines = [$"`{name}`"];
        if (!string.IsNullOrWhiteSpace(typeDisplay))
        {
            lines.Add($"type: `{typeDisplay}`");
        }

        lines.Add(isMutable ? "mutable binding" : "immutable binding");
        if (isInputShorthand)
        {
            lines.Add("declared via input shorthand");
        }

        return string.Join("\n\n", lines);
    }

    private static string snapshotText(AnalyzerState state, int startTokenIndex, int endTokenIndexExclusive)
        => string.Concat(state.Tokens.Skip(startTokenIndex).Take(Math.Max(0, endTokenIndexExclusive - startTokenIndex)).Select(token => token.Text == "\n" ? "\n" : token.Text + (NeedsTrailingSpace(token.Kind) ? " " : string.Empty))).Trim();

    private static bool NeedsTrailingSpace(TokenKind kind)
        => kind is TokenKind.Identifier or TokenKind.IntegerLiteral or TokenKind.FloatLiteral or TokenKind.StringLiteral or TokenKind.CharLiteral
            or TokenKind.Let or TokenKind.Var or TokenKind.Mut or TokenKind.Rec or TokenKind.If or TokenKind.Then or TokenKind.Else
            or TokenKind.For or TokenKind.In or TokenKind.Do or TokenKind.While or TokenKind.Return or TokenKind.And or TokenKind.Or or TokenKind.Xor or TokenKind.Not;

    private sealed record ParameterInfo(string Name, bool IsDiscard, string TypeDisplay, int NameTokenIndex, IReadOnlyList<int> TypeTokenIndices);

    private sealed record BindingTargetInfo(string Name, bool IsDiscard, int TokenIndex);

    private sealed record FunctionContext(PscpServerSymbol Symbol, bool IsRecursive, string ReturnType);
}
