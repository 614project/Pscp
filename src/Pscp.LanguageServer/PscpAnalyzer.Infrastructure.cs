using Pscp.Transpiler;

namespace Pscp.LanguageServer;

internal sealed partial class PscpAnalyzer
{
    private static void AddDefaultTokenClassifications(AnalyzerState state)
    {
        for (int i = 0; i < state.Tokens.Count; i++)
        {
            Token token = state.Tokens[i];
            switch (token.Kind)
            {
                case TokenKind.EndOfFile:
                case TokenKind.NewLine:
                case TokenKind.Semicolon:
                case TokenKind.Comma:
                case TokenKind.OpenParen:
                case TokenKind.CloseParen:
                case TokenKind.OpenBrace:
                case TokenKind.CloseBrace:
                case TokenKind.OpenBracket:
                case TokenKind.CloseBracket:
                    continue;
                case TokenKind.IntegerLiteral:
                case TokenKind.FloatLiteral:
                    state.MarkToken(i, "number");
                    break;
                case TokenKind.StringLiteral:
                case TokenKind.InterpolatedStringLiteral:
                case TokenKind.CharLiteral:
                    state.MarkToken(i, "string");
                    break;
                case TokenKind.Let:
                case TokenKind.Var:
                case TokenKind.Mut:
                case TokenKind.Rec:
                case TokenKind.If:
                case TokenKind.Then:
                case TokenKind.Else:
                case TokenKind.For:
                case TokenKind.In:
                case TokenKind.Do:
                case TokenKind.While:
                case TokenKind.Break:
                case TokenKind.Continue:
                case TokenKind.Return:
                case TokenKind.True:
                case TokenKind.False:
                case TokenKind.Null:
                case TokenKind.And:
                case TokenKind.Or:
                case TokenKind.Xor:
                case TokenKind.Not:
                case TokenKind.Match:
                case TokenKind.When:
                case TokenKind.Where:
                case TokenKind.New:
                case TokenKind.Class:
                case TokenKind.Struct:
                case TokenKind.Record:
                case TokenKind.Ref:
                case TokenKind.Out:
                case TokenKind.Namespace:
                case TokenKind.Using:
                case TokenKind.Is:
                    state.MarkToken(i, "keyword");
                    break;
                case TokenKind.Identifier when PscpIntrinsics.Keywords.Contains(token.Text):
                    state.MarkToken(i, token.Text is "public" or "private" or "protected" or "internal" ? "modifier" : "keyword");
                    break;
                case TokenKind.Equal:
                case TokenKind.EqualEqual:
                case TokenKind.Bang:
                case TokenKind.BangEqual:
                case TokenKind.Colon:
                case TokenKind.ColonEqual:
                case TokenKind.Question:
                case TokenKind.Plus:
                case TokenKind.PlusPlus:
                case TokenKind.PlusEqual:
                case TokenKind.Minus:
                case TokenKind.MinusMinus:
                case TokenKind.MinusEqual:
                case TokenKind.Star:
                case TokenKind.StarEqual:
                case TokenKind.Slash:
                case TokenKind.SlashEqual:
                case TokenKind.Percent:
                case TokenKind.PercentEqual:
                case TokenKind.Caret:
                case TokenKind.Tilde:
                case TokenKind.AmpAmp:
                case TokenKind.PipePipe:
                case TokenKind.Spaceship:
                case TokenKind.Dot:
                case TokenKind.DotDot:
                case TokenKind.DotDotLess:
                case TokenKind.DotDotEqual:
                case TokenKind.PipeGreater:
                case TokenKind.LessPipe:
                case TokenKind.Arrow:
                case TokenKind.FatArrow:
                case TokenKind.LessThan:
                case TokenKind.GreaterThan:
                case TokenKind.LessEqual:
                case TokenKind.GreaterEqual:
                    state.MarkToken(i, "operator");
                    break;
                case TokenKind.Identifier when PscpIntrinsics.BuiltinTypes.Contains(token.Text):
                    state.MarkToken(i, "type", "defaultLibrary");
                    break;
            }
        }
    }

    private static int SkipSeparators(IReadOnlyList<Token> tokens, int index, int endExclusive)
    {
        while (index < endExclusive && tokens[index].Kind is TokenKind.NewLine or TokenKind.Semicolon)
        {
            index++;
        }

        return index;
    }

    private static int FindMatching(IReadOnlyList<Token> tokens, int openIndex, TokenKind openKind, TokenKind closeKind, int endExclusive)
    {
        int depth = 0;
        for (int i = openIndex; i < endExclusive; i++)
        {
            if (tokens[i].Kind == openKind)
            {
                depth++;
            }
            else if (tokens[i].Kind == closeKind)
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

    private static int FindStatementEnd(IReadOnlyList<Token> tokens, int start, int endExclusive)
    {
        int parenDepth = 0;
        int bracketDepth = 0;
        int braceDepth = 0;

        for (int i = start; i < endExclusive; i++)
        {
            TokenKind kind = tokens[i].Kind;
            switch (kind)
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
                    if (braceDepth == 0 && parenDepth == 0 && bracketDepth == 0)
                    {
                        return i;
                    }

                    braceDepth = Math.Max(0, braceDepth - 1);
                    break;
                case TokenKind.NewLine:
                case TokenKind.Semicolon:
                    if (parenDepth == 0 && bracketDepth == 0 && braceDepth == 0)
                    {
                        return i;
                    }
                    break;
            }
        }

        return endExclusive;
    }

    private static int FindTopLevelToken(IReadOnlyList<Token> tokens, int start, int endExclusive, TokenKind kind)
    {
        int parenDepth = 0;
        int bracketDepth = 0;
        int braceDepth = 0;

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
            }

            if (parenDepth == 0 && bracketDepth == 0 && braceDepth == 0 && tokens[i].Kind == kind)
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindToken(IReadOnlyList<Token> tokens, int start, int endExclusive, TokenKind kind)
        => FindTopLevelToken(tokens, start, endExclusive, kind);

    private static int FindTopLevelAssignment(IReadOnlyList<Token> tokens, int start, int endExclusive)
    {
        int parenDepth = 0;
        int bracketDepth = 0;
        int braceDepth = 0;

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
            }

            if (parenDepth != 0 || bracketDepth != 0 || braceDepth != 0)
            {
                continue;
            }

            if (tokens[i].Kind is TokenKind.Equal
                or TokenKind.PlusEqual
                or TokenKind.MinusEqual
                or TokenKind.StarEqual
                or TokenKind.SlashEqual
                or TokenKind.PercentEqual)
            {
                return i;
            }
        }

        return -1;
    }

    private static int EndOf(Token token)
        => token.Position + Math.Max(token.Text.Length, 1);

    private static bool IsTerminatorToken(IReadOnlyList<Token> tokens, int index, int endExclusive)
        => index >= endExclusive || tokens[index].Kind is TokenKind.NewLine or TokenKind.Semicolon or TokenKind.CloseBrace or TokenKind.EndOfFile;

    private static bool IsIdentifier(IReadOnlyList<Token> tokens, int index)
        => index >= 0 && index < tokens.Count && tokens[index].Kind == TokenKind.Identifier;

    private static bool IsMemberName(IReadOnlyList<Token> tokens, int index)
        => PreviousNonTrivia(tokens, index - 1) is int prev && tokens[prev].Kind == TokenKind.Dot;

    private static int? PreviousNonTrivia(IReadOnlyList<Token> tokens, int index)
    {
        for (int i = index; i >= 0; i--)
        {
            if (tokens[i].Kind is not TokenKind.NewLine and not TokenKind.Semicolon)
            {
                return i;
            }
        }

        return null;
    }

    private static int? NextNonTrivia(IReadOnlyList<Token> tokens, int index)
    {
        for (int i = index; i < tokens.Count; i++)
        {
            if (tokens[i].Kind is not TokenKind.NewLine and not TokenKind.Semicolon)
            {
                return i;
            }
        }

        return null;
    }

    private bool TryReadParameters(AnalyzerState state, ref int index, out List<ParameterInfo> parameters)
    {
        parameters = [];
        if (state.Match(ref index, TokenKind.CloseParen, out _))
        {
            return true;
        }

        while (index < state.Tokens.Count)
        {
            if (!TryReadType(state, ref index, allowSizedArrays: false, out string typeDisplay, out List<int> typeTokenIndices))
            {
                return false;
            }

            if (!IsIdentifier(state.Tokens, index))
            {
                return false;
            }

            Token nameToken = state.Tokens[index];
            parameters.Add(new ParameterInfo(nameToken.Text, nameToken.Text == "_", typeDisplay, index, typeTokenIndices));
            index++;

            if (state.Match(ref index, TokenKind.Comma, out _))
            {
                continue;
            }

            return state.Match(ref index, TokenKind.CloseParen, out _);
        }

        return false;
    }

    private bool TryReadBindingTargets(AnalyzerState state, ref int index, out List<BindingTargetInfo> bindings)
    {
        bindings = [];
        if (!IsIdentifier(state.Tokens, index))
        {
            return false;
        }

        while (index < state.Tokens.Count)
        {
            bindings.Add(new BindingTargetInfo(state.Tokens[index].Text, state.Tokens[index].Text == "_", index));
            index++;
            if (state.Match(ref index, TokenKind.Comma, out _))
            {
                if (!IsIdentifier(state.Tokens, index))
                {
                    return false;
                }

                continue;
            }

            return true;
        }

        return bindings.Count > 0;
    }

    private bool TryReadType(AnalyzerState state, ref int index, bool allowSizedArrays, out string typeDisplay, out List<int> typeTokenIndices)
    {
        int saved = index;
        typeTokenIndices = [];
        if (!TryReadTypeCore(state, ref index, allowSizedArrays, typeTokenIndices))
        {
            typeDisplay = string.Empty;
            index = saved;
            return false;
        }

        int last = index;
        typeDisplay = BuildCompactText(state.Tokens, saved, last);
        return true;
    }

    private bool TryReadTypeCore(AnalyzerState state, ref int index, bool allowSizedArrays, List<int> typeTokenIndices)
    {
        if (index >= state.Tokens.Count)
        {
            return false;
        }

        if (state.Tokens[index].Kind == TokenKind.OpenParen)
        {
            int saved = index;
            typeTokenIndices.Add(index++);
            if (!TryReadTypeCore(state, ref index, allowSizedArrays: false, typeTokenIndices))
            {
                index = saved;
                return false;
            }

            if (!state.Match(ref index, TokenKind.Comma, out int commaIndex))
            {
                index = saved;
                typeTokenIndices.RemoveAt(typeTokenIndices.Count - 1);
                return false;
            }

            typeTokenIndices.Add(commaIndex);
            do
            {
                if (!TryReadTypeCore(state, ref index, allowSizedArrays: false, typeTokenIndices))
                {
                    index = saved;
                    return false;
                }
            }
            while (state.Match(ref index, TokenKind.Comma, out commaIndex) && typeTokenIndices.AddReturnTrue(commaIndex));

            if (!state.Match(ref index, TokenKind.CloseParen, out int closeParen))
            {
                index = saved;
                return false;
            }

            typeTokenIndices.Add(closeParen);
        }
        else
        {
            if (!IsIdentifier(state.Tokens, index))
            {
                return false;
            }

            typeTokenIndices.Add(index++);
            while (index + 1 < state.Tokens.Count && state.Tokens[index].Kind == TokenKind.Dot && IsIdentifier(state.Tokens, index + 1))
            {
                typeTokenIndices.Add(index++);
                typeTokenIndices.Add(index++);
            }

            if (state.Match(ref index, TokenKind.LessThan, out int lessThan))
            {
                typeTokenIndices.Add(lessThan);
                do
                {
                    if (!TryReadTypeCore(state, ref index, allowSizedArrays: false, typeTokenIndices))
                    {
                        return false;
                    }
                }
                while (state.Match(ref index, TokenKind.Comma, out int comma) && typeTokenIndices.AddReturnTrue(comma));

                if (!state.Match(ref index, TokenKind.GreaterThan, out int greaterThan))
                {
                    return false;
                }

                typeTokenIndices.Add(greaterThan);
            }
        }

        while (index < state.Tokens.Count && state.Tokens[index].Kind == TokenKind.OpenBracket)
        {
            int openBracket = index++;
            typeTokenIndices.Add(openBracket);

            if (state.Match(ref index, TokenKind.CloseBracket, out int closeBracket))
            {
                typeTokenIndices.Add(closeBracket);
                continue;
            }

            if (!allowSizedArrays)
            {
                return false;
            }

            int dimensionStart = index;
            int dimensionEnd = FindMatching(state.Tokens, openBracket, TokenKind.OpenBracket, TokenKind.CloseBracket, state.Tokens.Count);
            if (dimensionEnd < 0)
            {
                return false;
            }

            while (index <= dimensionEnd)
            {
                typeTokenIndices.Add(index);
                index++;
            }
        }

        return true;
    }

    private static string BuildCompactText(IReadOnlyList<Token> tokens, int start, int endExclusive)
    {
        string result = string.Empty;
        for (int i = start; i < endExclusive; i++)
        {
            Token current = tokens[i];
            result += current.Text;
            if (i + 1 < endExclusive && NeedsSpaceBetween(current.Kind, tokens[i + 1].Kind))
            {
                result += " ";
            }
        }

        return result;
    }

    private static bool NeedsSpaceBetween(TokenKind left, TokenKind right)
        => left switch
        {
            TokenKind.Identifier or TokenKind.IntegerLiteral or TokenKind.FloatLiteral or TokenKind.StringLiteral or TokenKind.CharLiteral or TokenKind.GreaterThan
                when right is TokenKind.Identifier or TokenKind.IntegerLiteral or TokenKind.FloatLiteral or TokenKind.StringLiteral or TokenKind.CharLiteral => true,
            _ => false,
        };
}

internal sealed class Scope
{
    private readonly Dictionary<string, List<PscpServerSymbol>> _symbols = new(StringComparer.Ordinal);

    public Scope(Scope? parent, int startOffset, int endOffset)
    {
        Parent = parent;
        StartOffset = startOffset;
        EndOffset = endOffset;
    }

    public Scope? Parent { get; }

    public int StartOffset { get; }

    public int EndOffset { get; }

    public void Add(PscpServerSymbol symbol)
    {
        if (!_symbols.TryGetValue(symbol.Name, out List<PscpServerSymbol>? list))
        {
            list = [];
            _symbols[symbol.Name] = list;
        }

        list.Add(symbol);
    }

    public bool TryResolve(string name, int position, out PscpServerSymbol? symbol)
    {
        if (_symbols.TryGetValue(name, out List<PscpServerSymbol>? list))
        {
            symbol = list
                .Where(candidate => candidate.DeclarationSpan.Start <= position)
                .OrderByDescending(candidate => candidate.DeclarationSpan.Start)
                .FirstOrDefault();
            if (symbol is not null)
            {
                return true;
            }
        }

        if (Parent is not null)
        {
            return Parent.TryResolve(name, position, out symbol);
        }

        symbol = null;
        return false;
    }
}

internal sealed class AnalyzerState
{
    private readonly List<PscpServerDiagnostic> _diagnostics = [];
    private readonly List<PscpServerSymbol> _symbols = [];
    private readonly List<PscpServerReference> _references = [];
    private readonly Dictionary<int, TokenClassificationBuilder> _tokenClassifications = [];
    private readonly List<PscpDocumentSymbol> _documentSymbols = [];
    private readonly Dictionary<int, string> _tokenToSymbolId = [];
    private readonly Dictionary<int, PscpHoverEntry> _hoverByTokenIndex = [];
    private readonly Dictionary<int, PscpCompletionEntry> _intrinsicMembers = [];
    private readonly List<PscpCodeActionEntry> _codeActions = [];
    private readonly List<PscpInlayHintEntry> _inlayHints = [];
    private int _symbolId;

    public AnalyzerState(DocumentSnapshot snapshot, IReadOnlyList<Token> tokens)
    {
        Snapshot = snapshot;
        Tokens = tokens;
        Signatures = new Dictionary<string, PscpSignatureEntry>(PscpIntrinsics.Signatures, StringComparer.Ordinal);
    }

    public DocumentSnapshot Snapshot { get; }

    public IReadOnlyList<Token> Tokens { get; }

    public Dictionary<string, PscpSignatureEntry> Signatures { get; }

    public Scope CreateScope(Scope? parent, int startOffset, int endOffset)
        => new(parent, startOffset, endOffset);

    public void AddDiagnostic(string code, string message, TextSpan span, ServerDiagnosticSeverity severity, string? relatedSymbolId = null)
        => _diagnostics.Add(new PscpServerDiagnostic(code, message, span, severity, relatedSymbolId));

    public PscpServerSymbol AddSyntheticSymbol(
        Scope scope,
        string name,
        PscpServerSymbolKind kind,
        string? typeDisplay,
        string? documentation)
        => AddSymbol(scope, name, kind, new TextSpan(0, 0), new TextSpan(0, 0), new ScopeSpan(0, Snapshot.Text.Length), typeDisplay, isMutable: false, isIntrinsic: true, containerSymbolId: null, documentation);

    public PscpServerSymbol AddSymbol(
        Scope scope,
        string name,
        PscpServerSymbolKind kind,
        TextSpan declarationSpan,
        TextSpan selectionSpan,
        ScopeSpan symbolScope,
        string? typeDisplay,
        bool isMutable,
        bool isIntrinsic,
        string? containerSymbolId,
        string? documentation)
    {
        PscpServerSymbol symbol = new(
            $"sym{_symbolId++}",
            name,
            kind,
            declarationSpan,
            selectionSpan,
            symbolScope,
            typeDisplay,
            isMutable,
            isIntrinsic,
            containerSymbolId,
            documentation);

        _symbols.Add(symbol);
        scope.Add(symbol);
        return symbol;
    }

    public bool TryResolve(Scope scope, string name, int position, out PscpServerSymbol? symbol)
        => scope.TryResolve(name, position, out symbol);

    public bool Match(ref int index, TokenKind kind, out int matchedIndex)
    {
        if (index < Tokens.Count && Tokens[index].Kind == kind)
        {
            matchedIndex = index;
            index++;
            return true;
        }

        matchedIndex = -1;
        return false;
    }

    public void AddReference(PscpServerSymbol symbol, int tokenIndex, bool isDeclaration, bool isWrite)
    {
        Token token = Tokens[tokenIndex];
        _references.Add(new PscpServerReference(symbol.Id, token.Span, isDeclaration, isWrite));
        _tokenToSymbolId[tokenIndex] = symbol.Id;
        if (!_hoverByTokenIndex.ContainsKey(tokenIndex) && !string.IsNullOrWhiteSpace(symbol.Documentation))
        {
            _hoverByTokenIndex[tokenIndex] = new PscpHoverEntry(symbol.Documentation!);
        }
    }

    public void AddDocumentSymbol(PscpDocumentSymbol symbol)
        => _documentSymbols.Add(symbol);

    public void AddInlayHint(TextSpan span, string label)
        => _inlayHints.Add(new PscpInlayHintEntry(span, label));

    public void AddIntrinsicMember(int tokenIndex, PscpCompletionEntry completion)
        => _intrinsicMembers[tokenIndex] = completion;

    public void SetHover(int tokenIndex, string markdown)
        => _hoverByTokenIndex[tokenIndex] = new PscpHoverEntry(markdown);

    public void MarkToken(int tokenIndex, string tokenType, params string[] modifiers)
        => MarkToken(tokenIndex, tokenType, (IEnumerable<string>)modifiers);

    public void MarkToken(int tokenIndex, string tokenType, IEnumerable<string>? modifiers)
    {
        if (!_tokenClassifications.TryGetValue(tokenIndex, out TokenClassificationBuilder? builder))
        {
            builder = new TokenClassificationBuilder(tokenType);
            _tokenClassifications[tokenIndex] = builder;
        }

        builder.TokenType = tokenType;
        if (modifiers is not null)
        {
            foreach (string modifier in modifiers)
            {
                builder.Modifiers.Add(modifier);
            }
        }
    }

    public PscpAnalysisResult Build()
        => new()
        {
            Snapshot = Snapshot,
            Tokens = Tokens,
            Diagnostics = _diagnostics.OrderBy(d => d.Span.Start).ToArray(),
            Symbols = _symbols.OrderBy(s => s.SelectionSpan.Start).ToArray(),
            References = _references.OrderBy(r => r.Span.Start).ToArray(),
            SemanticTokens = _tokenClassifications
                .OrderBy(pair => pair.Key)
                .Select(pair => new SemanticTokenClassification(
                    Tokens[pair.Key].Span,
                    pair.Value.TokenType,
                    pair.Value.Modifiers.OrderBy(x => x, StringComparer.Ordinal).ToArray()))
                .ToArray(),
            DocumentSymbols = _documentSymbols.OrderBy(symbol => symbol.Range.Start).ToArray(),
            TokenToSymbolId = new Dictionary<int, string>(_tokenToSymbolId),
            HoverByTokenIndex = new Dictionary<int, PscpHoverEntry>(_hoverByTokenIndex),
            IntrinsicMembers = new Dictionary<int, PscpCompletionEntry>(_intrinsicMembers),
            Signatures = new Dictionary<string, PscpSignatureEntry>(Signatures, StringComparer.Ordinal),
            CodeActions = _codeActions.ToArray(),
            InlayHints = _inlayHints.ToArray(),
        };

    private sealed class TokenClassificationBuilder
    {
        public TokenClassificationBuilder(string tokenType)
        {
            TokenType = tokenType;
        }

        public string TokenType { get; set; }

        public HashSet<string> Modifiers { get; } = new(StringComparer.Ordinal);
    }
}

internal static class AnalyzerCollectionExtensions
{
    public static bool AddReturnTrue(this List<int> list, int value)
    {
        list.Add(value);
        return true;
    }
}
