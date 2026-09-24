using System.Text.RegularExpressions;

namespace Pscp.Transpiler;

// Statement-level hoisting of value blocks.
//
// Many intrinsics (sum, min, count, materialized ranges, typed array reads, ...) lower to a sequence of
// statements that produce one value. Inside an arbitrary expression they have to be wrapped in a
// `__PscpThunk.run(() => { ... })` lambda, which allocates a closure, and cannot capture `ref` parameters or
// `this` inside structs. When such a value block sits in an unconditionally evaluated position of a
// statement, its statements are instead written right before the statement and the expression refers to the
// block's result temporary.
internal sealed partial class CSharpEmitter
{
    private static readonly Regex ReturnKeywordPattern = new(@"\breturn\b", RegexOptions.CultureInvariant);

    private readonly Dictionary<string, ValueBlock> _valueBlocks = new(StringComparer.Ordinal);
    private readonly Dictionary<Expression, string> _hoistedValues = new(ReferenceEqualityComparer.Instance);
    private int _hoistSuppression;

    private sealed record ValueBlock(string Prelude, string Result);

    // Emits statements that end in a single `return <result>;` as a thunk that statement emitters may hoist.
    private string EmitValueBlock(string body)
    {
        string thunk = EmitEval(body);
        string trimmed = body.TrimEnd();
        MatchCollection returns = ReturnKeywordPattern.Matches(trimmed);
        if (returns.Count == 1 && trimmed.EndsWith(';'))
        {
            Match finalReturn = returns[0];
            string prelude = trimmed[..finalReturn.Index].Trim();
            string result = trimmed[(finalReturn.Index + finalReturn.Length)..^1].Trim();
            if (prelude.Length > 0 && result.Length > 0)
            {
                _valueBlocks[thunk] = new ValueBlock(prelude, result);
            }
        }

        return thunk;
    }

    private void EmitStatementWithHoisting(Expression? root, TypeSyntax? rootHint, Action emit)
        => _ = TryEmitStatementWithHoisting(root, rootHint, emit);

    // Returns true when at least one value block was hoisted in front of the emitted statement.
    private bool TryEmitStatementWithHoisting(Expression? root, TypeSyntax? rootHint, Action emit)
    {
        if (root is null || _hoistSuppression > 0)
        {
            emit();
            return false;
        }

        int mark = _writer.Length;
        List<(Expression Node, string Result)> hoisted = [];
        _hoistSuppression++;
        try
        {
            HoistWalk(root, rootHint, isRoot: true, hoisted);
        }
        finally
        {
            _hoistSuppression--;
        }

        if (hoisted.Count == 0)
        {
            emit();
            return false;
        }

        int statementStart = _writer.Length;
        foreach ((Expression node, string result) in hoisted)
        {
            _hoistedValues[node] = result;
        }

        bool allUsed;
        try
        {
            emit();
            string statementText = _writer.TextFrom(statementStart);
            allUsed = hoisted.All(entry => ContainsIdentifier(statementText, entry.Result));
        }
        finally
        {
            foreach ((Expression node, _) in hoisted)
            {
                _hoistedValues.Remove(node);
            }
        }

        if (allUsed)
        {
            return true;
        }

        // A statement-level lowering consumed the expression without going through EmitExpression, so the
        // hoisted prelude would run twice. Fall back to the plain emission.
        _writer.Truncate(mark);
        emit();
        return false;
    }

    // Walks positions that are evaluated exactly once, left to right, before the statement's own effects.
    // Returns true when the visited expression may have side effects that were not hoisted; anything to its
    // right must then stay in place to preserve evaluation order.
    private bool HoistWalk(Expression expression, TypeSyntax? hint, bool isRoot, List<(Expression Node, string Result)> hoisted)
    {
        switch (expression)
        {
            case LiteralExpression or IdentifierExpression or DiscardExpression or LambdaExpression:
                return false;
            case CallExpression or AggregationExpression:
                if (TryHoistValueBlock(expression, isRoot ? hint : null, hoisted))
                {
                    return false;
                }

                if (expression is CallExpression call)
                {
                    bool barrier = call.Callee is MemberAccessExpression member
                        && HoistWalk(member.Receiver, null, isRoot: false, hoisted);
                    foreach (ArgumentSyntax argument in call.Arguments)
                    {
                        if (barrier)
                        {
                            break;
                        }

                        barrier = argument switch
                        {
                            ExpressionArgumentSyntax { Modifier: ArgumentModifier.None } plain
                                => HoistWalk(plain.Expression, null, isRoot: false, hoisted),
                            ExpressionArgumentSyntax byReference
                                => !IsSideEffectFreeReceiver(byReference.Expression),
                            _ => true,
                        };
                    }
                }

                return true;
            case CollectionExpression or RangeExpression or GeneratorExpression or NewArrayExpression or TargetTypedNewArrayExpression:
                // The lowering of these depends on the target type, which is only known for the root.
                return !(isRoot && TryHoistValueBlock(expression, hint, hoisted));
            case BinaryExpression binary:
                if (binary.Operator is BinaryOperator.LogicalAnd or BinaryOperator.LogicalOr
                    or BinaryOperator.PipeLeft or BinaryOperator.PipeRight)
                {
                    HoistWalk(binary.Left, null, isRoot: false, hoisted);
                    return true;
                }

                return HoistWalk(binary.Left, null, isRoot: false, hoisted)
                    || HoistWalk(binary.Right, null, isRoot: false, hoisted);
            case UnaryExpression unary:
                // `~x` may be a Peek on a known data structure; peeking has no side effect.
                return HoistWalk(unary.Operand, null, isRoot: false, hoisted);
            case TupleExpression tuple:
                foreach (Expression element in tuple.Elements)
                {
                    if (HoistWalk(element, null, isRoot: false, hoisted))
                    {
                        return true;
                    }
                }

                return false;
            case MemberAccessExpression member:
                return HoistWalk(member.Receiver, null, isRoot: false, hoisted);
            case TupleProjectionExpression projection:
                return HoistWalk(projection.Receiver, null, isRoot: false, hoisted);
            case IndexExpression index:
                if (HoistWalk(index.Receiver, null, isRoot: false, hoisted))
                {
                    return true;
                }

                foreach (Expression argument in index.Arguments)
                {
                    if (HoistWalk(argument, null, isRoot: false, hoisted))
                    {
                        return true;
                    }
                }

                return false;
            case ConditionalExpression conditional:
                HoistWalk(conditional.Condition, null, isRoot: false, hoisted);
                return true;
            case IfExpression ifExpression:
                HoistWalk(ifExpression.Condition, null, isRoot: false, hoisted);
                return true;
            case IsPatternExpression isPattern:
                return HoistWalk(isPattern.Left, null, isRoot: false, hoisted);
            default:
                return true;
        }
    }

    private bool TryHoistValueBlock(Expression expression, TypeSyntax? hint, List<(Expression Node, string Result)> hoisted)
    {
        string emitted = EmitExpression(expression, hint);
        if (!_valueBlocks.TryGetValue(emitted, out ValueBlock? block))
        {
            return false;
        }

        _writer.WriteLine(block.Prelude);
        hoisted.Add((expression, block.Result));
        return true;
    }

    // Rewrites `signature => expr;` into a block body when `expr` contains a hoistable value block.
    private bool TryEmitHoistedExpressionBody(string signature, Expression expression, bool isVoidLike, bool isProperty)
    {
        if (_hoistSuppression > 0)
        {
            return false;
        }

        int mark = _writer.Length;
        _writer.WriteLine(signature);
        _writer.WriteLine("{");
        _writer.Indent();
        if (isProperty)
        {
            _writer.WriteLine("get");
            _writer.WriteLine("{");
            _writer.Indent();
        }

        bool hoisted = TryEmitStatementWithHoisting(expression, null, () => _writer.WriteLine(isVoidLike
            ? $"{EmitStatementExpression(expression)};"
            : $"return {EmitExpression(expression)};"));

        if (isProperty)
        {
            _writer.Unindent();
            _writer.WriteLine("}");
        }

        _writer.Unindent();
        _writer.WriteLine("}");

        if (!hoisted)
        {
            _writer.Truncate(mark);
        }

        return hoisted;
    }

    private (Expression? Root, TypeSyntax? Hint) GetExpressionStatementHoistRoot(Expression expression)
        => expression switch
        {
            AssignmentExpression assignment when IsSideEffectFreeTarget(assignment.Target)
                => (assignment.Value, _semantic?.GetExpressionType(assignment.Target)),
            AssignmentExpression or PrefixExpression or PostfixExpression => (null, null),
            _ => (expression, null),
        };

    private static bool IsSideEffectFreeTarget(Expression expression)
        => expression switch
        {
            LiteralExpression => true,
            IndexExpression index => IsSideEffectFreeTarget(index.Receiver) && index.Arguments.All(IsSideEffectFreeTarget),
            TupleExpression tuple => tuple.Elements.All(IsSideEffectFreeTarget),
            BinaryExpression { Operator: not (BinaryOperator.PipeLeft or BinaryOperator.PipeRight) } binary
                => IsSideEffectFreeTarget(binary.Left) && IsSideEffectFreeTarget(binary.Right),
            _ => IsSideEffectFreeReceiver(expression),
        };

    private static bool ContainsIdentifier(string text, string identifierOrExpression)
    {
        int index = 0;
        while ((index = text.IndexOf(identifierOrExpression, index, StringComparison.Ordinal)) >= 0)
        {
            int end = index + identifierOrExpression.Length;
            bool leftBoundary = index == 0 || !IsIdentifierCharacter(text[index - 1]);
            bool rightBoundary = end >= text.Length || !IsIdentifierCharacter(text[end]);
            if (leftBoundary && rightBoundary)
            {
                return true;
            }

            index = end;
        }

        return false;
    }

    private static bool IsIdentifierCharacter(char ch)
        => char.IsLetterOrDigit(ch) || ch == '_';
}
