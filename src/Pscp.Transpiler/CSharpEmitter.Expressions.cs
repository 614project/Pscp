namespace Pscp.Transpiler;

internal sealed partial class CSharpEmitter
{

    private string EmitExpression(Expression expression, TypeSyntax? targetTypeHint = null)
        => _hoistedValues.TryGetValue(expression, out string? hoisted)
            ? hoisted
            : EmitExpressionCore(expression, targetTypeHint);

    private string EmitExpressionCore(Expression expression, TypeSyntax? targetTypeHint)
        => expression switch
        {
            LiteralExpression literal => literal.RawText,
            InterpolatedStringExpression interpolated => EmitInterpolatedStringExpression(interpolated),
            IdentifierExpression identifier => EmitIdentifierExpression(identifier),
            DiscardExpression => "_",
            TupleExpression tuple => $"({string.Join(", ", tuple.Elements.Select(element => EmitExpression(element)))})",
            BlockExpression block => EmitBlockExpression(block.Block),
            IfExpression ifExpression => $"({EmitExpression(ifExpression.Condition)} ? {EmitExpression(ifExpression.ThenExpression)} : {EmitExpression(ifExpression.ElseExpression)})",
            ConditionalExpression conditional => $"({EmitExpression(conditional.Condition)} ? {EmitExpression(conditional.WhenTrue)} : {EmitExpression(conditional.WhenFalse)})",
            UnaryExpression unary => EmitUnaryExpression(unary),
            AssignmentExpression assignment => EmitAssignmentExpression(assignment),
            PrefixExpression prefix => EmitPrefixExpression(prefix),
            PostfixExpression postfix => EmitPostfixExpression(postfix),
            BinaryExpression binary => EmitBinaryExpression(binary),
            RangeExpression range => EmitRangeEnumerable(range, targetTypeHint),
            IsPatternExpression isPattern => EmitIsPatternExpression(isPattern),
            CallExpression call => EmitCallExpression(call, targetTypeHint),
            MemberAccessExpression member => EmitMemberAccessExpression(member),
            IndexExpression index => EmitIndexExpression(index),
            WithExpression @with => EmitWithExpression(@with),
            SwitchExpression @switch => EmitSwitchExpression(@switch),
            FromEndExpression fromEnd => $"^{EmitExpression(fromEnd.Operand)}",
            SliceExpression slice => EmitSliceExpression(slice),
            TupleProjectionExpression projection => $"{EmitExpression(projection.Receiver)}.Item{projection.Position}",
            LambdaExpression lambda => EmitLambdaExpression(lambda),
            NewExpression creation => EmitNewExpression(creation, targetTypeHint),
            NewArrayExpression newArray => EmitNewArrayExpression(newArray),
            TargetTypedNewArrayExpression targetTypedNewArray => EmitTargetTypedNewArrayExpression(targetTypedNewArray, targetTypeHint),
            CollectionExpression collection => EmitCollectionExpression(collection, targetTypeHint),
            AggregationExpression aggregation => EmitAggregationExpression(aggregation),
            GeneratorExpression generator => EmitBuilderEnumerable(new BuilderElement(generator.Source, generator.IndexTarget, generator.ItemTarget, generator.Body)),
            CastExpression cast => $"(({EmitType(NormalizeSizedType(cast.Type))}){EmitExpression(cast.Operand)})",
            AsExpression asExpression => $"({EmitExpression(asExpression.Operand)} as {EmitType(NormalizeSizedType(asExpression.Type))})",
            NullForgivingExpression nullForgiving => $"{EmitExpression(nullForgiving.Operand)}!",
            ThrowExpression throwExpression => $"throw {EmitExpression(throwExpression.Expression)}",
            _ => "default!"
        };

    private string EmitAssignmentTarget(Expression expression)
        => expression switch
        {
            TupleExpression tuple => $"({string.Join(", ", tuple.Elements.Select(element => EmitAssignmentTarget(element)))})",
            _ => EmitExpression(expression)
        };

    private string EmitIdentifierExpression(IdentifierExpression identifier)
        => TryGetIdentifierAlias(identifier.Name, out string? alias) ? alias! : identifier.Name;

    private string EmitBindingPattern(BindingTarget target)
        => target switch
        {
            NameTarget nameTarget => nameTarget.Name,
            DiscardTarget => "_",
            TupleTarget tuple => $"({string.Join(", ", tuple.Elements.Select(EmitBindingPattern))})",
            _ => NextTemporary("target")
        };

    private string EmitTypedBindingPattern(BindingTarget target, TypeSyntax type)
        => target switch
        {
            NameTarget or DiscardTarget => $"{EmitType(NormalizeSizedType(type))} {EmitBindingPattern(target)}",
            TupleTarget tuple when type is TupleTypeSyntax tupleType && tupleType.Elements.Count == tuple.Elements.Count
                => $"({string.Join(", ", tuple.Elements.Select((element, index) => EmitTypedBindingPattern(element, tupleType.Elements[index])) )})",
            _ => EmitBindingPattern(target)
        };

    private string EmitBinaryExpression(BinaryExpression binary)
    {
        if (binary.Operator == BinaryOperator.PipeRight)
        {
            return EmitPipeRight(binary.Left, binary.Right);
        }

        if (binary.Operator == BinaryOperator.PipeLeft)
        {
            return EmitPipeLeft(binary.Left, binary.Right);
        }

        if (binary.Operator == BinaryOperator.Spaceship)
        {
            string left = EmitExpression(binary.Left);
            string right = EmitExpression(binary.Right);
            if (_semantic?.GetExpressionType(binary.Left) is TypeSyntax leftType
                && Equals(leftType, _semantic.GetExpressionType(binary.Right)))
            {
                return EmitDefaultOrderCompare(left, right, leftType);
            }

            return $"__PscpSeq.compare({left}, {right})";
        }

        string op = binary.Operator switch
        {
            BinaryOperator.Add => "+",
            BinaryOperator.Subtract => "-",
            BinaryOperator.ShiftLeft => "<<",
            BinaryOperator.ShiftRight => ">>",
            BinaryOperator.Multiply => "*",
            BinaryOperator.Divide => "/",
            BinaryOperator.Modulo => "%",
            BinaryOperator.LessThan => "<",
            BinaryOperator.LessThanOrEqual => "<=",
            BinaryOperator.GreaterThan => ">",
            BinaryOperator.GreaterThanOrEqual => ">=",
            BinaryOperator.Equal => "==",
            BinaryOperator.NotEqual => "!=",
            BinaryOperator.BitwiseAnd => "&",
            BinaryOperator.BitwiseXor => "^",
            BinaryOperator.BitwiseOr => "|",
            BinaryOperator.LogicalAnd => "&&",
            BinaryOperator.LogicalOr => "||",
            BinaryOperator.Coalesce => "??",
            _ => "^",
        };

        return $"({EmitExpression(binary.Left)} {op} {EmitExpression(binary.Right)})";
    }

    private string EmitPipeRight(Expression left, Expression right)
    {
        if (right is CallExpression call)
        {
            return EmitCallLike(call.Callee, [new ExpressionArgumentSyntax(null, ArgumentModifier.None, left), .. call.Arguments]);
        }

        if (right is IdentifierExpression identifier && TryEmitConversionPipe(identifier, left, out string? conversion))
        {
            return conversion!;
        }

        return $"{EmitExpression(right)}({EmitExpression(left)})";
    }

    private string EmitPipeLeft(Expression left, Expression right)
    {
        if (left is CallExpression call)
        {
            return EmitCallLike(call.Callee, [.. call.Arguments, new ExpressionArgumentSyntax(null, ArgumentModifier.None, right)]);
        }

        if (left is IdentifierExpression identifier && TryEmitConversionPipe(identifier, right, out string? conversion))
        {
            return conversion!;
        }

        return $"{EmitExpression(left)}({EmitExpression(right)})";
    }

    // Patterns are C# 10 pass-through text (spec §13.9).
    private string EmitIsPatternExpression(IsPatternExpression expression)
        => $"({EmitExpression(expression.Left)} is {expression.Pattern.Text})";

    private static string EmitUnaryOperator(UnaryOperator op)
        => op switch
        {
            UnaryOperator.Plus => "+",
            UnaryOperator.Negate => "-",
            UnaryOperator.Peek => "~",
            _ => "!"
        };

    private string EmitUnaryExpression(UnaryExpression unary)
    {
        if (unary.Operator == UnaryOperator.Peek
            && TryEmitKnownDataStructurePeek(unary.Operand, out string? knownPeek))
        {
            return knownPeek!;
        }

        if (unary.Operator == UnaryOperator.Negate
            && unary.Operand is MemberAccessExpression { MemberName: "CompareTo" } compareToMember
            && TryGetTypeLikeText(compareToMember.Receiver, out string? comparableType))
        {
            return $"Comparer<{comparableType}>.Create((__pscp_l, __pscp_r) => {comparableType}.CompareTo(__pscp_r, __pscp_l))";
        }

        return $"({EmitUnaryOperator(unary.Operator)}{EmitExpression(unary.Operand)})";
    }

    private string EmitPrefixExpression(PrefixExpression prefix)
    {
        if (prefix.Operator == PostfixOperator.Decrement
            && TryEmitKnownDataStructurePop(prefix.Operand, out string? knownPop))
        {
            return knownPop!;
        }

        return prefix.Operator == PostfixOperator.Increment
            ? $"(++{EmitExpression(prefix.Operand)})"
            : $"(--{EmitExpression(prefix.Operand)})";
    }

    private string EmitPostfixExpression(PostfixExpression postfix)
        => $"({EmitExpression(postfix.Operand)}{(postfix.Operator == PostfixOperator.Increment ? "++" : "--")})";

    private string EmitPostfixStatementExpression(PostfixExpression postfix)
        => $"{EmitExpression(postfix.Operand)}{(postfix.Operator == PostfixOperator.Increment ? "++" : "--")}";

    private string EmitAssignmentExpression(AssignmentExpression assignment)
        => EmitAssignmentCore(assignment, wrapNormalAssignment: true);

    private string EmitStatementAssignmentExpression(AssignmentExpression assignment)
        => EmitAssignmentCore(assignment, wrapNormalAssignment: false);

    private string EmitAssignmentCore(AssignmentExpression assignment, bool wrapNormalAssignment)
    {
        if (TryEmitKnownDataStructureAssignment(assignment, out string? knownRewrite))
        {
            return knownRewrite!;
        }

        string target = EmitAssignmentTarget(assignment.Target);
        TypeSyntax? targetTypeHint = _semantic?.GetExpressionType(assignment.Target);
        string value = EmitExpression(assignment.Value, targetTypeHint);
        string body = $"{target} {EmitAssignmentOperator(assignment.Operator)} {value}";
        return wrapNormalAssignment ? $"({body})" : body;
    }

    private string EmitCallExpression(CallExpression call, TypeSyntax? targetTypeHint)
    {
        if (call.Callee is MemberAccessExpression stdoutMember
            && stdoutMember.Receiver is IdentifierExpression { Name: PscpBinder.StdoutName })
        {
            RegisterExplicitStdoutCall(stdoutMember.MemberName, call.Arguments);
        }

        if (call.Callee is MemberAccessExpression stdinMember
            && stdinMember.Receiver is IdentifierExpression { Name: PscpBinder.StdinName }
            && TryEmitSpecializedStdinCall(stdinMember.MemberName, call.Arguments, out string? stdinCall))
        {
            return stdinCall!;
        }

        if (call.Callee is MemberAccessExpression member
            && member.Receiver is IdentifierExpression { Name: "Array" }
            && member.MemberName.StartsWith("zero", StringComparison.Ordinal))
        {
            string elementType = targetTypeHint is null ? "object" : EmitType(GetCollectionElementType(targetTypeHint) ?? new NamedTypeSyntax("object", Immutable.List<TypeSyntax>()));
            if (call.Arguments.Count == 1
                && call.Arguments[0] is ExpressionArgumentSyntax lengthArgument
                && string.IsNullOrWhiteSpace(lengthArgument.Name)
                && lengthArgument.Modifier == ArgumentModifier.None)
            {
                return NewArray(elementType, EmitExpression(lengthArgument.Expression));
            }

            return $"__PscpArray.zero<{elementType}>({string.Join(", ", call.Arguments.Select(argument => EmitExpressionArgument(argument)))})";
        }

        TypeSyntax? savedIntrinsicTarget = _intrinsicTarget;
        _intrinsicTarget = targetTypeHint;
        try
        {
            if (TryEmitDirectIntrinsicCall(call, out string? intrinsic))
            {
                return intrinsic!;
            }
        }
        finally
        {
            _intrinsicTarget = savedIntrinsicTarget;
        }

        return EmitCallLike(call.Callee, call.Arguments);
    }

    // The target type of the intrinsic call being emitted when the call is a whole target-typed expression
    // (`long total = sum a`): `sum`, `sumBy` and integer `pow` then compute in the wider type (§22.6).
    private TypeSyntax? _intrinsicTarget;

    private TypeSyntax WidenAccumulator(TypeSyntax resultType)
        => resultType is NamedTypeSyntax { Name: "int" or "short" or "byte" or "sbyte" or "ushort" } && _intrinsicTarget is NamedTypeSyntax { Name: "long" or "Int64" or "System.Int64" }
            ? new NamedTypeSyntax("long", Immutable.List<TypeSyntax>())
            : resultType;

    // `total += value`, checked in Debug for integers (§22.6).
    private static string AccumulateStatement(string target, string value, TypeSyntax accumulatorType)
        => accumulatorType is NamedTypeSyntax { TypeArguments.Count: 0, Name: "int" or "long" }
            ? $"{target} = __PscpSeq.add({target}, {value});"
            : $"{target} += {value};";

    private bool TryEmitSpecializedStdinCall(string memberName, IReadOnlyList<ArgumentSyntax> arguments, out string? emitted)
    {
        emitted = null;
        if (TryEmitCanonicalScalarStdinCall(memberName, arguments, out emitted))
        {
            return true;
        }

        if (TryEmitCanonicalShapedStdinCall(memberName, arguments, out emitted))
        {
            return true;
        }

        if (TryParseSingleGenericMember(memberName, "read", out string? readTypeText)
            && arguments.Count == 0)
        {
            return TryEmitInputReadByText(readTypeText!, out emitted);
        }

        if ((TryParseSingleGenericMember(memberName, "array", out string? arrayTypeText)
                || TryParseSingleGenericMember(memberName, "readArray", out arrayTypeText))
            && arguments.Count == 1
            && arguments[0] is ExpressionArgumentSyntax { Modifier: ArgumentModifier.None, Name: null } arrayLength)
        {
            return TryEmitExplicitArrayRead(arrayTypeText!, arrayLength.Expression, out emitted);
        }

        if ((TryParseSingleGenericMember(memberName, "list", out string? listTypeText)
                || TryParseSingleGenericMember(memberName, "readList", out listTypeText))
            && arguments.Count == 1
            && arguments[0] is ExpressionArgumentSyntax { Modifier: ArgumentModifier.None, Name: null } listLength
            && TryEmitExplicitArrayRead(listTypeText!, listLength.Expression, out string? listArrayRead))
        {
            emitted = $"new List<{listTypeText!.Trim()}>({listArrayRead})";
            return true;
        }

        if ((TryParseSingleGenericMember(memberName, "linkedList", out string? linkedListTypeText)
                || TryParseSingleGenericMember(memberName, "readLinkedList", out linkedListTypeText))
            && arguments.Count == 1
            && arguments[0] is ExpressionArgumentSyntax { Modifier: ArgumentModifier.None, Name: null } linkedListLength
            && TryEmitExplicitArrayRead(linkedListTypeText!, linkedListLength.Expression, out string? linkedListArrayRead))
        {
            emitted = $"new LinkedList<{linkedListTypeText!.Trim()}>({linkedListArrayRead})";
            return true;
        }

        // Spec §17.2: `readTuple<T1, ..., Tk>()` for 2 to 7 elements and `readGrid<T>(n, m)`.
        if (TryParseMultipleGenericMembers(memberName, "readTuple", out IReadOnlyList<string>? tupleTypes)
            && tupleTypes is { Count: >= 2 and <= 7 }
            && arguments.Count == 0
            && TryEmitTupleReadByTexts(tupleTypes, out string? tupleRead))
        {
            emitted = tupleRead;
            return true;
        }

        if (TryParseSingleGenericMember(memberName, "readGrid", out string? gridTypeText)
            && arguments.Count == 2
            && arguments[0] is ExpressionArgumentSyntax { Modifier: ArgumentModifier.None, Name: null } gridRows
            && arguments[1] is ExpressionArgumentSyntax { Modifier: ArgumentModifier.None, Name: null } gridColumns)
        {
            return TryEmitNestedArrayRead(gridTypeText!, gridRows.Expression, gridColumns.Expression, out emitted);
        }

        if ((TryParseMultipleGenericMembers(memberName, "tuple2", out IReadOnlyList<string>? tuple2Types)
                || TryParseMultipleGenericMembers(memberName, "readTuple2", out tuple2Types))
            && tuple2Types is not null
            && tuple2Types.Count == 2
            && arguments.Count == 0
            && TryEmitTupleReadByTexts(tuple2Types, out string? tuple2Read))
        {
            emitted = tuple2Read;
            return true;
        }

        if ((TryParseMultipleGenericMembers(memberName, "tuple3", out IReadOnlyList<string>? tuple3Types)
                || TryParseMultipleGenericMembers(memberName, "readTuple3", out tuple3Types))
            && tuple3Types is not null
            && tuple3Types.Count == 3
            && arguments.Count == 0
            && TryEmitTupleReadByTexts(tuple3Types, out string? tuple3Read))
        {
            emitted = tuple3Read;
            return true;
        }

        if ((TryParseMultipleGenericMembers(memberName, "tuples2", out IReadOnlyList<string>? tuples2Types)
                || TryParseMultipleGenericMembers(memberName, "readTuples2", out tuples2Types))
            && tuples2Types is not null
            && tuples2Types.Count == 2
            && arguments.Count == 1
            && arguments[0] is ExpressionArgumentSyntax { Modifier: ArgumentModifier.None, Name: null } tuples2Length
            && TryEmitTupleSequenceReadByTexts(tuples2Types, tuples2Length.Expression, out string? tuples2Read))
        {
            emitted = tuples2Read;
            return true;
        }

        if ((TryParseMultipleGenericMembers(memberName, "tuples3", out IReadOnlyList<string>? tuples3Types)
                || TryParseMultipleGenericMembers(memberName, "readTuples3", out tuples3Types))
            && tuples3Types is not null
            && tuples3Types.Count == 3
            && arguments.Count == 1
            && arguments[0] is ExpressionArgumentSyntax { Modifier: ArgumentModifier.None, Name: null } tuples3Length
            && TryEmitTupleSequenceReadByTexts(tuples3Types, tuples3Length.Expression, out string? tuples3Read))
        {
            emitted = tuples3Read;
            return true;
        }

        if ((TryParseSingleGenericMember(memberName, "nestedArray", out string? nestedTypeText)
                || TryParseSingleGenericMember(memberName, "readNestedArray", out nestedTypeText))
            && arguments.Count == 2
            && arguments[0] is ExpressionArgumentSyntax { Modifier: ArgumentModifier.None, Name: null } rowCount
            && arguments[1] is ExpressionArgumentSyntax { Modifier: ArgumentModifier.None, Name: null } columnCount)
        {
            return TryEmitNestedArrayRead(nestedTypeText!, rowCount.Expression, columnCount.Expression, out emitted);
        }

        return false;
    }

    private bool TryEmitCanonicalScalarStdinCall(string memberName, IReadOnlyList<ArgumentSyntax> arguments, out string? emitted)
    {
        emitted = null;
        if (arguments.Count != 0)
        {
            return false;
        }

        emitted = memberName switch
        {
            "int" or "readInt" => "__pscp_stdin.readInt()",
            "long" or "readLong" => "__pscp_stdin.readLong()",
            "double" or "readDouble" => "__pscp_stdin.readDouble()",
            "decimal" or "readDecimal" => "__pscp_stdin.readDecimal()",
            "bool" or "readBool" => "__pscp_stdin.readBool()",
            "char" or "readChar" => "__pscp_stdin.readChar()",
            "str" or "readString" => "__pscp_stdin.readString()",
            "line" or "readLine" => "__pscp_stdin.readLine()",
            "readRestOfLine" => "__pscp_stdin.readRestOfLine()",
            "words" or "readWords" => "__pscp_stdin.readWords()",
            "chars" or "readChars" => "__pscp_stdin.readChars()",
            "hasNext" => "__pscp_stdin.hasNext()",
            "hasNextLine" => "__pscp_stdin.hasNextLine()",
            _ => null,
        };

        return emitted is not null;
    }

    private bool TryEmitCanonicalShapedStdinCall(string memberName, IReadOnlyList<ArgumentSyntax> arguments, out string? emitted)
    {
        emitted = null;
        if (!TryGetPositionalExpressionArguments(arguments, out IReadOnlyList<ExpressionArgumentSyntax>? positional)
            || positional is null)
        {
            return false;
        }

        emitted = (memberName, positional.Count) switch
        {
            ("lines" or "readLines", 1) => $"__pscp_stdin.readLines({EmitExpression(positional[0].Expression)})",
            ("charGrid" or "readCharGrid", 1) => $"__pscp_stdin.readCharGrid({EmitExpression(positional[0].Expression)})",
            ("wordGrid" or "readWordGrid", 1) => $"__pscp_stdin.readWordGrid({EmitExpression(positional[0].Expression)})",
            ("gridInt" or "readGridInt", 2) => $"__pscp_stdin.readGridInt({EmitExpression(positional[0].Expression)}, {EmitExpression(positional[1].Expression)})",
            ("gridLong" or "readGridLong", 2) => $"__pscp_stdin.readGridLong({EmitExpression(positional[0].Expression)}, {EmitExpression(positional[1].Expression)})",
            _ => null,
        };

        return emitted is not null;
    }

    private static bool TryParseSingleGenericMember(string memberName, string expectedRoot, out string? argumentText)
    {
        argumentText = null;
        if (!memberName.StartsWith(expectedRoot + "<", StringComparison.Ordinal) || !memberName.EndsWith(">", StringComparison.Ordinal))
        {
            return false;
        }

        argumentText = memberName[(expectedRoot.Length + 1)..^1].Trim();
        return argumentText.Length > 0;
    }

    private bool TryEmitInputReadByText(string typeText, out string? emitted)
    {
        typeText = typeText.Trim();
        emitted = typeText switch
        {
            "int" => "__pscp_stdin.readInt()",
            "long" => "__pscp_stdin.readLong()",
            "double" => "__pscp_stdin.readDouble()",
            "decimal" => "__pscp_stdin.readDecimal()",
            "bool" => "__pscp_stdin.readBool()",
            "char" => "__pscp_stdin.readChar()",
            "string" => "__pscp_stdin.readString()",
            _ => null,
        };

        if (emitted is not null)
        {
            return true;
        }

        if (!TryParseTupleTypeText(typeText, out IReadOnlyList<string>? tupleElements) || tupleElements is null)
        {
            return false;
        }

        return TryEmitTupleReadByTexts(tupleElements, out emitted);
    }

    private bool TryEmitExplicitArrayRead(string typeText, Expression lengthExpression, out string? emitted)
    {
        typeText = typeText.Trim();
        emitted = typeText switch
        {
            "int" => $"__pscp_stdin.readArrayInt({EmitExpression(lengthExpression)})",
            "long" => $"__pscp_stdin.readArrayLong({EmitExpression(lengthExpression)})",
            "double" => $"__pscp_stdin.readArrayDouble({EmitExpression(lengthExpression)})",
            "decimal" => $"__pscp_stdin.readArrayDecimal({EmitExpression(lengthExpression)})",
            "bool" => $"__pscp_stdin.readArrayBool({EmitExpression(lengthExpression)})",
            "char" => $"__pscp_stdin.readArrayChar({EmitExpression(lengthExpression)})",
            "string" => $"__pscp_stdin.readArrayString({EmitExpression(lengthExpression)})",
            _ => null,
        };

        if (emitted is not null)
        {
            return true;
        }

        if (!TryParseTupleTypeText(typeText, out IReadOnlyList<string>? tupleTypes) || tupleTypes is null)
        {
            return false;
        }

        return TryEmitTupleSequenceReadByTexts(tupleTypes, lengthExpression, out emitted);
    }

    private bool TryEmitTupleReadByTexts(IReadOnlyList<string> elementTypes, out string? emitted)
    {
        emitted = null;
        List<string> reads = [];
        foreach (string elementType in elementTypes)
        {
            if (!TryEmitInputReadByText(elementType, out string? elementRead))
            {
                return false;
            }

            reads.Add(elementRead!);
        }

        emitted = $"({string.Join(", ", reads)})";
        return true;
    }

    private bool TryEmitTupleSequenceReadByTexts(IReadOnlyList<string> elementTypes, Expression lengthExpression, out string? emitted)
    {
        emitted = null;
        if (!TryEmitTupleReadByTexts(elementTypes, out string? tupleRead))
        {
            return false;
        }

        string tupleTypeText = $"({string.Join(", ", elementTypes.Select(type => type.Trim()))})";
        string lengthName = NextTemporary("length");
        string resultName = NextTemporary("result");
        string indexName = NextTemporary("i");
        emitted = EmitValueBlock($"int {lengthName} = {EmitExpression(lengthExpression)}; {tupleTypeText}[] {resultName} = new {tupleTypeText}[{lengthName}]; for (int {indexName} = 0; {indexName} < {lengthName}; {indexName}++) {{ {resultName}[{indexName}] = {tupleRead}; }} return {resultName};");
        return true;
    }

    private bool TryEmitNestedArrayRead(string typeText, Expression rowCountExpression, Expression columnCountExpression, out string? emitted)
    {
        typeText = typeText.Trim();
        emitted = typeText switch
        {
            "int" => $"__pscp_stdin.readGridInt({EmitExpression(rowCountExpression)}, {EmitExpression(columnCountExpression)})",
            "long" => $"__pscp_stdin.readGridLong({EmitExpression(rowCountExpression)}, {EmitExpression(columnCountExpression)})",
            "char" => $"__pscp_stdin.readGridChar({EmitExpression(rowCountExpression)}, {EmitExpression(columnCountExpression)})",
            _ => null,
        };

        if (emitted is not null)
        {
            return true;
        }

        if (!TryEmitExplicitArrayRead(typeText, columnCountExpression, out string? rowRead))
        {
            return false;
        }

        string rowCountName = NextTemporary("rows");
        string resultName = NextTemporary("result");
        string indexName = NextTemporary("i");
        emitted = EmitValueBlock($"int {rowCountName} = {EmitExpression(rowCountExpression)}; {typeText}[][] {resultName} = {NewArray(typeText + "[]", rowCountName)}; for (int {indexName} = 0; {indexName} < {rowCountName}; {indexName}++) {{ {resultName}[{indexName}] = {rowRead}; }} return {resultName};");
        return true;
    }

    private static bool TryParseMultipleGenericMembers(string memberName, string expectedRoot, out IReadOnlyList<string>? argumentTexts)
    {
        argumentTexts = null;
        if (!TryParseSingleGenericMember(memberName, expectedRoot, out string? rawArguments) || string.IsNullOrWhiteSpace(rawArguments))
        {
            return false;
        }

        List<string> parts = SplitTopLevelCommaSeparated(rawArguments!);
        if (parts.Count == 0)
        {
            return false;
        }

        argumentTexts = parts;
        return true;
    }

    private static bool TryParseTupleTypeText(string typeText, out IReadOnlyList<string>? elementTypes)
    {
        elementTypes = null;
        typeText = typeText.Trim();
        if (!typeText.StartsWith("(", StringComparison.Ordinal) || !typeText.EndsWith(")", StringComparison.Ordinal))
        {
            return false;
        }

        List<string> parts = SplitTopLevelCommaSeparated(typeText[1..^1]);
        if (parts.Count == 0)
        {
            return false;
        }

        elementTypes = parts;
        return true;
    }

    private static List<string> SplitTopLevelCommaSeparated(string text)
    {
        List<string> parts = [];
        int angleDepth = 0;
        int parenDepth = 0;
        int bracketDepth = 0;
        int start = 0;

        for (int i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '<':
                    angleDepth++;
                    break;
                case '>':
                    angleDepth = Math.Max(0, angleDepth - 1);
                    break;
                case '(':
                    parenDepth++;
                    break;
                case ')':
                    parenDepth = Math.Max(0, parenDepth - 1);
                    break;
                case '[':
                    bracketDepth++;
                    break;
                case ']':
                    bracketDepth = Math.Max(0, bracketDepth - 1);
                    break;
                case ',' when angleDepth == 0 && parenDepth == 0 && bracketDepth == 0:
                    parts.Add(text[start..i].Trim());
                    start = i + 1;
                    break;
            }
        }

        string last = text[start..].Trim();
        if (last.Length > 0)
        {
            parts.Add(last);
        }

        return parts;
    }


    private bool TryEmitBinaryMinMaxIntrinsic(CallExpression call, string name, out string? emitted)
    {
        emitted = null;
        if (call.Arguments.Count != 2
            || call.Arguments[0] is not ExpressionArgumentSyntax leftArgument
            || call.Arguments[1] is not ExpressionArgumentSyntax rightArgument
            || !string.IsNullOrWhiteSpace(leftArgument.Name)
            || !string.IsNullOrWhiteSpace(rightArgument.Name)
            || leftArgument.Modifier != ArgumentModifier.None
            || rightArgument.Modifier != ArgumentModifier.None)
        {
            return false;
        }

        string left = EmitExpression(leftArgument.Expression);
        string right = EmitExpression(rightArgument.Expression);
        if (_semantic?.GetExpressionType(leftArgument.Expression) is TypeSyntax leftType
            && _semantic.GetExpressionType(rightArgument.Expression) is TypeSyntax rightType
            && Equals(leftType, rightType)
            && PscpIntrinsicCatalog.IsMathMinMaxCompatible(leftType))
        {
            emitted = $"System.Math.{char.ToUpperInvariant(name[0]) + name[1..]}({left}, {right})";
            return true;
        }

        emitted = $"__PscpSeq.{name}({left}, {right})";
        return true;
    }
    private string EmitCallLike(Expression callee, IReadOnlyList<ArgumentSyntax> arguments)
    {
        if (callee is IdentifierExpression identifier
            && IsConversionKeyword(identifier.Name)
            && TryEmitConversionIntrinsic(new CallExpression(callee, arguments, IsSpaceSeparated: false), identifier.Name, out string? conversion))
        {
            return conversion!;
        }

        string argumentText = string.Join(", ", arguments.Select(EmitArgument));
        if (callee is IdentifierExpression typeIdentifier
            && _declaredTypeNames.Contains(typeIdentifier.Name))
        {
            return $"new {typeIdentifier.Name}({argumentText})";
        }

        return $"{EmitExpression(callee)}({argumentText})";
    }

    private bool TryEmitConversionPipe(IdentifierExpression identifier, Expression operand, out string? emitted)
    {
        emitted = null;
        if (!IsConversionKeyword(identifier.Name))
        {
            return false;
        }

        CallExpression conversionCall = new(
            identifier,
            [new ExpressionArgumentSyntax(null, ArgumentModifier.None, operand)],
            IsSpaceSeparated: false);
        return TryEmitConversionIntrinsic(conversionCall, identifier.Name, out emitted);
    }

    private string EmitArgument(ArgumentSyntax argument)
    {
        string namePrefix = string.IsNullOrWhiteSpace(argument.Name) ? string.Empty : argument.Name + ": ";
        return argument switch
        {
            OutDeclarationArgumentSyntax outDeclaration => $"{namePrefix}out {EmitType(outDeclaration.Type)} {EmitBindingPattern(outDeclaration.Target)}",
            ExpressionArgumentSyntax expressionArgument => string.IsNullOrEmpty(EmitArgumentModifier(expressionArgument.Modifier))
                ? $"{namePrefix}{EmitExpression(expressionArgument.Expression)}"
                : $"{namePrefix}{EmitArgumentModifier(expressionArgument.Modifier)} {EmitExpression(expressionArgument.Expression)}",
            _ => namePrefix + "default!"
        };
    }

    private string EmitExpressionArgument(ArgumentSyntax argument)
        => argument switch
        {
            ExpressionArgumentSyntax expressionArgument => EmitArgument(expressionArgument),
            OutDeclarationArgumentSyntax outDeclaration => EmitArgument(outDeclaration),
            _ => "default!"
        };

    private string EmitMemberAccessExpression(MemberAccessExpression member)
    {
        if ((member.MemberName == "asc" || member.MemberName == "desc") && TryGetTypeLikeText(member.Receiver, out string? typeText))
        {
            // Spec §25.2: the default order and its reverse, cached by `__PscpOrder<T>`.
            return member.MemberName == "asc" ? $"__PscpOrder<{typeText}>.Asc" : $"__PscpOrder<{typeText}>.Desc";
        }

        string access = member.IsNullConditional ? "?." : ".";
        return $"{EmitMemberReceiverExpression(member.Receiver)}{access}{EmitMemberName(member.Receiver, member.MemberName)}";
    }

    private bool TryGetTypeLikeText(Expression expression, out string? typeText)
    {
        switch (expression)
        {
            case IdentifierExpression identifier:
                typeText = identifier.Name;
                return true;
            case MemberAccessExpression member when TryGetTypeLikeText(member.Receiver, out string? receiverText):
                typeText = receiverText + "." + member.MemberName;
                return true;
            default:
                typeText = null;
                return false;
        }
    }

    private string EmitMemberName(Expression receiver, string name)
    {
        if (receiver is IdentifierExpression { Name: PscpBinder.StdinName })
        {
            string root = name.Split('<')[0];
            if (root is "int")
            {
                return "readInt";
            }

            if (root is "long")
            {
                return "readLong";
            }

            if (root is "double")
            {
                return "readDouble";
            }

            if (root is "decimal")
            {
                return "readDecimal";
            }

            if (root is "bool")
            {
                return "readBool";
            }

            if (root is "char")
            {
                return "readChar";
            }

            if (root is "str")
            {
                return "readString";
            }

            if (root is "line")
            {
                return "readLine";
            }
        }

        return name;
    }

    private string EmitNewExpression(NewExpression creation, TypeSyntax? targetTypeHint)
    {
        TypeSyntax? effectiveType = UnwrapNullableType(NormalizeSizedTypeOrNull(creation.Type ?? targetTypeHint));
        if (effectiveType is not null
            && TryGetDeclaredTypeShape(effectiveType, out DeclaredTypeShape? shape)
            && TryEmitObjectInitializerNewExpression(shape!, effectiveType, creation.Arguments, out string? objectInitializer))
        {
            return objectInitializer!;
        }

        string arguments = string.Join(", ", creation.Arguments.Select(EmitArgument));
        return effectiveType is null
            ? $"new({arguments})"
            : $"new {EmitType(effectiveType)}({arguments})";
    }

    private bool TryEmitObjectInitializerNewExpression(
        DeclaredTypeShape shape,
        TypeSyntax effectiveType,
        IReadOnlyList<ArgumentSyntax> arguments,
        out string? emitted)
    {
        emitted = null;
        if (!shape.IsValueType
            || shape.Constructors.Any(constructor => constructor.ParameterCount == arguments.Count)
            || shape.Fields.Count != arguments.Count)
        {
            return false;
        }

        List<string> assignments = [];
        for (int i = 0; i < arguments.Count; i++)
        {
            if (arguments[i] is not ExpressionArgumentSyntax expressionArgument
                || expressionArgument.Modifier != ArgumentModifier.None
                || !string.IsNullOrWhiteSpace(expressionArgument.Name))
            {
                return false;
            }

            DeclaredFieldShape field = shape.Fields[i];
            assignments.Add($"{field.Name} = {EmitExpression(expressionArgument.Expression, field.Type)}");
        }

        emitted = $"new {EmitType(effectiveType)} {{ {string.Join(", ", assignments)} }}";
        return true;
    }

    private string EmitNewArrayExpression(NewArrayExpression newArray)
    {
        if (newArray.Dimensions.Count == 1)
        {
            return NewArray(EmitType(newArray.ElementType), EmitExpression(newArray.Dimensions[0]));
        }

        return EmitJaggedArrayAllocation(EmitType(newArray.ElementType), newArray.Dimensions);
    }

    private string EmitTargetTypedNewArrayExpression(TargetTypedNewArrayExpression targetTypedNewArray, TypeSyntax? targetTypeHint)
    {
        if (NormalizeSizedTypeOrNull(targetTypeHint) is not ArrayTypeSyntax arrayType || targetTypedNewArray.Dimensions.Count == 0)
        {
            return "default!";
        }

        if (!targetTypedNewArray.AutoConstructElements)
        {
            return EmitTargetTypedArrayAllocation(arrayType, targetTypedNewArray.Dimensions);
        }

        if (targetTypedNewArray.Dimensions.Count == 1
            && arrayType.Depth == 1
            && arrayType.ElementType is NamedTypeSyntax named
            && IsKnownAutoConstructType(named))
        {
            return EmitAutoConstructArrayAllocation(arrayType.ElementType, targetTypedNewArray.Dimensions[0]);
        }

        return EmitTargetTypedArrayAllocation(arrayType, targetTypedNewArray.Dimensions);
    }

    private string EmitCollectionExpression(CollectionExpression collection, TypeSyntax? targetTypeHint)
    {
        TypeSyntax? inferredTargetType = targetTypeHint ?? _semantic?.GetExpressionType(collection);
        TypeSyntax? normalizedTargetType = NormalizeSizedTypeOrNull(inferredTargetType);
        TypeSyntax? elementHint = GetCollectionElementType(normalizedTargetType ?? new NamedTypeSyntax("object", Immutable.List<TypeSyntax>()));
        if (collection.Elements.Count == 0)
        {
            return normalizedTargetType switch
            {
                NamedTypeSyntax named when IsListType(named) => $"new {EmitType(named)}()",
                NamedTypeSyntax named when IsLinkedListType(named) => $"new {EmitType(named)}()",
                NamedTypeSyntax named when IsKnownAutoConstructType(named) => EmitAutoConstruction(named, targetTyped: false),
                _ => $"Array.Empty<{EmitType(elementHint ?? new NamedTypeSyntax("object", Immutable.List<TypeSyntax>()))}>()",
            };
        }

        if (collection.Elements.All(element => element is ExpressionElement))
        {
            return EmitSimpleCollectionExpression(collection, normalizedTargetType, elementHint);
        }

        if (collection.Elements.Count == 1)
        {
            switch (collection.Elements[0])
            {
                case RangeElement rangeElement:
                    if (TryEmitMaterializedRange(rangeElement.Range, normalizedTargetType, elementHint, out string? rangeMaterialization))
                    {
                        return rangeMaterialization!;
                    }

                    break;
                case BuilderElement builderElement:
                    if (TryEmitMaterializedBuilder(builderElement, normalizedTargetType, elementHint, out string? builderMaterialization))
                    {
                        return builderMaterialization!;
                    }

                    break;
            }
        }

        return EmitGeneralCollectionMaterialization(collection, normalizedTargetType, elementHint);
    }

    private string MaterializeCollection(string sequence, TypeSyntax? targetTypeHint)
    {
        if (targetTypeHint is NamedTypeSyntax named)
        {
            if (IsListType(named))
            {
                return $"__PscpSeq.toList({sequence})";
            }

            if (IsLinkedListType(named))
            {
                return $"__PscpSeq.toLinkedList({sequence})";
            }
        }

        return sequence.StartsWith("__PscpSeq.arrayOf(", StringComparison.Ordinal)
            ? sequence
            : $"__PscpSeq.toArray({sequence})";
    }

    private string EmitBuilderEnumerable(BuilderElement builder)
    {
        string source = EmitEnumerable(builder.Source);
        if (builder.IndexTarget is null)
        {
            string itemName = ChooseBindingName(builder.ItemTarget, "item");
            string body = EmitLambdaBodyExpression(builder.Body, null, null, builder.ItemTarget, itemName);
            return builder.Body switch
            {
                LambdaExpressionBody or LambdaBlockBody => $"System.Linq.Enumerable.Select({source}, {itemName} => {body})",
                _ => source
            };
        }

        string itemNameBound = ChooseBindingName(builder.ItemTarget, "item");
        string indexNameBound = ChooseBindingName(builder.IndexTarget, "index");
        string indexedBody = EmitLambdaBodyExpression(builder.Body, builder.IndexTarget, indexNameBound, builder.ItemTarget, itemNameBound);
        return builder.Body switch
        {
            LambdaExpressionBody or LambdaBlockBody => $"System.Linq.Enumerable.Select({source}, ({itemNameBound}, {indexNameBound}) => {indexedBody})",
            _ => source
        };
    }

    private string EmitAggregationExpression(AggregationExpression aggregation)
    {
        return TryEmitDirectAggregationExpression(aggregation, out string? emitted)
            ? emitted!
            : EmitFallbackAggregationExpression(aggregation);
    }

    private static string EmitAggregationTerminal(string name, string sequence, string selector)
    {
        return name switch
        {
            "sum" => $"System.Linq.Enumerable.Sum(System.Linq.Enumerable.Select({sequence}, {selector}))",
            "min" => $"System.Linq.Enumerable.Min(System.Linq.Enumerable.Select({sequence}, {selector}))",
            "max" => $"System.Linq.Enumerable.Max(System.Linq.Enumerable.Select({sequence}, {selector}))",
            "count" => $"System.Linq.Enumerable.Count({sequence}, {selector})",
            _ => $"System.Linq.Enumerable.Select({sequence}, {selector}).FirstOrDefault()"
        };
    }

    private string EmitLambdaExpression(LambdaExpression lambda)
    {
        string parameters = lambda.Parameters.Count == 1 && lambda.Parameters[0].Modifier == ArgumentModifier.None && lambda.Parameters[0].Type is null
            ? EmitBindingPattern(lambda.Parameters[0].Target)
            : $"({string.Join(", ", lambda.Parameters.Select(EmitLambdaParameter))})";

        return lambda.Body switch
        {
            LambdaExpressionBody expressionBody => $"{parameters} => {EmitExpression(expressionBody.Expression)}",
            LambdaBlockBody blockBody => $"{parameters} => {{ {WithReturnType(null, () => EmitInlineBlock(blockBody.Block, isVoidLike: !LambdaBlockReturnsValue(blockBody.Block)))} }}",
            _ => $"{parameters} => default!"
        };
    }

    private string EmitLambdaParameter(LambdaParameter parameter)
    {
        string modifier = EmitArgumentModifier(parameter.Modifier);
        if (parameter.Type is null)
        {
            return EmitBindingPattern(parameter.Target);
        }

        return string.IsNullOrEmpty(modifier)
            ? $"{EmitType(parameter.Type)} {EmitBindingPattern(parameter.Target)}"
            : $"{modifier} {EmitType(parameter.Type)} {EmitBindingPattern(parameter.Target)}";
    }

    private string EmitInlineBlock(BlockStatement block, bool isVoidLike)
    {
        _hoistSuppression++;
        try
        {
            return EmitInlineBlockCore(block, isVoidLike);
        }
        finally
        {
            _hoistSuppression--;
        }
    }

    private string EmitInlineBlockCore(BlockStatement block, bool isVoidLike)
    {
        List<string> parts = [];
        for (int i = 0; i < block.Statements.Count - 1; i++)
        {
            parts.Add(EmitInlineStatement(block.Statements[i]));
        }

        if (block.Statements.Count > 0)
        {
            parts.Add(isVoidLike ? EmitInlineStatement(block.Statements[^1]) : EmitInlineTailStatement(block.Statements[^1]));
        }

        return string.Join(" ", parts);
    }

    // Tail position inside an inline block (spec §10.5), see EmitTailStatement.
    private string EmitInlineTailStatement(Statement statement)
        => statement switch
        {
            ExpressionStatement expressionStatement when IsTailReturnEligible(expressionStatement.Expression) => $"return {EmitExpression(expressionStatement.Expression, CurrentReturnType)};",
            BlockStatement block => $"{{ {EmitInlineBlockCore(block, isVoidLike: false)} }}",
            IfStatement { ElseBranch: not null } ifStatement
                => $"if ({EmitExpression(ifStatement.Condition)}) {{ {EmitInlineTailStatement(ifStatement.ThenBranch)} }} else {{ {EmitInlineTailStatement(ifStatement.ElseBranch)} }}",
            _ => EmitInlineStatement(statement),
        };

    // A block lambda returns a value when it has `return value` or a tail expression with a value. Without the
    // delegate type a tail call of unknown type is taken as a statement (`xs.ForEach(x => { sb.Append(x) })`).
    private bool LambdaBlockReturnsValue(BlockStatement block)
    {
        if (ContainsValueReturn(block))
        {
            return true;
        }

        List<Expression> tails = [];
        CollectTailExpressions(block, tails);
        return tails.Any(tail => _semantic?.GetExpressionType(tail) switch
        {
            NamedTypeSyntax { Name: "void" } => false,
            null => tail is not CallExpression,
            _ => true,
        });
    }

    private void CollectTailExpressions(Statement statement, List<Expression> tails)
    {
        switch (statement)
        {
            case ExpressionStatement expressionStatement when IsTailReturnEligible(expressionStatement.Expression):
                tails.Add(expressionStatement.Expression);
                break;
            case BlockStatement { Statements.Count: > 0 } block:
                CollectTailExpressions(block.Statements[^1], tails);
                break;
            case IfStatement { ElseBranch: not null } ifStatement:
                CollectTailExpressions(ifStatement.ThenBranch, tails);
                CollectTailExpressions(ifStatement.ElseBranch, tails);
                break;
        }
    }

    private static bool ContainsValueReturn(Statement statement)
        => statement switch
        {
            ReturnStatement { Expression: not null } => true,
            BlockStatement block => block.Statements.Any(ContainsValueReturn),
            IfStatement ifStatement => ContainsValueReturn(ifStatement.ThenBranch) || (ifStatement.ElseBranch is not null && ContainsValueReturn(ifStatement.ElseBranch)),
            WhileStatement whileStatement => ContainsValueReturn(whileStatement.Body),
            ForInStatement forIn => ContainsValueReturn(forIn.Body),
            CStyleForStatement cStyleFor => ContainsValueReturn(cStyleFor.Body),
            FastForStatement fastFor => ContainsValueReturn(fastFor.Body),
            TryStatement tryStatement => ContainsValueReturn(tryStatement.Body)
                || tryStatement.Catches.Any(catchClause => ContainsValueReturn(catchClause.Body))
                || (tryStatement.Finally is not null && ContainsValueReturn(tryStatement.Finally)),
            _ => false,
        };

    private string EmitInlineStatement(Statement statement)
        => statement switch
        {
            BlockStatement block => $"{{ {EmitInlineBlock(block, isVoidLike: true)} }}",
            DeclarationStatement declaration => EmitInlineDeclaration(declaration),
            ExpressionStatement expressionStatement => expressionStatement.Expression is AssignmentExpression assignmentExpression
                ? $"{EmitStatementAssignmentExpression(assignmentExpression)};"
                : expressionStatement.Expression is IfExpression ifExpression
                    ? $"{EmitIfExpressionAsStatement(ifExpression)};"
                    : $"{EmitExpression(expressionStatement.Expression)};",
            AssignmentStatement assignment => $"{EmitAssignmentTarget(assignment.Target)} {EmitAssignmentOperator(assignment.Operator)} {EmitExpression(assignment.Value)};",
            OutputStatement output => EmitOutputInvocation(output.Kind, output.Expression),
            IfStatement ifStatement => $"if ({EmitExpression(ifStatement.Condition)}) {EmitInlineEmbeddedStatement(ifStatement.ThenBranch)}{(ifStatement.ElseBranch is null ? string.Empty : $" else {EmitInlineEmbeddedStatement(ifStatement.ElseBranch)}")}",
            WhileStatement whileStatement => $"while ({EmitExpression(whileStatement.Condition)}) {EmitInlineEmbeddedStatement(whileStatement.Body)}",
            ForInStatement forIn => $"foreach (var {EmitBindingPattern(forIn.Iterator)} in {EmitEnumerable(forIn.Source)}) {EmitInlineEmbeddedStatement(forIn.Body)}",
            CStyleForStatement cStyleFor => $"for ({cStyleFor.HeaderText}) {EmitInlineEmbeddedStatement(cStyleFor.Body)}",
            FastForStatement fastFor => EmitInlineFastFor(fastFor),
            ReturnStatement returnStatement => returnStatement.Expression is null ? "return;" : $"return {EmitExpression(returnStatement.Expression, CurrentReturnType)};",
            BreakStatement => "break;",
            ContinueStatement => "continue;",
            LocalFunctionStatement localFunction => $"{EmitInlineFunction(localFunction.Function)}",
            _ => string.Empty
        };

    private string EmitInlineFunction(FunctionDeclaration function)
    {
        string parameters = string.Join(", ", function.Parameters.Select(EmitParameter));
        string body = WithReturnType(function.ReturnType, () => EmitInlineBlock(function.Body, isVoidLike: GetIsVoid(function.ReturnType)));
        return $"{EmitType(function.ReturnType)} {function.Name}({parameters}) {{ {body} }}";
    }

    private string EmitInlineEmbeddedStatement(Statement statement)
        => statement is BlockStatement block
            ? $"{{ {EmitInlineBlock(block, isVoidLike: true)} }}"
            : $"{{ {EmitInlineStatement(statement)} }}";

    private string EmitInlineDeclaration(DeclarationStatement declaration)
    {
        if (declaration.IsInputShorthand)
        {
            return EmitInlineInputDeclaration(declaration);
        }

        if (declaration.Targets.Count == 1 && declaration.Targets[0] is TupleTarget tupleTarget)
        {
            string initializer = declaration.Initializer is null ? "default!" : EmitExpression(declaration.Initializer, declaration.ExplicitType);
            return $"var {EmitBindingPattern(tupleTarget)} = {initializer};";
        }

        if (declaration.Targets.Count > 1)
        {
            string left = declaration.ExplicitType is TupleTypeSyntax tupleType && tupleType.Elements.Count == declaration.Targets.Count
                ? $"({string.Join(", ", declaration.Targets.Select((target, index) => EmitTypedBindingPattern(target, tupleType.Elements[index])) )})"
                : declaration.ExplicitType is null
                    ? $"var ({string.Join(", ", declaration.Targets.Select(EmitBindingPattern))})"
                    : $"({string.Join(", ", declaration.Targets.Select(target => $"{EmitType(NormalizeSizedType(declaration.ExplicitType!))} {EmitBindingPattern(target)}"))})";
            return $"{left} = {EmitExpression(declaration.Initializer!, declaration.ExplicitType)};";
        }

        string name = EmitBindingPattern(declaration.Targets[0]);
        string typeText = declaration.ExplicitType is null ? "var" : EmitType(NormalizeSizedType(declaration.ExplicitType));
        string initializerText = declaration.Initializer is null ? EmitImplicitInitializer(declaration.ExplicitType) : EmitExpression(declaration.Initializer, declaration.ExplicitType);
        return $"{typeText} {name} = {initializerText};";
    }

    private string EmitInlineInputDeclaration(DeclarationStatement declaration)
    {
        if (declaration.ExplicitType is null)
        {
            return "/* invalid input shorthand */";
        }

        if (declaration.Targets.Count == 1 && declaration.Targets[0] is TupleTarget tupleTarget)
        {
            return $"var {EmitBindingPattern(tupleTarget)} = {EmitInputRead(declaration.ExplicitType)};";
        }

        if (declaration.Targets.Count > 1)
        {
            List<string> parts = [];
            foreach (BindingTarget target in declaration.Targets)
            {
                string readExpression = EmitInputRead(declaration.ExplicitType);
                parts.Add(target is DiscardTarget
                    ? $"_ = {readExpression};"
                    : $"{EmitType(NormalizeSizedType(declaration.ExplicitType))} {EmitBindingPattern(target)} = {readExpression};");
            }

            return JoinInlineStatements(parts);
        }

        BindingTarget onlyTarget = declaration.Targets[0];
        string onlyReadExpression = EmitInputRead(declaration.ExplicitType);
        if (onlyTarget is DiscardTarget)
        {
            return $"_ = {onlyReadExpression};";
        }

        return $"{EmitType(NormalizeSizedType(declaration.ExplicitType))} {EmitBindingPattern(onlyTarget)} = {onlyReadExpression};";
    }

    private string EmitInlineFastFor(FastForStatement fastFor)
    {
        return EmitInlineFastForCore(fastFor);
    }

    private static string EmitAssignmentOperator(AssignmentOperator op)
        => op switch
        {
            AssignmentOperator.Assign => "=",
            AssignmentOperator.AddAssign => "+=",
            AssignmentOperator.SubtractAssign => "-=",
            AssignmentOperator.MultiplyAssign => "*=",
            AssignmentOperator.DivideAssign => "/=",
            AssignmentOperator.ModuloAssign => "%=",
            AssignmentOperator.BitwiseAndAssign => "&=",
            AssignmentOperator.BitwiseOrAssign => "|=",
            AssignmentOperator.BitwiseXorAssign => "^=",
            AssignmentOperator.ShiftLeftAssign => "<<=",
            AssignmentOperator.ShiftRightAssign => ">>=",
            AssignmentOperator.CoalesceAssign => "??=",
            _ => throw new InvalidOperationException($"Unknown assignment operator {op}."),
        };

    private void EmitInputDeclaration(DeclarationStatement declaration)
    {
        if (declaration.ExplicitType is null)
        {
            _writer.WriteLine("// invalid input shorthand");
            return;
        }

        if (TryEmitDirectInputDeclaration(declaration, hoisted: false))
        {
            return;
        }

        if (declaration.Targets.Count == 1 && declaration.Targets[0] is TupleTarget tupleTarget)
        {
            _writer.WriteLine($"var {EmitBindingPattern(tupleTarget)} = {EmitInputRead(declaration.ExplicitType)};");
            return;
        }

        if (declaration.Targets.Count > 1)
        {
            foreach (BindingTarget target in declaration.Targets)
            {
                if (target is DiscardTarget)
                {
                    _writer.WriteLine($"_ = {EmitInputRead(declaration.ExplicitType)};");
                }
                else
                {
                    _writer.WriteLine($"{EmitType(declaration.ExplicitType)} {EmitBindingPattern(target)} = {EmitInputRead(declaration.ExplicitType)};");
                }
            }

            return;
        }

        BindingTarget onlyTarget = declaration.Targets[0];
        string readExpression = EmitInputRead(declaration.ExplicitType);
        if (onlyTarget is DiscardTarget)
        {
            _writer.WriteLine($"_ = {readExpression};");
            return;
        }

        _writer.WriteLine($"{EmitType(NormalizeSizedType(declaration.ExplicitType))} {EmitBindingPattern(onlyTarget)} = {readExpression};");
    }

    private bool TryEmitDirectInputDeclaration(DeclarationStatement declaration, bool hoisted)
    {
        if (declaration.ExplicitType is not SizedArrayTypeSyntax sized
            || declaration.Targets.Count != 1
            || declaration.Targets[0] is not NameTarget nameTarget)
        {
            return false;
        }

        return TryEmitDirectSizedInputInitialization(nameTarget.Name, sized, hoisted);
    }

    private bool TryEmitDirectSizedInputInitialization(string targetName, SizedArrayTypeSyntax sized, bool hoisted)
    {
        string normalizedTargetName = EmitBindingPattern(new NameTarget(targetName));
        string elementRead = EmitInputRead(sized.ElementType);
        if (elementRead == "default!")
        {
            return false;
        }

        string lengthName = NextTemporary("length");
        string indexName = NextTemporary("i");
        string elementTypeText = EmitType(sized.ElementType);
        string arrayTypeText = EmitType(new ArrayTypeSyntax(sized.ElementType, sized.Dimensions.Count));
        string declarationPrefix = hoisted ? string.Empty : arrayTypeText + " ";

        if (sized.Dimensions.Count == 1)
        {
            _writer.WriteLine($"int {lengthName} = {EmitExpression(sized.Dimensions[0])};");
            _writer.WriteLine($"{declarationPrefix}{normalizedTargetName} = {NewArray(elementTypeText, lengthName)};");
            _writer.WriteLine($"for (int {indexName} = 0; {indexName} < {lengthName}; {indexName}++)");
            _writer.WriteLine("{");
            _writer.Indent();
            _writer.WriteLine($"{normalizedTargetName}[{indexName}] = {elementRead};");
            _writer.Unindent();
            _writer.WriteLine("}");
            return true;
        }

        if (sized.Dimensions.Count == 2)
        {
            string innerLengthName = NextTemporary("innerLength");
            string innerIndexName = NextTemporary("j");
            _writer.WriteLine($"int {lengthName} = {EmitExpression(sized.Dimensions[0])};");
            _writer.WriteLine($"int {innerLengthName} = {EmitExpression(sized.Dimensions[1])};");
            _writer.WriteLine($"{declarationPrefix}{normalizedTargetName} = {NewArray(elementTypeText + "[]", lengthName)};");
            _writer.WriteLine($"for (int {indexName} = 0; {indexName} < {lengthName}; {indexName}++)");
            _writer.WriteLine("{");
            _writer.Indent();
            _writer.WriteLine($"{normalizedTargetName}[{indexName}] = {NewArray(elementTypeText, innerLengthName)};");
            _writer.WriteLine($"for (int {innerIndexName} = 0; {innerIndexName} < {innerLengthName}; {innerIndexName}++)");
            _writer.WriteLine("{");
            _writer.Indent();
            _writer.WriteLine($"{normalizedTargetName}[{indexName}][{innerIndexName}] = {elementRead};");
            _writer.Unindent();
            _writer.WriteLine("}");
            _writer.Unindent();
            _writer.WriteLine("}");
            return true;
        }

        return false;
    }

    private string EmitInputRead(TypeSyntax type)
    {
        return type switch
            {
                NamedTypeSyntax named => EmitScalarReader(named),
                TupleTypeSyntax tuple => $"({string.Join(", ", tuple.Elements.Select(EmitInputRead))})",
                SizedArrayTypeSyntax sized => EmitSizedInputRead(sized),
                _ => "default!"
            };
    }

    private string EmitScalarReader(NamedTypeSyntax named)
    {
        return named.Name switch
        {
            "int" => "__pscp_stdin.readInt()",
            "long" => "__pscp_stdin.readLong()",
            "double" => "__pscp_stdin.readDouble()",
            "decimal" => "__pscp_stdin.readDecimal()",
            "bool" => "__pscp_stdin.readBool()",
            "char" => "__pscp_stdin.readChar()",
            "string" => "__pscp_stdin.readString()",
            _ => "default!"
        };
    }

    private string EmitSizedInputRead(SizedArrayTypeSyntax sized)
    {
        if (sized.ElementType is TupleTypeSyntax tuple && sized.Dimensions.Count == 1)
        {
            string tupleTypeText = $"({string.Join(", ", tuple.Elements.Select(EmitType))})";
            string tupleRead = $"({string.Join(", ", tuple.Elements.Select(EmitInputRead))})";
            string lengthName = NextTemporary("length");
            string resultName = NextTemporary("result");
            string indexName = NextTemporary("i");
            return EmitValueBlock($"int {lengthName} = {EmitExpression(sized.Dimensions[0])}; {tupleTypeText}[] {resultName} = new {tupleTypeText}[{lengthName}]; for (int {indexName} = 0; {indexName} < {lengthName}; {indexName}++) {{ {resultName}[{indexName}] = {tupleRead}; }} return {resultName};");
        }

        if (sized.Dimensions.Count == 1)
        {
            return sized.ElementType switch
            {
                NamedTypeSyntax { Name: "int" } => $"__pscp_stdin.readArrayInt({EmitExpression(sized.Dimensions[0])})",
                NamedTypeSyntax { Name: "long" } => $"__pscp_stdin.readArrayLong({EmitExpression(sized.Dimensions[0])})",
                NamedTypeSyntax { Name: "double" } => $"__pscp_stdin.readArrayDouble({EmitExpression(sized.Dimensions[0])})",
                NamedTypeSyntax { Name: "decimal" } => $"__pscp_stdin.readArrayDecimal({EmitExpression(sized.Dimensions[0])})",
                NamedTypeSyntax { Name: "bool" } => $"__pscp_stdin.readArrayBool({EmitExpression(sized.Dimensions[0])})",
                NamedTypeSyntax { Name: "char" } => $"__pscp_stdin.readArrayChar({EmitExpression(sized.Dimensions[0])})",
                NamedTypeSyntax { Name: "string" } => $"__pscp_stdin.readArrayString({EmitExpression(sized.Dimensions[0])})",
                _ => "default!",
            };
        }

        if (sized.Dimensions.Count == 2)
        {
            return sized.ElementType switch
            {
                NamedTypeSyntax { Name: "int" } => $"__pscp_stdin.readGridInt({EmitExpression(sized.Dimensions[0])}, {EmitExpression(sized.Dimensions[1])})",
                NamedTypeSyntax { Name: "long" } => $"__pscp_stdin.readGridLong({EmitExpression(sized.Dimensions[0])}, {EmitExpression(sized.Dimensions[1])})",
                _ => "default!",
            };
        }

        return "default!";
    }

    private void EmitSizedArrayDeclaration(string name, SizedArrayTypeSyntax sizedType)
    {
        string elementType = EmitType(sizedType.ElementType);
        string arrayType = EmitType(new ArrayTypeSyntax(sizedType.ElementType, sizedType.Dimensions.Count));

        if (sizedType.Dimensions.Count == 1)
        {
            _writer.WriteLine($"{arrayType} {name} = {NewArray(elementType, EmitExpression(sizedType.Dimensions[0]))};");
            return;
        }

        _writer.WriteLine($"{arrayType} {name} = {NewArray(elementType + "[]", EmitExpression(sizedType.Dimensions[0]))};");
        string index = NextTemporary("i");
        _writer.WriteLine($"for (int {index} = 0; {index} < {EmitExpression(sizedType.Dimensions[0])}; {index}++)");
        _writer.WriteLine("{");
        _writer.Indent();
        _writer.WriteLine($"{name}[{index}] = {NewArray(elementType, EmitExpression(sizedType.Dimensions[1]))};");
        _writer.Unindent();
        _writer.WriteLine("}");
    }

    private string EmitSizedArrayCreationExpression(SizedArrayTypeSyntax sizedType)
    {
        if (sizedType.Dimensions.Count == 1)
        {
            return NewArray(EmitType(sizedType.ElementType), EmitExpression(sizedType.Dimensions[0]));
        }

        return EmitJaggedArrayAllocation(EmitType(sizedType.ElementType), sizedType.Dimensions);
    }

    private string EmitType(TypeSyntax type)
        => type switch
        {
            NamedTypeSyntax named => named.TypeArguments.Count == 0
                ? named.Name
                : $"{named.Name}<{string.Join(", ", named.TypeArguments.Select(argument => EmitType(argument)))}>",
            TupleTypeSyntax tuple => $"({string.Join(", ", tuple.Elements.Select(element => EmitType(element)))})",
            ArrayTypeSyntax array => $"{EmitType(array.ElementType)}{string.Concat(Enumerable.Repeat("[]", array.Depth))}",
            NullableTypeSyntax nullable => $"{EmitType(nullable.InnerType)}?",
            SizedArrayTypeSyntax sized => EmitType(new ArrayTypeSyntax(sized.ElementType, sized.Dimensions.Count)),
            _ => "object"
        };

    private string EmitEnumerable(Expression expression)
        => expression is RangeExpression range ? EmitRangeEnumerable(range, null) : EmitExpression(expression);

    private string EmitIndexExpression(IndexExpression index)
    {
        string receiver = index.Receiver is IdentifierExpression identifier
            && IsIdentifierAliasActive(identifier.Name)
            && _declaredValueNames.Contains(identifier.Name)
                ? identifier.Name
                : EmitExpression(index.Receiver);

        // Spec §15.3: `List<T>` has no range indexer; its slices use `GetRange`.
        if (index.Arguments.Count == 1
            && index.Arguments[0] is SliceExpression listSlice
            && UnwrapNullableType(_semantic?.GetExpressionType(index.Receiver)) is NamedTypeSyntax { Name: "List" or "System.Collections.Generic.List" })
        {
            return EmitListSlice(index.Receiver, listSlice);
        }

        string open = index.IsNullConditional ? "?[" : "[";
        return $"{receiver}{open}{string.Join(", ", index.Arguments.Select(EmitIndexArgument))}]";
    }

    private string EmitIndexArgument(Expression expression)
        => expression switch
        {
            FromEndExpression fromEnd => $"^{EmitExpression(fromEnd.Operand)}",
            SliceExpression slice => EmitSliceExpression(slice),
            _ => EmitExpression(expression)
        };

    // `a..<b` is C# `a..b`; `a..=b` is `a..(b + 1)`, and an inclusive end `^k` is `^(k - 1)` (spec §15.3).
    private string EmitSliceExpression(SliceExpression slice)
    {
        string start = slice.Start is null ? string.Empty : EmitIndexArgument(slice.Start);
        string end = slice.End is null ? string.Empty : EmitSliceEnd(slice.End, slice.Kind == SliceKind.Inclusive);
        return $"{start}..{end}";
    }

    private string EmitSliceEnd(Expression end, bool inclusive)
    {
        if (!inclusive)
        {
            return EmitIndexArgument(end);
        }

        if (end is FromEndExpression fromEnd)
        {
            return PscpSyntaxFacts.TryEvaluateIntegerConstant(fromEnd.Operand, out long fromEndValue)
                ? (fromEndValue == 1 ? string.Empty : $"^{fromEndValue - 1}")
                : $"^({EmitExpression(fromEnd.Operand)} - 1)";
        }

        return PscpSyntaxFacts.TryEvaluateIntegerConstant(end, out long endValue)
            ? (endValue + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : $"({EmitExpression(end)} + 1)";
    }

    // `list[a..<b]` → `list.GetRange(a, b - a)`.
    private string EmitListSlice(Expression receiver, SliceExpression slice)
    {
        string list = EmitExpression(receiver);
        string start = slice.Start is null ? "0" : EmitListIndex(list, slice.Start);
        string end = slice.End is null
            ? $"{list}.Count"
            : slice.Kind == SliceKind.Inclusive ? $"({EmitListIndex(list, slice.End)} + 1)" : EmitListIndex(list, slice.End);
        if (!IsSideEffectFreeReceiver(receiver) || slice.Start is not null && !IsSimpleIndexExpression(slice.Start))
        {
            return $"__PscpSeq.sliceList({list}, {start}, {end})";
        }

        return start == "0" ? $"{list}.GetRange(0, {end})" : $"{list}.GetRange({start}, {end} - {start})";
    }

    private string EmitListIndex(string list, Expression index)
        => index is FromEndExpression fromEnd ? $"({list}.Count - {EmitExpression(fromEnd.Operand)})" : EmitExpression(index);

    private static bool IsSimpleIndexExpression(Expression expression)
        => expression is LiteralExpression or IdentifierExpression || expression is FromEndExpression { Operand: LiteralExpression or IdentifierExpression };

    // Spec §19: holes without a format follow the rendering rules (§18.3); formatted holes use C# formatting with
    // the invariant culture, so the whole string is created with `CultureInfo.InvariantCulture` when a hole
    // could depend on the culture.
    private string EmitInterpolatedStringExpression(InterpolatedStringExpression interpolated)
    {
        if (interpolated.Parts.Count == 0)
        {
            return "\"\"";
        }

        bool cultureSensitive = false;
        System.Text.StringBuilder builder = new("$\"");
        foreach (InterpolatedStringPart part in interpolated.Parts)
        {
            switch (part)
            {
                case InterpolatedStringTextPart textPart:
                    builder.Append(EscapeCSharpInterpolatedText(textPart.Text));
                    break;
                case InterpolatedStringInterpolationPart interpolationPart:
                {
                    TypeSyntax? type = _semantic?.GetExpressionType(interpolationPart.Expression);
                    string value = EmitExpression(interpolationPart.Expression);
                    if (interpolationPart.Format is null)
                    {
                        (value, bool rendered) = EmitRenderedHole(value, type);
                        cultureSensitive |= !rendered;
                    }
                    else
                    {
                        cultureSensitive = true;
                    }

                    builder.Append('{').Append(ParenthesizeInterpolationHole(value));
                    if (interpolationPart.Alignment is not null)
                    {
                        builder.Append(',').Append(interpolationPart.Alignment);
                    }

                    if (interpolationPart.Format is not null)
                    {
                        builder.Append(':').Append(interpolationPart.Format);
                    }

                    builder.Append('}');
                    break;
                }
            }
        }

        builder.Append('"');
        return cultureSensitive
            ? $"string.Create(CultureInfo.InvariantCulture, {builder})"
            : builder.ToString();
    }

    // The hole as a rendered string when C# formatting differs from §18.3 (`double`, `float`, `bool`, tuples,
    // collections, values of unknown type). `Rendered` is false when C# formats the value itself.
    private static (string Value, bool Rendered) EmitRenderedHole(string value, TypeSyntax? type)
        => type switch
        {
            NamedTypeSyntax { Name: "string" or "char" } => (value, true),
            NamedTypeSyntax { TypeArguments.Count: 0, Name: "int" or "long" or "short" or "byte" or "sbyte" or "uint" or "ulong" or "ushort" or "decimal" } => (value, false),
            NamedTypeSyntax { Name: "double" } => ($"__PscpRender.formatDouble({value})", true),
            NamedTypeSyntax { Name: "float" } => ($"__PscpRender.formatFloat({value})", true),
            NamedTypeSyntax { Name: "bool" } => ($"({value} ? \"true\" : \"false\")", true),
            _ => ($"__PscpRender.format({value})", true),
        };

    // A top-level `:` or `?` in a hole would start a format specifier; parentheses keep the expression whole.
    private static string ParenthesizeInterpolationHole(string value)
    {
        int depth = 0;
        bool inString = false;
        bool inChar = false;
        for (int i = 0; i < value.Length; i++)
        {
            char ch = value[i];
            if (inString || inChar)
            {
                if (ch == '\\')
                {
                    i++;
                }
                else if ((inString && ch == '"') || (inChar && ch == '\''))
                {
                    inString = false;
                    inChar = false;
                }

                continue;
            }

            switch (ch)
            {
                case '"':
                    inString = true;
                    break;
                case '\'':
                    inChar = true;
                    break;
                case '(' or '[' or '{':
                    depth++;
                    break;
                case ')' or ']' or '}':
                    depth--;
                    break;
                case ':' or '?' or ',' when depth == 0:
                    return $"({value})";
            }
        }

        return value;
    }

    private static string EscapeCSharpStringLiteral(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        System.Text.StringBuilder builder = new(value.Length);
        foreach (char ch in value)
        {
            _ = ch switch
            {
                '\\' => builder.Append(@"\\"),
                '"' => builder.Append("\\\""),
                '\0' => builder.Append(@"\0"),
                '\a' => builder.Append(@"\a"),
                '\b' => builder.Append(@"\b"),
                '\f' => builder.Append(@"\f"),
                '\n' => builder.Append(@"\n"),
                '\r' => builder.Append(@"\r"),
                '\t' => builder.Append(@"\t"),
                '\v' => builder.Append(@"\v"),
                _ when char.IsControl(ch) => builder.Append(@"\u").Append(((int)ch).ToString("X4", System.Globalization.CultureInfo.InvariantCulture)),
                _ => builder.Append(ch),
            };
        }

        return builder.ToString();
    }

    private bool TryEmitConversionIntrinsic(CallExpression call, string name, out string? emitted)
    {
        emitted = null;
        if (!IsConversionKeyword(name)
            || call.Arguments.Count != 1
            || call.Arguments[0] is not ExpressionArgumentSyntax expressionArgument
            || !string.IsNullOrWhiteSpace(expressionArgument.Name)
            || expressionArgument.Modifier != ArgumentModifier.None)
        {
            return false;
        }

        string operand = EmitExpression(expressionArgument.Expression);
        TypeSyntax? sourceType = _semantic?.GetExpressionType(expressionArgument.Expression)
            ?? InferConversionOperandType(expressionArgument.Expression);
        emitted = name switch
        {
            "int" => EmitNumericConversion("int", "int.Parse", "Convert.ToInt32", operand, sourceType),
            "long" => EmitNumericConversion("long", "long.Parse", "Convert.ToInt64", operand, sourceType),
            "double" => EmitNumericConversion("double", "double.Parse", "Convert.ToDouble", operand, sourceType),
            "decimal" => EmitNumericConversion("decimal", "decimal.Parse", "Convert.ToDecimal", operand, sourceType),
            "bool" => EmitBooleanConversion(operand, sourceType),
            "char" => EmitCharConversion(operand, sourceType),
            "string" => EmitStringConversion(operand, sourceType),
            _ => null,
        };
        return emitted is not null;
    }

    private static TypeSyntax? InferConversionOperandType(Expression expression)
        => expression switch
        {
            LiteralExpression { Kind: LiteralKind.True or LiteralKind.False }
                or IsPatternExpression
                or BinaryExpression { Operator: BinaryOperator.LessThan or BinaryOperator.LessThanOrEqual or BinaryOperator.GreaterThan or BinaryOperator.GreaterThanOrEqual or BinaryOperator.Equal or BinaryOperator.NotEqual or BinaryOperator.LogicalAnd or BinaryOperator.LogicalOr or BinaryOperator.BitwiseAnd or BinaryOperator.BitwiseXor or BinaryOperator.BitwiseOr }
                or UnaryExpression { Operator: UnaryOperator.LogicalNot } => new NamedTypeSyntax("bool", Immutable.List<TypeSyntax>()),
            CallExpression { Callee: MemberAccessExpression { MemberName: "Any" or "All" or "Contains" } } => new NamedTypeSyntax("bool", Immutable.List<TypeSyntax>()),
            _ => null,
        };

    private string EmitNumericConversion(string targetType, string parseMethod, string convertMethod, string operand, TypeSyntax? sourceType)
    {
        if (IsStringType(sourceType))
        {
            return $"{parseMethod}({operand}, CultureInfo.InvariantCulture)";
        }

        if (IsBoolType(sourceType))
        {
            string zeroLiteral = targetType == "decimal" ? "0m" : "0";
            string oneLiteral = targetType switch
            {
                "long" => "1L",
                "double" => "1d",
                "decimal" => "1m",
                _ => "1",
            };
            return $"({operand} ? {oneLiteral} : {zeroLiteral})";
        }

        // Spec §21: a real number converts to an integer by truncation toward zero. A value out of the target's
        // range is a precondition violation, reported by the checked cast.
        if (targetType is "int" or "long" && sourceType is NamedTypeSyntax { Name: "double" or "float" })
        {
            return $"checked(({targetType})({operand}))";
        }

        if (IsNumericType(sourceType) || IsCharType(sourceType))
        {
            return $"(({targetType})({operand}))";
        }

        return $"{convertMethod}({operand}, CultureInfo.InvariantCulture)";
    }

    // Spec §21.1: `string x` renders `x` like output does (§18.3).
    private static string EmitStringConversion(string operand, TypeSyntax? sourceType)
        => sourceType switch
        {
            NamedTypeSyntax { Name: "string" } => operand,
            NamedTypeSyntax { Name: "char" } => $"{operand}.ToString()",
            NamedTypeSyntax { Name: "int" or "long" or "short" or "byte" or "sbyte" or "uint" or "ulong" or "ushort" or "decimal" } => $"{operand}.ToString(CultureInfo.InvariantCulture)",
            NamedTypeSyntax { Name: "double" } => $"__PscpRender.formatDouble({operand})",
            NamedTypeSyntax { Name: "float" } => $"__PscpRender.formatFloat({operand})",
            NamedTypeSyntax { Name: "bool" } => $"({operand} ? \"true\" : \"false\")",
            ArrayTypeSyntax { Depth: 1, ElementType: NamedTypeSyntax { Name: "char" } } => $"new string({operand})",
            _ => $"__PscpRender.format({operand})",
        };

    private string EmitBooleanConversion(string operand, TypeSyntax? sourceType)
    {
        // Spec §21.1: the same parsing as `stdin.readBool()`.
        if (IsStringType(sourceType))
        {
            return $"__PscpRender.parseBool({operand})";
        }

        if (IsNumericType(sourceType))
        {
            return $"({operand} != 0)";
        }

        if (IsBoolType(sourceType))
        {
            return operand;
        }

        return $"Convert.ToBoolean({operand}, CultureInfo.InvariantCulture)";
    }

    private string EmitCharConversion(string operand, TypeSyntax? sourceType)
    {
        if (IsStringType(sourceType))
        {
            return $"__PscpRender.parseChar({operand})";
        }

        return $"((char)({operand}))";
    }

    private bool TryEmitKnownDataStructureAssignment(AssignmentExpression assignment, out string? emitted)
    {
        emitted = null;
        if (_semantic?.GetExpressionType(assignment.Target) is not NamedTypeSyntax named)
        {
            return false;
        }

        string target = EmitAssignmentTarget(assignment.Target);
        string value = EmitExpression(assignment.Value);
        emitted = named.Name switch
        {
            "List" or "System.Collections.Generic.List" when assignment.Operator == AssignmentOperator.AddAssign
                => $"{target}.Add({value})",
            "LinkedList" or "System.Collections.Generic.LinkedList" when assignment.Operator == AssignmentOperator.AddAssign
                => $"{target}.AddLast({value})",
            "HashSet" or "System.Collections.Generic.HashSet" when assignment.Operator == AssignmentOperator.AddAssign
                => $"{target}.Add({value})",
            "HashSet" or "System.Collections.Generic.HashSet" when assignment.Operator == AssignmentOperator.SubtractAssign
                => $"{target}.Remove({value})",
            "SortedSet" or "System.Collections.Generic.SortedSet" when assignment.Operator == AssignmentOperator.AddAssign
                => $"{target}.Add({value})",
            "SortedSet" or "System.Collections.Generic.SortedSet" when assignment.Operator == AssignmentOperator.SubtractAssign
                => $"{target}.Remove({value})",
            "Dictionary" or "System.Collections.Generic.Dictionary" when assignment.Operator == AssignmentOperator.AddAssign
                => EmitDictionaryAdd(target, assignment.Value),
            "Dictionary" or "System.Collections.Generic.Dictionary" when assignment.Operator == AssignmentOperator.SubtractAssign
                => $"{target}.Remove({value})",
            "Queue" or "System.Collections.Generic.Queue" when assignment.Operator == AssignmentOperator.AddAssign
                => $"{target}.Enqueue({value})",
            "Stack" or "System.Collections.Generic.Stack" when assignment.Operator == AssignmentOperator.AddAssign
                => $"{target}.Push({value})",
            "PriorityQueue" or "System.Collections.Generic.PriorityQueue" when assignment.Operator == AssignmentOperator.AddAssign
                => EmitPriorityQueueEnqueue(target, assignment.Value),
            _ => null,
        };
        return emitted is not null;
    }

    private string EmitDictionaryAdd(string target, Expression valueExpression)
    {
        if (valueExpression is TupleExpression tuple && tuple.Elements.Count == 2)
        {
            return $"{target}.TryAdd({EmitExpression(tuple.Elements[0])}, {EmitExpression(tuple.Elements[1])})";
        }

        string value = EmitExpression(valueExpression);
        return IsSideEffectFreeReceiver(valueExpression)
            ? $"{target}.TryAdd({value}.Item1, {value}.Item2)"
            : $"__PscpCollection.tryAdd({target}, {value})";
    }

    private string EmitPriorityQueueEnqueue(string target, Expression valueExpression)
    {
        if (valueExpression is TupleExpression tuple && tuple.Elements.Count == 2)
        {
            return $"{target}.Enqueue({EmitExpression(tuple.Elements[0])}, {EmitExpression(tuple.Elements[1])})";
        }

        string value = EmitExpression(valueExpression);
        return IsSideEffectFreeReceiver(valueExpression)
            ? $"{target}.Enqueue({value}.Item1, {value}.Item2)"
            : $"__PscpCollection.enqueue({target}, {value})";
    }

    private string EmitWithExpression(WithExpression withExpression)
    {
        string receiver = EmitExpression(withExpression.Receiver);
        if (withExpression.Assignments.Count == 0)
        {
            return $"{receiver} with {{ }}";
        }

        string assignments = string.Join(", ", withExpression.Assignments.Select(assignment => $"{assignment.MemberName} = {EmitExpression(assignment.Value)}"));
        return $"{receiver} with {{ {assignments} }}";
    }

    private string EmitSwitchExpression(SwitchExpression switchExpression)
    {
        string receiver = EmitExpression(switchExpression.Receiver);
        IEnumerable<string> arms = switchExpression.Arms.Select(arm =>
        {
            string guard = arm.Guard is null ? string.Empty : $" when {EmitExpression(arm.Guard)}";
            return $"{arm.Pattern.Text}{guard} => {EmitExpression(arm.Result)}";
        });
        return $"{receiver} switch {{ {string.Join(", ", arms)} }}";
    }

    // Expressions that can be emitted more than once without changing program behavior.
    private static bool IsSideEffectFreeReceiver(Expression expression)
        => expression switch
        {
            IdentifierExpression => true,
            MemberAccessExpression member => IsSideEffectFreeReceiver(member.Receiver),
            TupleProjectionExpression projection => IsSideEffectFreeReceiver(projection.Receiver),
            _ => false,
        };

    private bool TryEmitKnownDataStructurePeek(Expression operand, out string? emitted)
    {
        emitted = null;
        if (_semantic?.GetExpressionType(operand) is not NamedTypeSyntax named)
        {
            return false;
        }

        string receiver = EmitExpression(operand);
        emitted = named.Name switch
        {
            "Stack" or "System.Collections.Generic.Stack" => $"{receiver}.Peek()",
            "Queue" or "System.Collections.Generic.Queue" => $"{receiver}.Peek()",
            "PriorityQueue" or "System.Collections.Generic.PriorityQueue" => $"{receiver}.Peek()",
            _ => null,
        };
        return emitted is not null;
    }

    private bool TryEmitKnownDataStructurePop(Expression operand, out string? emitted)
    {
        emitted = null;
        if (_semantic?.GetExpressionType(operand) is not NamedTypeSyntax named)
        {
            return false;
        }

        string receiver = EmitExpression(operand);
        emitted = named.Name switch
        {
            "Stack" or "System.Collections.Generic.Stack" => $"{receiver}.Pop()",
            "Queue" or "System.Collections.Generic.Queue" => $"{receiver}.Dequeue()",
            "PriorityQueue" or "System.Collections.Generic.PriorityQueue" => $"{receiver}.Dequeue()",
            _ => null,
        };
        return emitted is not null;
    }

    private string EmitTargetTypedArrayAllocation(ArrayTypeSyntax arrayType, IReadOnlyList<Expression> dimensions)
    {
        if (dimensions.Count == 1)
        {
            string trailingRanks = string.Concat(Enumerable.Repeat("[]", Math.Max(0, arrayType.Depth - 1)));
            return NewArray(EmitType(arrayType.ElementType) + trailingRanks, EmitExpression(dimensions[0]));
        }

        return EmitJaggedArrayAllocation(EmitType(arrayType.ElementType), dimensions);
    }

    private static bool IsConversionKeyword(string name)
        => name is "int" or "long" or "double" or "decimal" or "bool" or "char" or "string";

    private static bool IsStringType(TypeSyntax? type)
        => type is NamedTypeSyntax { Name: "string" };

    private static bool IsBoolType(TypeSyntax? type)
        => type is NamedTypeSyntax { Name: "bool" };

    private static bool IsCharType(TypeSyntax? type)
        => type is NamedTypeSyntax { Name: "char" };

    private static bool IsNumericType(TypeSyntax? type)
        => type is NamedTypeSyntax named
            && named.Name is "int" or "long" or "double" or "decimal";

    private static bool IsListType(NamedTypeSyntax named)
        => named.Name is "List" or "System.Collections.Generic.List";

    private static bool IsLinkedListType(NamedTypeSyntax named)
        => named.Name is "LinkedList" or "System.Collections.Generic.LinkedList";

    private static bool IsKnownAutoConstructType(NamedTypeSyntax named)
        => named.Name is "List" or "System.Collections.Generic.List"
            or "LinkedList" or "System.Collections.Generic.LinkedList"
            or "Queue" or "System.Collections.Generic.Queue"
            or "Stack" or "System.Collections.Generic.Stack"
            or "HashSet" or "System.Collections.Generic.HashSet"
            or "Dictionary" or "System.Collections.Generic.Dictionary"
            or "SortedSet" or "System.Collections.Generic.SortedSet"
            or "SortedDictionary" or "System.Collections.Generic.SortedDictionary"
            or "PriorityQueue" or "System.Collections.Generic.PriorityQueue";

    private static TypeSyntax? GetCollectionElementType(TypeSyntax type)
        => type switch
        {
            ArrayTypeSyntax array => array.Depth > 1 ? new ArrayTypeSyntax(array.ElementType, array.Depth - 1) : array.ElementType,
            SizedArrayTypeSyntax sized => sized.Dimensions.Count > 1 ? new ArrayTypeSyntax(sized.ElementType, sized.Dimensions.Count - 1) : sized.ElementType,
            NamedTypeSyntax { Name: "string" } => new NamedTypeSyntax("char", Immutable.List<TypeSyntax>()),
            NamedTypeSyntax named when named.TypeArguments.Count == 1 && (IsListType(named) || IsLinkedListType(named)) => named.TypeArguments[0],
            NamedTypeSyntax named when named.TypeArguments.Count == 1 && named.Name is "IEnumerable" or "System.Collections.Generic.IEnumerable" or "Queue" or "System.Collections.Generic.Queue" or "Stack" or "System.Collections.Generic.Stack" or "HashSet" or "System.Collections.Generic.HashSet" or "SortedSet" or "System.Collections.Generic.SortedSet" => named.TypeArguments[0],
            NamedTypeSyntax named when named.TypeArguments.Count > 0 && named.Name is "PriorityQueue" or "System.Collections.Generic.PriorityQueue" => named.TypeArguments[0],
            _ => null
        };

    private static TypeSyntax? NormalizeSizedTypeOrNull(TypeSyntax? type)
        => type is null ? null : NormalizeSizedType(type);

    private string EmitMemberReceiverExpression(Expression receiver)
    {
        string emittedReceiver = EmitExpression(receiver);
        return NeedsNullableValueTypeAccess(_semantic?.GetExpressionType(receiver))
            ? $"({emittedReceiver}).GetValueOrDefault()"
            : emittedReceiver;
    }

    private bool NeedsNullableValueTypeAccess(TypeSyntax? type)
        => type is NullableTypeSyntax nullable && IsValueTypeLike(nullable.InnerType);

    private bool IsValueTypeLike(TypeSyntax? type)
        => type switch
        {
            null => false,
            NullableTypeSyntax nullable => IsValueTypeLike(nullable.InnerType),
            TupleTypeSyntax => true,
            NamedTypeSyntax { Name: "int" or "long" or "double" or "decimal" or "bool" or "char" } => true,
            NamedTypeSyntax { Name: "string" } => false,
            ArrayTypeSyntax => false,
            SizedArrayTypeSyntax => false,
            NamedTypeSyntax named when TryGetDeclaredTypeShape(named, out DeclaredTypeShape? shape) => shape!.IsValueType,
            _ => false,
        };

    private static TypeSyntax? UnwrapNullableType(TypeSyntax? type)
        => type is NullableTypeSyntax nullable ? nullable.InnerType : type;

    private bool IsLongRange(RangeExpression range, TypeSyntax? targetTypeHint)
    {
        TypeSyntax? targetElement = GetCollectionElementType(targetTypeHint ?? new NamedTypeSyntax("object", Immutable.List<TypeSyntax>()));
        if (targetElement is NamedTypeSyntax { Name: "long" })
        {
            return true;
        }

        // An explicit `int` element type (`int[] xs = [0..<n]`) keeps `int` elements even for `long` bounds.
        if (targetElement is NamedTypeSyntax { Name: "int" })
        {
            return false;
        }

        return IsLongExpression(range.Start) || IsLongExpression(range.End) || (range.Step is not null && IsLongExpression(range.Step));
    }

    // A range iterates with a `long` counter when any bound or the step is statically wider than `int`. Using the
    // semantic type (not just an `L` suffix) keeps `for i in 0..<n` with `long n` from overflowing an `int` counter.
    private bool IsLongExpression(Expression expression)
    {
        if (expression is LiteralExpression { Kind: LiteralKind.Integer, RawText: var text })
        {
            return PscpNumericLiterals.IsWiderThanInt(text, isFloat: false);
        }

        if (expression is UnaryExpression { Operator: UnaryOperator.Negate or UnaryOperator.Plus } unary)
        {
            return IsLongExpression(unary.Operand);
        }

        return _semantic?.GetExpressionType(expression) is NamedTypeSyntax { Name: "long" or "Int64" or "System.Int64" or "uint" or "UInt32" or "System.UInt32" };
    }

    private string EmitInlineBlockWithBindings(BlockStatement block, (BindingTarget? Target, string? Name) indexBinding, (BindingTarget Target, string Name) itemBinding, bool isVoidLike)
    {
        List<string> parts = [];
        if (indexBinding.Target is not null && indexBinding.Name is not null)
        {
            string indexPattern = EmitBindingPattern(indexBinding.Target);
            if (!string.Equals(indexPattern, indexBinding.Name, StringComparison.Ordinal))
            {
                parts.Add($"var {indexPattern} = {indexBinding.Name};");
            }
        }

        string itemPattern = EmitBindingPattern(itemBinding.Target);
        if (!string.Equals(itemPattern, itemBinding.Name, StringComparison.Ordinal))
        {
            parts.Add($"var {itemPattern} = {itemBinding.Name};");
        }
        string blockText = EmitInlineBlock(block, isVoidLike);
        if (!string.IsNullOrWhiteSpace(blockText))
        {
            parts.Add(blockText);
        }

        return string.Join(" ", parts);
    }

    private string EmitBlockExpression(BlockStatement block)
        => EmitEval($"{EmitInlineBlock(block, isVoidLike: false)}");

    private static string EmitEval(string body)
        => $"__PscpThunk.run(() => {{ {body} }})";

    private static string EmitDebugEmptySequenceCheckInline(string hasValueName)
        => $"\n#if DEBUG\nif (!{hasValueName}) throw new InvalidOperationException(\"Sequence contains no elements.\");\n#endif\n";

    private static string? TryGetPreferredBindingName(BindingTarget? target)
        => target switch
        {
            NameTarget nameTarget => nameTarget.Name,
            _ => null,
        };

    private string ChooseBindingName(BindingTarget? target, string prefix)
    {
        string? preferred = TryGetPreferredBindingName(target);
        if (preferred is null
            || _declaredValueNames.Contains(preferred)
            || IsIdentifierAliasActive(preferred))
        {
            return NextTemporary(prefix);
        }

        return preferred;
    }

    private string EmitLoopBindingPattern(BindingTarget target, string prefix)
        => target switch
        {
            DiscardTarget => NextTemporary(prefix),
            NameTarget nameTarget when _declaredValueNames.Contains(nameTarget.Name) || IsIdentifierAliasActive(nameTarget.Name) => NextTemporary(prefix),
            _ => EmitBindingPattern(target),
        };

    private bool TryGetIdentifierAlias(string name, out string? alias)
    {
        alias = null;
        if (!_identifierAliases.TryGetValue(name, out Stack<string>? aliases) || aliases.Count == 0)
        {
            return false;
        }

        alias = aliases.Peek();
        return true;
    }

    private bool IsIdentifierAliasActive(string name)
        => _identifierAliases.TryGetValue(name, out Stack<string>? aliases) && aliases.Count > 0;

    private void PushBindingAlias(BindingTarget? target, string? sourceName)
    {
        if (target is not NameTarget nameTarget
            || string.IsNullOrWhiteSpace(sourceName)
            || string.Equals(nameTarget.Name, sourceName, StringComparison.Ordinal))
        {
            return;
        }

        if (!_identifierAliases.TryGetValue(nameTarget.Name, out Stack<string>? aliases))
        {
            aliases = new Stack<string>();
            _identifierAliases[nameTarget.Name] = aliases;
        }

        aliases.Push(sourceName!);
    }

    private void PopBindingAlias(BindingTarget? target, string? sourceName)
    {
        if (target is not NameTarget nameTarget
            || string.IsNullOrWhiteSpace(sourceName)
            || string.Equals(nameTarget.Name, sourceName, StringComparison.Ordinal)
            || !_identifierAliases.TryGetValue(nameTarget.Name, out Stack<string>? aliases)
            || aliases.Count == 0)
        {
            return;
        }

        aliases.Pop();
    }

    private T EmitWithBindingAliases<T>(BindingTarget? indexTarget, string? indexName, BindingTarget itemTarget, string itemName, Func<T> emit)
    {
        PushBindingAlias(indexTarget, indexName);
        PushBindingAlias(itemTarget, itemName);
        try
        {
            return emit();
        }
        finally
        {
            PopBindingAlias(itemTarget, itemName);
            PopBindingAlias(indexTarget, indexName);
        }
    }

    private string EmitBindingAliasStatements(BindingTarget? indexTarget, string? indexName, BindingTarget itemTarget, string itemName)
    {
        List<string> aliases = [];
        string indexAlias = EmitBindingAliasStatement(indexTarget, indexName);
        if (!string.IsNullOrWhiteSpace(indexAlias))
        {
            aliases.Add(indexAlias);
        }

        string itemAlias = EmitBindingAliasStatement(itemTarget, itemName);
        if (!string.IsNullOrWhiteSpace(itemAlias))
        {
            aliases.Add(itemAlias);
        }

        return string.Join(" ", aliases);
    }

    private string EmitBindingAliasStatement(BindingTarget? target, string? sourceName)
    {
        if (target is null || string.IsNullOrWhiteSpace(sourceName))
        {
            return string.Empty;
        }

        return target switch
        {
            NameTarget nameTarget when nameTarget.Name == sourceName => string.Empty,
            NameTarget nameTarget when IsIdentifierAliasActive(nameTarget.Name) => string.Empty,
            NameTarget nameTarget => $"var {nameTarget.Name} = {sourceName};",
            TupleTarget tupleTarget => $"var {EmitBindingPattern(tupleTarget)} = {sourceName};",
            DiscardTarget => string.Empty,
            _ => string.Empty,
        };
    }

    private string EmitLoopOverSource(Expression source, string itemName, string bodyStatements, string? indexName = null)
    {
        if (source is RangeExpression range)
        {
            return EmitRangeLoopText(range, itemName, bodyStatements, indexName);
        }

        if (indexName is null)
        {
            return $"foreach (var {itemName} in {EmitEnumerable(source)}) {{ {bodyStatements} }}";
        }

        return $"{{ int {indexName} = 0; foreach (var {itemName} in {EmitEnumerable(source)}) {{ {bodyStatements} {indexName}++; }} }}";
    }

    // `new T[n]` for an element type T. The ranks of an array element type follow the length: `new int[n][]`.
    private static string NewArray(string elementType, string length)
    {
        int split = elementType.Length;
        while (split > 0)
        {
            if (split >= 2 && elementType[split - 1] == ']' && elementType[split - 2] == '[')
            {
                split -= 2;
            }
            else if (split >= 2 && elementType[split - 1] == '?' && elementType[split - 2] == ']')
            {
                split--;
            }
            else
            {
                break;
            }
        }

        return $"new {elementType[..split]}[{length}]{elementType[split..].Replace("?", string.Empty, StringComparison.Ordinal)}";
    }

    private string EmitAutoConstructArrayAllocation(TypeSyntax elementType, Expression lengthExpression)
    {
        string lengthName = NextTemporary("length");
        string resultName = NextTemporary("result");
        string indexName = NextTemporary("i");
        string elementTypeText = EmitType(elementType);
        string construction = elementType is NamedTypeSyntax named ? EmitAutoConstruction(named, targetTyped: false) : $"new {elementTypeText}()";
        return EmitValueBlock($"int {lengthName} = {EmitExpression(lengthExpression)}; {elementTypeText}[] {resultName} = {NewArray(elementTypeText, lengthName)}; for (int {indexName} = 0; {indexName} < {lengthName}; {indexName}++) {{ {resultName}[{indexName}] = {construction}; }} return {resultName};");
    }

    private string EmitJaggedArrayAllocation(string elementType, IReadOnlyList<Expression> dimensions)
    {
        if (dimensions.Count == 2)
        {
            string outerLength = NextTemporary("outerLength");
            string innerLength = NextTemporary("innerLength");
            string resultName = NextTemporary("result");
            string indexName = NextTemporary("i");
            return EmitValueBlock($"int {outerLength} = {EmitExpression(dimensions[0])}; int {innerLength} = {EmitExpression(dimensions[1])}; {elementType}[][] {resultName} = {NewArray(elementType + "[]", outerLength)}; for (int {indexName} = 0; {indexName} < {outerLength}; {indexName}++) {{ {resultName}[{indexName}] = {NewArray(elementType, innerLength)}; }} return {resultName};");
        }

        return $"__PscpArray.jagged<{elementType}>({string.Join(", ", dimensions.Select(dimension => EmitExpression(dimension)))})";
    }

    private static string EscapeCSharpInterpolatedText(string value)
        => EscapeCSharpStringLiteral(value)
            .Replace("{", "{{", StringComparison.Ordinal)
            .Replace("}", "}}", StringComparison.Ordinal);

    private string EmitSimpleCollectionExpression(CollectionExpression collection, TypeSyntax? targetTypeHint, TypeSyntax? elementHint)
    {
        string[] values = collection.Elements
            .Cast<ExpressionElement>()
            .Select(element => EmitExpression(element.Expression, elementHint))
            .ToArray();

        return targetTypeHint switch
        {
            NamedTypeSyntax named when IsListType(named) => $"new {EmitType(named)} {{ {string.Join(", ", values)} }}",
            NamedTypeSyntax named when IsLinkedListType(named) => $"new {EmitType(named)}(new[] {{ {string.Join(", ", values)} }})",
            ArrayTypeSyntax arrayType => $"new {EmitType(arrayType.ElementType)}[] {{ {string.Join(", ", values)} }}",
            _ => $"new[] {{ {string.Join(", ", values)} }}",
        };
    }

    private bool TryEmitMaterializedRange(RangeExpression range, TypeSyntax? targetTypeHint, TypeSyntax? elementHint, out string? emitted)
    {
        emitted = null;
        if (!IsArrayMaterializationTarget(targetTypeHint) || elementHint is null)
        {
            return false;
        }

        string itemName = NextTemporary("value");
        emitted = EmitRangeArrayMaterialization(range, elementHint, itemName, null, itemName);
        return true;
    }

    private bool TryEmitMaterializedBuilder(BuilderElement builder, TypeSyntax? targetTypeHint, TypeSyntax? elementHint, out string? emitted)
    {
        emitted = null;
        if (!IsArrayMaterializationTarget(targetTypeHint)
            || elementHint is null
            || builder.Source is not RangeExpression range)
        {
            return false;
        }

        string itemName = ChooseBindingName(builder.ItemTarget, "item");
        string? indexName = builder.IndexTarget is null ? null : ChooseBindingName(builder.IndexTarget, "index");
        string valueExpression = EmitLambdaBodyExpression(builder.Body, builder.IndexTarget, indexName, builder.ItemTarget, itemName);
        emitted = EmitRangeArrayMaterialization(range, elementHint, itemName, indexName, valueExpression);
        return true;
    }

    private string EmitRangeArrayMaterialization(RangeExpression range, TypeSyntax elementType, string itemName, string? indexName, string valueExpression)
        => EmitRangeArrayValueBlock(PlanRange(range, new ArrayTypeSyntax(elementType, 1)), EmitType(elementType), itemName, indexName, valueExpression);

    private static bool IsZeroExpressionText(string text)
        => string.Equals(text.Trim(), "0", StringComparison.Ordinal)
            || string.Equals(text.Trim(), "0L", StringComparison.OrdinalIgnoreCase);

    private string EmitGeneralCollectionMaterialization(CollectionExpression collection, TypeSyntax? targetTypeHint, TypeSyntax? elementHint)
    {
        string elementTypeText = EmitType(elementHint ?? new NamedTypeSyntax("object", Immutable.List<TypeSyntax>()));
        string collectionName = NextTemporary("items");
        bool returnList = targetTypeHint is NamedTypeSyntax named && IsListType(named);
        bool returnLinkedList = targetTypeHint is NamedTypeSyntax linked && IsLinkedListType(linked);
        string creation = targetTypeHint switch
        {
            NamedTypeSyntax listType when IsListType(listType) => $"var {collectionName} = new {EmitType(listType)}();",
            NamedTypeSyntax linkedType when IsLinkedListType(linkedType) => $"var {collectionName} = new {EmitType(linkedType)}();",
            _ => $"var {collectionName} = new List<{elementTypeText}>();",
        };

        string AddExpression(string valueExpression)
            => returnLinkedList ? $"{collectionName}.AddLast({valueExpression});" : $"{collectionName}.Add({valueExpression});";

        List<string> parts = [creation];
        foreach (CollectionElement element in collection.Elements)
        {
            switch (element)
            {
                case ExpressionElement expressionElement:
                    parts.Add(AddExpression(EmitExpression(expressionElement.Expression, elementHint)));
                    break;
                case SpreadElement spreadElement:
                {
                    string spreadName = NextTemporary("spread");
                    parts.Add($"foreach (var {spreadName} in {EmitExpression(spreadElement.Expression)}) {{ {AddExpression(spreadName)} }}");
                    break;
                }
                case RangeElement rangeElement:
                {
                    string itemName = NextTemporary("value");
                    parts.Add(EmitLoopOverSource(rangeElement.Range, itemName, AddExpression(itemName)));
                    break;
                }
                case BuilderElement builderElement:
                {
                    string itemName = ChooseBindingName(builderElement.ItemTarget, "item");
                    string? indexName = builderElement.IndexTarget is null ? null : ChooseBindingName(builderElement.IndexTarget, "index");
                    string valueExpression = EmitLambdaBodyExpression(builderElement.Body, builderElement.IndexTarget, indexName, builderElement.ItemTarget, itemName);
                    parts.Add(EmitLoopOverSource(builderElement.Source, itemName, AddExpression(valueExpression), indexName));
                    break;
                }
            }
        }

        parts.Add(returnList || returnLinkedList ? $"return {collectionName};" : $"return {collectionName}.ToArray();");
        return EmitValueBlock(string.Join(" ", parts));
    }

    private static bool IsArrayMaterializationTarget(TypeSyntax? targetTypeHint)
        => targetTypeHint is null or ArrayTypeSyntax;

    private string EmitLambdaBodyExpression(LambdaBody body, BindingTarget? indexTarget, string? indexName, BindingTarget itemTarget, string itemName)
    {
        return EmitWithBindingAliases(indexTarget, indexName, itemTarget, itemName, () =>
        {
            string aliases = EmitBindingAliasStatements(indexTarget, indexName, itemTarget, itemName);
            return body switch
            {
                LambdaExpressionBody expressionBody when string.IsNullOrWhiteSpace(aliases) => EmitExpression(expressionBody.Expression),
                LambdaExpressionBody expressionBody => EmitEval($"{aliases} return {EmitExpression(expressionBody.Expression)};"),
                LambdaBlockBody blockBody when string.IsNullOrWhiteSpace(aliases) => EmitEval($"{EmitInlineBlock(blockBody.Block, isVoidLike: false)}"),
                LambdaBlockBody blockBody => EmitEval($"{aliases} {EmitInlineBlock(blockBody.Block, isVoidLike: false)}"),
                _ => "default!",
            };
        });
    }

    private bool TryEmitLambdaBodyValueStatement(
        LambdaBody body,
        BindingTarget? indexTarget,
        string? indexName,
        BindingTarget itemTarget,
        string itemName,
        Func<string, string> emitValueStatement,
        out string emitted)
    {
        string result = string.Empty;
        bool success = EmitWithBindingAliases(indexTarget, indexName, itemTarget, itemName, () =>
        {
            string aliases = EmitBindingAliasStatements(indexTarget, indexName, itemTarget, itemName);
            if (body is LambdaExpressionBody expressionBody)
            {
                result = JoinInlineStatements(aliases, emitValueStatement(EmitExpression(expressionBody.Expression)));
                return true;
            }

            if (body is LambdaBlockBody blockBody
                && TryEmitBlockValueStatement(blockBody.Block, emitValueStatement, out string blockStatement))
            {
                result = JoinInlineStatements(aliases, blockStatement);
                return true;
            }

            return false;
        });
        emitted = result;
        return success;
    }

    private bool TryEmitBlockValueStatement(
        BlockStatement block,
        Func<string, string> emitValueStatement,
        out string emitted)
    {
        emitted = string.Empty;
        if (!TryGetTerminalValueExpression(block, out Expression? valueExpression, out int regularCount))
        {
            return false;
        }

        List<string> parts = [];
        for (int i = 0; i < regularCount; i++)
        {
            if (ContainsControlFlowReturn(block.Statements[i]))
            {
                return false;
            }

            parts.Add(EmitInlineStatement(block.Statements[i]));
        }

        parts.Add(emitValueStatement(EmitExpression(valueExpression!)));
        emitted = JoinInlineStatements(parts);
        return true;
    }

    private bool TryGetTerminalValueExpression(BlockStatement block, out Expression? expression, out int regularCount)
    {
        expression = GetImplicitReturnExpression(block);
        if (expression is not null)
        {
            regularCount = block.Statements.Count - 1;
            return true;
        }

        if (block.Statements.LastOrDefault() is ReturnStatement { Expression: not null } returnStatement)
        {
            expression = returnStatement.Expression;
            regularCount = block.Statements.Count - 1;
            return true;
        }

        regularCount = block.Statements.Count;
        return false;
    }

    private static bool ContainsControlFlowReturn(Statement statement)
        => statement switch
        {
            ReturnStatement => true,
            BlockStatement block => block.Statements.Any(ContainsControlFlowReturn),
            IfStatement ifStatement => ContainsControlFlowReturn(ifStatement.ThenBranch)
                || (ifStatement.ElseBranch is not null && ContainsControlFlowReturn(ifStatement.ElseBranch)),
            WhileStatement whileStatement => ContainsControlFlowReturn(whileStatement.Body),
            ForInStatement forIn => ContainsControlFlowReturn(forIn.Body),
            CStyleForStatement cStyleFor => ContainsControlFlowReturn(cStyleFor.Body),
            FastForStatement fastFor => ContainsControlFlowReturn(fastFor.Body),
            LocalFunctionStatement => false,
            _ => false,
        };

    private static string JoinInlineStatements(params string[] statements)
        => JoinInlineStatements((IEnumerable<string>)statements);

    private static string JoinInlineStatements(IEnumerable<string> statements)
        => string.Join(" ", statements.Where(statement => !string.IsNullOrWhiteSpace(statement)));

    private bool TryEmitDirectIntrinsicCall(CallExpression call, out string? emitted)
    {
        emitted = null;
        if (call.Callee is IdentifierExpression conversionIdentifier
            && TryEmitConversionIntrinsic(call, conversionIdentifier.Name, out emitted))
        {
            return true;
        }

        if (_semantic is not null && !_semantic.IsIntrinsicCall(call))
        {
            return false;
        }

        if (call.Callee is IdentifierExpression identifier)
        {
            if (TryEmitDirectIntrinsicCallCore(call, identifier.Name, receiver: null, call.Arguments, out emitted))
            {
                return true;
            }

            if (PscpIntrinsicCatalog.IntrinsicCallNames.Contains(identifier.Name))
            {
                emitted = $"__PscpSeq.{identifier.Name}({string.Join(", ", call.Arguments.Select(argument => EmitExpressionArgument(argument)))})";
                return true;
            }

            return false;
        }

        if (call.Callee is MemberAccessExpression member
            && PscpIntrinsicCatalog.IntrinsicCallNames.Contains(member.MemberName))
        {
            return TryEmitDirectIntrinsicCallCore(call, member.MemberName, member.Receiver, call.Arguments, out emitted);
        }

        return false;
    }

    private bool TryEmitDirectIntrinsicCallCore(CallExpression call, string name, Expression? receiver, IReadOnlyList<ArgumentSyntax> arguments, out string? emitted)
    {
        emitted = null;
        return (name switch
        {
            "sum" => TryEmitDirectSumIntrinsic(call, receiver, arguments, out emitted),
            "sumBy" => TryEmitDirectSumByIntrinsic(call, receiver, arguments, out emitted),
            "min" or "max" => TryEmitDirectMinMaxIntrinsic(call, name, receiver, arguments, out emitted),
            "minBy" or "maxBy" => TryEmitDirectMinMaxByIntrinsic(call, name, receiver, arguments, out emitted),
            "count" or "any" or "all" or "find" or "findIndex" or "findLastIndex" => TryEmitDirectPredicateIntrinsic(call, name, receiver, arguments, out emitted),
            "chmin" or "chmax" => TryEmitDirectCompareUpdateIntrinsic(name, arguments, out emitted),
            "abs" or "sqrt" or "clamp" or "gcd" or "lcm" or "floor" or "ceil" or "round" or "pow" or "popcount" or "bitLength"
                => TryEmitDirectMathIntrinsic(name, arguments, out emitted),
            _ => false,
        });
    }

    private bool TryEmitDirectSumIntrinsic(CallExpression call, Expression? receiver, IReadOnlyList<ArgumentSyntax> arguments, out string? emitted)
    {
        emitted = null;
        if (!TryGetSourceOnlyCall(receiver, arguments, out Expression? source))
        {
            return false;
        }

        TypeSyntax? resultType = _semantic?.GetExpressionType(call) ?? GetCollectionElementType(_semantic?.GetExpressionType(source!) ?? new NamedTypeSyntax("object", Immutable.List<TypeSyntax>()));
        if (resultType is null)
        {
            return false;
        }

        resultType = WidenAccumulator(resultType);
        string sumType = EmitType(resultType);
        string sumName = NextTemporary("sum");
        string loop;
        if (source is GeneratorExpression generator)
        {
            string itemName = ChooseBindingName(generator.ItemTarget, "item");
            string? indexName = generator.IndexTarget is null ? null : ChooseBindingName(generator.IndexTarget, "index");
            string valueExpression = EmitLambdaBodyExpression(generator.Body, generator.IndexTarget, indexName, generator.ItemTarget, itemName);
            loop = EmitLoopOverSource(generator.Source, itemName, AccumulateStatement(sumName, valueExpression, resultType), indexName);
        }
        else
        {
            string itemName = NextTemporary("item");
            loop = EmitLoopOverSource(source!, itemName, AccumulateStatement(sumName, itemName, resultType));
        }
        emitted = EmitValueBlock($"{sumType} {sumName} = default; {loop} return {sumName};");
        return true;
    }

    private bool TryEmitDirectSumByIntrinsic(CallExpression call, Expression? receiver, IReadOnlyList<ArgumentSyntax> arguments, out string? emitted)
    {
        emitted = null;
        if (!TryGetSourceAndUnaryLambdaCall(receiver, arguments, out Expression? source, out LambdaExpression? selector))
        {
            return false;
        }

        TypeSyntax? resultType = _semantic?.GetExpressionType(call);
        if (resultType is null)
        {
            return false;
        }

        resultType = WidenAccumulator(resultType);
        string sumType = EmitType(resultType);
        string sumName = NextTemporary("sum");
        string itemName = ChooseBindingName(selector!.Parameters[0].Target, "item");
        string selectorExpression = EmitLambdaBodyExpression(selector.Body, null, null, selector.Parameters[0].Target, itemName);
        string loop = EmitLoopOverSource(source!, itemName, AccumulateStatement(sumName, selectorExpression, resultType));
        emitted = EmitValueBlock($"{sumType} {sumName} = default; {loop} return {sumName};");
        return true;
    }

    private bool TryEmitDirectMinMaxIntrinsic(CallExpression call, string name, Expression? receiver, IReadOnlyList<ArgumentSyntax> arguments, out string? emitted)
    {
        emitted = null;
        if (receiver is null && TryEmitFixedArityMinMaxIntrinsic(name, arguments, out emitted))
        {
            return true;
        }

        if (!TryGetSourceOnlyCall(receiver, arguments, out Expression? source))
        {
            return false;
        }

        TypeSyntax? resultType = _semantic?.GetExpressionType(call) ?? GetCollectionElementType(_semantic?.GetExpressionType(source!) ?? new NamedTypeSyntax("object", Immutable.List<TypeSyntax>()));
        if (resultType is null)
        {
            return false;
        }

        string bestName = NextTemporary("best");
        string hasValueName = NextTemporary("hasValue");
        string loop = source switch
        {
            GeneratorExpression generator => EmitGeneratedMinMaxLoop(generator, resultType, bestName, hasValueName, preferLower: name == "min"),
            _ => EmitMinMaxLoop(source!, resultType, bestName, hasValueName, preferLower: name == "min"),
        };
        emitted = EmitValueBlock($"bool {hasValueName} = false; {EmitType(resultType)} {bestName} = default!; {loop}{EmitDebugEmptySequenceCheckInline(hasValueName)}return {bestName};");
        return true;
    }

    private bool TryEmitDirectMinMaxByIntrinsic(CallExpression call, string name, Expression? receiver, IReadOnlyList<ArgumentSyntax> arguments, out string? emitted)
    {
        emitted = null;
        if (!TryGetSourceAndUnaryLambdaCall(receiver, arguments, out Expression? source, out LambdaExpression? selector))
        {
            return false;
        }

        TypeSyntax? resultType = _semantic?.GetExpressionType(call);
        TypeSyntax? keyType = selector!.Body switch
        {
            LambdaExpressionBody expressionBody => _semantic?.GetExpressionType(expressionBody.Expression),
            LambdaBlockBody blockBody => blockBody.Block.Statements.LastOrDefault() is ExpressionStatement { HasSemicolon: false } tail
                ? _semantic?.GetExpressionType(tail.Expression)
                : null,
            _ => null,
        };

        if (resultType is null || keyType is null)
        {
            return false;
        }

        string hasValueName = NextTemporary("hasValue");
        string bestItemName = NextTemporary("bestItem");
        string bestKeyName = NextTemporary("bestKey");
        string itemName = ChooseBindingName(selector.Parameters[0].Target, "item");
        string keyName = NextTemporary("key");
        string selectorExpression = EmitLambdaBodyExpression(selector.Body, null, null, selector.Parameters[0].Target, itemName);
        string comparison = EmitPreferredComparison(keyName, bestKeyName, keyType, preferLower: name == "minBy");
        string loop = EmitLoopOverSource(source!, itemName, $"var {keyName} = {selectorExpression}; if (!{hasValueName} || {comparison}) {{ {bestItemName} = {itemName}; {bestKeyName} = {keyName}; {hasValueName} = true; }}");
        emitted = EmitValueBlock($"bool {hasValueName} = false; {EmitType(resultType)} {bestItemName} = default!; {EmitType(keyType)} {bestKeyName} = default!; {loop}{EmitDebugEmptySequenceCheckInline(hasValueName)}return {bestItemName};");
        return true;
    }

    // Spec §23. Integer `pow` is exact (never `Math.Pow`), `round` rounds half away from zero, and the two-argument
    // `floor`/`ceil` are floor and ceiling division.
    private bool TryEmitDirectMathIntrinsic(string name, IReadOnlyList<ArgumentSyntax> arguments, out string? emitted)
    {
        emitted = null;
        if (!TryGetPositionalExpressionArguments(arguments, out IReadOnlyList<ExpressionArgumentSyntax>? positional)
            || positional is null)
        {
            return false;
        }

        Expression[] values = positional.Select(argument => argument.Expression).ToArray();
        emitted = (name, values.Length) switch
        {
            ("abs", 1) => $"System.Math.Abs({EmitExpression(values[0])})",
            ("sqrt", 1) => $"System.Math.Sqrt({EmitExpression(values[0])})",
            ("floor", 1) => $"System.Math.Floor({EmitFloatingMathArgument(values[0])})",
            ("ceil", 1) => $"System.Math.Ceiling({EmitFloatingMathArgument(values[0])})",
            ("floor", 2) => EmitIntegralMathHelperCall("floorDiv", values),
            ("ceil", 2) => EmitIntegralMathHelperCall("ceilDiv", values),
            ("round", 1) => $"System.Math.Round({EmitFloatingMathArgument(values[0])}, MidpointRounding.AwayFromZero)",
            ("round", 2) => $"System.Math.Round({EmitFloatingMathArgument(values[0])}, {EmitExpression(values[1])}, MidpointRounding.AwayFromZero)",
            ("pow", 2) => EmitPowIntrinsic(values[0], values[1]),
            ("pow", 3) => $"__PscpSeq.powMod({EmitExpression(values[0])}, {EmitExpression(values[1])}, {EmitExpression(values[2])})",
            ("clamp", 3) => EmitClampIntrinsic(positional),
            ("gcd", 2) => EmitIntegralMathHelperCall("gcd", values),
            ("lcm", 2) => EmitIntegralMathHelperCall("lcm", values),
            ("popcount", 1) => EmitIntegralMathHelperCall("popcount", values),
            ("bitLength", 1) => EmitIntegralMathHelperCall("bitLength", values),
            _ => null,
        };
        return emitted is not null;
    }

    // `floor`, `ceil` and `round` of an integer operate on its `double` value.
    private string EmitFloatingMathArgument(Expression expression)
        => _semantic?.GetExpressionType(expression) is NamedTypeSyntax { Name: "int" or "long" }
            ? $"(double)({EmitExpression(expression)})"
            : EmitExpression(expression);

    private string EmitPowIntrinsic(Expression value, Expression exponent)
    {
        TypeSyntax? valueType = _semantic?.GetExpressionType(value);
        TypeSyntax? exponentType = _semantic?.GetExpressionType(exponent);
        bool integral = PscpIntrinsicCatalog.IsIntegralMathCompatible(valueType) && PscpIntrinsicCatalog.IsIntegralMathCompatible(exponentType);
        if (!integral)
        {
            return $"System.Math.Pow({EmitExpression(value)}, {EmitExpression(exponent)})";
        }

        TypeSyntax resultType = WidenAccumulator(valueType!);
        string valueText = EmitExpression(value);
        if (!Equals(resultType, valueType))
        {
            valueText = $"(long)({valueText})";
        }

        return $"__PscpSeq.pow({valueText}, {EmitExpression(exponent)})";
    }

    private string? EmitClampIntrinsic(IReadOnlyList<ExpressionArgumentSyntax> positional)
    {
        TypeSyntax? commonType = PromoteMathType(positional.Select(argument => argument.Expression));
        if (!PscpIntrinsicCatalog.IsMathMinMaxCompatible(commonType))
        {
            return null;
        }

        return $"System.Math.Clamp({EmitMathArgument(positional[0].Expression, commonType)}, {EmitMathArgument(positional[1].Expression, commonType)}, {EmitMathArgument(positional[2].Expression, commonType)})";
    }

    private string? EmitIntegralMathHelperCall(string helperName, params Expression[] expressions)
    {
        TypeSyntax? commonType = PromoteMathType(expressions);
        if (!PscpIntrinsicCatalog.IsIntegralMathCompatible(commonType))
        {
            return null;
        }

        return $"__PscpSeq.{helperName}({string.Join(", ", expressions.Select(expression => EmitMathArgument(expression, commonType)))})";
    }

    private string EmitMathArgument(Expression expression, TypeSyntax? targetType)
    {
        string emitted = EmitExpression(expression);
        return targetType is null || Equals(_semantic?.GetExpressionType(expression), targetType)
            ? emitted
            : $"({EmitType(targetType)})({emitted})";
    }

    private TypeSyntax? PromoteMathType(IEnumerable<Expression> expressions)
    {
        TypeSyntax? promoted = null;
        foreach (Expression expression in expressions)
        {
            TypeSyntax? current = _semantic?.GetExpressionType(expression);
            if (promoted is null)
            {
                promoted = current;
                continue;
            }

            promoted = PromoteMathType(promoted, current);
        }

        return promoted;
    }

    private static TypeSyntax? PromoteMathType(TypeSyntax? left, TypeSyntax? right)
    {
        if (Equals(left, right))
        {
            return left;
        }

        if (left is not NamedTypeSyntax leftNamed || right is not NamedTypeSyntax rightNamed)
        {
            return left ?? right;
        }

        static int Rank(string name) => name switch
        {
            "int" => 0,
            "long" => 1,
            "double" => 2,
            "decimal" => 3,
            _ => -1,
        };

        int leftRank = Rank(leftNamed.Name);
        int rightRank = Rank(rightNamed.Name);
        if (leftRank < 0 || rightRank < 0)
        {
            return left ?? right;
        }

        return leftRank >= rightRank ? left : right;
    }

    private string EmitMinMaxLoop(Expression source, TypeSyntax resultType, string bestName, string hasValueName, bool preferLower)
    {
        string itemName = NextTemporary("item");
        string comparison = EmitPreferredComparison(itemName, bestName, resultType, preferLower);
        return EmitLoopOverSource(source, itemName, $"if (!{hasValueName} || {comparison}) {{ {bestName} = {itemName}; {hasValueName} = true; }}");
    }

    private string EmitGeneratedMinMaxLoop(GeneratorExpression generator, TypeSyntax resultType, string bestName, string hasValueName, bool preferLower)
    {
        string itemName = ChooseBindingName(generator.ItemTarget, "item");
        string? indexName = generator.IndexTarget is null ? null : ChooseBindingName(generator.IndexTarget, "index");
        string valueName = NextTemporary("value");
        string valueExpression = EmitLambdaBodyExpression(generator.Body, generator.IndexTarget, indexName, generator.ItemTarget, itemName);
        string comparison = EmitPreferredComparison(valueName, bestName, resultType, preferLower);
        return EmitLoopOverSource(generator.Source, itemName, $"var {valueName} = {valueExpression}; if (!{hasValueName} || {comparison}) {{ {bestName} = {valueName}; {hasValueName} = true; }}", indexName);
    }

    private bool TryEmitDirectPredicateIntrinsic(CallExpression call, string name, Expression? receiver, IReadOnlyList<ArgumentSyntax> arguments, out string? emitted)
    {
        emitted = null;
        if (!TryGetSourceWithOptionalUnaryLambdaCall(receiver, arguments, out Expression? source, out LambdaExpression? predicate))
        {
            return false;
        }

        string itemName = predicate is null ? NextTemporary("item") : ChooseBindingName(predicate.Parameters[0].Target, "item");
        string predicateExpression = predicate is null ? "true" : EmitLambdaBodyExpression(predicate.Body, null, null, predicate.Parameters[0].Target, itemName);
        switch (name)
        {
            case "count":
            {
                string countName = NextTemporary("count");
                string loop = EmitLoopOverSource(source!, itemName, $"if ({predicateExpression}) {{ {countName}++; }}");
                emitted = EmitValueBlock($"int {countName} = 0; {loop} return {countName};");
                return true;
            }
            case "any":
            {
                string resultName = NextTemporary("any");
                string doneLabel = NextTemporary("done");
                string loop = EmitLoopOverSource(source!, itemName, $"if ({predicateExpression}) {{ {resultName} = true; goto {doneLabel}; }}");
                emitted = EmitValueBlock($"bool {resultName} = false; {loop} {doneLabel}: ; return {resultName};");
                return true;
            }
            case "all":
            {
                string resultName = NextTemporary("all");
                string doneLabel = NextTemporary("done");
                string loop = EmitLoopOverSource(source!, itemName, $"if (!({predicateExpression})) {{ {resultName} = false; goto {doneLabel}; }}");
                emitted = EmitValueBlock($"bool {resultName} = true; {loop} {doneLabel}: ; return {resultName};");
                return true;
            }
            case "find":
            {
                TypeSyntax? resultType = _semantic?.GetExpressionType(call);
                if (resultType is null)
                {
                    return false;
                }

                string resultName = NextTemporary("found");
                string doneLabel = NextTemporary("done");
                string loop = EmitLoopOverSource(source!, itemName, $"if ({predicateExpression}) {{ {resultName} = {itemName}; goto {doneLabel}; }}");
                emitted = EmitValueBlock($"{EmitType(resultType)} {resultName} = default; {loop} {doneLabel}: ; return {resultName};");
                return true;
            }
            case "findIndex":
            {
                string indexName = NextTemporary("index");
                string resultName = NextTemporary("found");
                string doneLabel = NextTemporary("done");
                string loop = EmitLoopOverSource(source!, itemName, $"if ({predicateExpression}) {{ {resultName} = {indexName}; goto {doneLabel}; }}", indexName);
                emitted = EmitValueBlock($"int {resultName} = -1; {loop} {doneLabel}: ; return {resultName};");
                return true;
            }
            case "findLastIndex":
            {
                string indexName = NextTemporary("index");
                string foundName = NextTemporary("found");
                string loop = EmitLoopOverSource(source!, itemName, $"if ({predicateExpression}) {{ {foundName} = {indexName}; }}", indexName);
                emitted = EmitValueBlock($"int {foundName} = -1; {loop} return {foundName};");
                return true;
            }
        }

        return false;
    }

    private bool TryEmitDirectCompareUpdateIntrinsic(string name, IReadOnlyList<ArgumentSyntax> arguments, out string? emitted)
    {
        emitted = null;
        if (arguments.Count != 2
            || arguments[0] is not ExpressionArgumentSyntax targetArgument
            || arguments[1] is not ExpressionArgumentSyntax valueArgument
            || targetArgument.Modifier != ArgumentModifier.Ref
            || valueArgument.Modifier != ArgumentModifier.None
            || !string.IsNullOrWhiteSpace(targetArgument.Name)
            || !string.IsNullOrWhiteSpace(valueArgument.Name))
        {
            return false;
        }

        TypeSyntax? targetType = _semantic?.GetExpressionType(targetArgument.Expression);
        if (targetType is null)
        {
            return false;
        }

        // Passing the target by ref evaluates its receiver/index exactly once and avoids a closure,
        // so this also works for ref parameters and inside struct members.
        string target = EmitExpression(targetArgument.Expression);
        emitted = $"__PscpSeq.{name}(ref {target}, {EmitExpression(valueArgument.Expression, targetType)})";
        return true;
    }

    private bool TryEmitFixedArityMinMaxIntrinsic(string name, IReadOnlyList<ArgumentSyntax> arguments, out string? emitted)
    {
        emitted = null;
        if (!TryGetPositionalExpressionArguments(arguments, out IReadOnlyList<ExpressionArgumentSyntax>? positional)
            || positional is null
            || positional.Count < 2)
        {
            return false;
        }

        TypeSyntax? commonType = positional
            .Select(argument => _semantic?.GetExpressionType(argument.Expression))
            .Aggregate<TypeSyntax?, TypeSyntax?>(null, (current, next) => current is null ? next : MergeTypes(current, next));

        if (commonType is null)
        {
            return false;
        }

        // Left to right, keeping the earlier value on ties; the runtime helper uses the default order (§25.1)
        // and evaluates each argument once.
        string current = EmitExpression(positional[0].Expression);
        for (int i = 1; i < positional.Count; i++)
        {
            string candidate = EmitExpression(positional[i].Expression);
            current = PscpIntrinsicCatalog.IsMathMinMaxCompatible(commonType)
                ? $"System.Math.{char.ToUpperInvariant(name[0]) + name[1..]}({current}, {candidate})"
                : $"__PscpSeq.{name}({current}, {candidate})";
        }

        emitted = current;
        return true;
    }

    private bool TryEmitDirectAggregationExpression(AggregationExpression aggregation, out string? emitted)
    {
        emitted = null;
        string itemName = ChooseBindingName(aggregation.ItemTarget, "item");
        string? indexName = aggregation.IndexTarget is null ? null : ChooseBindingName(aggregation.IndexTarget, "index");

        string? result = null;
        bool success = EmitWithBindingAliases(aggregation.IndexTarget, indexName, aggregation.ItemTarget, itemName, () =>
        {
            string aliases = EmitBindingAliasStatements(aggregation.IndexTarget, indexName, aggregation.ItemTarget, itemName);
            string prefix = string.IsNullOrWhiteSpace(aliases) ? string.Empty : aliases + " ";
            string wherePrefix = aggregation.WhereExpression is null ? string.Empty : $"if (!({EmitExpression(aggregation.WhereExpression)})) continue; ";

            switch (aggregation.AggregatorName)
            {
                case "sum":
                {
                    TypeSyntax? resultType = _semantic?.GetExpressionType(aggregation);
                    if (resultType is null)
                    {
                        return false;
                    }

                    string sumName = NextTemporary("sum");
                    string loop = EmitLoopOverSource(aggregation.Source, itemName, $"{prefix}{wherePrefix}{sumName} += {EmitExpression(aggregation.Body)};", indexName);
                    result = EmitValueBlock($"{EmitType(resultType)} {sumName} = default; {loop} return {sumName};");
                    return true;
                }
                case "count":
                {
                    string countName = NextTemporary("count");
                    string loop = EmitLoopOverSource(aggregation.Source, itemName, $"{prefix}{wherePrefix}if ({EmitExpression(aggregation.Body)}) {{ {countName}++; }}", indexName);
                    result = EmitValueBlock($"int {countName} = 0; {loop} return {countName};");
                    return true;
                }
                case "min":
                case "max":
                {
                    TypeSyntax? resultType = _semantic?.GetExpressionType(aggregation);
                    if (resultType is null)
                    {
                        return false;
                    }

                    string hasValueName = NextTemporary("hasValue");
                    string bestName = NextTemporary("best");
                    string valueName = NextTemporary("value");
                    string comparison = EmitPreferredComparison(valueName, bestName, resultType, preferLower: aggregation.AggregatorName == "min");
                    string loop = EmitLoopOverSource(aggregation.Source, itemName, $"{prefix}{wherePrefix}var {valueName} = {EmitExpression(aggregation.Body)}; if (!{hasValueName} || {comparison}) {{ {bestName} = {valueName}; {hasValueName} = true; }}", indexName);
                    result = EmitValueBlock($"bool {hasValueName} = false; {EmitType(resultType)} {bestName} = default!; {loop}{EmitDebugEmptySequenceCheckInline(hasValueName)}return {bestName};");
                    return true;
                }
            }

            return false;
        });
        emitted = result;
        return success;
    }

    private string EmitFallbackAggregationExpression(AggregationExpression aggregation)
    {
        string sequence = EmitEnumerable(aggregation.Source);
        string itemName = EmitBindingPattern(aggregation.ItemTarget);

        if (aggregation.IndexTarget is not null)
        {
            string itemTemp = NextTemporary("item");
            string indexTemp = NextTemporary("index");
            string indexName = EmitBindingPattern(aggregation.IndexTarget);
            sequence = $"System.Linq.Enumerable.Select({sequence}, ({itemTemp}, {indexTemp}) => ({indexTemp}, {itemTemp}))";

            if (aggregation.WhereExpression is not null)
            {
                sequence = $"System.Linq.Enumerable.Where({sequence}, __pair => __PscpThunk.run(() => {{ var {indexName} = __pair.Item1; var {itemName} = __pair.Item2; return {EmitExpression(aggregation.WhereExpression)}; }}))";
            }

            string bodySelector = $"__pair => __PscpThunk.run(() => {{ var {indexName} = __pair.Item1; var {itemName} = __pair.Item2; return {EmitExpression(aggregation.Body)}; }})";
            return EmitAggregationTerminal(aggregation.AggregatorName, sequence, bodySelector);
        }

        if (aggregation.WhereExpression is not null)
        {
            sequence = $"System.Linq.Enumerable.Where({sequence}, {itemName} => {EmitExpression(aggregation.WhereExpression)})";
        }

        string selector = $"{itemName} => {EmitExpression(aggregation.Body)}";
        return EmitAggregationTerminal(aggregation.AggregatorName, sequence, selector);
    }

    private static TypeSyntax? MergeTypes(TypeSyntax? left, TypeSyntax? right)
    {
        if (left is null)
        {
            return right;
        }

        if (right is null)
        {
            return left;
        }

        return Equals(left, right) ? left : null;
    }

    private static bool TryGetPositionalExpressionArguments(IReadOnlyList<ArgumentSyntax> arguments, out IReadOnlyList<ExpressionArgumentSyntax>? positional)
    {
        List<ExpressionArgumentSyntax> values = [];
        foreach (ArgumentSyntax argument in arguments)
        {
            if (argument is not ExpressionArgumentSyntax expressionArgument
                || expressionArgument.Modifier != ArgumentModifier.None
                || !string.IsNullOrWhiteSpace(expressionArgument.Name))
            {
                positional = null;
                return false;
            }

            values.Add(expressionArgument);
        }

        positional = values;
        return true;
    }

    private static bool TryGetSourceOnlyCall(Expression? receiver, IReadOnlyList<ArgumentSyntax> arguments, out Expression? source)
    {
        source = null;
        if (receiver is not null)
        {
            if (arguments.Count == 0)
            {
                source = receiver;
                return true;
            }

            return false;
        }

        return arguments.Count == 1
            && arguments[0] is ExpressionArgumentSyntax expressionArgument
            && expressionArgument.Modifier == ArgumentModifier.None
            && string.IsNullOrWhiteSpace(expressionArgument.Name)
            && (source = expressionArgument.Expression) is not null;
    }

    private static bool TryGetSourceAndUnaryLambdaCall(Expression? receiver, IReadOnlyList<ArgumentSyntax> arguments, out Expression? source, out LambdaExpression? lambda)
    {
        lambda = null;
        if (receiver is not null)
        {
            if (arguments.Count == 1
                && arguments[0] is ExpressionArgumentSyntax expressionArgument
                && expressionArgument.Modifier == ArgumentModifier.None
                && string.IsNullOrWhiteSpace(expressionArgument.Name)
                && expressionArgument.Expression is LambdaExpression lambdaExpression
                && lambdaExpression.Parameters.Count == 1
                && lambdaExpression.Parameters[0].Modifier == ArgumentModifier.None)
            {
                source = receiver;
                lambda = lambdaExpression;
                return true;
            }

            source = null;
            return false;
        }

        if (arguments.Count == 2
            && arguments[0] is ExpressionArgumentSyntax sourceArgument
            && arguments[1] is ExpressionArgumentSyntax lambdaArgument
            && sourceArgument.Modifier == ArgumentModifier.None
            && lambdaArgument.Modifier == ArgumentModifier.None
            && string.IsNullOrWhiteSpace(sourceArgument.Name)
            && string.IsNullOrWhiteSpace(lambdaArgument.Name)
            && lambdaArgument.Expression is LambdaExpression argLambda
            && argLambda.Parameters.Count == 1
            && argLambda.Parameters[0].Modifier == ArgumentModifier.None)
        {
            source = sourceArgument.Expression;
            lambda = argLambda;
            return true;
        }

        source = null;
        return false;
    }

    private static bool TryGetSourceWithOptionalUnaryLambdaCall(Expression? receiver, IReadOnlyList<ArgumentSyntax> arguments, out Expression? source, out LambdaExpression? lambda)
    {
        if (TryGetSourceOnlyCall(receiver, arguments, out source))
        {
            lambda = null;
            return true;
        }

        return TryGetSourceAndUnaryLambdaCall(receiver, arguments, out source, out lambda);
    }

    private string EmitInlineFastForCore(FastForStatement fastFor)
    {
        string itemName = ChooseBindingName(fastFor.ItemTarget, "item");
        string? indexName = fastFor.IndexTarget is null ? null : ChooseBindingName(fastFor.IndexTarget, "index");
        string aliases = EmitBindingAliasStatements(fastFor.IndexTarget, indexName, fastFor.ItemTarget, itemName);
        string prefix = string.IsNullOrWhiteSpace(aliases) ? string.Empty : aliases + " ";
        return EmitLoopOverSource(fastFor.Source, itemName, $"{prefix}{EmitInlineStatement(fastFor.Body)}", indexName);
    }
}



