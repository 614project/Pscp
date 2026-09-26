namespace Pscp.Transpiler;

// Range lowering (spec §14). A range used by `for`, `->`, a collection expression or an aggregate becomes a
// direct `for` loop: the bounds and the step are evaluated once, in the order start, step, end; a constant step
// needs one comparison, a run-time step uses `s > 0 ? i <= b : s < 0 && i >= b`, which is empty for step 0.
// Debug builds report a range whose next value would overflow the element type (§14.4).
internal sealed partial class CSharpEmitter
{
    private sealed record RangePlan(
        string ElementType,
        IReadOnlyList<string> Prelude,
        string Start,
        string End,
        string? Step,
        long? ConstantStep,
        bool Inclusive,
        IReadOnlyList<string> DebugGuard)
    {
        public bool IsChar => ElementType == "char";

        public bool IsLong => ElementType == "long";

        // Everything that runs before the loop: temporaries, then the Debug overflow check.
        public IEnumerable<string> Setup => Prelude.Concat(DebugGuard);
    }

    private RangePlan PlanRange(RangeExpression range, TypeSyntax? targetTypeHint)
    {
        string elementType = IsCharRange(range) ? "char" : IsLongRange(range, targetTypeHint) ? "long" : "int";
        string stepType = elementType == "char" ? "int" : elementType;
        bool inclusive = range.Kind != RangeKind.RightExclusive;
        long? constantStep = range.Step is null ? 1 : PscpSyntaxFacts.TryEvaluateIntegerConstant(range.Step, out long stepValue) ? stepValue : null;
        bool stableEnd = IsStableRangeOperand(range.End);
        bool stableStep = range.Step is null || constantStep is not null || IsStableRangeOperand(range.Step);

        // The start is read by the loop initializer; it only needs a temporary to keep it evaluated before a
        // captured step or end.
        List<string> prelude = [];
        string start = EmitRangeOperand(range.Start, elementType);
        if (!IsStableRangeOperand(range.Start) && (!stableEnd || !stableStep))
        {
            string startName = NextTemporary("start");
            prelude.Add($"{elementType} {startName} = {start};");
            start = startName;
        }

        string? step = null;
        if (range.Step is not null)
        {
            step = constantStep is long value ? value.ToString(System.Globalization.CultureInfo.InvariantCulture) : EmitRangeOperand(range.Step, stepType);
            if (!stableStep)
            {
                string stepName = NextTemporary("step");
                prelude.Add($"{stepType} {stepName} = {step};");
                step = stepName;
            }
        }

        string end = EmitRangeOperand(range.End, elementType);
        if (!stableEnd)
        {
            string endName = NextTemporary("end");
            prelude.Add($"{elementType} {endName} = {end};");
            end = endName;
        }

        List<string> guard = CreateRangeOverflowGuard(range, elementType, start, end, step, constantStep, inclusive);
        return new RangePlan(elementType, prelude, start, end, step, constantStep, inclusive, guard);
    }

    private string EmitRangeOperand(Expression expression, string type)
        => type == "char" && !IsCharTyped(expression) ? $"(char)({EmitExpression(expression)})" : EmitExpression(expression);

    // A bound may be repeated in the loop condition only when re-reading it cannot observe another value:
    // literals, names that are never reassigned, and arithmetic over those (spec §14.6).
    private bool IsStableRangeOperand(Expression expression)
        => expression switch
        {
            LiteralExpression => true,
            IdentifierExpression identifier => _semantic is not null && !_semantic.MutatedNames.Contains(identifier.Name) && !IsIdentifierAliasActive(identifier.Name),
            UnaryExpression { Operator: UnaryOperator.Negate or UnaryOperator.Plus } unary => IsStableRangeOperand(unary.Operand),
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
                => IsStableRangeOperand(binary.Left) && IsStableRangeOperand(binary.Right),
            _ => false,
        };

    private bool IsCharRange(RangeExpression range)
        => IsCharTyped(range.Start) && IsCharTyped(range.End);

    private bool IsCharTyped(Expression expression)
        => expression is LiteralExpression { Kind: LiteralKind.Char }
            || _semantic?.GetExpressionType(expression) is NamedTypeSyntax { Name: "char" or "Char" or "System.Char" };

    // §14.4: the value after the last element must fit in the element type. A step of ±1 overflows exactly when
    // an inclusive range ends at the type's bound; other steps are checked by a runtime helper. Ranges whose
    // bounds make an overflow impossible get no check.
    private static List<string> CreateRangeOverflowGuard(RangeExpression range, string elementType, string start, string end, string? step, long? constantStep, bool inclusive)
    {
        (long min, long max, string minText, string maxText) = elementType switch
        {
            "long" => (long.MinValue, long.MaxValue, "long.MinValue", "long.MaxValue"),
            "char" => (char.MinValue, char.MaxValue, "char.MinValue", "char.MaxValue"),
            _ => (int.MinValue, int.MaxValue, "int.MinValue", "int.MaxValue"),
        };

        long? constantEnd = TryGetRangeBoundConstant(range.End);
        if (constantStep is 1 or -1)
        {
            if (!inclusive)
            {
                return [];
            }

            long bound = constantStep == 1 ? max : min;
            if (constantEnd is long endValue && endValue != bound)
            {
                return [];
            }

            string boundText = constantStep == 1 ? maxText : minText;
            return
            [
                "#if DEBUG",
                $"if ({end} == {boundText}) throw new OverflowException(\"The range ends at {boundText}, so its next value overflows (spec §14.4).\");",
                "#endif",
            ];
        }

        if (constantStep is long stepValue && constantEnd is long constantEndValue)
        {
            long last = inclusive ? constantEndValue : constantEndValue - Math.Sign(stepValue);
            bool fits = stepValue > 0 ? last <= max - stepValue : last >= min - stepValue;
            if (fits)
            {
                return [];
            }
        }

        return
        [
            "#if DEBUG",
            $"__PscpSeq.checkRange({start}, {end}, {step}, {(inclusive ? "true" : "false")}, {minText}, {maxText});",
            "#endif",
        ];
    }

    private static long? TryGetRangeBoundConstant(Expression expression)
    {
        if (expression is LiteralExpression { Kind: LiteralKind.Char } charLiteral
            && TryGetCharLiteralValue(charLiteral.RawText, out char value))
        {
            return value;
        }

        return PscpSyntaxFacts.TryEvaluateIntegerConstant(expression, out long constant) ? constant : null;
    }

    private static bool TryGetCharLiteralValue(string rawText, out char value)
    {
        value = '\0';
        if (rawText.Length == 3 && rawText[0] == '\'' && rawText[2] == '\'' && rawText[1] != '\\')
        {
            value = rawText[1];
            return true;
        }

        return false;
    }

    private static string RangeCondition(RangePlan plan, string item)
    {
        string forward = plan.Inclusive ? "<=" : "<";
        string backward = plan.Inclusive ? ">=" : ">";
        return plan.ConstantStep switch
        {
            > 0 => $"{item} {forward} {plan.End}",
            < 0 => $"{item} {backward} {plan.End}",
            // A run-time step: step 0 gives an empty range (spec §14.3).
            _ => $"{plan.Step} > 0 ? {item} {forward} {plan.End} : {plan.Step} < 0 && {item} {backward} {plan.End}",
        };
    }

    private static string RangeUpdate(RangePlan plan, string item)
        => plan.ConstantStep switch
        {
            1 => $"{item}++",
            -1 => $"{item}--",
            _ when plan.IsChar => $"{item} = (char)({item} + {plan.Step})",
            < 0 => $"{item} -= {-plan.ConstantStep!.Value}",
            _ => $"{item} += {plan.Step}",
        };

    // `for (T item = start[, index = 0]; condition; update[, index++])`. An `int` index shares the initializer
    // with an `int` counter; otherwise it is declared by `indexDeclaration` before the loop.
    private static (string Header, string? IndexDeclaration) RangeForHeader(RangePlan plan, string item, string? index)
    {
        string condition = RangeCondition(plan, item);
        string update = RangeUpdate(plan, item);
        if (index is null)
        {
            return ($"for ({plan.ElementType} {item} = {plan.Start}; {condition}; {update})", null);
        }

        if (plan.ElementType == "int")
        {
            return ($"for (int {item} = {plan.Start}, {index} = 0; {condition}; {update}, {index}++)", null);
        }

        return ($"for ({plan.ElementType} {item} = {plan.Start}; {condition}; {update}, {index}++)", $"int {index} = 0;");
    }

    // The number of elements, never negative, as an `int` array length.
    private static string RangeCountExpression(RangePlan plan)
    {
        string count = plan.ConstantStep switch
        {
            1 => ClampedDifference(plan.End, plan.Start, plan.Inclusive, plan.IsLong),
            -1 => ClampedDifference(plan.Start, plan.End, plan.Inclusive, plan.IsLong),
            > 0 => SteppedCount(plan.Start, plan.End, plan.Step!, plan.Inclusive, forward: true),
            < 0 => SteppedCount(plan.Start, plan.End, (-plan.ConstantStep!.Value).ToString(System.Globalization.CultureInfo.InvariantCulture), plan.Inclusive, forward: false),
            _ => $"{plan.Step} > 0 ? {SteppedCount(plan.Start, plan.End, plan.Step!, plan.Inclusive, forward: true)} : {plan.Step} < 0 ? {SteppedCount(plan.Start, plan.End, "-" + plan.Step, plan.Inclusive, forward: false)} : 0",
        };

        return plan.IsLong ? $"checked((int)({count}))" : count;
    }

    private static string ClampedDifference(string high, string low, bool inclusive, bool isLong)
    {
        if (PscpNumericLiterals.TryGetInt64Value(high, out long highValue) && PscpNumericLiterals.TryGetInt64Value(low, out long lowValue))
        {
            long count = Math.Max(0, highValue - lowValue + (inclusive ? 1 : 0));
            return count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        string zero = isLong ? "0L" : "0";
        string difference = IsZeroExpressionText(low) ? high : $"{high} - {low}";
        return inclusive ? $"System.Math.Max({zero}, {difference} + 1)" : $"System.Math.Max({zero}, {difference})";
    }

    private static string SteppedCount(string start, string end, string step, bool inclusive, bool forward)
    {
        (string high, string low) = forward ? (end, start) : (start, end);
        return inclusive
            ? $"({low} <= {high} ? ({high} - {low}) / {step} + 1 : 0)"
            : $"({low} < {high} ? ({high} - {low} - 1) / {step} + 1 : 0)";
    }

    // A statement-level `for` over a range. The binding aliases and the body are written by `emitBody`, which
    // receives the loop's item name.
    private void EmitRangeLoop(RangeExpression range, string itemName, string? indexName, Action emitBody)
    {
        RangePlan plan = PlanRange(range, null);
        (string header, string? indexDeclaration) = RangeForHeader(plan, itemName, indexName);
        bool scoped = plan.Prelude.Count > 0 || indexDeclaration is not null;
        if (scoped)
        {
            _writer.WriteLine("{");
            _writer.Indent();
        }

        foreach (string line in plan.Setup)
        {
            _writer.WriteLine(line);
        }

        if (indexDeclaration is not null)
        {
            _writer.WriteLine(indexDeclaration);
        }

        _writer.WriteLine(header);
        emitBody();
        if (scoped)
        {
            _writer.Unindent();
            _writer.WriteLine("}");
        }
    }

    // The same loop as text, for lowerings that build statements as strings.
    private string EmitRangeLoopText(RangeExpression range, string itemName, string bodyStatements, string? indexName)
    {
        RangePlan plan = PlanRange(range, null);
        (string header, string? indexDeclaration) = RangeForHeader(plan, itemName, indexName);
        List<string> lines = [.. plan.Setup];
        if (indexDeclaration is not null)
        {
            lines.Add(indexDeclaration);
        }

        lines.Add($"{header} {{ {bodyStatements} }}");
        return lines.Count == 1 && plan.Prelude.Count == 0
            ? lines[0]
            : "{\n" + string.Join("\n", lines) + "\n}";
    }

    // `T[] name = new T[count]; for (...) name[slot] = value;` written as statements.
    private void EmitRangeArrayFill(RangePlan plan, string arrayName, string arrayTypeText, string elementTypeText, string itemName, string? indexName, Func<string> valueExpression, bool declareArray)
    {
        foreach (string line in plan.Setup)
        {
            _writer.WriteLine(line);
        }

        string countName = NextTemporary("count");
        _writer.WriteLine($"int {countName} = {RangeCountExpression(plan)};");
        _writer.WriteLine(declareArray
            ? $"{arrayTypeText} {arrayName} = {NewArray(elementTypeText, countName)};"
            : $"{arrayName} = {NewArray(elementTypeText, countName)};");
        string slotName = indexName ?? NextTemporary("slot");
        (string header, string? indexDeclaration) = RangeForHeader(plan, itemName, slotName);
        if (indexDeclaration is not null)
        {
            _writer.WriteLine(indexDeclaration);
        }

        _writer.WriteLine(header);
        _writer.WriteLine("{");
        _writer.Indent();
        _writer.WriteLine($"{arrayName}[{slotName}] = {valueExpression()};");
        _writer.Unindent();
        _writer.WriteLine("}");
    }

    // The array as a value block (for expression positions).
    private string EmitRangeArrayValueBlock(RangePlan plan, string elementTypeText, string itemName, string? indexName, string valueExpression)
    {
        string resultName = NextTemporary("result");
        string countName = NextTemporary("count");
        string slotName = indexName ?? NextTemporary("slot");
        (string header, string? indexDeclaration) = RangeForHeader(plan, itemName, slotName);
        List<string> lines = [.. plan.Setup];
        lines.Add($"int {countName} = {RangeCountExpression(plan)};");
        lines.Add($"{elementTypeText}[] {resultName} = {NewArray(elementTypeText, countName)};");
        if (indexDeclaration is not null)
        {
            lines.Add(indexDeclaration);
        }

        lines.Add($"{header} {{ {resultName}[{slotName}] = {valueExpression}; }}");
        lines.Add($"return {resultName};");

        // A preprocessor directive must start its own line, also inside `__PscpThunk.run(() => { ... })`.
        return EmitValueBlock((lines[0].StartsWith('#') ? "\n" : string.Empty) + string.Join("\n", lines));
    }

    // A range used as a value is an `IEnumerable<T>` that re-creates the sequence on every enumeration (§14.5).
    private string EmitRangeEnumerable(RangeExpression range, TypeSyntax? targetTypeHint)
    {
        string helper = IsCharRange(range) ? "__PscpSeq.rangeChar" : IsLongRange(range, targetTypeHint) ? "__PscpSeq.rangeLong" : "__PscpSeq.rangeInt";
        string inclusive = range.Kind == RangeKind.RightExclusive ? "false" : "true";
        return range.Step is null
            ? $"{helper}({EmitExpression(range.Start)}, {EmitExpression(range.End)}, {inclusive})"
            : $"{helper}({EmitExpression(range.Start)}, {EmitExpression(range.End)}, {EmitExpression(range.Step)}, {inclusive})";
    }
}
