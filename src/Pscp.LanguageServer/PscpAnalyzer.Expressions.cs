using Pscp.Transpiler;

namespace Pscp.LanguageServer;

internal sealed partial class PscpAnalyzer
{
    private static readonly HashSet<string> KnownAggregators = new(StringComparer.Ordinal)
    {
        "sum",
        "sumBy",
        "min",
        "max",
        "minBy",
        "maxBy",
        "count",
        "any",
        "all",
        "find",
        "findIndex",
        "findLastIndex",
        "chmin",
        "chmax",
    };

    private void AnalyzeExpression(
        AnalyzerState state,
        Scope scope,
        int start,
        int endExclusive,
        FunctionContext? functionContext,
        int loopDepth)
    {
        start = SkipSeparators(state.Tokens, start, endExclusive);
        if (start >= endExclusive)
        {
            return;
        }

        int arrowIndex = FindTopLevelToken(state.Tokens, start, endExclusive, TokenKind.Arrow);
        if (arrowIndex >= 0)
        {
            AnalyzeFastFor(state, scope, start, endExclusive, arrowIndex, functionContext, loopDepth);
            return;
        }

        int index = start;
        while (index < endExclusive)
        {
            Token token = state.Tokens[index];
            switch (token.Kind)
            {
                case TokenKind.OpenParen:
                    if (TryAnalyzeParenthesizedLambda(state, scope, ref index, endExclusive, functionContext, loopDepth))
                    {
                        continue;
                    }

                    int closeParen = FindMatching(state.Tokens, index, TokenKind.OpenParen, TokenKind.CloseParen, endExclusive);
                    if (closeParen >= 0)
                    {
                        AnalyzeExpression(state, scope, index + 1, closeParen, functionContext, loopDepth);
                        index = closeParen + 1;
                        continue;
                    }

                    break;
                case TokenKind.OpenBracket:
                {
                    int closeBracket = FindMatching(state.Tokens, index, TokenKind.OpenBracket, TokenKind.CloseBracket, endExclusive);
                    if (closeBracket >= 0)
                    {
                        AnalyzeExpression(state, scope, index + 1, closeBracket, functionContext, loopDepth);
                        index = closeBracket + 1;
                        continue;
                    }

                    break;
                }
                case TokenKind.OpenBrace:
                {
                    int closeBrace = FindMatching(state.Tokens, index, TokenKind.OpenBrace, TokenKind.CloseBrace, endExclusive);
                    Scope blockScope = state.CreateScope(scope, token.Position, closeBrace >= 0 ? EndOf(state.Tokens[closeBrace]) : scope.EndOffset);
                    AnalyzeStatements(state, blockScope, index + 1, closeBrace < 0 ? endExclusive : closeBrace, functionContext, topLevel: false, loopDepth);
                    index = closeBrace < 0 ? endExclusive : closeBrace + 1;
                    continue;
                }
                case TokenKind.Identifier:
                    if (TryAnalyzeSingleParameterLambda(state, scope, ref index, endExclusive, functionContext, loopDepth))
                    {
                        continue;
                    }

                    AnalyzeIdentifierUsage(state, scope, index);
                    break;
                case TokenKind.Dot when index + 1 < endExclusive && state.Tokens[index + 1].Kind == TokenKind.IntegerLiteral:
                    state.MarkToken(index, "operator");
                    break;
                case TokenKind.FatArrow:
                    state.MarkToken(index, "operator");
                    break;
            }

            index++;
        }
    }

    private bool TryAnalyzeSingleParameterLambda(
        AnalyzerState state,
        Scope scope,
        ref int index,
        int endExclusive,
        FunctionContext? functionContext,
        int loopDepth)
    {
        if (!IsIdentifier(state.Tokens, index))
        {
            return false;
        }

        int? fatArrowIndex = NextNonTrivia(state.Tokens, index + 1);
        if (fatArrowIndex is not int arrow || arrow >= endExclusive || state.Tokens[arrow].Kind != TokenKind.FatArrow)
        {
            return false;
        }

        Scope lambdaScope = state.CreateScope(scope, state.Tokens[index].Position, scope.EndOffset);
        AddLambdaParameter(state, lambdaScope, index, scope.EndOffset, InferSingleLambdaParameterTypeFromCallContext(state, scope, index));
        state.MarkToken(arrow, "operator");

        int bodyStart = SkipSeparators(state.Tokens, arrow + 1, endExclusive);
        int bodyEnd = AnalyzeLambdaBody(state, lambdaScope, bodyStart, endExclusive, functionContext, loopDepth);
        index = bodyEnd;
        return true;
    }

    private bool TryAnalyzeParenthesizedLambda(
        AnalyzerState state,
        Scope scope,
        ref int index,
        int endExclusive,
        FunctionContext? functionContext,
        int loopDepth)
    {
        int openParen = index;
        int closeParen = FindMatching(state.Tokens, openParen, TokenKind.OpenParen, TokenKind.CloseParen, endExclusive);
        if (closeParen < 0)
        {
            return false;
        }

        int? afterClose = NextNonTrivia(state.Tokens, closeParen + 1);
        if (afterClose is not int arrow || arrow >= endExclusive || state.Tokens[arrow].Kind != TokenKind.FatArrow)
        {
            return false;
        }

        List<int> parameterTokens = [];
        for (int cursor = openParen + 1; cursor < closeParen; cursor++)
        {
            Token token = state.Tokens[cursor];
            if (token.Kind is TokenKind.NewLine or TokenKind.Semicolon or TokenKind.Comma)
            {
                continue;
            }

            if (token.Kind != TokenKind.Identifier)
            {
                return false;
            }

            parameterTokens.Add(cursor);
        }

        Scope lambdaScope = state.CreateScope(scope, state.Tokens[openParen].Position, scope.EndOffset);
        string? unaryParameterType = parameterTokens.Count == 1
            ? InferSingleLambdaParameterTypeFromCallContext(state, scope, parameterTokens[0])
            : null;
        foreach (int parameterToken in parameterTokens)
        {
            AddLambdaParameter(state, lambdaScope, parameterToken, scope.EndOffset, unaryParameterType);
        }

        state.MarkToken(arrow, "operator");
        int bodyStart = SkipSeparators(state.Tokens, arrow + 1, endExclusive);
        int bodyEnd = AnalyzeLambdaBody(state, lambdaScope, bodyStart, endExclusive, functionContext, loopDepth);
        index = bodyEnd;
        return true;
    }

    private int AnalyzeLambdaBody(
        AnalyzerState state,
        Scope lambdaScope,
        int bodyStart,
        int endExclusive,
        FunctionContext? functionContext,
        int loopDepth)
    {
        if (bodyStart >= endExclusive)
        {
            return endExclusive;
        }

        if (state.Tokens[bodyStart].Kind == TokenKind.OpenBrace)
        {
            int closeBrace = FindMatching(state.Tokens, bodyStart, TokenKind.OpenBrace, TokenKind.CloseBrace, endExclusive);
            AnalyzeStatements(state, lambdaScope, bodyStart + 1, closeBrace < 0 ? endExclusive : closeBrace, functionContext, topLevel: false, loopDepth);
            return closeBrace < 0 ? endExclusive : closeBrace + 1;
        }

        int bodyEnd = FindExpressionBoundary(state.Tokens, bodyStart, endExclusive);
        AnalyzeExpression(state, lambdaScope, bodyStart, bodyEnd, functionContext, loopDepth);
        return bodyEnd;
    }

    private void AddLambdaParameter(AnalyzerState state, Scope scope, int tokenIndex, int scopeEnd, string? typeDisplay = null)
    {
        state.MarkToken(tokenIndex, "parameter", "declaration");
        if (state.Tokens[tokenIndex].Text == "_")
        {
            return;
        }

        PscpServerSymbol symbol = state.AddSymbol(
            scope,
            state.Tokens[tokenIndex].Text,
            PscpServerSymbolKind.Parameter,
            state.Tokens[tokenIndex].Span,
            state.Tokens[tokenIndex].Span,
            new ScopeSpan(state.Tokens[tokenIndex].Position, scopeEnd),
            typeDisplay,
            isMutable: false,
            isIntrinsic: false,
            containerSymbolId: null,
            documentation: string.IsNullOrWhiteSpace(typeDisplay)
                ? $"lambda parameter `{state.Tokens[tokenIndex].Text}`"
                : $"lambda parameter `{state.Tokens[tokenIndex].Text}: {typeDisplay}`");
        state.AddReference(symbol, tokenIndex, isDeclaration: true, isWrite: false);
    }

    private void AnalyzeIdentifierUsage(AnalyzerState state, Scope scope, int tokenIndex)
    {
        Token token = state.Tokens[tokenIndex];
        if (token.Text == "_")
        {
            state.MarkToken(tokenIndex, "variable");
            return;
        }

        if (PscpIntrinsics.BuiltinTypes.Contains(token.Text))
        {
            state.MarkToken(tokenIndex, "type", "defaultLibrary");
            state.SetHover(tokenIndex, $"```pscp\n{token.Text}\n```\n\nBuilt-in PSCP/.NET type.");
            return;
        }

        if (PscpExternalMetadata.TryGetTopLevelEntry(token.Text, out PscpCompletionEntry? externalTopLevel))
        {
            state.MarkToken(tokenIndex, PscpExternalMetadata.IsKnownNamespace(token.Text) ? "namespace" : "type", "defaultLibrary");
            if (!string.IsNullOrWhiteSpace(externalTopLevel?.Documentation))
            {
                state.SetHover(tokenIndex, externalTopLevel.Documentation!);
            }

            return;
        }

        if (IsMemberName(state.Tokens, tokenIndex))
        {
            AnalyzeMemberUsage(state, scope, tokenIndex);
            return;
        }

        if (IsAggregationName(state.Tokens, tokenIndex))
        {
            state.MarkToken(tokenIndex, "function", "defaultLibrary");
            if (PscpIntrinsics.HoverDocs.TryGetValue(token.Text, out string? hover))
            {
                state.SetHover(tokenIndex, hover);
            }
            else
            {
                state.SetHover(tokenIndex, $"```pscp\n{token.Text}{{ for ... in ... do ... }}\n```\n\nBuilt-in aggregation form.");
            }
            return;
        }

        if (!state.TryResolve(scope, token.Text, token.Position, out PscpServerSymbol? symbol) || symbol is null)
        {
            if (PscpIntrinsics.IntrinsicFunctions.TryGetValue(token.Text, out PscpCompletionEntry? intrinsicFunction))
            {
                state.MarkToken(tokenIndex, "function", "defaultLibrary");
                state.SetHover(tokenIndex, PscpIntrinsics.HoverDocs.TryGetValue(token.Text, out string? hover)
                    ? hover
                    : $"```pscp\n{intrinsicFunction.Detail}\n```\n\n{intrinsicFunction.Documentation}");
                return;
            }

            if (LooksLikeTypeReference(state.Tokens, tokenIndex))
            {
                state.MarkToken(tokenIndex, "type");
            }
            else if (LooksLikeCallableIdentifier(state.Tokens, tokenIndex))
            {
                state.MarkToken(tokenIndex, "function");
            }
            else
            {
                state.MarkToken(tokenIndex, "variable");
            }

            return;
        }

        state.AddReference(symbol, tokenIndex, isDeclaration: false, isWrite: false);
        switch (symbol.Kind)
        {
            case PscpServerSymbolKind.Function:
                state.MarkToken(tokenIndex, "function");
                break;
            case PscpServerSymbolKind.Type:
                state.MarkToken(tokenIndex, "type");
                break;
            case PscpServerSymbolKind.Parameter:
                state.MarkToken(tokenIndex, "parameter");
                break;
            case PscpServerSymbolKind.Intrinsic:
                state.MarkToken(tokenIndex, token.Text == "Array" ? "type" : "variable", "defaultLibrary");
                break;
            default:
                state.MarkToken(tokenIndex, "variable", symbol.IsMutable ? new[] { "mutable" } : Array.Empty<string>());
                break;
        }
    }

    private void AnalyzeMemberUsage(AnalyzerState state, Scope scope, int tokenIndex)
    {
        int? dotIndex = PreviousNonTrivia(state.Tokens, tokenIndex - 1);
        if (dotIndex is not int dot || state.Tokens[dot].Kind != TokenKind.Dot)
        {
            state.MarkToken(tokenIndex, "property");
            return;
        }

        int? receiverTokenIndex = PreviousNonTrivia(state.Tokens, dot - 1);
        bool instanceContext = false;
        string? receiverName = null;
        if (receiverTokenIndex is int receiver)
        {
            receiverName = ResolveReceiverName(state, scope, receiver, out instanceContext, out _);
            if (receiverName is null && state.Tokens[receiver].Kind is TokenKind.CloseBracket or TokenKind.CloseParen)
            {
                int receiverStart = FindSimpleReceiverStart(state.Tokens, dot);
                string? inferredReceiverType = receiverStart < dot
                    ? InferExpressionType(state, scope, receiverStart, dot)
                    : null;
                if (!string.IsNullOrWhiteSpace(inferredReceiverType))
                {
                    receiverName = inferredReceiverType;
                    instanceContext = true;
                }
            }
        }

        bool isCallable = LooksLikeCallableIdentifier(state.Tokens, tokenIndex);

        if (receiverName is null)
        {
            state.MarkToken(tokenIndex, isCallable ? "method" : "property");
            return;
        }

        if (instanceContext && state.TryResolveTypeMember(receiverName, state.Tokens[tokenIndex].Text, out PscpServerSymbol? memberSymbol) && memberSymbol is not null)
        {
            state.AddReference(memberSymbol, tokenIndex, isDeclaration: false, isWrite: false);
            state.MarkToken(tokenIndex, isCallable ? "method" : "property");
            return;
        }

        if (PscpIntrinsics.ComparatorMembers.ContainsKey(state.Tokens[tokenIndex].Text) && PscpIntrinsics.IsTypeLikeReceiverName(receiverName))
        {
            state.MarkToken(tokenIndex, isCallable ? "method" : "property", "defaultLibrary");
            state.SetHover(tokenIndex, PscpIntrinsics.HoverDocs[$"comparer.{state.Tokens[tokenIndex].Text}"]);
            return;
        }

        if (IsCollectionLikeReceiver(receiverName, instanceContext)
            && PscpIntrinsics.CollectionMembers.TryGetValue(state.Tokens[tokenIndex].Text, out PscpCompletionEntry? collectionMember))
        {
            state.MarkToken(tokenIndex, isCallable ? "method" : "property", "defaultLibrary");
            state.AddIntrinsicMember(tokenIndex, collectionMember);
            state.SetHover(tokenIndex, PscpIntrinsics.HoverDocs[$"collection.{state.Tokens[tokenIndex].Text}"]);
            return;
        }

        if (TryGetIntrinsicMember(receiverName, state.Tokens[tokenIndex].Text, out PscpCompletionEntry? completion, out string? hoverKey))
        {
            state.MarkToken(tokenIndex, isCallable ? "method" : "property", "defaultLibrary");
            state.AddIntrinsicMember(tokenIndex, completion!);
            if (hoverKey is not null && PscpIntrinsics.HoverDocs.TryGetValue(hoverKey, out string? hover))
            {
                state.SetHover(tokenIndex, hover);
            }

            return;
        }

        if (PscpExternalMetadata.TryGetMemberEntry(receiverName, instanceContext, state.Tokens[tokenIndex].Text, out PscpCompletionEntry? externalMember))
        {
            ApplyExternalCompletionClassification(state, tokenIndex, externalMember!);
            if (!string.IsNullOrWhiteSpace(externalMember?.Documentation))
            {
                state.SetHover(tokenIndex, externalMember.Documentation!);
            }
        }
        else
        {
            state.MarkToken(tokenIndex, isCallable ? "method" : "property");
        }
    }

    private static bool IsAggregationName(IReadOnlyList<Token> tokens, int tokenIndex)
    {
        if (tokenIndex + 1 >= tokens.Count || tokens[tokenIndex + 1].Kind != TokenKind.OpenBrace)
        {
            return false;
        }

        return KnownAggregators.Contains(tokens[tokenIndex].Text);
    }

    private static bool LooksLikeTypeReference(IReadOnlyList<Token> tokens, int tokenIndex)
    {
        int? previous = PreviousNonTrivia(tokens, tokenIndex - 1);
        if (previous is int prev && tokens[prev].Kind is TokenKind.New or TokenKind.LessThan or TokenKind.Comma or TokenKind.OpenParen)
        {
            return true;
        }

        int? next = NextNonTrivia(tokens, tokenIndex + 1);
        return next is int nextIndex && tokens[nextIndex].Kind is TokenKind.LessThan or TokenKind.OpenBracket;
    }

    private static bool LooksLikeCallableIdentifier(IReadOnlyList<Token> tokens, int tokenIndex)
    {
        int? next = NextNonTrivia(tokens, tokenIndex + 1);
        return next is int nextIndex && tokens[nextIndex].Kind == TokenKind.OpenParen;
    }

    private static void ApplyExternalCompletionClassification(AnalyzerState state, int tokenIndex, PscpCompletionEntry completion)
    {
        string tokenType = completion.Kind switch
        {
            2 => "method",
            7 => "type",
            9 => "namespace",
            10 => "property",
            _ => "property",
        };
        state.MarkToken(tokenIndex, tokenType, "defaultLibrary");
    }

    private static string? ResolveReceiverName(AnalyzerState state, Scope scope, int tokenIndex, out bool instanceContext, out PscpServerSymbol? receiverSymbol)
    {
        instanceContext = false;
        receiverSymbol = null;
        if (!IsIdentifier(state.Tokens, tokenIndex))
        {
            return null;
        }

        Token token = state.Tokens[tokenIndex];
        if (state.TryResolve(scope, token.Text, token.Position, out receiverSymbol) && receiverSymbol is not null)
        {
            if (receiverSymbol.Kind == PscpServerSymbolKind.Intrinsic)
            {
                return receiverSymbol.Name;
            }

            if (receiverSymbol.Kind == PscpServerSymbolKind.Type)
            {
                return receiverSymbol.Name;
            }

            if (!string.IsNullOrWhiteSpace(receiverSymbol.TypeDisplay))
            {
                instanceContext = true;
                return receiverSymbol.TypeDisplay;
            }
        }

        return token.Text;
    }

    private static bool IsCollectionLikeReceiver(string receiverName, bool instanceContext)
    {
        if (!instanceContext)
        {
            return false;
        }

        string normalized = PscpExternalMetadata.NormalizeTypeReceiver(receiverName);
        return receiverName.EndsWith("[]", StringComparison.Ordinal)
            || normalized is "Array" or "List" or "LinkedList" or "Queue" or "Stack" or "HashSet" or "SortedSet"
            || normalized.StartsWith("IEnumerable", StringComparison.Ordinal);
    }

    private static bool TryGetIntrinsicMember(
        string receiverName,
        string memberName,
        out PscpCompletionEntry? completion,
        out string? hoverKey)
    {
        completion = null;
        hoverKey = null;
        IReadOnlyDictionary<string, PscpCompletionEntry>? table = receiverName switch
        {
            "stdin" => PscpIntrinsics.StdinMembers,
            "stdout" => PscpIntrinsics.StdoutMembers,
            "Array" => PscpIntrinsics.ArrayMembers,
            _ => null,
        };

        if (table is not null && table.TryGetValue(memberName, out PscpCompletionEntry? found))
        {
            completion = found;
            hoverKey = $"{receiverName}.{memberName}";
            return true;
        }

        if (PscpIntrinsics.ComparatorMembers.TryGetValue(memberName, out found) && PscpIntrinsics.IsTypeLikeReceiverName(receiverName))
        {
            completion = found;
            hoverKey = $"comparer.{memberName}";
            return true;
        }

        if (table is null)
        {
            return false;
        }
        
        return false;
    }

    private string? InferExpressionType(AnalyzerState state, Scope scope, int start, int endExclusive)
    {
        TrimExpressionBounds(state.Tokens, ref start, ref endExclusive);
        if (start >= endExclusive)
        {
            return null;
        }

        if (TryInferIntrinsicCallType(state, scope, start, endExclusive, out string? intrinsicCallType))
        {
            return intrinsicCallType;
        }

        if (TryInferCallType(state, scope, start, endExclusive, out string? callType))
        {
            return callType;
        }

        if (TryInferSpecialFormType(state, scope, start, endExclusive, out string? specialType))
        {
            return specialType;
        }

        if (TryInferArrowExpressionType(state, scope, start, endExclusive, out string? arrowType))
        {
            return arrowType;
        }

        if (endExclusive - start == 1)
        {
            return InferSingleTokenType(state, scope, start);
        }

        if (TryInferIndexerExpressionType(state, scope, start, endExclusive, out string? indexerType))
        {
            return indexerType;
        }

        if (TryInferMemberAccessType(state, scope, start, endExclusive, out string? memberType))
        {
            return memberType;
        }

        int topLevelRange = FindTopLevelRangeOperator(state.Tokens, start, endExclusive);
        if (topLevelRange >= 0)
        {
            return "IEnumerable<int>";
        }

        int topLevelComparison = FindFirstTopLevelOperator(
            state.Tokens,
            start,
            endExclusive,
            TokenKind.EqualEqual,
            TokenKind.BangEqual,
            TokenKind.LessThan,
            TokenKind.LessEqual,
            TokenKind.GreaterThan,
            TokenKind.GreaterEqual,
            TokenKind.Spaceship,
            TokenKind.AmpAmp,
            TokenKind.PipePipe,
            TokenKind.And,
            TokenKind.Or);
        if (topLevelComparison >= 0)
        {
            return state.Tokens[topLevelComparison].Kind == TokenKind.Spaceship ? "int" : "bool";
        }

        int topLevelBitwise = FindFirstTopLevelOperator(
            state.Tokens,
            start,
            endExclusive,
            TokenKind.Amp,
            TokenKind.Caret,
            TokenKind.Pipe,
            TokenKind.Xor);
        if (topLevelBitwise >= 0)
        {
            string? left = InferExpressionType(state, scope, start, topLevelBitwise);
            string? right = InferExpressionType(state, scope, topLevelBitwise + 1, endExclusive);
            return InferBitwiseType(left, right);
        }

        int additive = FindFirstTopLevelOperator(state.Tokens, start, endExclusive, TokenKind.Plus, TokenKind.Minus);
        if (additive >= 0)
        {
            string? left = InferExpressionType(state, scope, start, additive);
            string? right = InferExpressionType(state, scope, additive + 1, endExclusive);
            if (left == "string" || right == "string")
            {
                return "string";
            }

            return PromoteNumericType(left, right);
        }

        int multiplicative = FindFirstTopLevelOperator(state.Tokens, start, endExclusive, TokenKind.Star, TokenKind.Slash, TokenKind.Percent);
        if (multiplicative >= 0)
        {
            string? left = InferExpressionType(state, scope, start, multiplicative);
            string? right = InferExpressionType(state, scope, multiplicative + 1, endExclusive);
            return PromoteNumericType(left, right);
        }

        return null;
    }

    private static string? InferBitwiseType(string? left, string? right)
        => left == "bool" && right == "bool"
            ? "bool"
            : PromoteNumericType(left, right);

    private bool TryInferSpecialFormType(AnalyzerState state, Scope scope, int start, int endExclusive, out string? type)
    {
        type = null;

        if (state.Tokens[start].Kind == TokenKind.OpenParen)
        {
            int closeParen = FindMatching(state.Tokens, start, TokenKind.OpenParen, TokenKind.CloseParen, endExclusive);
            if (closeParen == endExclusive - 1)
            {
                List<int> commas = FindTopLevelSeparators(state.Tokens, start + 1, closeParen, TokenKind.Comma);
                if (commas.Count == 0)
                {
                    type = InferExpressionType(state, scope, start + 1, closeParen);
                    return type is not null;
                }

                List<string> elementTypes = [];
                int elementStart = start + 1;
                foreach (int comma in commas.Concat([closeParen]))
                {
                    elementTypes.Add(InferExpressionType(state, scope, elementStart, comma) ?? "object");
                    elementStart = comma + 1;
                }

                type = $"({string.Join(", ", elementTypes)})";
                return true;
            }
        }

        if (state.Tokens[start].Kind == TokenKind.OpenBracket)
        {
            int closeBracket = FindMatching(state.Tokens, start, TokenKind.OpenBracket, TokenKind.CloseBracket, endExclusive);
            if (closeBracket == endExclusive - 1)
            {
                if (closeBracket == start + 1)
                {
                    type = "object[]";
                    return true;
                }

                int firstComma = FindTopLevelToken(state.Tokens, start + 1, closeBracket, TokenKind.Comma);
                int firstElementEnd = firstComma >= 0 ? firstComma : closeBracket;
                string? elementType = InferExpressionType(state, scope, start + 1, firstElementEnd);
                if (FindTopLevelToken(state.Tokens, start + 1, firstElementEnd, TokenKind.Arrow) >= 0
                    || FindTopLevelRangeOperator(state.Tokens, start + 1, firstElementEnd) >= 0)
                {
                    elementType = UnwrapEnumerableType(elementType) ?? elementType;
                }

                type = $"{elementType ?? "object"}[]";
                return true;
            }
        }

        if (state.Tokens[start].Kind == TokenKind.New)
        {
            int cursor = start + 1;
            if (cursor < endExclusive && state.Tokens[cursor].Kind != TokenKind.OpenParen && TryReadType(state, ref cursor, allowSizedArrays: false, out string explicitType, out _))
            {
                type = explicitType;
                return true;
            }

            type = "object";
            return true;
        }

        if (state.Tokens[start].Kind == TokenKind.If)
        {
            int thenIndex = FindTopLevelToken(state.Tokens, start + 1, endExclusive, TokenKind.Then);
            int elseIndex = FindTopLevelToken(state.Tokens, start + 1, endExclusive, TokenKind.Else);
            if (thenIndex >= 0 && elseIndex > thenIndex)
            {
                string? thenType = InferExpressionType(state, scope, thenIndex + 1, elseIndex);
                string? elseType = InferExpressionType(state, scope, elseIndex + 1, endExclusive);
                type = string.Equals(thenType, elseType, StringComparison.Ordinal) ? thenType : thenType ?? elseType;
                return type is not null;
            }
        }

        return false;
    }

    private bool TryInferArrowExpressionType(AnalyzerState state, Scope scope, int start, int endExclusive, out string? type)
    {
        type = null;
        int arrowIndex = FindTopLevelToken(state.Tokens, start, endExclusive, TokenKind.Arrow);
        if (arrowIndex < 0)
        {
            return false;
        }

        string? itemType = InferLoopVariableType(state, scope, start, arrowIndex);
        int cursor = arrowIndex + 1;
        cursor = SkipSeparators(state.Tokens, cursor, endExclusive);
        if (!IsIdentifier(state.Tokens, cursor))
        {
            return false;
        }

        int firstBinding = cursor++;
        int? secondBinding = null;
        if (cursor < endExclusive && state.Tokens[cursor].Kind == TokenKind.Comma && IsIdentifier(state.Tokens, cursor + 1))
        {
            secondBinding = cursor + 1;
            cursor += 2;
        }

        cursor = SkipSeparators(state.Tokens, cursor, endExclusive);
        if (cursor < endExclusive && state.Tokens[cursor].Kind == TokenKind.Do)
        {
            cursor++;
        }

        Scope bodyScope = state.CreateScope(scope, state.Tokens[firstBinding].Position, scope.EndOffset);
        AddSyntheticInferenceBinding(bodyScope, state.Tokens[firstBinding], secondBinding is null ? itemType : "int");
        if (secondBinding is int second)
        {
            AddSyntheticInferenceBinding(bodyScope, state.Tokens[second], itemType);
        }

        string? bodyType;
        int bodyStart = SkipSeparators(state.Tokens, cursor, endExclusive);
        if (bodyStart < endExclusive && state.Tokens[bodyStart].Kind == TokenKind.OpenBrace)
        {
            int close = FindMatching(state.Tokens, bodyStart, TokenKind.OpenBrace, TokenKind.CloseBrace, endExclusive);
            int bodyEnd = close < 0 ? endExclusive : close;
            int tailStart = FindLastStatementStart(state.Tokens, bodyStart + 1, bodyEnd);
            bodyType = tailStart < bodyEnd ? InferExpressionType(state, bodyScope, tailStart, bodyEnd) : null;
        }
        else
        {
            bodyType = bodyStart < endExclusive ? InferExpressionType(state, bodyScope, bodyStart, endExclusive) : null;
        }

        type = $"IEnumerable<{bodyType ?? "object"}>";
        return true;
    }

    private static void AddSyntheticInferenceBinding(Scope scope, Token token, string? typeDisplay)
    {
        if (token.Text == "_")
        {
            return;
        }

        scope.Add(new PscpServerSymbol(
            $"__infer_{token.Position}",
            token.Text,
            PscpServerSymbolKind.Parameter,
            token.Span,
            token.Span,
            new ScopeSpan(token.Position, scope.EndOffset),
            typeDisplay,
            IsMutable: false,
            IsIntrinsic: false,
            ContainerSymbolId: null,
            Documentation: null));
    }

    private bool TryInferIndexerExpressionType(AnalyzerState state, Scope scope, int start, int endExclusive, out string? type)
    {
        type = null;
        if (endExclusive <= start || state.Tokens[endExclusive - 1].Kind != TokenKind.CloseBracket)
        {
            return false;
        }

        int openBracket = FindMatchingBackward(state.Tokens, endExclusive - 1, TokenKind.OpenBracket, TokenKind.CloseBracket, start);
        if (openBracket <= start)
        {
            return false;
        }

        string? receiverType = InferExpressionType(state, scope, start, openBracket);
        if (string.IsNullOrWhiteSpace(receiverType))
        {
            return false;
        }

        if (receiverType.EndsWith("[][]", StringComparison.Ordinal))
        {
            type = receiverType[..^2];
            return true;
        }

        if (receiverType.EndsWith("[]", StringComparison.Ordinal))
        {
            type = receiverType[..^2];
            return true;
        }

        if (receiverType == "string")
        {
            type = "char";
            return true;
        }

        return false;
    }

    private bool TryInferMemberAccessType(AnalyzerState state, Scope scope, int start, int endExclusive, out string? type)
    {
        type = null;
        int dot = -1;
        int searchStart = start;
        while (true)
        {
            int nextDot = FindTopLevelToken(state.Tokens, searchStart, endExclusive, TokenKind.Dot);
            if (nextDot < 0)
            {
                break;
            }

            dot = nextDot;
            searchStart = nextDot + 1;
        }

        if (dot < 0 || dot + 1 >= endExclusive || !IsIdentifier(state.Tokens, dot + 1))
        {
            return false;
        }

        string? receiverType = InferExpressionType(state, scope, start, dot);
        if (!string.IsNullOrWhiteSpace(receiverType)
            && state.TryResolveTypeMember(receiverType!, state.Tokens[dot + 1].Text, out PscpServerSymbol? memberSymbol)
            && memberSymbol is not null)
        {
            type = memberSymbol.TypeDisplay;
            return type is not null;
        }

        if (!string.IsNullOrWhiteSpace(receiverType)
            && TryInferKnownExternalMemberReturnType(receiverType!, instanceContext: true, state.Tokens[dot + 1].Text, out type))
        {
            return type is not null;
        }

        return false;
    }

    private bool TryInferCallType(AnalyzerState state, Scope scope, int start, int endExclusive, out string? type)
    {
        type = null;
        if (!TryFindCallOpenParen(state.Tokens, start, endExclusive, out int openParen))
        {
            return false;
        }

        string calleeName = BuildCallName(state.Tokens, start, openParen);
        if (calleeName.Length == 0)
        {
            return false;
        }

        if (calleeName.IndexOf('.') < 0 && state.TryResolve(scope, calleeName, state.Tokens[start].Position, out PscpServerSymbol? symbol) && symbol is not null)
        {
            type = symbol.TypeDisplay;
            return type is not null;
        }

        if (TryInferKnownMemberCallType(state, scope, start, openParen, out type))
        {
            return type is not null;
        }

        return false;
    }

    private bool TryInferKnownMemberCallType(AnalyzerState state, Scope scope, int start, int openParen, out string? type)
    {
        type = null;
        int dot = -1;
        int searchStart = start;
        while (true)
        {
            int nextDot = FindTopLevelToken(state.Tokens, searchStart, openParen, TokenKind.Dot);
            if (nextDot < 0)
            {
                break;
            }

            dot = nextDot;
            searchStart = nextDot + 1;
        }

        if (dot < 0 || dot + 1 >= openParen || !IsIdentifier(state.Tokens, dot + 1))
        {
            return false;
        }

        string memberName = state.Tokens[dot + 1].Text;
        string? receiverType = InferExpressionType(state, scope, start, dot);
        string receiverName = receiverType ?? BuildCompactText(state.Tokens, start, dot);
        bool instanceContext = receiverType is not null;
        if (instanceContext && IsCollectionLikeReceiver(receiverName, instanceContext)
            && TryInferCollectionMemberReturnType(receiverName, memberName, out type))
        {
            return type is not null;
        }

        return TryInferKnownExternalMemberReturnType(receiverName, instanceContext, memberName, out type);
    }

    private static bool TryInferCollectionMemberReturnType(string receiverType, string memberName, out string? type)
    {
        type = null;
        string? elementType = InferElementType(receiverType);
        if (string.IsNullOrWhiteSpace(elementType))
        {
            return false;
        }

        type = memberName switch
        {
            "sum" or "min" or "max" or "minBy" or "maxBy" => elementType,
            "count" or "findIndex" or "findLastIndex" => "int",
            "any" or "all" => "bool",
            "find" => $"{elementType}?",
            "sort" or "sortBy" or "sortWith" or "distinct" or "reverse" or "copy" => $"{elementType}[]",
            "groupCount" or "freq" or "index" => $"Dictionary<{elementType}, int>",
            "map" or "filter" or "fold" or "scan" or "mapFold" => null,
            _ => null,
        };
        return type is not null;
    }

    private static string? InferElementType(string type)
    {
        string normalized = type.Trim();
        if (normalized.EndsWith("?", StringComparison.Ordinal))
        {
            normalized = normalized[..^1];
        }

        if (normalized.EndsWith("[]", StringComparison.Ordinal))
        {
            return normalized[..^2];
        }

        int open = normalized.IndexOf('<');
        int close = normalized.LastIndexOf('>');
        if (open > 0 && close > open)
        {
            string name = PscpExternalMetadata.NormalizeTypeReceiver(normalized[..open]);
            if (name is "IEnumerable" or "List" or "LinkedList" or "Queue" or "Stack" or "HashSet" or "SortedSet")
            {
                return normalized[(open + 1)..close].Trim();
            }
        }

        return normalized is "string" or "String" ? "char" : null;
    }

    private static string? UnwrapEnumerableType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return null;
        }

        string normalized = type.Trim();
        if (normalized.StartsWith("IEnumerable<", StringComparison.Ordinal) && normalized.EndsWith(">", StringComparison.Ordinal))
        {
            return normalized["IEnumerable<".Length..^1];
        }

        return null;
    }

    private static bool TryInferKnownExternalMemberReturnType(string receiverName, bool instanceContext, string memberName, out string? type)
    {
        type = null;
        string normalized = PscpExternalMetadata.NormalizeTypeReceiver(receiverName);
        if (instanceContext && normalized == "String")
        {
            type = memberName switch
            {
                "Length" or "IndexOf" or "LastIndexOf" => "int",
                "Contains" or "StartsWith" or "EndsWith" => "bool",
                "Split" => "string[]",
                "ToCharArray" => "char[]",
                "Substring" or "Replace" or "Trim" or "TrimStart" or "TrimEnd" or "ToLower" or "ToUpper" => "string",
                _ => null,
            };
            return type is not null;
        }

        if (!instanceContext && normalized == "Console")
        {
            type = memberName switch
            {
                "ReadLine" => "string?",
                "Write" or "WriteLine" => "void",
                _ => null,
            };
            return type is not null;
        }

        if (!instanceContext && normalized == "Math")
        {
            type = memberName switch
            {
                "Sqrt" or "Pow" or "Log" or "Log10" or "Sin" or "Cos" or "Tan" or "Asin" or "Acos" or "Atan" or "Atan2" => "double",
                _ => null,
            };
            return type is not null;
        }

        return false;
    }

    private bool TryInferIntrinsicCallType(AnalyzerState state, Scope scope, int start, int endExclusive, out string? type)
    {
        type = null;
        if (!TryFindCallOpenParen(state.Tokens, start, endExclusive, out int openParen))
        {
            return false;
        }

        string calleeName = BuildCallName(state.Tokens, start, openParen);
        if (calleeName.Length == 0)
        {
            return false;
        }

        type = InferIntrinsicReturnType(state, scope, start, openParen, calleeName);
        return type is not null;
    }

    private string? InferSingleTokenType(AnalyzerState state, Scope scope, int tokenIndex)
    {
        Token token = state.Tokens[tokenIndex];
        return token.Kind switch
        {
            TokenKind.IntegerLiteral => token.Text.EndsWith("L", StringComparison.OrdinalIgnoreCase) ? "long" : "int",
            TokenKind.FloatLiteral => token.Text.EndsWith("m", StringComparison.OrdinalIgnoreCase) ? "decimal" : "double",
            TokenKind.StringLiteral or TokenKind.InterpolatedStringLiteral => "string",
            TokenKind.CharLiteral => "char",
            TokenKind.True or TokenKind.False => "bool",
            TokenKind.Null => null,
            TokenKind.Identifier when state.TryResolve(scope, token.Text, token.Position, out PscpServerSymbol? symbol) && symbol is not null => symbol.TypeDisplay,
            _ => null,
        };
    }

    private string? InferIntrinsicReturnType(AnalyzerState state, Scope scope, int start, int openParen, string calleeName)
    {
        if (calleeName.IndexOf('.') < 0)
        {
            return InferIntrinsicFunctionReturnType(state, scope, start, openParen, calleeName);
        }

        string[] parts = calleeName.Split('.', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            return null;
        }

        return parts[0] switch
        {
            "stdin" => InferStdinReturnType(state, start, openParen, parts[1]),
            "stdout" => "void",
            "Array" when parts[1] == "zero" => InferArrayZeroReturnType(state, scope, start),
            _ => null,
        };
    }

    private string? InferStdinReturnType(AnalyzerState state, int start, int openParen, string member)
    {
        return member switch
        {
            "int" or "readInt" => "int",
            "long" or "readLong" => "long",
            "double" or "readDouble" => "double",
            "decimal" or "readDecimal" => "decimal",
            "bool" or "readBool" => "bool",
            "char" or "readChar" => "char",
            "str" or "readString" => "string",
            "line" or "readLine" or "readRestOfLine" => "string",
            "lines" or "readLines" => "string[]",
            "words" or "readWords" => "string[]",
            "chars" or "readChars" => "char[]",
            "gridInt" or "readGridInt" => "int[][]",
            "gridLong" or "readGridLong" => "long[][]",
            "charGrid" or "readCharGrid" => "char[][]",
            "wordGrid" or "readWordGrid" => "string[][]",
            "tuple2" or "readTuple2" => BuildTupleTypeFromGenericArguments(state.Tokens, start, openParen, 2),
            "tuple3" or "readTuple3" => BuildTupleTypeFromGenericArguments(state.Tokens, start, openParen, 3),
            "tuples2" or "readTuples2" => BuildTupleTypeFromGenericArguments(state.Tokens, start, openParen, 2) is string tuple2 ? tuple2 + "[]" : "(object, object)[]",
            "tuples3" or "readTuples3" => BuildTupleTypeFromGenericArguments(state.Tokens, start, openParen, 3) is string tuple3 ? tuple3 + "[]" : "(object, object, object)[]",
            "array" or "readArray" => BuildGenericCollectionType(state.Tokens, start, openParen, "[]", "object[]"),
            "list" or "readList" => BuildGenericCollectionType(state.Tokens, start, openParen, prefix: "List<", suffix: ">", fallback: "List<object>"),
            "linkedList" or "readLinkedList" => BuildGenericCollectionType(state.Tokens, start, openParen, prefix: "LinkedList<", suffix: ">", fallback: "LinkedList<object>"),
            "nestedArray" or "readNestedArray" => BuildNestedArrayType(state.Tokens, start, openParen),
            _ => null,
        };
    }

    private string? InferIntrinsicFunctionReturnType(AnalyzerState state, Scope scope, int start, int openParen, string calleeName)
    {
        if (!PscpIntrinsics.IntrinsicFunctions.ContainsKey(calleeName))
        {
            return null;
        }

        return calleeName switch
        {
            "count" or "findIndex" or "findLastIndex" => "int",
            "any" or "all" or "chmin" or "chmax" => "bool",
            "sum" or "min" or "max" => InferFirstArgumentElementType(state, scope, openParen),
            "find" or "minBy" or "maxBy" => InferFirstArgumentElementType(state, scope, openParen),
            "sort" or "sortBy" or "sortWith" or "distinct" or "reverse" or "copy" => InferFirstArgumentMaterializedType(state, scope, openParen),
            "abs" => InferFirstArgumentType(state, scope, openParen),
            "sqrt" or "pow" => "double",
            "clamp" => InferPromotedArgumentType(state, scope, openParen, 3),
            "gcd" or "lcm" => InferPromotedArgumentType(state, scope, openParen, 2),
            "floor" or "ceil" or "round" => InferMathRoundingReturnType(state, scope, openParen),
            "popcount" or "bitLength" => "int",
            "groupCount" or "freq" or "index" => InferDictionaryTypeFromFirstArgument(state, scope, openParen),
            _ => null,
        };
    }

    private static string? InferArrayZeroReturnType(AnalyzerState state, Scope scope, int start)
    {
        int assignment = FindFirstTopLevelOperator(state.Tokens, 0, start, TokenKind.Equal);
        if (assignment <= 0)
        {
            return null;
        }

        for (int i = assignment - 1; i >= 0; i--)
        {
            if (IsIdentifier(state.Tokens, i) && state.TryResolve(scope, state.Tokens[i].Text, state.Tokens[i].Position, out PscpServerSymbol? symbol) && symbol is not null)
            {
                return symbol.TypeDisplay;
            }
        }

        return null;
    }

    private static string? BuildGenericCollectionType(IReadOnlyList<Token> tokens, int start, int openParen, string suffix, string fallback)
        => BuildGenericCollectionType(tokens, start, openParen, prefix: string.Empty, suffix: suffix, fallback: fallback);

    private static string? BuildGenericCollectionType(IReadOnlyList<Token> tokens, int start, int openParen, string prefix, string suffix, string fallback)
    {
        if (!TryReadGenericArgumentSlice(tokens, start, openParen, out int genericStart, out int genericEnd))
        {
            return fallback;
        }

        return $"{prefix}{BuildCompactText(tokens, genericStart, genericEnd)}{suffix}";
    }

    private static string? BuildTupleTypeFromGenericArguments(IReadOnlyList<Token> tokens, int start, int openParen, int arity)
    {
        if (!TryReadGenericArgumentSlice(tokens, start, openParen, out int genericStart, out int genericEnd))
        {
            return arity == 2 ? "(object, object)" : "(object, object, object)";
        }

        List<int> commas = FindTopLevelSeparators(tokens, genericStart, genericEnd, TokenKind.Comma);
        if (commas.Count + 1 != arity)
        {
            return arity == 2 ? "(object, object)" : "(object, object, object)";
        }

        List<string> elements = [];
        int elementStart = genericStart;
        foreach (int comma in commas.Concat([genericEnd]))
        {
            elements.Add(BuildCompactText(tokens, elementStart, comma));
            elementStart = comma + 1;
        }

        return $"({string.Join(", ", elements)})";
    }

    private static string BuildSingleGenericType(IReadOnlyList<Token> tokens, int start, int openParen, string fallback)
    {
        if (!TryReadGenericArgumentSlice(tokens, start, openParen, out int genericStart, out int genericEnd))
        {
            return fallback;
        }

        return BuildCompactText(tokens, genericStart, genericEnd);
    }

    private static string BuildNestedArrayType(IReadOnlyList<Token> tokens, int start, int openParen)
    {
        string elementType = BuildSingleGenericType(tokens, start, openParen, "object");
        return $"{elementType}[][]";
    }

    private string? InferFirstArgumentElementType(AnalyzerState state, Scope scope, int openParen)
    {
        if (!TryReadFirstArgumentBounds(state.Tokens, openParen, out int argumentStart, out int argumentEnd))
        {
            return null;
        }

        string? firstType = InferExpressionType(state, scope, argumentStart, argumentEnd);
        if (string.IsNullOrWhiteSpace(firstType))
        {
            return null;
        }

        if (firstType.EndsWith("[]", StringComparison.Ordinal))
        {
            return firstType[..^2];
        }

        if (firstType.StartsWith("List<", StringComparison.Ordinal) && firstType.EndsWith(">", StringComparison.Ordinal))
        {
            return firstType["List<".Length..^1];
        }

        if (firstType.StartsWith("LinkedList<", StringComparison.Ordinal) && firstType.EndsWith(">", StringComparison.Ordinal))
        {
            return firstType["LinkedList<".Length..^1];
        }

        if (firstType.StartsWith("IEnumerable<", StringComparison.Ordinal) && firstType.EndsWith(">", StringComparison.Ordinal))
        {
            return firstType["IEnumerable<".Length..^1];
        }

        return firstType;
    }

    private string? InferFirstArgumentMaterializedType(AnalyzerState state, Scope scope, int openParen)
    {
        string? elementType = InferFirstArgumentElementType(state, scope, openParen);
        return string.IsNullOrWhiteSpace(elementType) ? null : $"{elementType}[]";
    }

    private string? InferFirstArgumentType(AnalyzerState state, Scope scope, int openParen)
    {
        if (!TryReadFirstArgumentBounds(state.Tokens, openParen, out int argumentStart, out int argumentEnd))
        {
            return null;
        }

        return InferExpressionType(state, scope, argumentStart, argumentEnd);
    }

    private string? InferDictionaryTypeFromFirstArgument(AnalyzerState state, Scope scope, int openParen)
    {
        string? elementType = InferFirstArgumentElementType(state, scope, openParen);
        return string.IsNullOrWhiteSpace(elementType) ? null : $"Dictionary<{elementType}, int>";
    }

    private string? InferPromotedArgumentType(AnalyzerState state, Scope scope, int openParen, int count)
    {
        if (!TryReadArgumentBounds(state.Tokens, openParen, count, out IReadOnlyList<(int Start, int EndExclusive)>? bounds) || bounds is null)
        {
            return null;
        }

        string? promoted = null;
        foreach ((int start, int endExclusive) in bounds)
        {
            promoted = PromoteNumericType(promoted, InferExpressionType(state, scope, start, endExclusive));
        }

        return promoted;
    }

    private string InferMathRoundingReturnType(AnalyzerState state, Scope scope, int openParen)
    {
        string? argumentType = InferFirstArgumentType(state, scope, openParen);
        return argumentType == "decimal" ? "decimal" : "double";
    }

    private string? InferSingleLambdaParameterTypeFromCallContext(AnalyzerState state, Scope scope, int parameterIndex)
    {
        int? previous = PreviousNonTrivia(state.Tokens, parameterIndex - 1);
        if (previous is not int openParen || state.Tokens[openParen].Kind != TokenKind.OpenParen)
        {
            return null;
        }

        int? memberIndex = PreviousNonTrivia(state.Tokens, openParen - 1);
        if (memberIndex is not int member || !IsIdentifier(state.Tokens, member))
        {
            return null;
        }

        int? dotIndex = PreviousNonTrivia(state.Tokens, member - 1);
        if (dotIndex is not int dot || state.Tokens[dot].Kind != TokenKind.Dot)
        {
            return null;
        }

        int receiverStart = FindSimpleReceiverStart(state.Tokens, dot);
        string? receiverType = InferExpressionType(state, scope, receiverStart, dot);
        return string.IsNullOrWhiteSpace(receiverType) ? null : InferElementType(receiverType!);
    }

    private static int FindSimpleReceiverStart(IReadOnlyList<Token> tokens, int dotIndex)
    {
        int? previous = PreviousNonTrivia(tokens, dotIndex - 1);
        if (previous is not int index)
        {
            return dotIndex;
        }

        if (tokens[index].Kind == TokenKind.CloseBracket)
        {
            int open = FindMatchingBackward(tokens, index, TokenKind.OpenBracket, TokenKind.CloseBracket, 0);
            int? beforeOpen = PreviousNonTrivia(tokens, open - 1);
            return beforeOpen is int before && IsIdentifier(tokens, before) ? before : open;
        }

        return index;
    }

    private static int FindMatchingBackward(IReadOnlyList<Token> tokens, int closeIndex, TokenKind openKind, TokenKind closeKind, int startInclusive)
    {
        int depth = 0;
        for (int i = closeIndex; i >= startInclusive; i--)
        {
            if (tokens[i].Kind == closeKind)
            {
                depth++;
            }
            else if (tokens[i].Kind == openKind)
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private static int FindLastStatementStart(IReadOnlyList<Token> tokens, int start, int endExclusive)
    {
        int parenDepth = 0;
        int bracketDepth = 0;
        int braceDepth = 0;
        for (int i = endExclusive - 1; i >= start; i--)
        {
            switch (tokens[i].Kind)
            {
                case TokenKind.CloseParen:
                    parenDepth++;
                    break;
                case TokenKind.OpenParen:
                    parenDepth = Math.Max(0, parenDepth - 1);
                    break;
                case TokenKind.CloseBracket:
                    bracketDepth++;
                    break;
                case TokenKind.OpenBracket:
                    bracketDepth = Math.Max(0, bracketDepth - 1);
                    break;
                case TokenKind.CloseBrace:
                    braceDepth++;
                    break;
                case TokenKind.OpenBrace:
                    braceDepth = Math.Max(0, braceDepth - 1);
                    break;
                case TokenKind.NewLine:
                case TokenKind.Semicolon:
                    if (parenDepth == 0 && bracketDepth == 0 && braceDepth == 0)
                    {
                        return SkipSeparators(tokens, i + 1, endExclusive);
                    }

                    break;
            }
        }

        return SkipSeparators(tokens, start, endExclusive);
    }

    private static bool TryReadFirstArgumentBounds(IReadOnlyList<Token> tokens, int openParen, out int argumentStart, out int argumentEnd)
    {
        argumentStart = SkipSeparators(tokens, openParen + 1, tokens.Count);
        if (argumentStart >= tokens.Count)
        {
            argumentEnd = argumentStart;
            return false;
        }

        int closeParen = FindMatching(tokens, openParen, TokenKind.OpenParen, TokenKind.CloseParen, tokens.Count);
        if (closeParen < 0)
        {
            argumentEnd = argumentStart;
            return false;
        }

        int firstComma = FindTopLevelToken(tokens, argumentStart, closeParen, TokenKind.Comma);
        argumentEnd = firstComma >= 0 ? firstComma : closeParen;
        return argumentStart < argumentEnd;
    }

    private static bool TryReadArgumentBounds(IReadOnlyList<Token> tokens, int openParen, int count, out IReadOnlyList<(int Start, int EndExclusive)>? bounds)
    {
        bounds = null;
        int closeParen = FindMatching(tokens, openParen, TokenKind.OpenParen, TokenKind.CloseParen, tokens.Count);
        if (closeParen < 0)
        {
            return false;
        }

        List<int> separators = FindTopLevelSeparators(tokens, openParen + 1, closeParen, TokenKind.Comma);
        List<(int Start, int EndExclusive)> result = [];
        int segmentStart = openParen + 1;
        foreach (int separator in separators.Concat([closeParen]))
        {
            int start = segmentStart;
            int endExclusive = separator;
            TrimExpressionBounds(tokens, ref start, ref endExclusive);
            if (start < endExclusive)
            {
                result.Add((start, endExclusive));
            }

            segmentStart = separator + 1;
        }

        if (result.Count != count)
        {
            return false;
        }

        bounds = result;
        return true;
    }

    private static bool TryReadGenericArgumentSlice(IReadOnlyList<Token> tokens, int start, int openParen, out int genericStart, out int genericEnd)
    {
        genericStart = -1;
        genericEnd = -1;
        for (int i = start; i < openParen; i++)
        {
            if (tokens[i].Kind != TokenKind.LessThan)
            {
                continue;
            }

            int depth = 1;
            for (int j = i + 1; j < openParen; j++)
            {
                if (tokens[j].Kind == TokenKind.LessThan)
                {
                    depth++;
                }
                else if (tokens[j].Kind == TokenKind.GreaterThan)
                {
                    depth--;
                    if (depth == 0)
                    {
                        genericStart = i + 1;
                        genericEnd = j;
                        return genericStart < genericEnd;
                    }
                }
            }
        }

        return false;
    }

    private static bool TryFindCallOpenParen(IReadOnlyList<Token> tokens, int start, int endExclusive, out int openParen)
    {
        openParen = -1;
        int parenDepth = 0;
        int bracketDepth = 0;
        int braceDepth = 0;

        for (int i = start; i < endExclusive; i++)
        {
            switch (tokens[i].Kind)
            {
                case TokenKind.OpenParen:
                    if (parenDepth == 0 && bracketDepth == 0 && braceDepth == 0)
                    {
                        int closeParen = FindMatching(tokens, i, TokenKind.OpenParen, TokenKind.CloseParen, endExclusive);
                        if (closeParen == endExclusive - 1)
                        {
                            openParen = i;
                            return true;
                        }
                    }

                    parenDepth++;
                    break;
                case TokenKind.CloseParen:
                    parenDepth = Math.Max(0, parenDepth - 1);
                    break;
                case TokenKind.OpenBracket:
                    bracketDepth++;
                    break;
                case TokenKind.CloseBracket:
                    bracketDepth = Math.Max(0, bracketDepth - 1);
                    break;
                case TokenKind.OpenBrace:
                    braceDepth++;
                    break;
                case TokenKind.CloseBrace:
                    braceDepth = Math.Max(0, braceDepth - 1);
                    break;
            }
        }

        return false;
    }

    private static string BuildCallName(IReadOnlyList<Token> tokens, int start, int endExclusive)
    {
        List<Token> pieces = [];
        for (int i = start; i < endExclusive; i++)
        {
            if (tokens[i].Kind is TokenKind.Identifier or TokenKind.Dot)
            {
                pieces.Add(tokens[i]);
            }
        }

        return string.Concat(pieces.Select(piece => piece.Text));
    }

    private static List<int> FindTopLevelSeparators(IReadOnlyList<Token> tokens, int start, int endExclusive, TokenKind separator)
    {
        List<int> results = [];
        int parenDepth = 0;
        int bracketDepth = 0;
        int braceDepth = 0;
        int angleDepth = 0;

        for (int i = start; i < endExclusive; i++)
        {
            switch (tokens[i].Kind)
            {
                case TokenKind.OpenParen:
                    parenDepth++;
                    break;
                case TokenKind.CloseParen:
                    parenDepth = Math.Max(0, parenDepth - 1);
                    break;
                case TokenKind.OpenBracket:
                    bracketDepth++;
                    break;
                case TokenKind.CloseBracket:
                    bracketDepth = Math.Max(0, bracketDepth - 1);
                    break;
                case TokenKind.OpenBrace:
                    braceDepth++;
                    break;
                case TokenKind.CloseBrace:
                    braceDepth = Math.Max(0, braceDepth - 1);
                    break;
                case TokenKind.LessThan:
                    angleDepth++;
                    break;
                case TokenKind.GreaterThan:
                    angleDepth = Math.Max(0, angleDepth - 1);
                    break;
            }

            if (parenDepth == 0 && bracketDepth == 0 && braceDepth == 0 && angleDepth == 0 && tokens[i].Kind == separator)
            {
                results.Add(i);
            }
        }

        return results;
    }

    private static int FindTopLevelRangeOperator(IReadOnlyList<Token> tokens, int start, int endExclusive)
        => FindFirstTopLevelOperator(tokens, start, endExclusive, TokenKind.DotDot, TokenKind.DotDotLess, TokenKind.DotDotEqual);

    private static int FindFirstTopLevelOperator(IReadOnlyList<Token> tokens, int start, int endExclusive, params TokenKind[] kinds)
    {
        HashSet<TokenKind> set = kinds.ToHashSet();
        int parenDepth = 0;
        int bracketDepth = 0;
        int braceDepth = 0;
        int angleDepth = 0;

        for (int i = start; i < endExclusive; i++)
        {
            switch (tokens[i].Kind)
            {
                case TokenKind.OpenParen:
                    parenDepth++;
                    continue;
                case TokenKind.CloseParen:
                    parenDepth = Math.Max(0, parenDepth - 1);
                    continue;
                case TokenKind.OpenBracket:
                    bracketDepth++;
                    continue;
                case TokenKind.CloseBracket:
                    bracketDepth = Math.Max(0, bracketDepth - 1);
                    continue;
                case TokenKind.OpenBrace:
                    braceDepth++;
                    continue;
                case TokenKind.CloseBrace:
                    braceDepth = Math.Max(0, braceDepth - 1);
                    continue;
                case TokenKind.LessThan:
                    angleDepth++;
                    continue;
                case TokenKind.GreaterThan:
                    angleDepth = Math.Max(0, angleDepth - 1);
                    continue;
            }

            if (parenDepth == 0 && bracketDepth == 0 && braceDepth == 0 && angleDepth == 0 && set.Contains(tokens[i].Kind))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindExpressionBoundary(IReadOnlyList<Token> tokens, int start, int endExclusive)
    {
        int parenDepth = 0;
        int bracketDepth = 0;
        int braceDepth = 0;
        int angleDepth = 0;

        for (int i = start; i < endExclusive; i++)
        {
            switch (tokens[i].Kind)
            {
                case TokenKind.OpenParen:
                    parenDepth++;
                    break;
                case TokenKind.CloseParen:
                    if (parenDepth == 0 && bracketDepth == 0 && braceDepth == 0 && angleDepth == 0)
                    {
                        return i;
                    }

                    parenDepth = Math.Max(0, parenDepth - 1);
                    break;
                case TokenKind.OpenBracket:
                    bracketDepth++;
                    break;
                case TokenKind.CloseBracket:
                    if (parenDepth == 0 && bracketDepth == 0 && braceDepth == 0 && angleDepth == 0)
                    {
                        return i;
                    }

                    bracketDepth = Math.Max(0, bracketDepth - 1);
                    break;
                case TokenKind.OpenBrace:
                    braceDepth++;
                    break;
                case TokenKind.CloseBrace:
                    if (parenDepth == 0 && bracketDepth == 0 && braceDepth == 0 && angleDepth == 0)
                    {
                        return i;
                    }

                    braceDepth = Math.Max(0, braceDepth - 1);
                    break;
                case TokenKind.LessThan:
                    angleDepth++;
                    break;
                case TokenKind.GreaterThan:
                    angleDepth = Math.Max(0, angleDepth - 1);
                    break;
                case TokenKind.Comma:
                case TokenKind.NewLine:
                case TokenKind.Semicolon:
                    if (parenDepth == 0 && bracketDepth == 0 && braceDepth == 0 && angleDepth == 0)
                    {
                        return i;
                    }

                    break;
            }
        }

        return endExclusive;
    }

    private static void TrimExpressionBounds(IReadOnlyList<Token> tokens, ref int start, ref int endExclusive)
    {
        start = SkipSeparators(tokens, start, endExclusive);
        while (endExclusive > start && tokens[endExclusive - 1].Kind is TokenKind.NewLine or TokenKind.Semicolon)
        {
            endExclusive--;
        }
    }

    private static string? PromoteNumericType(string? left, string? right)
    {
        if (left is null)
        {
            return right;
        }

        if (right is null)
        {
            return left;
        }

        int Rank(string type)
            => type switch
            {
                "int" => 0,
                "long" => 1,
                "double" => 2,
                "decimal" => 3,
                _ => -1,
            };

        int leftRank = Rank(left);
        int rightRank = Rank(right);
        if (leftRank < 0 || rightRank < 0)
        {
            return left == right ? left : null;
        }

        return leftRank >= rightRank ? left : right;
    }
}
