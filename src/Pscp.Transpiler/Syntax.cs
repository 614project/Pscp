using System.Collections.ObjectModel;

namespace Pscp.Transpiler;

public enum TokenKind
{
    EndOfFile,
    NewLine,
    Semicolon,
    Comma,
    Dot,
    Colon,
    ColonEqual,
    Question,
    QuestionDot,
    QuestionQuestion,
    QuestionQuestionEqual,
    OpenParen,
    CloseParen,
    OpenBrace,
    CloseBrace,
    OpenBracket,
    CloseBracket,
    LessThan,
    GreaterThan,
    LessEqual,
    GreaterEqual,
    Equal,
    EqualEqual,
    Bang,
    BangEqual,
    Plus,
    PlusPlus,
    PlusEqual,
    Minus,
    MinusMinus,
    MinusEqual,
    Star,
    StarEqual,
    Slash,
    SlashEqual,
    Percent,
    PercentEqual,
    AmpEqual,
    PipeEqual,
    CaretEqual,
    LessLessEqual,
    GreaterGreaterEqual,
    Caret,
    Tilde,
    Amp,
    Pipe,
    AmpAmp,
    PipePipe,
    Spaceship,
    DotDot,
    DotDotLess,
    DotDotEqual,
    PipeGreater,
    LessPipe,
    Arrow,
    FatArrow,
    Identifier,
    IntegerLiteral,
    FloatLiteral,
    StringLiteral,
    InterpolatedStringLiteral,
    CharLiteral,
    Let,
    Var,
    Mut,
    Rec,
    If,
    Then,
    Else,
    For,
    In,
    Do,
    While,
    Break,
    Continue,
    Return,
    True,
    False,
    Null,
    And,
    Or,
    Xor,
    Not,
    Match,
    When,
    Where,
    New,
    Class,
    Struct,
    Record,
    Ref,
    Out,
    Namespace,
    Using,
    Is,
}

public readonly record struct TextSpan(int Start, int Length)
{
    public int End => Start + Length;
}

public sealed record Token(TokenKind Kind, string Text, int Position)
{
    public TextSpan Span => new(Position, Text.Length);
}

public enum DiagnosticSeverity
{
    Warning,
    Error,
    // Editor-only information (PSCP5xxx). The CLI does not print it and it never fails a build.
    Info,
}

public enum HelperEmissionMode
{
    Compact,
    Verbose,
}

public static class PscpVersionInfo
{
    public const string LanguageVersion = "0.7";
    public const string ToolVersion = "0.7.0";
}

// `Code` identifies the diagnostic kind (PSCPxxxx, see DiagnosticCodes). `RelatedSpan` points at a related location
// such as the declaration of a binding whose mutation is reported.
public sealed record Diagnostic(
    string Message,
    TextSpan Span,
    DiagnosticSeverity Severity = DiagnosticSeverity.Error,
    string? Code = null,
    TextSpan? RelatedSpan = null)
{
    public string EffectiveCode => Code ?? (Severity == DiagnosticSeverity.Error ? DiagnosticCodes.GenericError : DiagnosticCodes.GenericWarning);
}

// Generated C# always stays within C# 10 / .NET 6 APIs, so it compiles for both the default SDK project and the
// `pscp build --older` (net6.0, LangVersion 10) project; there is no separate "older" emission mode.
public sealed record TranspilationOptions(
    string Namespace = "Pscp.Generated",
    string ClassName = "GeneratedProgram",
    HelperEmissionMode HelperEmission = HelperEmissionMode.Compact,
    bool Explain = false,
    string? ExplainSource = null,
    bool Pretty = false,
    bool LargeStack = false);

public sealed record TranspilationResult(
    string Source,
    PscpProgram Program,
    string CSharpCode,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool Success => Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
}

public enum MutabilityKind
{
    Immutable,
    Mutable,
}

public enum AssignmentOperator
{
    Assign,
    AddAssign,
    SubtractAssign,
    MultiplyAssign,
    DivideAssign,
    ModuloAssign,
    BitwiseAndAssign,
    BitwiseOrAssign,
    BitwiseXorAssign,
    ShiftLeftAssign,
    ShiftRightAssign,
    CoalesceAssign,
}

public enum OutputKind
{
    Write,
    WriteLine,
}

public enum LiteralKind
{
    Integer,
    Float,
    String,
    Char,
    True,
    False,
    Null,
}

public enum UnaryOperator
{
    Plus,
    Negate,
    LogicalNot,
    Peek,
}

public enum BinaryOperator
{
    Add,
    Subtract,
    ShiftLeft,
    ShiftRight,
    Multiply,
    Divide,
    Modulo,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
    Equal,
    NotEqual,
    Spaceship,
    BitwiseAnd,
    BitwiseXor,
    BitwiseOr,
    LogicalAnd,
    LogicalOr,
    PipeRight,
    PipeLeft,
    Coalesce,
}

public enum RangeKind
{
    Inclusive,
    RightExclusive,
    ExplicitInclusive,
}

// Slices inside an indexer must state whether the end is included (spec §15.2).
public enum SliceKind
{
    // `a..` or `..`: to the end.
    Open,
    // `a..<b` or `..<b`.
    Exclusive,
    // `a..=b` or `..=b`.
    Inclusive,
    // `a..b` or `..b`: rejected (PSCP2303), kept for error recovery.
    Ambiguous,
}

// How a call was written. Pipes are desugared into calls; the binder uses the kind for helper receiver insertion.
public enum PipeKind
{
    None,
    // `lhs |> target`: `lhs` is the first argument.
    Forward,
    // `target <| rhs`: `rhs` is the last argument.
    Backward,
    // `lhs |> (e)`: the parenthesized target is invoked with `lhs`.
    Delegate,
}

public enum ArgumentModifier
{
    None,
    Ref,
    Out,
    In,
}

public enum PostfixOperator
{
    Increment,
    Decrement,
}

public abstract record BindingTarget;

public sealed record NameTarget(string Name) : BindingTarget;

public sealed record DiscardTarget() : BindingTarget;

public sealed record TupleTarget(IReadOnlyList<BindingTarget> Elements) : BindingTarget;

public sealed record UsingDirective(string Text);

public sealed record PscpProgram(
    IReadOnlyList<UsingDirective> Usings,
    string? NamespaceName,
    IReadOnlyList<TypeDeclaration> Types,
    IReadOnlyList<FunctionDeclaration> Functions,
    IReadOnlyList<Statement> GlobalStatements);

public sealed record TypeDeclaration(
    string HeaderText,
    string Name,
    IReadOnlyList<TypeMember> Members,
    bool HasBody);

public abstract record TypeMember;

public sealed record OrderingShorthandMember(
    IReadOnlyList<string> ParameterNames,
    MethodBody Body) : TypeMember;

public sealed record FieldMember(
    IReadOnlyList<string> Modifiers,
    DeclarationStatement Declaration) : TypeMember;

public sealed record PropertyMember(
    IReadOnlyList<string> Modifiers,
    TypeSyntax Type,
    string Name,
    MethodBody Body) : TypeMember;

public sealed record MethodMember(
    IReadOnlyList<string> Modifiers,
    TypeSyntax? ReturnType,
    string Name,
    IReadOnlyList<ParameterSyntax> Parameters,
    MethodBody Body,
    bool IsConstructor,
    string? InitializerText = null,
    string? ConstraintText = null) : TypeMember;

public sealed record OperatorMember(
    IReadOnlyList<string> Modifiers,
    TypeSyntax ReturnType,
    string OperatorTokenText,
    IReadOnlyList<ParameterSyntax> Parameters,
    MethodBody Body) : TypeMember;

public sealed record NestedTypeMember(TypeDeclaration Declaration) : TypeMember;

// Body text passed through unchanged: `enum` members and `interface` members.
public sealed record RawTypeMember(string Text) : TypeMember;

public abstract record MethodBody;

public sealed record BlockMethodBody(BlockStatement Block) : MethodBody;

public sealed record ExpressionMethodBody(Expression Expression) : MethodBody;

// `TypeParameterText` (`<T>`) and `ConstraintText` (`where T : IComparable<T>`) are C# pass-through text.
// An expression body (`=> e`) is parsed as a block whose only statement is `e`.
public sealed record FunctionDeclaration(
    bool IsRecursive,
    TypeSyntax ReturnType,
    string Name,
    IReadOnlyList<ParameterSyntax> Parameters,
    BlockStatement Body,
    string? TypeParameterText = null,
    string? ConstraintText = null);

public sealed record ParameterSyntax(
    ArgumentModifier Modifier,
    TypeSyntax Type,
    BindingTarget Target);

public abstract record Statement;

public sealed record BlockStatement(IReadOnlyList<Statement> Statements) : Statement;

public sealed record DeclarationStatement(
    MutabilityKind Mutability,
    TypeSyntax? ExplicitType,
    IReadOnlyList<BindingTarget> Targets,
    Expression? Initializer,
    bool IsInputShorthand) : Statement;

public sealed record ExpressionStatement(Expression Expression, bool HasSemicolon) : Statement;

public sealed record AssignmentStatement(
    Expression Target,
    AssignmentOperator Operator,
    Expression Value,
    bool IsTupleAssignment) : Statement;

public sealed record OutputStatement(OutputKind Kind, Expression Expression) : Statement;

public sealed record IfStatement(
    Expression Condition,
    Statement ThenBranch,
    Statement? ElseBranch,
    bool IsOneLineForm) : Statement;

public sealed record WhileStatement(
    Expression Condition,
    Statement Body,
    bool IsDoForm) : Statement;

public sealed record ForInStatement(
    BindingTarget Iterator,
    Expression Source,
    Statement Body,
    bool IsDoForm) : Statement;

public sealed record CStyleForStatement(
    string HeaderText,
    Statement Body) : Statement;

public sealed record FastForStatement(
    Expression Source,
    BindingTarget? IndexTarget,
    BindingTarget ItemTarget,
    Statement Body,
    bool IsDoForm) : Statement;

public sealed record ReturnStatement(Expression? Expression) : Statement;

public sealed record BreakStatement() : Statement;

public sealed record ContinueStatement() : Statement;

public sealed record LocalFunctionStatement(FunctionDeclaration Function) : Statement;

// `try` / `catch` / `finally` / `throw` are C# pass-through (spec §12.6).
public sealed record TryStatement(BlockStatement Body, IReadOnlyList<CatchClause> Catches, BlockStatement? Finally) : Statement;

public sealed record CatchClause(TypeSyntax? Type, string? Name, Expression? Filter, BlockStatement Body);

public sealed record ThrowStatement(Expression? Expression) : Statement;

public abstract record Expression;

public sealed record LiteralExpression(LiteralKind Kind, string RawText) : Expression;

public sealed record InterpolatedStringExpression(IReadOnlyList<InterpolatedStringPart> Parts) : Expression;

public abstract record InterpolatedStringPart;

public sealed record InterpolatedStringTextPart(string Text) : InterpolatedStringPart;

// `{expr,alignment:format}`. Without a format the hole uses the PSCP rendering rules (spec §19).
public sealed record InterpolatedStringInterpolationPart(Expression Expression, string? Alignment = null, string? Format = null) : InterpolatedStringPart;

public sealed record IdentifierExpression(string Name) : Expression;

public sealed record DiscardExpression() : Expression;

public sealed record TupleExpression(IReadOnlyList<Expression> Elements) : Expression;

public sealed record BlockExpression(BlockStatement Block) : Expression;

public sealed record IfExpression(
    Expression Condition,
    Expression ThenExpression,
    Expression ElseExpression) : Expression;

public sealed record ConditionalExpression(
    Expression Condition,
    Expression WhenTrue,
    Expression WhenFalse) : Expression;

public sealed record UnaryExpression(UnaryOperator Operator, Expression Operand) : Expression;

public sealed record AssignmentExpression(
    Expression Target,
    AssignmentOperator Operator,
    Expression Value,
    bool IsExplicitValueAssignment) : Expression;

public sealed record PrefixExpression(PostfixOperator Operator, Expression Operand) : Expression;

public sealed record PostfixExpression(Expression Operand, PostfixOperator Operator) : Expression;

public sealed record BinaryExpression(
    Expression Left,
    BinaryOperator Operator,
    Expression Right) : Expression;

public sealed record RangeExpression(
    Expression Start,
    Expression? Step,
    Expression End,
    RangeKind Kind) : Expression;

// `x is pattern`. Patterns are C# 10 pass-through text (spec §13.9); `Designations` are the names the pattern
// declares, with their type when the pattern states one (`int v`).
public sealed record IsPatternExpression(Expression Left, PatternSyntax Pattern) : Expression;

public sealed record PatternDesignation(string Name, TypeSyntax? Type);

public sealed record PatternSyntax(string Text, IReadOnlyList<PatternDesignation> Designations)
{
    // The pattern is exactly a type, optionally with a designation (`int`, `int v`, `Point p`).
    public TypeSyntax? SimpleType { get; init; }
}

public sealed record CastExpression(TypeSyntax Type, Expression Operand) : Expression;

public sealed record AsExpression(Expression Operand, TypeSyntax Type) : Expression;

public sealed record NullForgivingExpression(Expression Operand) : Expression;

public sealed record CallExpression(
    Expression Callee,
    IReadOnlyList<ArgumentSyntax> Arguments,
    bool IsSpaceSeparated,
    PipeKind Pipe = PipeKind.None) : Expression;

public abstract record ArgumentSyntax(string? Name, ArgumentModifier Modifier);

public sealed record ExpressionArgumentSyntax(
    string? Name,
    ArgumentModifier Modifier,
    Expression Expression) : ArgumentSyntax(Name, Modifier);

public sealed record OutDeclarationArgumentSyntax(
    string? Name,
    TypeSyntax Type,
    BindingTarget Target) : ArgumentSyntax(Name, ArgumentModifier.Out);

public sealed record MemberAccessExpression(Expression Receiver, string MemberName, bool IsNullConditional = false) : Expression;

public sealed record IndexExpression(Expression Receiver, IReadOnlyList<Expression> Arguments, bool IsNullConditional = false) : Expression;

public sealed record WithExpression(Expression Receiver, IReadOnlyList<WithAssignment> Assignments) : Expression;

public sealed record WithAssignment(string MemberName, Expression Value);

public sealed record SwitchExpression(Expression Receiver, IReadOnlyList<SwitchArm> Arms) : Expression;

// The pattern's designations (`int x`, `var (a, b)`) are in scope for the guard and the result.
public sealed record SwitchArm(PatternSyntax Pattern, Expression? Guard, Expression Result);

public sealed record FromEndExpression(Expression Operand) : Expression;

public sealed record SliceExpression(Expression? Start, Expression? End, SliceKind Kind) : Expression;

public sealed record TupleProjectionExpression(Expression Receiver, int Position) : Expression;

public sealed record LambdaExpression(
    IReadOnlyList<LambdaParameter> Parameters,
    LambdaBody Body) : Expression;

public sealed record NewExpression(
    TypeSyntax? Type,
    IReadOnlyList<ArgumentSyntax> Arguments,
    IReadOnlyList<WithAssignment>? Initializer = null) : Expression;

public sealed record ThrowExpression(Expression Expression) : Expression;

public sealed record NewArrayExpression(TypeSyntax ElementType, IReadOnlyList<Expression> Dimensions) : Expression;

public sealed record TargetTypedNewArrayExpression(
    IReadOnlyList<Expression> Dimensions,
    bool AutoConstructElements) : Expression;

public sealed record CollectionExpression(IReadOnlyList<CollectionElement> Elements) : Expression;

public sealed record AggregationExpression(
    string AggregatorName,
    BindingTarget? IndexTarget,
    BindingTarget ItemTarget,
    Expression Source,
    Expression? WhereExpression,
    Expression Body) : Expression;

public sealed record GeneratorExpression(
    BindingTarget? IndexTarget,
    BindingTarget ItemTarget,
    Expression Source,
    LambdaBody Body) : Expression;

public abstract record LambdaBody;

public sealed record LambdaExpressionBody(Expression Expression) : LambdaBody;

public sealed record LambdaBlockBody(BlockStatement Block) : LambdaBody;

public sealed record LambdaParameter(
    ArgumentModifier Modifier,
    TypeSyntax? Type,
    BindingTarget Target);

public abstract record CollectionElement;

public sealed record ExpressionElement(Expression Expression) : CollectionElement;

public sealed record RangeElement(RangeExpression Range) : CollectionElement;

public sealed record SpreadElement(Expression Expression) : CollectionElement;

public sealed record BuilderElement(
    Expression Source,
    BindingTarget? IndexTarget,
    BindingTarget ItemTarget,
    LambdaBody Body) : CollectionElement;

public abstract record TypeSyntax;

// Type syntax nodes are compared structurally: the compiler-generated record equality would
// compare the argument lists by reference, making `List<int>` differ from another `List<int>`.
public sealed record NamedTypeSyntax(string Name, IReadOnlyList<TypeSyntax> TypeArguments) : TypeSyntax
{
    public bool Equals(NamedTypeSyntax? other)
        => other is not null
            && string.Equals(Name, other.Name, StringComparison.Ordinal)
            && TypeTextEquality.SequenceEquals(TypeArguments, other.TypeArguments);

    public override int GetHashCode()
        => HashCode.Combine(Name, TypeTextEquality.SequenceHash(TypeArguments));
}

public sealed record TupleTypeSyntax(IReadOnlyList<TypeSyntax> Elements) : TypeSyntax
{
    public bool Equals(TupleTypeSyntax? other)
        => other is not null && TypeTextEquality.SequenceEquals(Elements, other.Elements);

    public override int GetHashCode()
        => TypeTextEquality.SequenceHash(Elements);
}

internal static class TypeTextEquality
{
    public static bool SequenceEquals(IReadOnlyList<TypeSyntax> left, IReadOnlyList<TypeSyntax> right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        for (int i = 0; i < left.Count; i++)
        {
            if (!Equals(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }

    public static int SequenceHash(IReadOnlyList<TypeSyntax> values)
    {
        HashCode hash = new();
        foreach (TypeSyntax value in values)
        {
            hash.Add(value);
        }

        return hash.ToHashCode();
    }
}

public sealed record ArrayTypeSyntax(TypeSyntax ElementType, int Depth) : TypeSyntax;

public sealed record NullableTypeSyntax(TypeSyntax InnerType) : TypeSyntax;

public sealed record SizedArrayTypeSyntax(TypeSyntax ElementType, IReadOnlyList<Expression> Dimensions) : TypeSyntax;

internal static class Immutable
{
    public static IReadOnlyList<T> List<T>(params T[] values) => Array.AsReadOnly(values);

    public static IReadOnlyList<T> ToList<T>(this IEnumerable<T> values)
        => new ReadOnlyCollection<T>(values.ToArray());
}
