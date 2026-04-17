namespace Pscp.Transpiler;

public sealed partial class Parser
{
    private bool TryParseBindingTarget(out BindingTarget? target)
    {
        target = null;

        if (Current.Kind == TokenKind.OpenParen)
        {
            target = ParseTupleBindingTarget();
            return true;
        }

        if (Current.Kind != TokenKind.Identifier)
        {
            return false;
        }

        target = ParseBindingTarget();
        return true;
    }

    private BindingTarget ParseBindingTarget()
    {
        if (Current.Kind == TokenKind.OpenParen)
        {
            return ParseTupleBindingTarget();
        }

        Token token = Expect(TokenKind.Identifier, "Expected identifier or discard target.");
        return token.Text == "_" ? new DiscardTarget() : new NameTarget(token.Text);
    }

    private TupleTarget ParseTupleBindingTarget()
    {
        Expect(TokenKind.OpenParen, "Expected '(' to start tuple binding target.");
        List<BindingTarget> elements = [];
        do
        {
            elements.Add(ParseBindingTarget());
        }
        while (Match(TokenKind.Comma));

        Expect(TokenKind.CloseParen, "Expected ')' after tuple binding target.");
        return new TupleTarget(elements);
    }

    private TypeSyntax ParseTypeSyntax(bool allowSizedArrays)
        => TryParseTypeSyntax(allowSizedArrays) ?? new NamedTypeSyntax("object", Immutable.List<TypeSyntax>());

    private TypeSyntax? TryParseTypeSyntax(bool allowSizedArrays)
    {
        int savedPosition = _position;

        TypeSyntax? baseType = Current.Kind == TokenKind.OpenParen
            ? TryParseTupleType()
            : TryParseNamedType();

        if (baseType is null)
        {
            _position = savedPosition;
            return null;
        }

        TypeSyntax current = baseType;
        while (true)
        {
            if (Match(TokenKind.Question))
            {
                current = new NullableTypeSyntax(current);
                continue;
            }

            if (!Match(TokenKind.OpenBracket))
            {
                break;
            }

            if (Match(TokenKind.CloseBracket))
            {
                current = current is ArrayTypeSyntax arrayType
                    ? new ArrayTypeSyntax(arrayType.ElementType, arrayType.Depth + 1)
                    : new ArrayTypeSyntax(current, 1);
                continue;
            }

            if (!allowSizedArrays)
            {
                _position = savedPosition;
                return null;
            }

            List<Expression> sizedDimensions = [];
            do
            {
                Expression dimension = ParseExpression();
                Expect(TokenKind.CloseBracket, "Expected ']' after sized array dimension.");
                sizedDimensions.Add(dimension);
            }
            while (Match(TokenKind.OpenBracket));

            current = new SizedArrayTypeSyntax(current, sizedDimensions);
            break;
        }

        return current;
    }

    private TypeSyntax? TryParseNamedType()
    {
        if (!IsTypeNameToken(Current.Kind))
        {
            return null;
        }

        string name = ParseQualifiedName();
        IReadOnlyList<TypeSyntax> typeArguments = ParseOptionalTypeArguments();
        return new NamedTypeSyntax(name, typeArguments);
    }

    private TypeSyntax? TryParseTupleType()
    {
        int savedPosition = _position;
        if (!Match(TokenKind.OpenParen))
        {
            return null;
        }

        List<TypeSyntax> elements = [];
        TypeSyntax? first = TryParseTypeSyntax(allowSizedArrays: false);
        if (first is null)
        {
            _position = savedPosition;
            return null;
        }

        elements.Add(first);
        if (!Match(TokenKind.Comma))
        {
            _position = savedPosition;
            return null;
        }

        do
        {
            TypeSyntax? next = TryParseTypeSyntax(allowSizedArrays: false);
            if (next is null)
            {
                _position = savedPosition;
                return null;
            }

            elements.Add(next);
        }
        while (Match(TokenKind.Comma));

        Expect(TokenKind.CloseParen, "Expected ')' after tuple type.");
        return new TupleTypeSyntax(elements);
    }

    private IReadOnlyList<TypeSyntax> ParseOptionalTypeArguments()
    {
        if (!Match(TokenKind.LessThan))
        {
            return Immutable.List<TypeSyntax>();
        }

        List<TypeSyntax> typeArguments = [];
        do
        {
            typeArguments.Add(ParseTypeSyntax(allowSizedArrays: false));
        }
        while (Match(TokenKind.Comma));

        Expect(TokenKind.GreaterThan, "Expected '>' after generic type arguments.");
        return typeArguments;
    }

    private string ParseQualifiedName()
    {
        Token first = ExpectTypeNameToken("Expected type name.");
        string name = first.Text;

        while (Match(TokenKind.Dot))
        {
            Token next = ExpectTypeNameToken("Expected identifier after '.'.");
            name += "." + next.Text;
        }

        return name;
    }

    private string ParseIdentifierWithOptionalGenericSuffix()
    {
        Token identifier = Expect(TokenKind.Identifier, "Expected identifier.");
        string name = identifier.Text;

        if (Current.Kind == TokenKind.LessThan && LooksLikeGenericArgumentSuffix())
        {
            name += ParseGenericArgumentSuffixText();
        }

        return name;
    }

    private bool LooksLikeGenericArgumentSuffix()
    {
        int savedPosition = _position;

        if (!Match(TokenKind.LessThan))
        {
            return false;
        }

        do
        {
            TypeSyntax? type = TryParseTypeSyntax(allowSizedArrays: false);
            if (type is null)
            {
                _position = savedPosition;
                return false;
            }
        }
        while (Match(TokenKind.Comma));

        bool result = Match(TokenKind.GreaterThan);
        _position = savedPosition;
        return result;
    }

    private string ParseGenericArgumentSuffixText()
    {
        int start = _position;
        Expect(TokenKind.LessThan, "Expected '<' in generic argument list.");
        int depth = 1;

        while (depth > 0 && Current.Kind != TokenKind.EndOfFile)
        {
            if (Current.Kind == TokenKind.LessThan)
            {
                depth++;
            }
            else if (Current.Kind == TokenKind.GreaterThan)
            {
                depth--;
            }

            Next();
        }

        int end = _position;
        return TokensToText(start, end);
    }

    private bool LooksLikeParenthesizedLambda()
    {
        int savedPosition = _position;
        if (!Match(TokenKind.OpenParen))
        {
            return false;
        }

        int depth = 1;
        while (depth > 0 && Current.Kind != TokenKind.EndOfFile)
        {
            if (Current.Kind == TokenKind.OpenParen)
            {
                depth++;
            }
            else if (Current.Kind == TokenKind.CloseParen)
            {
                depth--;
            }

            Next();
        }

        bool result = Current.Kind == TokenKind.FatArrow;
        _position = savedPosition;
        return result;
    }
}
