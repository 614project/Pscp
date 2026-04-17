namespace Pscp.Transpiler;

internal sealed partial class CSharpEmitter
{

    private string EmitExpression(Expression expression, TypeSyntax? targetTypeHint = null)
        => expression switch
        {
            LiteralExpression literal => literal.RawText,
            InterpolatedStringExpression interpolated => EmitInterpolatedStringExpression(interpolated),
            IdentifierExpression identifier => identifier.Name,
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
            WithExpression @with => $"{EmitExpression(@with.Receiver)} with {@with.InitializerText}",
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
            _ => "default!"
        };

    private string EmitAssignmentTarget(Expression expression)
        => expression switch
        {
            TupleExpression tuple => $"({string.Join(", ", tuple.Elements.Select(element => EmitAssignmentTarget(element)))})",
            _ => EmitExpression(expression)
        };

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
                return $"System.Collections.Generic.Comparer<{EmitType(leftType)}>.Default.Compare({left}, {right})";
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
            BinaryOperator.LogicalAnd => "&&",
            BinaryOperator.LogicalOr => "||",
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

        return $"{EmitExpression(right)}({EmitExpression(left)})";
    }

    private string EmitPipeLeft(Expression left, Expression right)
    {
        if (left is CallExpression call)
        {
            return EmitCallLike(call.Callee, [.. call.Arguments, new ExpressionArgumentSyntax(null, ArgumentModifier.None, right)]);
        }

        return $"{EmitExpression(left)}({EmitExpression(right)})";
    }

    private string EmitIsPatternExpression(IsPatternExpression expression)
    {
        string pattern = expression.Pattern switch
        {
            TypePatternSyntax typePattern => EmitType(typePattern.Type),
            ConstantPatternSyntax constantPattern => EmitExpression(constantPattern.Expression),
            _ => "_"
        };

        return $"({EmitExpression(expression.Left)} is {(expression.Negated ? "not " : string.Empty)}{pattern})";
    }

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
            return $"Comparer<{comparableType}>.Create((__left, __right) => {comparableType}.CompareTo(__right, __left))";
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
                return $"new {elementType}[{EmitExpression(lengthArgument.Expression)}]";
            }

            return $"__PscpArray.zero<{elementType}>({string.Join(", ", call.Arguments.Select(argument => EmitExpressionArgument(argument)))})";
        }

        if (TryEmitDirectIntrinsicCall(call, out string? intrinsic))
        {
            return intrinsic!;
        }

        return EmitCallLike(call.Callee, call.Arguments);
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
        => $"{EmitExpression(callee)}({string.Join(", ", arguments.Select(EmitArgument))})";

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
            string comparer = $"Comparer<{typeText}>.Default";
            return member.MemberName == "asc"
                ? comparer
                : $"Comparer<{typeText}>.Create((__left, __right) => {comparer}.Compare(__right, __left))";
        }

        return $"{EmitMemberReceiverExpression(member.Receiver)}.{EmitMemberName(member.Receiver, member.MemberName)}";
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
        if (receiver is IdentifierExpression { Name: "stdin" })
        {
            string root = name.Split('<')[0];
            if (root is "int" or "long" or "double" or "decimal" or "bool" or "char")
            {
                return "@" + name;
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
            return $"new {EmitType(newArray.ElementType)}[{EmitExpression(newArray.Dimensions[0])}]";
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
                NamedTypeSyntax named when IsKnownAutoConstructType(named) => $"new {EmitType(named)}()",
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

    private string EmitRangeEnumerable(RangeExpression range, TypeSyntax? targetTypeHint)
    {
        bool useLong = IsLongRange(range, targetTypeHint);
        string helper = useLong ? "__PscpSeq.rangeLong" : "__PscpSeq.rangeInt";
        string inclusive = range.Kind == RangeKind.RightExclusive ? "false" : "true";

        return range.Step is null
            ? $"{helper}({EmitExpression(range.Start)}, {EmitExpression(range.End)}, {inclusive})"
            : $"{helper}({EmitExpression(range.Start)}, {EmitExpression(range.End)}, {EmitExpression(range.Step)}, {inclusive})";
    }

    private string EmitBuilderEnumerable(BuilderElement builder)
    {
        string source = EmitEnumerable(builder.Source);
        if (builder.IndexTarget is null)
        {
            string itemName = EmitBindingPattern(builder.ItemTarget);
            return builder.Body switch
            {
                LambdaExpressionBody expressionBody => $"System.Linq.Enumerable.Select({source}, {itemName} => {EmitExpression(expressionBody.Expression)})",
                LambdaBlockBody blockBody => $"System.Linq.Enumerable.Select({source}, {itemName} => __PscpSeq.expr(() => {{ {EmitInlineBlockWithBindings(blockBody.Block, (null, null), (builder.ItemTarget, itemName), isVoidLike: false)} }}))",
                _ => source
            };
        }

        string itemTemp = NextTemporary("item");
        string indexTemp = NextTemporary("index");
        string itemNameBound = EmitBindingPattern(builder.ItemTarget);
        string indexNameBound = EmitBindingPattern(builder.IndexTarget);
        return builder.Body switch
        {
            LambdaExpressionBody expressionBody => $"System.Linq.Enumerable.Select({source}, ({itemTemp}, {indexTemp}) => __PscpSeq.expr(() => {{ var {indexNameBound} = {indexTemp}; var {itemNameBound} = {itemTemp}; return {EmitExpression(expressionBody.Expression)}; }}))",
            LambdaBlockBody blockBody => $"System.Linq.Enumerable.Select({source}, ({itemTemp}, {indexTemp}) => __PscpSeq.expr(() => {{ var {indexNameBound} = {indexTemp}; var {itemNameBound} = {itemTemp}; {EmitInlineBlock(blockBody.Block, isVoidLike: false)} }}))",
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
            LambdaBlockBody blockBody => $"{parameters} => {{ {EmitInlineBlock(blockBody.Block, isVoidLike: false)} }}",
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
        List<string> parts = [];
        Expression? implicitReturn = GetImplicitReturnExpression(block);
        int regularCount = implicitReturn is null ? block.Statements.Count : block.Statements.Count - 1;

        for (int i = 0; i < regularCount; i++)
        {
            parts.Add(EmitInlineStatement(block.Statements[i]));
        }

        if (implicitReturn is not null)
        {
            parts.Add(isVoidLike
                ? $"{EmitExpression(implicitReturn)};"
                : $"return {EmitExpression(implicitReturn)};");
        }
        else if (!isVoidLike && !ContainsExplicitReturn(block))
        {
            parts.Add("return default!;");
        }

        return string.Join(" ", parts);
    }

    private string EmitInlineStatement(Statement statement)
        => statement switch
        {
            BlockStatement block => $"{{ {EmitInlineBlock(block, isVoidLike: true)} }}",
            DeclarationStatement declaration => EmitInlineDeclaration(declaration),
            ExpressionStatement expressionStatement => expressionStatement.Expression is AssignmentExpression assignmentExpression
                ? $"{EmitStatementAssignmentExpression(assignmentExpression)};"
                : $"{EmitExpression(expressionStatement.Expression)};",
            AssignmentStatement assignment => $"{EmitAssignmentTarget(assignment.Target)} {EmitAssignmentOperator(assignment.Operator)} {EmitExpression(assignment.Value)};",
            OutputStatement output => $"stdout.{(output.Kind == OutputKind.Write ? "write" : "writeln")}({EmitExpression(output.Expression)});",
            IfStatement ifStatement => $"if ({EmitExpression(ifStatement.Condition)}) {EmitInlineEmbeddedStatement(ifStatement.ThenBranch)}{(ifStatement.ElseBranch is null ? string.Empty : $" else {EmitInlineEmbeddedStatement(ifStatement.ElseBranch)}")}",
            WhileStatement whileStatement => $"while ({EmitExpression(whileStatement.Condition)}) {EmitInlineEmbeddedStatement(whileStatement.Body)}",
            ForInStatement forIn => $"foreach (var {EmitBindingPattern(forIn.Iterator)} in {EmitEnumerable(forIn.Source)}) {EmitInlineEmbeddedStatement(forIn.Body)}",
            CStyleForStatement cStyleFor => $"for ({cStyleFor.HeaderText}) {EmitInlineEmbeddedStatement(cStyleFor.Body)}",
            FastForStatement fastFor => EmitInlineFastFor(fastFor),
            ReturnStatement returnStatement => returnStatement.Expression is null ? "return;" : $"return {EmitExpression(returnStatement.Expression)};",
            BreakStatement => "break;",
            ContinueStatement => "continue;",
            LocalFunctionStatement localFunction => $"{EmitInlineFunction(localFunction.Function)}",
            _ => string.Empty
        };

    private string EmitInlineFunction(FunctionDeclaration function)
    {
        string parameters = string.Join(", ", function.Parameters.Select(EmitParameter));
        return $"{EmitType(function.ReturnType)} {function.Name}({parameters}) {{ {EmitInlineBlock(function.Body, isVoidLike: GetIsVoid(function.ReturnType))} }}";
    }

    private string EmitInlineEmbeddedStatement(Statement statement)
        => statement is BlockStatement block
            ? $"{{ {EmitInlineBlock(block, isVoidLike: true)} }}"
            : $"{{ {EmitInlineStatement(statement)} }}";

    private string EmitInlineDeclaration(DeclarationStatement declaration)
    {
        if (declaration.IsInputShorthand)
        {
            return "throw new InvalidOperationException(\"Input shorthand is not supported in expression blocks.\");";
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
            _ => "%="
        };

    private void EmitInputDeclaration(DeclarationStatement declaration)
    {
        if (declaration.ExplicitType is null)
        {
            _writer.WriteLine("// invalid input shorthand");
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

    private string EmitInputRead(TypeSyntax type)
    {
        return type switch
        {
            NamedTypeSyntax named => EmitScalarReader(named),
            TupleTypeSyntax tuple => tuple.Elements.Count switch
            {
                2 => $"stdin.tuple2<{string.Join(", ", tuple.Elements.Select(element => EmitType(element)))}>()",
                3 => $"stdin.tuple3<{string.Join(", ", tuple.Elements.Select(element => EmitType(element)))}>()",
                _ => "default!"
            },
            SizedArrayTypeSyntax sized => EmitSizedInputRead(sized),
            _ => "default!"
        };
    }

    private string EmitScalarReader(NamedTypeSyntax named)
    {
        return named.Name switch
        {
            "int" => "stdin.@int()",
            "long" => "stdin.@long()",
            "double" => "stdin.@double()",
            "decimal" => "stdin.@decimal()",
            "bool" => "stdin.@bool()",
            "char" => "stdin.@char()",
            "string" => "stdin.str()",
            _ => $"stdin.read<{EmitType(named)}>()"
        };
    }

    private string EmitSizedInputRead(SizedArrayTypeSyntax sized)
    {
        if (sized.ElementType is TupleTypeSyntax tuple && sized.Dimensions.Count == 1)
        {
            string genericArgs = string.Join(", ", tuple.Elements.Select(element => EmitType(element)));
            return tuple.Elements.Count switch
            {
                2 => $"stdin.tuples2<{genericArgs}>({EmitExpression(sized.Dimensions[0])})",
                3 => $"stdin.tuples3<{genericArgs}>({EmitExpression(sized.Dimensions[0])})",
                _ => "default!"
            };
        }

        if (sized.Dimensions.Count == 1)
        {
            return $"stdin.array<{EmitType(sized.ElementType)}>({EmitExpression(sized.Dimensions[0])})";
        }

        if (sized.Dimensions.Count == 2)
        {
            return $"stdin.nestedArray<{EmitType(sized.ElementType)}>({EmitExpression(sized.Dimensions[0])}, {EmitExpression(sized.Dimensions[1])})";
        }

        return "default!";
    }

    private void EmitSizedArrayDeclaration(string name, SizedArrayTypeSyntax sizedType)
    {
        string elementType = EmitType(sizedType.ElementType);
        string arrayType = EmitType(new ArrayTypeSyntax(sizedType.ElementType, sizedType.Dimensions.Count));

        if (sizedType.Dimensions.Count == 1)
        {
            _writer.WriteLine($"{arrayType} {name} = new {elementType}[{EmitExpression(sizedType.Dimensions[0])}];");
            return;
        }

        _writer.WriteLine($"{arrayType} {name} = new {elementType}[{EmitExpression(sizedType.Dimensions[0])}][];");
        string index = NextTemporary("i");
        _writer.WriteLine($"for (int {index} = 0; {index} < {EmitExpression(sizedType.Dimensions[0])}; {index}++)");
        _writer.WriteLine("{");
        _writer.Indent();
        _writer.WriteLine($"{name}[{index}] = new {elementType}[{EmitExpression(sizedType.Dimensions[1])}];");
        _writer.Unindent();
        _writer.WriteLine("}");
    }

    private string EmitSizedArrayCreationExpression(SizedArrayTypeSyntax sizedType)
    {
        if (sizedType.Dimensions.Count == 1)
        {
            return $"new {EmitType(sizedType.ElementType)}[{EmitExpression(sizedType.Dimensions[0])}]";
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
        => $"{EmitExpression(index.Receiver)}[{string.Join(", ", index.Arguments.Select(EmitIndexArgument))}]";

    private string EmitIndexArgument(Expression expression)
        => expression switch
        {
            FromEndExpression fromEnd => $"^{EmitExpression(fromEnd.Operand)}",
            SliceExpression slice => EmitSliceExpression(slice),
            _ => EmitExpression(expression)
        };

    private string EmitSliceExpression(SliceExpression slice)
    {
        string start = slice.Start is null ? string.Empty : EmitIndexArgument(slice.Start);
        string end = slice.End is null ? string.Empty : EmitIndexArgument(slice.End);
        return $"{start}..{end}";
    }

    private string EmitInterpolatedStringExpression(InterpolatedStringExpression interpolated)
    {
        if (interpolated.Parts.Count == 0)
        {
            return "\"\"";
        }

        System.Text.StringBuilder builder = new("$\"");
        foreach (InterpolatedStringPart part in interpolated.Parts)
        {
            switch (part)
            {
                case InterpolatedStringTextPart textPart:
                    builder.Append(EscapeCSharpInterpolatedText(textPart.Text));
                    break;
                case InterpolatedStringInterpolationPart interpolationPart:
                    builder.Append('{').Append(EmitExpression(interpolationPart.Expression)).Append('}');
                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
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
        TypeSyntax? sourceType = _semantic?.GetExpressionType(expressionArgument.Expression);
        emitted = name switch
        {
            "int" => EmitNumericConversion("int", "int.Parse", "Convert.ToInt32", operand, sourceType),
            "long" => EmitNumericConversion("long", "long.Parse", "Convert.ToInt64", operand, sourceType),
            "double" => EmitNumericConversion("double", "double.Parse", "Convert.ToDouble", operand, sourceType),
            "decimal" => EmitNumericConversion("decimal", "decimal.Parse", "Convert.ToDecimal", operand, sourceType),
            "bool" => EmitBooleanConversion(operand, sourceType),
            "char" => EmitCharConversion(operand, sourceType),
            "string" => $"(Convert.ToString({operand}, CultureInfo.InvariantCulture) ?? string.Empty)",
            _ => null,
        };
        return emitted is not null;
    }

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

        if (IsNumericType(sourceType) || IsCharType(sourceType))
        {
            return $"(({targetType})({operand}))";
        }

        return $"{convertMethod}({operand}, CultureInfo.InvariantCulture)";
    }

    private string EmitBooleanConversion(string operand, TypeSyntax? sourceType)
    {
        if (IsStringType(sourceType))
        {
            return $"(!string.IsNullOrEmpty({operand}))";
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
            return $"(string.IsNullOrEmpty({operand}) ? '\\0' : {operand}[0])";
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
            "Queue" or "System.Collections.Generic.Queue" when assignment.Operator == AssignmentOperator.AddAssign
                => $"{target}.Enqueue({value})",
            "Stack" or "System.Collections.Generic.Stack" when assignment.Operator == AssignmentOperator.AddAssign
                => $"{target}.Push({value})",
            "PriorityQueue" or "System.Collections.Generic.PriorityQueue" when assignment.Operator == AssignmentOperator.AddAssign
                => $"{target}.Enqueue(({value}).Item1, ({value}).Item2)",
            _ => null,
        };
        return emitted is not null;
    }

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
            return $"new {EmitType(arrayType.ElementType)}[{EmitExpression(dimensions[0])}]{trailingRanks}";
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
            or "PriorityQueue" or "System.Collections.Generic.PriorityQueue";

    private static TypeSyntax? GetCollectionElementType(TypeSyntax type)
        => type switch
        {
            ArrayTypeSyntax array => array.ElementType,
            SizedArrayTypeSyntax sized => sized.ElementType,
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

    private static bool IsLongRange(RangeExpression range, TypeSyntax? targetTypeHint)
    {
        if (GetCollectionElementType(targetTypeHint ?? new NamedTypeSyntax("object", Immutable.List<TypeSyntax>())) is NamedTypeSyntax { Name: "long" })
        {
            return true;
        }

        return IsLongExpression(range.Start) || IsLongExpression(range.End) || (range.Step is not null && IsLongExpression(range.Step));
    }

    private static bool IsLongExpression(Expression expression)
        => expression is LiteralExpression { RawText: var text } && text.EndsWith("L", StringComparison.OrdinalIgnoreCase);

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
        => $"__PscpSeq.expr(() => {{ {body} }})";

    private static string? TryGetPreferredBindingName(BindingTarget? target)
        => target switch
        {
            NameTarget nameTarget => nameTarget.Name,
            _ => null,
        };

    private string ChooseBindingName(BindingTarget? target, string prefix)
        => TryGetPreferredBindingName(target) ?? NextTemporary(prefix);

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
            NameTarget nameTarget => $"var {nameTarget.Name} = {sourceName};",
            TupleTarget tupleTarget => $"var {EmitBindingPattern(tupleTarget)} = {sourceName};",
            DiscardTarget => $"_ = {sourceName};",
            _ => string.Empty,
        };
    }

    private string EmitRangeStepExpression(RangeExpression range, string startName, string endName, bool useLong)
    {
        if (range.Step is not null)
        {
            return EmitExpression(range.Step);
        }

        string one = useLong ? "1L" : "1";
        return $"{startName} <= {endName} ? {one} : -{one}";
    }

    private string EmitLoopOverSource(Expression source, string itemName, string bodyStatements, string? indexName = null)
    {
        if (source is RangeExpression range)
        {
            bool useLong = IsLongRange(range, null);
            string numericType = useLong ? "long" : "int";
            string startName = NextTemporary("start");
            string endName = NextTemporary("end");
            string stepName = NextTemporary("step");
            string forwardOp = range.Kind == RangeKind.RightExclusive ? "<" : "<=";
            string backwardOp = range.Kind == RangeKind.RightExclusive ? ">" : ">=";
            string indexPrefix = indexName is null ? string.Empty : $"int {indexName} = 0; ";
            string update = indexName is null ? $"{itemName} += {stepName}" : $"{itemName} += {stepName}, {indexName}++";
            return $"{{ {numericType} {startName} = {EmitExpression(range.Start)}; {numericType} {endName} = {EmitExpression(range.End)}; {numericType} {stepName} = {EmitRangeStepExpression(range, startName, endName, useLong)}; if ({stepName} == 0) throw new InvalidOperationException(\"Range step cannot be zero.\"); {indexPrefix}for ({numericType} {itemName} = {startName}; {stepName} > 0 ? {itemName} {forwardOp} {endName} : {itemName} {backwardOp} {endName}; {update}) {{ {bodyStatements} }} }}";
        }

        if (indexName is null)
        {
            return $"foreach (var {itemName} in {EmitEnumerable(source)}) {{ {bodyStatements} }}";
        }

        return $"{{ int {indexName} = 0; foreach (var {itemName} in {EmitEnumerable(source)}) {{ {bodyStatements} {indexName}++; }} }}";
    }

    private string EmitAutoConstructArrayAllocation(TypeSyntax elementType, Expression lengthExpression)
    {
        string lengthName = NextTemporary("length");
        string resultName = NextTemporary("result");
        string indexName = NextTemporary("i");
        string elementTypeText = EmitType(elementType);
        return EmitEval($"int {lengthName} = {EmitExpression(lengthExpression)}; {elementTypeText}[] {resultName} = new {elementTypeText}[{lengthName}]; for (int {indexName} = 0; {indexName} < {lengthName}; {indexName}++) {{ {resultName}[{indexName}] = new {elementTypeText}(); }} return {resultName};");
    }

    private string EmitJaggedArrayAllocation(string elementType, IReadOnlyList<Expression> dimensions)
    {
        if (dimensions.Count == 2)
        {
            string outerLength = NextTemporary("outerLength");
            string innerLength = NextTemporary("innerLength");
            string resultName = NextTemporary("result");
            string indexName = NextTemporary("i");
            return EmitEval($"int {outerLength} = {EmitExpression(dimensions[0])}; int {innerLength} = {EmitExpression(dimensions[1])}; {elementType}[][] {resultName} = new {elementType}[{outerLength}][]; for (int {indexName} = 0; {indexName} < {outerLength}; {indexName}++) {{ {resultName}[{indexName}] = new {elementType}[{innerLength}]; }} return {resultName};");
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
        if (!IsArrayMaterializationTarget(targetTypeHint)
            || elementHint is null
            || IsLongRange(range, targetTypeHint))
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
            || builder.Source is not RangeExpression range
            || IsLongRange(range, targetTypeHint))
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
    {
        string resultName = NextTemporary("result");
        string slotName = NextTemporary("slot");
        string startName = NextTemporary("start");
        string endName = NextTemporary("end");
        string stepName = NextTemporary("step");
        string countName = NextTemporary("count");
        string absStepName = NextTemporary("absStep");
        string elementTypeText = EmitType(elementType);
        string forwardCount = range.Kind == RangeKind.RightExclusive
            ? $"{startName} < {endName} ? (({endName} - {startName} - 1) / {stepName}) + 1 : 0"
            : $"{startName} <= {endName} ? (({endName} - {startName}) / {stepName}) + 1 : 0";
        string backwardCount = range.Kind == RangeKind.RightExclusive
            ? $"{startName} > {endName} ? (({startName} - {endName} - 1) / {absStepName}) + 1 : 0"
            : $"{startName} >= {endName} ? (({startName} - {endName}) / {absStepName}) + 1 : 0";
        string forwardOp = range.Kind == RangeKind.RightExclusive ? "<" : "<=";
        string backwardOp = range.Kind == RangeKind.RightExclusive ? ">" : ">=";
        string indexInit = indexName is null ? string.Empty : $"int {indexName} = 0; ";
        string loopUpdate = indexName is null ? $"{itemName} += {stepName}" : $"{itemName} += {stepName}, {indexName}++";
        return EmitEval($"int {startName} = {EmitExpression(range.Start)}; int {endName} = {EmitExpression(range.End)}; int {stepName} = {EmitRangeStepExpression(range, startName, endName, useLong: false)}; if ({stepName} == 0) throw new InvalidOperationException(\"Range step cannot be zero.\"); int {absStepName} = {stepName} > 0 ? {stepName} : -{stepName}; int {countName} = {stepName} > 0 ? {forwardCount} : {backwardCount}; {elementTypeText}[] {resultName} = new {elementTypeText}[{countName}]; int {slotName} = 0; {indexInit}for (int {itemName} = {startName}; {stepName} > 0 ? {itemName} {forwardOp} {endName} : {itemName} {backwardOp} {endName}; {loopUpdate}) {{ {resultName}[{slotName}++] = {valueExpression}; }} return {resultName};");
    }

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
        return EmitEval(string.Join(" ", parts));
    }

    private static bool IsArrayMaterializationTarget(TypeSyntax? targetTypeHint)
        => targetTypeHint is null or ArrayTypeSyntax;

    private string EmitLambdaBodyExpression(LambdaBody body, BindingTarget? indexTarget, string? indexName, BindingTarget itemTarget, string itemName)
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
    }

    private bool TryEmitDirectIntrinsicCall(CallExpression call, out string? emitted)
    {
        emitted = null;
        if (call.Callee is IdentifierExpression identifier)
        {
            if (TryEmitConversionIntrinsic(call, identifier.Name, out emitted))
            {
                return true;
            }

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

        string sumType = EmitType(resultType);
        string sumName = NextTemporary("sum");
        string loop;
        if (source is GeneratorExpression generator)
        {
            string itemName = ChooseBindingName(generator.ItemTarget, "item");
            string? indexName = generator.IndexTarget is null ? null : ChooseBindingName(generator.IndexTarget, "index");
            string valueExpression = EmitLambdaBodyExpression(generator.Body, generator.IndexTarget, indexName, generator.ItemTarget, itemName);
            loop = EmitLoopOverSource(generator.Source, itemName, $"{sumName} += {valueExpression};", indexName);
        }
        else
        {
            string itemName = NextTemporary("item");
            loop = EmitLoopOverSource(source!, itemName, $"{sumName} += {itemName};");
        }
        emitted = EmitEval($"{sumType} {sumName} = default; {loop} return {sumName};");
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

        string sumType = EmitType(resultType);
        string sumName = NextTemporary("sum");
        string itemName = ChooseBindingName(selector!.Parameters[0].Target, "item");
        string selectorExpression = EmitLambdaBodyExpression(selector.Body, null, null, selector.Parameters[0].Target, itemName);
        string loop = EmitLoopOverSource(source!, itemName, $"{sumName} += {selectorExpression};");
        emitted = EmitEval($"{sumType} {sumName} = default; {loop} return {sumName};");
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
        emitted = EmitEval($"bool {hasValueName} = false; {EmitType(resultType)} {bestName} = default!; {loop} return {hasValueName} ? {bestName} : default!;");
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
        emitted = EmitEval($"bool {hasValueName} = false; {EmitType(resultType)} {bestItemName} = default!; {EmitType(keyType)} {bestKeyName} = default!; {loop} return {hasValueName} ? {bestItemName} : default!;");
        return true;
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
                emitted = EmitEval($"int {countName} = 0; {loop} return {countName};");
                return true;
            }
            case "any":
            {
                string loop = EmitLoopOverSource(source!, itemName, $"if ({predicateExpression}) return true;");
                emitted = EmitEval($"{loop} return false;");
                return true;
            }
            case "all":
            {
                string loop = EmitLoopOverSource(source!, itemName, $"if (!({predicateExpression})) return false;");
                emitted = EmitEval($"{loop} return true;");
                return true;
            }
            case "find":
            {
                TypeSyntax? resultType = _semantic?.GetExpressionType(call);
                if (resultType is null)
                {
                    return false;
                }

                string loop = EmitLoopOverSource(source!, itemName, $"if ({predicateExpression}) return {itemName};");
                emitted = EmitEval($"{loop} return default({EmitType(resultType)});");
                return true;
            }
            case "findIndex":
            {
                string indexName = NextTemporary("index");
                string loop = EmitLoopOverSource(source!, itemName, $"if ({predicateExpression}) return {indexName};", indexName);
                emitted = EmitEval($"{loop} return -1;");
                return true;
            }
            case "findLastIndex":
            {
                string indexName = NextTemporary("index");
                string foundName = NextTemporary("found");
                string loop = EmitLoopOverSource(source!, itemName, $"if ({predicateExpression}) {{ {foundName} = {indexName}; }}", indexName);
                emitted = EmitEval($"int {foundName} = -1; {loop} return {foundName};");
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

        string target = EmitExpression(targetArgument.Expression);
        string valueName = NextTemporary("value");
        string compare = EmitPreferredComparison(valueName, target, targetType, preferLower: name == "chmin");
        emitted = EmitEval($"var {valueName} = {EmitExpression(valueArgument.Expression, targetType)}; if ({compare}) {{ {target} = {valueName}; return true; }} return false;");
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

        string current = EmitExpression(positional[0].Expression);
        for (int i = 1; i < positional.Count; i++)
        {
            string candidate = EmitExpression(positional[i].Expression);
            if (PscpIntrinsicCatalog.IsMathMinMaxCompatible(commonType))
            {
                current = $"System.Math.{char.ToUpperInvariant(name[0]) + name[1..]}({current}, {candidate})";
            }
            else
            {
                string compare = EmitPreferredComparison(current, candidate, commonType, preferLower: name == "min");
                current = name == "min"
                    ? $"({compare} ? {current} : {candidate})"
                    : $"({compare} ? {current} : {candidate})";
            }
        }

        emitted = current;
        return true;
    }

    private bool TryEmitDirectAggregationExpression(AggregationExpression aggregation, out string? emitted)
    {
        emitted = null;
        string itemName = ChooseBindingName(aggregation.ItemTarget, "item");
        string? indexName = aggregation.IndexTarget is null ? null : ChooseBindingName(aggregation.IndexTarget, "index");
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
                emitted = EmitEval($"{EmitType(resultType)} {sumName} = default; {loop} return {sumName};");
                return true;
            }
            case "count":
            {
                string countName = NextTemporary("count");
                string loop = EmitLoopOverSource(aggregation.Source, itemName, $"{prefix}{wherePrefix}if ({EmitExpression(aggregation.Body)}) {{ {countName}++; }}", indexName);
                emitted = EmitEval($"int {countName} = 0; {loop} return {countName};");
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
                emitted = EmitEval($"bool {hasValueName} = false; {EmitType(resultType)} {bestName} = default!; {loop} return {hasValueName} ? {bestName} : default!;");
                return true;
            }
        }

        return false;
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
                sequence = $"System.Linq.Enumerable.Where({sequence}, __pair => __PscpSeq.expr(() => {{ var {indexName} = __pair.Item1; var {itemName} = __pair.Item2; return {EmitExpression(aggregation.WhereExpression)}; }}))";
            }

            string bodySelector = $"__pair => __PscpSeq.expr(() => {{ var {indexName} = __pair.Item1; var {itemName} = __pair.Item2; return {EmitExpression(aggregation.Body)}; }})";
            return EmitAggregationTerminal(aggregation.AggregatorName, sequence, bodySelector);
        }

        if (aggregation.WhereExpression is not null)
        {
            sequence = $"System.Linq.Enumerable.Where({sequence}, {itemName} => {EmitExpression(aggregation.WhereExpression)})";
        }

        string selector = $"{itemName} => {EmitExpression(aggregation.Body)}";
        return EmitAggregationTerminal(aggregation.AggregatorName, sequence, selector);
    }

    private string EmitPreferredComparison(string left, string right, TypeSyntax type, bool preferLower)
    {
        string op = preferLower ? "<" : ">";
        if (PscpIntrinsicCatalog.IsMathMinMaxCompatible(type)
            || type is NamedTypeSyntax { Name: "char" })
        {
            return $"{left} {op} {right}";
        }

        return $"System.Collections.Generic.Comparer<{EmitType(type)}>.Default.Compare({left}, {right}) {op} 0";
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



