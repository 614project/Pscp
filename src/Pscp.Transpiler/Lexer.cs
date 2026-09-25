using System.Globalization;
using System.Text;

namespace Pscp.Transpiler;

public sealed class Lexer
{
    private readonly string _text;
    private readonly List<Diagnostic> _diagnostics = [];
    private int _position;
    private int _parenDepth;
    private int _bracketDepth;
    private int _braceDepth;
    private TokenKind _previousKind = TokenKind.NewLine;

    public Lexer(string text)
    {
        _text = text ?? string.Empty;
    }

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    public IReadOnlyList<Token> Lex()
    {
        List<Token> tokens = [];

        while (true)
        {
            Token token = NextToken();
            tokens.Add(token);
            _previousKind = token.Kind;
            if (token.Kind == TokenKind.EndOfFile)
            {
                break;
            }
        }

        return tokens;
    }

    private Token NextToken()
    {
        while (true)
        {
            if (IsEnd)
            {
                return new Token(TokenKind.EndOfFile, string.Empty, _position);
            }

            char ch = Current;
            if (ch is ' ' or '\t' or '\r')
            {
                _position++;
                continue;
            }

            if (ch == '\n')
            {
                _position++;
                if (_braceDepth > 0 || (_parenDepth == 0 && _bracketDepth == 0))
                {
                    return new Token(TokenKind.NewLine, "\n", _position - 1);
                }

                continue;
            }

            if (ch == '/' && Peek(1) == '/')
            {
                _position += 2;
                while (!IsEnd && Current != '\n')
                {
                    _position++;
                }

                continue;
            }

            if (ch == '/' && Peek(1) == '*')
            {
                int start = _position;
                _position += 2;

                while (!IsEnd && !(Current == '*' && Peek(1) == '/'))
                {
                    _position++;
                }

                if (IsEnd)
                {
                    _diagnostics.Add(new Diagnostic("Unterminated block comment.", new TextSpan(start, _position - start)));
                    return new Token(TokenKind.EndOfFile, string.Empty, _position);
                }

                _position += 2;
                continue;
            }

            break;
        }

        int tokenStart = _position;

        if (TryReadMultiCharacterToken(out Token? multiCharacterToken))
        {
            return multiCharacterToken!;
        }

        if (char.IsDigit(Current))
        {
            return ReadNumber();
        }

        if (Current == '$' && Peek(1) == '"')
        {
            return ReadInterpolatedString();
        }

        if (Current == '"')
        {
            return ReadString();
        }

        if (Current == '\'')
        {
            return ReadChar();
        }

        if (IsIdentifierStart(Current))
        {
            return ReadIdentifierOrKeyword();
        }

        _position++;

        return _text[tokenStart] switch
        {
            ';' => new Token(TokenKind.Semicolon, ";", tokenStart),
            ',' => new Token(TokenKind.Comma, ",", tokenStart),
            '.' => new Token(TokenKind.Dot, ".", tokenStart),
            ':' => new Token(TokenKind.Colon, ":", tokenStart),
            '?' => new Token(TokenKind.Question, "?", tokenStart),
            '(' => OpenToken(TokenKind.OpenParen, "(", ref _parenDepth, tokenStart),
            ')' => CloseToken(TokenKind.CloseParen, ")", ref _parenDepth, tokenStart),
            '{' => OpenToken(TokenKind.OpenBrace, "{", ref _braceDepth, tokenStart),
            '}' => CloseToken(TokenKind.CloseBrace, "}", ref _braceDepth, tokenStart),
            '[' => OpenToken(TokenKind.OpenBracket, "[", ref _bracketDepth, tokenStart),
            ']' => CloseToken(TokenKind.CloseBracket, "]", ref _bracketDepth, tokenStart),
            '<' => new Token(TokenKind.LessThan, "<", tokenStart),
            '>' => new Token(TokenKind.GreaterThan, ">", tokenStart),
            '=' => new Token(TokenKind.Equal, "=", tokenStart),
            '+' => new Token(TokenKind.Plus, "+", tokenStart),
            '-' => new Token(TokenKind.Minus, "-", tokenStart),
            '*' => new Token(TokenKind.Star, "*", tokenStart),
            '/' => new Token(TokenKind.Slash, "/", tokenStart),
            '%' => new Token(TokenKind.Percent, "%", tokenStart),
            '!' => new Token(TokenKind.Bang, "!", tokenStart),
            '^' => new Token(TokenKind.Caret, "^", tokenStart),
            '~' => new Token(TokenKind.Tilde, "~", tokenStart),
            '&' => new Token(TokenKind.Amp, "&", tokenStart),
            '|' => new Token(TokenKind.Pipe, "|", tokenStart),
            _ => UnknownToken(tokenStart),
        };
    }

    private bool TryReadMultiCharacterToken(out Token? token)
    {
        token = null;
        int start = _position;

        if (Match("<<="))
        {
            token = new Token(TokenKind.LessLessEqual, "<<=", start);
        }
        else if (Match(">>="))
        {
            token = new Token(TokenKind.GreaterGreaterEqual, ">>=", start);
        }
        else if (Match("<=>"))
        {
            token = new Token(TokenKind.Spaceship, "<=>", start);
        }
        else if (Match("..<"))
        {
            token = new Token(TokenKind.DotDotLess, "..<", start);
        }
        else if (Match("..="))
        {
            token = new Token(TokenKind.DotDotEqual, "..=", start);
        }
        else if (Match(".."))
        {
            token = new Token(TokenKind.DotDot, "..", start);
        }
        else if (Match("=>"))
        {
            token = new Token(TokenKind.FatArrow, "=>", start);
        }
        else if (Match(":="))
        {
            token = new Token(TokenKind.ColonEqual, ":=", start);
        }
        else if (Match("->"))
        {
            token = new Token(TokenKind.Arrow, "->", start);
        }
        else if (Match("|>"))
        {
            token = new Token(TokenKind.PipeGreater, "|>", start);
        }
        else if (Match("<|"))
        {
            token = new Token(TokenKind.LessPipe, "<|", start);
        }
        else if (Match("&&"))
        {
            token = new Token(TokenKind.AmpAmp, "&&", start);
        }
        else if (Match("||"))
        {
            token = new Token(TokenKind.PipePipe, "||", start);
        }
        else if (Match("=="))
        {
            token = new Token(TokenKind.EqualEqual, "==", start);
        }
        else if (Match("!="))
        {
            token = new Token(TokenKind.BangEqual, "!=", start);
        }
        else if (Match("<="))
        {
            token = new Token(TokenKind.LessEqual, "<=", start);
        }
        else if (Match(">="))
        {
            token = new Token(TokenKind.GreaterEqual, ">=", start);
        }
        else if (Match("++"))
        {
            token = new Token(TokenKind.PlusPlus, "++", start);
        }
        else if (Match("--"))
        {
            token = new Token(TokenKind.MinusMinus, "--", start);
        }
        else if (Match("+="))
        {
            token = new Token(TokenKind.PlusEqual, "+=", start);
        }
        else if (Match("-="))
        {
            token = new Token(TokenKind.MinusEqual, "-=", start);
        }
        else if (Match("*="))
        {
            token = new Token(TokenKind.StarEqual, "*=", start);
        }
        else if (Match("/="))
        {
            token = new Token(TokenKind.SlashEqual, "/=", start);
        }
        else if (Match("%="))
        {
            token = new Token(TokenKind.PercentEqual, "%=", start);
        }
        else if (Match("&="))
        {
            token = new Token(TokenKind.AmpEqual, "&=", start);
        }
        else if (Match("|="))
        {
            token = new Token(TokenKind.PipeEqual, "|=", start);
        }
        else if (Match("^="))
        {
            token = new Token(TokenKind.CaretEqual, "^=", start);
        }

        return token is not null;
    }

    // Numeric literals follow C#: decimal, `0x` hex and `0b` binary integers, `_` digit separators, fractions and
    // exponents, and the `u`/`l`/`ul` (integer) and `f`/`d`/`m` (real) suffixes. Right after a `.` member access
    // only plain digits are read, so the tuple projection `p.1.2` is `p.1` then `.2`, not `p` then `1.2`.
    private Token ReadNumber()
    {
        int start = _position;
        bool isFloat = false;

        if (_previousKind == TokenKind.Dot)
        {
            ReadDigits(char.IsAsciiDigit);
            return new Token(TokenKind.IntegerLiteral, _text[start.._position], start);
        }

        if (Current == '0' && (Peek(1) is 'x' or 'X' or 'b' or 'B'))
        {
            bool hex = Peek(1) is 'x' or 'X';
            _position += 2;
            if (!ReadDigits(hex ? char.IsAsciiHexDigit : static ch => ch is '0' or '1'))
            {
                _diagnostics.Add(new Diagnostic(hex ? "Hexadecimal literal requires at least one digit." : "Binary literal requires at least one digit.", new TextSpan(start, _position - start)));
            }

            ReadIntegerSuffix();
            return new Token(TokenKind.IntegerLiteral, _text[start.._position], start);
        }

        ReadDigits(char.IsAsciiDigit);

        if (!IsEnd && Current == '.' && char.IsAsciiDigit(Peek(1)))
        {
            isFloat = true;
            _position++;
            ReadDigits(char.IsAsciiDigit);
        }

        if (!IsEnd && (Current == 'e' || Current == 'E')
            && (char.IsAsciiDigit(Peek(1)) || (Peek(1) is '+' or '-' && char.IsAsciiDigit(Peek(2)))))
        {
            isFloat = true;
            _position++;
            if (Current is '+' or '-')
            {
                _position++;
            }

            ReadDigits(char.IsAsciiDigit);
        }

        if (!IsEnd && Current is 'f' or 'F' or 'd' or 'D' or 'm' or 'M' && !IsIdentifierPart(Peek(1)))
        {
            isFloat = true;
            _position++;
        }
        else if (!isFloat)
        {
            ReadIntegerSuffix();
        }

        string text = _text[start.._position];
        return new Token(isFloat ? TokenKind.FloatLiteral : TokenKind.IntegerLiteral, text, start);
    }

    // Reads digits with `_` separators allowed between them; returns false when no digit was read.
    private bool ReadDigits(Func<char, bool> isDigit)
    {
        bool any = false;
        while (!IsEnd && (isDigit(Current) || (Current == '_' && any && Peek(1) is var next && (next == '_' || isDigit(next)))))
        {
            any |= Current != '_';
            _position++;
        }

        return any;
    }

    private void ReadIntegerSuffix()
    {
        if (IsEnd)
        {
            return;
        }

        if (Current is 'u' or 'U')
        {
            _position++;
            if (!IsEnd && Current is 'l' or 'L')
            {
                _position++;
            }
        }
        else if (Current is 'l' or 'L')
        {
            _position++;
            if (!IsEnd && Current is 'u' or 'U')
            {
                _position++;
            }
        }
    }

    private Token ReadString()
    {
        int start = _position++;
        StringBuilder builder = new();
        bool closed = false;

        while (!IsEnd)
        {
            char ch = Current;
            _position++;

            if (ch == '"')
            {
                closed = true;
                break;
            }

            if (ch == '\\' && !IsEnd)
            {
                builder.Append(ch);
                builder.Append(Current);
                _position++;
                continue;
            }

            builder.Append(ch);
        }

        if (!closed)
        {
            _diagnostics.Add(new Diagnostic("Unterminated string literal.", new TextSpan(start, _position - start)));
        }

        return new Token(TokenKind.StringLiteral, _text[start.._position], start);
    }

    private Token ReadInterpolatedString()
    {
        int start = _position;
        _position += 2;
        int interpolationDepth = 0;
        bool closed = false;

        while (!IsEnd)
        {
            char ch = Current;

            if (interpolationDepth == 0)
            {
                if (ch == '"')
                {
                    _position++;
                    closed = true;
                    break;
                }

                if (ch == '\\')
                {
                    _position++;
                    if (!IsEnd)
                    {
                        _position++;
                    }

                    continue;
                }

                if ((ch == '{' || ch == '}') && Peek(1) == ch)
                {
                    _position += 2;
                    continue;
                }

                if (ch == '{')
                {
                    interpolationDepth = 1;
                    _position++;
                    continue;
                }

                _position++;
                continue;
            }

            if (ch == '"' || ch == '\'')
            {
                SkipStringLike(ch);
                continue;
            }

            if (ch == '/' && Peek(1) == '/')
            {
                _position += 2;
                while (!IsEnd && Current != '\n')
                {
                    _position++;
                }

                continue;
            }

            if (ch == '/' && Peek(1) == '*')
            {
                _position += 2;
                while (!IsEnd && !(Current == '*' && Peek(1) == '/'))
                {
                    _position++;
                }

                if (!IsEnd)
                {
                    _position += 2;
                }

                continue;
            }

            if (ch == '{')
            {
                interpolationDepth++;
                _position++;
                continue;
            }

            if (ch == '}')
            {
                interpolationDepth--;
                _position++;
                continue;
            }

            _position++;
        }

        if (!closed)
        {
            _diagnostics.Add(new Diagnostic("Unterminated interpolated string literal.", new TextSpan(start, _position - start)));
        }

        return new Token(TokenKind.InterpolatedStringLiteral, _text[start.._position], start);
    }

    private Token ReadChar()
    {
        int start = _position++;
        bool escaped = false;
        bool closed = false;

        while (!IsEnd)
        {
            char ch = Current;
            _position++;

            if (!escaped && ch == '\\')
            {
                escaped = true;
                continue;
            }

            if (!escaped && ch == '\'')
            {
                closed = true;
                break;
            }

            escaped = false;
        }

        if (!closed)
        {
            _diagnostics.Add(new Diagnostic("Unterminated character literal.", new TextSpan(start, _position - start)));
        }

        return new Token(TokenKind.CharLiteral, _text[start.._position], start);
    }

    private void SkipStringLike(char delimiter)
    {
        _position++;
        bool escaped = false;

        while (!IsEnd)
        {
            char ch = Current;
            _position++;

            if (!escaped && ch == '\\')
            {
                escaped = true;
                continue;
            }

            if (!escaped && ch == delimiter)
            {
                break;
            }

            escaped = false;
        }
    }

    private Token ReadIdentifierOrKeyword()
    {
        int start = _position;
        _position++;

        while (!IsEnd && IsIdentifierPart(Current))
        {
            _position++;
        }

        string text = _text[start.._position];

        return new Token(text switch
        {
            "let" => TokenKind.Let,
            "var" => TokenKind.Var,
            "mut" => TokenKind.Mut,
            "rec" => TokenKind.Rec,
            "if" => TokenKind.If,
            "then" => TokenKind.Then,
            "else" => TokenKind.Else,
            "for" => TokenKind.For,
            "in" => TokenKind.In,
            "do" => TokenKind.Do,
            "while" => TokenKind.While,
            "break" => TokenKind.Break,
            "continue" => TokenKind.Continue,
            "return" => TokenKind.Return,
            "true" => TokenKind.True,
            "false" => TokenKind.False,
            "null" => TokenKind.Null,
            "and" => TokenKind.And,
            "or" => TokenKind.Or,
            "xor" => TokenKind.Xor,
            "not" => TokenKind.Not,
            "where" => TokenKind.Where,
            "new" => TokenKind.New,
            "class" => TokenKind.Class,
            "struct" => TokenKind.Struct,
            "record" => TokenKind.Record,
            "ref" => TokenKind.Ref,
            "out" => TokenKind.Out,
            "namespace" => TokenKind.Namespace,
            "using" => TokenKind.Using,
            "is" => TokenKind.Is,
            _ => TokenKind.Identifier,
        }, text, start);
    }

    private static bool IsIdentifierStart(char ch)
        => ch == '_' || char.IsLetter(ch);

    private static bool IsIdentifierPart(char ch)
        => ch == '_' || char.IsLetterOrDigit(ch);

    private Token UnknownToken(int start)
    {
        _diagnostics.Add(new Diagnostic(
            $"Unexpected character '{_text[start]}'.",
            new TextSpan(start, 1)));
        return new Token(TokenKind.Identifier, _text[start].ToString(CultureInfo.InvariantCulture), start);
    }

    private Token OpenToken(TokenKind kind, string text, ref int depth, int start)
    {
        depth++;
        return new Token(kind, text, start);
    }

    private Token CloseToken(TokenKind kind, string text, ref int depth, int start)
    {
        depth = Math.Max(0, depth - 1);
        return new Token(kind, text, start);
    }

    private bool Match(string text)
    {
        if (_position + text.Length > _text.Length)
        {
            return false;
        }

        if (!_text.AsSpan(_position, text.Length).SequenceEqual(text.AsSpan()))
        {
            return false;
        }

        _position += text.Length;
        return true;
    }

    private char Peek(int offset)
        => _position + offset < _text.Length ? _text[_position + offset] : '\0';

    private bool IsEnd => _position >= _text.Length;

    private char Current => _text[_position];
}


