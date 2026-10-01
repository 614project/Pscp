using System.Text.RegularExpressions;

namespace Pscp.Transpiler;

// Syntax questions shared by the binder, the semantic analyzer and the emitter.
internal static class PscpSyntaxFacts
{
    // A compile-time constant built from literals: `1_000_000_007`, `'\n'`, `"abc"`, `1L << 20`, `-1`, `(a + 1)`
    // over other literals. Names of other constants are not followed.
    public static bool IsConstantExpression(Expression expression)
        => expression switch
        {
            LiteralExpression { Kind: not LiteralKind.Null } => true,
            UnaryExpression { Operator: UnaryOperator.Plus or UnaryOperator.Negate } unary => IsConstantExpression(unary.Operand),
            UnaryExpression { Operator: UnaryOperator.LogicalNot } unary => IsConstantExpression(unary.Operand),
            BinaryExpression binary when binary.Operator is BinaryOperator.Add
                    or BinaryOperator.Subtract
                    or BinaryOperator.Multiply
                    or BinaryOperator.Divide
                    or BinaryOperator.Modulo
                    or BinaryOperator.ShiftLeft
                    or BinaryOperator.ShiftRight
                    or BinaryOperator.BitwiseAnd
                    or BinaryOperator.BitwiseOr
                    or BinaryOperator.BitwiseXor
                => IsConstantExpression(binary.Left) && IsConstantExpression(binary.Right),
            _ => false,
        };

    // Value of an integer constant expression such as `0`, `-3`, `2 * 5`, `1L << 20`.
    public static bool TryEvaluateIntegerConstant(Expression expression, out long value)
    {
        value = 0;
        switch (expression)
        {
            case LiteralExpression { Kind: LiteralKind.Integer } literal:
                return PscpNumericLiterals.TryGetInt64Value(literal.RawText, out value);
            case UnaryExpression { Operator: UnaryOperator.Negate } negate when TryEvaluateIntegerConstant(negate.Operand, out long operand):
                value = -operand;
                return true;
            case UnaryExpression { Operator: UnaryOperator.Plus } plus:
                return TryEvaluateIntegerConstant(plus.Operand, out value);
            case BinaryExpression binary when TryEvaluateIntegerConstant(binary.Left, out long left) && TryEvaluateIntegerConstant(binary.Right, out long right):
                switch (binary.Operator)
                {
                    case BinaryOperator.Add: value = left + right; return true;
                    case BinaryOperator.Subtract: value = left - right; return true;
                    case BinaryOperator.Multiply: value = left * right; return true;
                    case BinaryOperator.Divide when right != 0: value = left / right; return true;
                    case BinaryOperator.Modulo when right != 0: value = left % right; return true;
                    case BinaryOperator.ShiftLeft when right is >= 0 and < 64: value = left << (int)right; return true;
                    case BinaryOperator.ShiftRight when right is >= 0 and < 64: value = left >> (int)right; return true;
                    default: return false;
                }
            default:
                return false;
        }
    }

    // Names declared by the initializer of a C-style `for` header: `int i = 0; ...` declares `i`.
    public static IEnumerable<string> GetCStyleForHeaderBindings(string headerText)
    {
        string firstSegment = headerText.Split(';')[0];
        Match match = Regex.Match(firstSegment, @"^\s*(?:var\s+|let\s+|mut\s+)?[A-Za-z_][A-Za-z0-9_<>.,\[\]]*\s+([A-Za-z_][A-Za-z0-9_]*)\s*=");
        if (match.Success && match.Groups.Count > 1)
        {
            yield return match.Groups[1].Value;
        }
    }

    // Parameter names of a positional record header: `record struct Job(int Id, long Time)`.
    public static IEnumerable<string> GetPrimaryConstructorParameterNames(string headerText)
    {
        int open = headerText.IndexOf('(');
        int close = headerText.LastIndexOf(')');
        if (open < 0 || close <= open)
        {
            yield break;
        }

        int depth = 0;
        int start = open + 1;
        for (int i = open + 1; i <= close; i++)
        {
            char ch = i == close ? ',' : headerText[i];
            if (ch is '<' or '(' or '[')
            {
                depth++;
            }
            else if (ch is '>' or ')' or ']')
            {
                depth--;
            }
            else if (ch == ',' && depth == 0)
            {
                string parameter = headerText[start..i].Trim();
                int equals = parameter.IndexOf('=');
                if (equals >= 0)
                {
                    parameter = parameter[..equals].Trim();
                }

                Match match = Regex.Match(parameter, @"([A-Za-z_][A-Za-z0-9_]*)$");
                if (match.Success)
                {
                    yield return match.Groups[1].Value;
                }

                start = i + 1;
            }
        }
    }
}
