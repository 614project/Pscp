namespace Pscp.Transpiler;

// Rebuilds a syntax tree bottom-up. Every Visit method returns the node it was given when nothing below it
// changed, so a rewriter that changes nothing allocates nothing and doubles as a walker. Rebuilt nodes keep the
// source span of the node they replace.
internal abstract class SyntaxRewriter
{
    protected SyntaxRewriter(SyntaxSpans spans)
    {
        Spans = spans;
    }

    protected SyntaxSpans Spans { get; }

    protected T Replace<T>(T original, T rewritten)
        where T : class
    {
        if (!ReferenceEquals(original, rewritten))
        {
            Spans.Copy(original, rewritten);
        }

        return rewritten;
    }

    protected static IReadOnlyList<T> VisitList<T>(IReadOnlyList<T> items, Func<T, T> visit)
        where T : class
    {
        T[]? rewritten = null;
        for (int i = 0; i < items.Count; i++)
        {
            T item = visit(items[i]);
            if (rewritten is null && !ReferenceEquals(item, items[i]))
            {
                rewritten = new T[items.Count];
                for (int j = 0; j < i; j++)
                {
                    rewritten[j] = items[j];
                }
            }

            if (rewritten is not null)
            {
                rewritten[i] = item;
            }
        }

        return rewritten is null ? items : Array.AsReadOnly(rewritten);
    }

    protected static bool Same<T>(T? left, T? right)
        where T : class
        => ReferenceEquals(left, right);

    public virtual PscpProgram VisitProgram(PscpProgram program)
    {
        IReadOnlyList<TypeDeclaration> types = VisitList(program.Types, VisitTypeDeclaration);
        IReadOnlyList<FunctionDeclaration> functions = VisitList(program.Functions, VisitFunction);
        IReadOnlyList<Statement> statements = VisitStatements(program.GlobalStatements);
        return Same(types, program.Types) && Same(functions, program.Functions) && Same(statements, program.GlobalStatements)
            ? program
            : Replace(program, program with { Types = types, Functions = functions, GlobalStatements = statements });
    }

    public virtual TypeDeclaration VisitTypeDeclaration(TypeDeclaration declaration)
    {
        IReadOnlyList<TypeMember> members = VisitList(declaration.Members, VisitTypeMember);
        return Same(members, declaration.Members) ? declaration : Replace(declaration, declaration with { Members = members });
    }

    public virtual TypeMember VisitTypeMember(TypeMember member)
    {
        switch (member)
        {
            case OrderingShorthandMember ordering:
            {
                MethodBody body = VisitMethodBody(ordering.Body);
                return Same(body, ordering.Body) ? member : Replace(member, ordering with { Body = body });
            }
            case FieldMember field:
            {
                Statement declaration = VisitDeclarationStatement(field.Declaration);
                return declaration is DeclarationStatement rewritten && !Same(rewritten, field.Declaration)
                    ? Replace(member, field with { Declaration = rewritten })
                    : member;
            }
            case PropertyMember property:
            {
                MethodBody body = VisitMethodBody(property.Body);
                return Same(body, property.Body) ? member : Replace(member, property with { Body = body });
            }
            case MethodMember method:
            {
                IReadOnlyList<ParameterSyntax> parameters = VisitList(method.Parameters, VisitParameter);
                MethodBody body = VisitMethodBody(method.Body);
                return Same(parameters, method.Parameters) && Same(body, method.Body)
                    ? member
                    : Replace(member, method with { Parameters = parameters, Body = body });
            }
            case OperatorMember @operator:
            {
                IReadOnlyList<ParameterSyntax> parameters = VisitList(@operator.Parameters, VisitParameter);
                MethodBody body = VisitMethodBody(@operator.Body);
                return Same(parameters, @operator.Parameters) && Same(body, @operator.Body)
                    ? member
                    : Replace(member, @operator with { Parameters = parameters, Body = body });
            }
            case NestedTypeMember nested:
            {
                TypeDeclaration declaration = VisitTypeDeclaration(nested.Declaration);
                return Same(declaration, nested.Declaration) ? member : Replace(member, nested with { Declaration = declaration });
            }
            default:
                return member;
        }
    }

    public virtual MethodBody VisitMethodBody(MethodBody body)
    {
        switch (body)
        {
            case BlockMethodBody block:
            {
                BlockStatement rewritten = VisitBlock(block.Block);
                return Same(rewritten, block.Block) ? body : Replace(body, block with { Block = rewritten });
            }
            case ExpressionMethodBody expression:
            {
                Expression rewritten = VisitExpression(expression.Expression);
                return Same(rewritten, expression.Expression) ? body : Replace(body, expression with { Expression = rewritten });
            }
            default:
                return body;
        }
    }

    public virtual FunctionDeclaration VisitFunction(FunctionDeclaration function)
    {
        IReadOnlyList<ParameterSyntax> parameters = VisitList(function.Parameters, VisitParameter);
        BlockStatement body = VisitBlock(function.Body);
        return Same(parameters, function.Parameters) && Same(body, function.Body)
            ? function
            : Replace(function, function with { Parameters = parameters, Body = body });
    }

    public virtual ParameterSyntax VisitParameter(ParameterSyntax parameter)
        => parameter;

    public virtual IReadOnlyList<Statement> VisitStatements(IReadOnlyList<Statement> statements)
        => VisitList(statements, VisitStatement);

    public virtual Statement VisitStatement(Statement statement)
        => statement switch
        {
            BlockStatement block => VisitBlock(block),
            DeclarationStatement declaration => VisitDeclarationStatement(declaration),
            ExpressionStatement expression => VisitExpressionStatement(expression),
            AssignmentStatement assignment => VisitAssignmentStatement(assignment),
            OutputStatement output => VisitOutputStatement(output),
            IfStatement ifStatement => VisitIfStatement(ifStatement),
            WhileStatement whileStatement => VisitWhileStatement(whileStatement),
            ForInStatement forIn => VisitForInStatement(forIn),
            CStyleForStatement cStyleFor => VisitCStyleForStatement(cStyleFor),
            FastForStatement fastFor => VisitFastForStatement(fastFor),
            ReturnStatement returnStatement => VisitReturnStatement(returnStatement),
            LocalFunctionStatement localFunction => VisitLocalFunctionStatement(localFunction),
            TryStatement tryStatement => VisitTryStatement(tryStatement),
            ThrowStatement throwStatement => VisitThrowStatement(throwStatement),
            _ => statement,
        };

    public virtual BlockStatement VisitBlock(BlockStatement block)
    {
        IReadOnlyList<Statement> statements = VisitStatements(block.Statements);
        return Same(statements, block.Statements) ? block : Replace(block, block with { Statements = statements });
    }

    public virtual Statement VisitDeclarationStatement(DeclarationStatement declaration)
    {
        TypeSyntax? type = declaration.ExplicitType is null ? null : VisitType(declaration.ExplicitType);
        Expression? initializer = declaration.Initializer is null ? null : VisitExpression(declaration.Initializer);
        return Same(type, declaration.ExplicitType) && Same(initializer, declaration.Initializer)
            ? declaration
            : Replace(declaration, declaration with { ExplicitType = type, Initializer = initializer });
    }

    // Only sized array types contain expressions (`int[n] a`).
    public virtual TypeSyntax VisitType(TypeSyntax type)
    {
        if (type is not SizedArrayTypeSyntax sized)
        {
            return type;
        }

        IReadOnlyList<Expression> dimensions = VisitList(sized.Dimensions, VisitExpression);
        return Same(dimensions, sized.Dimensions) ? type : Replace(type, sized with { Dimensions = dimensions });
    }

    public virtual Statement VisitExpressionStatement(ExpressionStatement statement)
    {
        Expression expression = VisitExpression(statement.Expression);
        return Same(expression, statement.Expression) ? statement : Replace(statement, statement with { Expression = expression });
    }

    public virtual Statement VisitAssignmentStatement(AssignmentStatement statement)
    {
        Expression target = VisitExpression(statement.Target);
        Expression value = VisitExpression(statement.Value);
        return Same(target, statement.Target) && Same(value, statement.Value)
            ? statement
            : Replace(statement, statement with { Target = target, Value = value });
    }

    public virtual Statement VisitOutputStatement(OutputStatement statement)
    {
        Expression expression = VisitExpression(statement.Expression);
        return Same(expression, statement.Expression) ? statement : Replace(statement, statement with { Expression = expression });
    }

    public virtual Statement VisitIfStatement(IfStatement statement)
    {
        Expression condition = VisitExpression(statement.Condition);
        Statement thenBranch = VisitEmbeddedStatement(statement.ThenBranch);
        Statement? elseBranch = statement.ElseBranch is null ? null : VisitEmbeddedStatement(statement.ElseBranch);
        return Same(condition, statement.Condition) && Same(thenBranch, statement.ThenBranch) && Same(elseBranch, statement.ElseBranch)
            ? statement
            : Replace(statement, statement with { Condition = condition, ThenBranch = thenBranch, ElseBranch = elseBranch });
    }

    // The body of `if`, `while`, `for` and `->`.
    public virtual Statement VisitEmbeddedStatement(Statement statement)
        => VisitStatement(statement);

    public virtual Statement VisitWhileStatement(WhileStatement statement)
    {
        Expression condition = VisitExpression(statement.Condition);
        Statement body = VisitEmbeddedStatement(statement.Body);
        return Same(condition, statement.Condition) && Same(body, statement.Body)
            ? statement
            : Replace(statement, statement with { Condition = condition, Body = body });
    }

    public virtual Statement VisitForInStatement(ForInStatement statement)
    {
        Expression source = VisitExpression(statement.Source);
        Statement body = VisitEmbeddedStatement(statement.Body);
        return Same(source, statement.Source) && Same(body, statement.Body)
            ? statement
            : Replace(statement, statement with { Source = source, Body = body });
    }

    public virtual Statement VisitCStyleForStatement(CStyleForStatement statement)
    {
        Statement body = VisitEmbeddedStatement(statement.Body);
        return Same(body, statement.Body) ? statement : Replace(statement, statement with { Body = body });
    }

    public virtual Statement VisitFastForStatement(FastForStatement statement)
    {
        Expression source = VisitExpression(statement.Source);
        Statement body = VisitEmbeddedStatement(statement.Body);
        return Same(source, statement.Source) && Same(body, statement.Body)
            ? statement
            : Replace(statement, statement with { Source = source, Body = body });
    }

    public virtual Statement VisitReturnStatement(ReturnStatement statement)
    {
        if (statement.Expression is null)
        {
            return statement;
        }

        Expression expression = VisitExpression(statement.Expression);
        return Same(expression, statement.Expression) ? statement : Replace(statement, statement with { Expression = expression });
    }

    public virtual Statement VisitLocalFunctionStatement(LocalFunctionStatement statement)
    {
        FunctionDeclaration function = VisitFunction(statement.Function);
        return Same(function, statement.Function) ? statement : Replace(statement, statement with { Function = function });
    }

    public virtual Statement VisitTryStatement(TryStatement statement)
    {
        BlockStatement body = VisitBlock(statement.Body);
        IReadOnlyList<CatchClause> catches = VisitList(statement.Catches, VisitCatchClause);
        BlockStatement? finallyBlock = statement.Finally is null ? null : VisitBlock(statement.Finally);
        return Same(body, statement.Body) && Same(catches, statement.Catches) && Same(finallyBlock, statement.Finally)
            ? statement
            : Replace(statement, statement with { Body = body, Catches = catches, Finally = finallyBlock });
    }

    public virtual CatchClause VisitCatchClause(CatchClause clause)
    {
        Expression? filter = clause.Filter is null ? null : VisitExpression(clause.Filter);
        BlockStatement body = VisitBlock(clause.Body);
        return Same(filter, clause.Filter) && Same(body, clause.Body)
            ? clause
            : Replace(clause, clause with { Filter = filter, Body = body });
    }

    public virtual Statement VisitThrowStatement(ThrowStatement statement)
    {
        if (statement.Expression is null)
        {
            return statement;
        }

        Expression expression = VisitExpression(statement.Expression);
        return Same(expression, statement.Expression) ? statement : Replace(statement, statement with { Expression = expression });
    }

    public virtual Expression VisitExpression(Expression expression)
        => expression switch
        {
            LiteralExpression or DiscardExpression => expression,
            IdentifierExpression identifier => VisitIdentifier(identifier),
            InterpolatedStringExpression interpolated => VisitInterpolatedString(interpolated),
            TupleExpression tuple => VisitTuple(tuple),
            BlockExpression block => VisitBlockExpression(block),
            IfExpression ifExpression => VisitIfExpression(ifExpression),
            ConditionalExpression conditional => VisitConditional(conditional),
            UnaryExpression unary => VisitUnary(unary),
            AssignmentExpression assignment => VisitAssignment(assignment),
            PrefixExpression prefix => VisitPrefix(prefix),
            PostfixExpression postfix => VisitPostfix(postfix),
            BinaryExpression binary => VisitBinary(binary),
            RangeExpression range => VisitRange(range),
            IsPatternExpression isPattern => VisitIsPattern(isPattern),
            CastExpression cast => VisitCast(cast),
            AsExpression asExpression => VisitAs(asExpression),
            NullForgivingExpression nullForgiving => VisitNullForgiving(nullForgiving),
            CallExpression call => VisitCall(call),
            MemberAccessExpression member => VisitMemberAccess(member),
            IndexExpression index => VisitIndex(index),
            WithExpression with => VisitWith(with),
            SwitchExpression @switch => VisitSwitch(@switch),
            FromEndExpression fromEnd => VisitFromEnd(fromEnd),
            SliceExpression slice => VisitSlice(slice),
            TupleProjectionExpression projection => VisitTupleProjection(projection),
            LambdaExpression lambda => VisitLambda(lambda),
            NewExpression creation => VisitNew(creation),
            NewArrayExpression newArray => VisitNewArray(newArray),
            TargetTypedNewArrayExpression targetTyped => VisitTargetTypedNewArray(targetTyped),
            CollectionExpression collection => VisitCollection(collection),
            AggregationExpression aggregation => VisitAggregation(aggregation),
            GeneratorExpression generator => VisitGenerator(generator),
            ThrowExpression throwExpression => VisitThrowExpression(throwExpression),
            _ => expression,
        };

    public virtual Expression VisitIdentifier(IdentifierExpression identifier)
        => identifier;

    public virtual Expression VisitInterpolatedString(InterpolatedStringExpression interpolated)
    {
        IReadOnlyList<InterpolatedStringPart> parts = VisitList(interpolated.Parts, part => part switch
        {
            InterpolatedStringInterpolationPart hole when VisitExpression(hole.Expression) is var rewritten && !Same(rewritten, hole.Expression)
                => hole with { Expression = rewritten },
            _ => part,
        });
        return Same(parts, interpolated.Parts) ? interpolated : Replace(interpolated, interpolated with { Parts = parts });
    }

    public virtual Expression VisitTuple(TupleExpression tuple)
    {
        IReadOnlyList<Expression> elements = VisitList(tuple.Elements, VisitExpression);
        return Same(elements, tuple.Elements) ? tuple : Replace(tuple, tuple with { Elements = elements });
    }

    public virtual Expression VisitBlockExpression(BlockExpression block)
    {
        BlockStatement rewritten = VisitBlock(block.Block);
        return Same(rewritten, block.Block) ? block : Replace(block, block with { Block = rewritten });
    }

    public virtual Expression VisitIfExpression(IfExpression expression)
    {
        Expression condition = VisitExpression(expression.Condition);
        Expression thenExpression = VisitExpression(expression.ThenExpression);
        Expression elseExpression = VisitExpression(expression.ElseExpression);
        return Same(condition, expression.Condition) && Same(thenExpression, expression.ThenExpression) && Same(elseExpression, expression.ElseExpression)
            ? expression
            : Replace(expression, expression with { Condition = condition, ThenExpression = thenExpression, ElseExpression = elseExpression });
    }

    public virtual Expression VisitConditional(ConditionalExpression expression)
    {
        Expression condition = VisitExpression(expression.Condition);
        Expression whenTrue = VisitExpression(expression.WhenTrue);
        Expression whenFalse = VisitExpression(expression.WhenFalse);
        return Same(condition, expression.Condition) && Same(whenTrue, expression.WhenTrue) && Same(whenFalse, expression.WhenFalse)
            ? expression
            : Replace(expression, expression with { Condition = condition, WhenTrue = whenTrue, WhenFalse = whenFalse });
    }

    public virtual Expression VisitUnary(UnaryExpression unary)
    {
        Expression operand = VisitExpression(unary.Operand);
        return Same(operand, unary.Operand) ? unary : Replace(unary, unary with { Operand = operand });
    }

    public virtual Expression VisitAssignment(AssignmentExpression assignment)
    {
        Expression target = VisitExpression(assignment.Target);
        Expression value = VisitExpression(assignment.Value);
        return Same(target, assignment.Target) && Same(value, assignment.Value)
            ? assignment
            : Replace(assignment, assignment with { Target = target, Value = value });
    }

    public virtual Expression VisitPrefix(PrefixExpression prefix)
    {
        Expression operand = VisitExpression(prefix.Operand);
        return Same(operand, prefix.Operand) ? prefix : Replace(prefix, prefix with { Operand = operand });
    }

    public virtual Expression VisitPostfix(PostfixExpression postfix)
    {
        Expression operand = VisitExpression(postfix.Operand);
        return Same(operand, postfix.Operand) ? postfix : Replace(postfix, postfix with { Operand = operand });
    }

    public virtual Expression VisitBinary(BinaryExpression binary)
    {
        Expression left = VisitExpression(binary.Left);
        Expression right = VisitExpression(binary.Right);
        return Same(left, binary.Left) && Same(right, binary.Right)
            ? binary
            : Replace(binary, binary with { Left = left, Right = right });
    }

    public virtual Expression VisitRange(RangeExpression range)
    {
        Expression start = VisitExpression(range.Start);
        Expression? step = range.Step is null ? null : VisitExpression(range.Step);
        Expression end = VisitExpression(range.End);
        return Same(start, range.Start) && Same(step, range.Step) && Same(end, range.End)
            ? range
            : Replace(range, range with { Start = start, Step = step, End = end });
    }

    public virtual Expression VisitIsPattern(IsPatternExpression expression)
    {
        Expression left = VisitExpression(expression.Left);
        return Same(left, expression.Left) ? expression : Replace(expression, expression with { Left = left });
    }

    public virtual Expression VisitCast(CastExpression cast)
    {
        Expression operand = VisitExpression(cast.Operand);
        return Same(operand, cast.Operand) ? cast : Replace(cast, cast with { Operand = operand });
    }

    public virtual Expression VisitAs(AsExpression expression)
    {
        Expression operand = VisitExpression(expression.Operand);
        return Same(operand, expression.Operand) ? expression : Replace(expression, expression with { Operand = operand });
    }

    public virtual Expression VisitNullForgiving(NullForgivingExpression expression)
    {
        Expression operand = VisitExpression(expression.Operand);
        return Same(operand, expression.Operand) ? expression : Replace(expression, expression with { Operand = operand });
    }

    public virtual Expression VisitCall(CallExpression call)
    {
        Expression callee = VisitExpression(call.Callee);
        IReadOnlyList<ArgumentSyntax> arguments = VisitList(call.Arguments, VisitArgument);
        return Same(callee, call.Callee) && Same(arguments, call.Arguments)
            ? call
            : Replace(call, call with { Callee = callee, Arguments = arguments });
    }

    public virtual ArgumentSyntax VisitArgument(ArgumentSyntax argument)
    {
        switch (argument)
        {
            case ExpressionArgumentSyntax expressionArgument:
            {
                Expression expression = VisitExpression(expressionArgument.Expression);
                return Same(expression, expressionArgument.Expression)
                    ? argument
                    : Replace(argument, expressionArgument with { Expression = expression });
            }
            default:
                return argument;
        }
    }

    public virtual Expression VisitMemberAccess(MemberAccessExpression member)
    {
        Expression receiver = VisitExpression(member.Receiver);
        return Same(receiver, member.Receiver) ? member : Replace(member, member with { Receiver = receiver });
    }

    public virtual Expression VisitIndex(IndexExpression index)
    {
        Expression receiver = VisitExpression(index.Receiver);
        IReadOnlyList<Expression> arguments = VisitList(index.Arguments, VisitExpression);
        return Same(receiver, index.Receiver) && Same(arguments, index.Arguments)
            ? index
            : Replace(index, index with { Receiver = receiver, Arguments = arguments });
    }

    public virtual Expression VisitWith(WithExpression with)
    {
        Expression receiver = VisitExpression(with.Receiver);
        IReadOnlyList<WithAssignment> assignments = VisitList(with.Assignments, VisitWithAssignment);
        return Same(receiver, with.Receiver) && Same(assignments, with.Assignments)
            ? with
            : Replace(with, with with { Receiver = receiver, Assignments = assignments });
    }

    public virtual WithAssignment VisitWithAssignment(WithAssignment assignment)
    {
        Expression value = VisitExpression(assignment.Value);
        return Same(value, assignment.Value) ? assignment : Replace(assignment, assignment with { Value = value });
    }

    public virtual Expression VisitSwitch(SwitchExpression @switch)
    {
        Expression receiver = VisitExpression(@switch.Receiver);
        IReadOnlyList<SwitchArm> arms = VisitList(@switch.Arms, VisitSwitchArm);
        return Same(receiver, @switch.Receiver) && Same(arms, @switch.Arms)
            ? @switch
            : Replace(@switch, @switch with { Receiver = receiver, Arms = arms });
    }

    public virtual SwitchArm VisitSwitchArm(SwitchArm arm)
    {
        Expression? guard = arm.Guard is null ? null : VisitExpression(arm.Guard);
        Expression result = VisitExpression(arm.Result);
        return Same(guard, arm.Guard) && Same(result, arm.Result)
            ? arm
            : Replace(arm, arm with { Guard = guard, Result = result });
    }

    public virtual Expression VisitFromEnd(FromEndExpression fromEnd)
    {
        Expression operand = VisitExpression(fromEnd.Operand);
        return Same(operand, fromEnd.Operand) ? fromEnd : Replace(fromEnd, fromEnd with { Operand = operand });
    }

    public virtual Expression VisitSlice(SliceExpression slice)
    {
        Expression? start = slice.Start is null ? null : VisitExpression(slice.Start);
        Expression? end = slice.End is null ? null : VisitExpression(slice.End);
        return Same(start, slice.Start) && Same(end, slice.End)
            ? slice
            : Replace(slice, slice with { Start = start, End = end });
    }

    public virtual Expression VisitTupleProjection(TupleProjectionExpression projection)
    {
        Expression receiver = VisitExpression(projection.Receiver);
        return Same(receiver, projection.Receiver) ? projection : Replace(projection, projection with { Receiver = receiver });
    }

    public virtual Expression VisitLambda(LambdaExpression lambda)
    {
        LambdaBody body = VisitLambdaBody(lambda.Body);
        return Same(body, lambda.Body) ? lambda : Replace(lambda, lambda with { Body = body });
    }

    public virtual LambdaBody VisitLambdaBody(LambdaBody body)
    {
        switch (body)
        {
            case LambdaExpressionBody expressionBody:
            {
                Expression expression = VisitExpression(expressionBody.Expression);
                return Same(expression, expressionBody.Expression) ? body : Replace(body, expressionBody with { Expression = expression });
            }
            case LambdaBlockBody blockBody:
            {
                BlockStatement block = VisitBlock(blockBody.Block);
                return Same(block, blockBody.Block) ? body : Replace(body, blockBody with { Block = block });
            }
            default:
                return body;
        }
    }

    public virtual Expression VisitNew(NewExpression creation)
    {
        IReadOnlyList<ArgumentSyntax> arguments = VisitList(creation.Arguments, VisitArgument);
        IReadOnlyList<WithAssignment>? initializer = creation.Initializer is null ? null : VisitList(creation.Initializer, VisitWithAssignment);
        return Same(arguments, creation.Arguments) && Same(initializer, creation.Initializer)
            ? creation
            : Replace(creation, creation with { Arguments = arguments, Initializer = initializer });
    }

    public virtual Expression VisitNewArray(NewArrayExpression newArray)
    {
        IReadOnlyList<Expression> dimensions = VisitList(newArray.Dimensions, VisitExpression);
        return Same(dimensions, newArray.Dimensions) ? newArray : Replace(newArray, newArray with { Dimensions = dimensions });
    }

    public virtual Expression VisitTargetTypedNewArray(TargetTypedNewArrayExpression newArray)
    {
        IReadOnlyList<Expression> dimensions = VisitList(newArray.Dimensions, VisitExpression);
        return Same(dimensions, newArray.Dimensions) ? newArray : Replace(newArray, newArray with { Dimensions = dimensions });
    }

    public virtual Expression VisitCollection(CollectionExpression collection)
    {
        IReadOnlyList<CollectionElement> elements = VisitList(collection.Elements, VisitCollectionElement);
        return Same(elements, collection.Elements) ? collection : Replace(collection, collection with { Elements = elements });
    }

    public virtual CollectionElement VisitCollectionElement(CollectionElement element)
    {
        switch (element)
        {
            case ExpressionElement expressionElement:
            {
                Expression expression = VisitExpression(expressionElement.Expression);
                return Same(expression, expressionElement.Expression) ? element : Replace(element, expressionElement with { Expression = expression });
            }
            case RangeElement rangeElement:
            {
                Expression range = VisitExpression(rangeElement.Range);
                return range is RangeExpression rewritten && !Same(rewritten, rangeElement.Range)
                    ? Replace(element, rangeElement with { Range = rewritten })
                    : element;
            }
            case SpreadElement spreadElement:
            {
                Expression expression = VisitExpression(spreadElement.Expression);
                return Same(expression, spreadElement.Expression) ? element : Replace(element, spreadElement with { Expression = expression });
            }
            case BuilderElement builder:
            {
                Expression source = VisitExpression(builder.Source);
                LambdaBody body = VisitLambdaBody(builder.Body);
                return Same(source, builder.Source) && Same(body, builder.Body)
                    ? element
                    : Replace(element, builder with { Source = source, Body = body });
            }
            default:
                return element;
        }
    }

    public virtual Expression VisitAggregation(AggregationExpression aggregation)
    {
        Expression source = VisitExpression(aggregation.Source);
        Expression? where = aggregation.WhereExpression is null ? null : VisitExpression(aggregation.WhereExpression);
        Expression body = VisitExpression(aggregation.Body);
        return Same(source, aggregation.Source) && Same(where, aggregation.WhereExpression) && Same(body, aggregation.Body)
            ? aggregation
            : Replace(aggregation, aggregation with { Source = source, WhereExpression = where, Body = body });
    }

    public virtual Expression VisitGenerator(GeneratorExpression generator)
    {
        Expression source = VisitExpression(generator.Source);
        LambdaBody body = VisitLambdaBody(generator.Body);
        return Same(source, generator.Source) && Same(body, generator.Body)
            ? generator
            : Replace(generator, generator with { Source = source, Body = body });
    }

    public virtual Expression VisitThrowExpression(ThrowExpression expression)
    {
        Expression operand = VisitExpression(expression.Expression);
        return Same(operand, expression.Expression) ? expression : Replace(expression, expression with { Expression = operand });
    }
}
