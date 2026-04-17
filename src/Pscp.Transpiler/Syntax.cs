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
    Caret,
    Tilde,
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

public sealed record Diagnostic(string Message, TextSpan Span);

public sealed record TranspilationOptions(
    string Namespace = "Pscp.Generated",
    string ClassName = "GeneratedProgram");

public sealed record TranspilationResult(
    string Source,
    PscpProgram Program,
    string CSharpCode,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool Success => Diagnostics.Count == 0;
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
    LogicalAnd,
    LogicalOr,
    LogicalXor,
    PipeRight,
    PipeLeft,
}

public enum RangeKind
{
    Inclusive,
    RightExclusive,
    ExplicitInclusive,
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
    string? InitializerText = null) : TypeMember;

public sealed record OperatorMember(
    IReadOnlyList<string> Modifiers,
    TypeSyntax ReturnType,
    string OperatorTokenText,
    IReadOnlyList<ParameterSyntax> Parameters,
    MethodBody Body) : TypeMember;

public sealed record NestedTypeMember(TypeDeclaration Declaration) : TypeMember;

public abstract record MethodBody;

public sealed record BlockMethodBody(BlockStatement Block) : MethodBody;

public sealed record ExpressionMethodBody(Expression Expression) : MethodBody;

public sealed record FunctionDeclaration(
    bool IsRecursive,
    TypeSyntax ReturnType,
    string Name,
    IReadOnlyList<ParameterSyntax> Parameters,
    BlockStatement Body);

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

public abstract record Expression;

public sealed record LiteralExpression(LiteralKind Kind, string RawText) : Expression;

public sealed record InterpolatedStringExpression(IReadOnlyList<InterpolatedStringPart> Parts) : Expression;

public abstract record InterpolatedStringPart;

public sealed record InterpolatedStringTextPart(string Text) : InterpolatedStringPart;

public sealed record InterpolatedStringInterpolationPart(Expression Expression) : InterpolatedStringPart;

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

public sealed record IsPatternExpression(
    Expression Left,
    IsPatternSyntax Pattern,
    bool Negated) : Expression;

public abstract record IsPatternSyntax;

public sealed record TypePatternSyntax(TypeSyntax Type) : IsPatternSyntax;

public sealed record ConstantPatternSyntax(Expression Expression) : IsPatternSyntax;

public sealed record CallExpression(
    Expression Callee,
    IReadOnlyList<ArgumentSyntax> Arguments,
    bool IsSpaceSeparated) : Expression;

public abstract record ArgumentSyntax(string? Name, ArgumentModifier Modifier);

public sealed record ExpressionArgumentSyntax(
    string? Name,
    ArgumentModifier Modifier,
    Expression Expression) : ArgumentSyntax(Name, Modifier);

public sealed record OutDeclarationArgumentSyntax(
    string? Name,
    TypeSyntax Type,
    BindingTarget Target) : ArgumentSyntax(Name, ArgumentModifier.Out);

public sealed record MemberAccessExpression(Expression Receiver, string MemberName) : Expression;

public sealed record IndexExpression(Expression Receiver, IReadOnlyList<Expression> Arguments) : Expression;

public sealed record WithExpression(Expression Receiver, string InitializerText) : Expression;

public sealed record FromEndExpression(Expression Operand) : Expression;

public sealed record SliceExpression(Expression? Start, Expression? End) : Expression;

public sealed record TupleProjectionExpression(Expression Receiver, int Position) : Expression;

public sealed record LambdaExpression(
    IReadOnlyList<LambdaParameter> Parameters,
    LambdaBody Body) : Expression;

public sealed record NewExpression(TypeSyntax? Type, IReadOnlyList<ArgumentSyntax> Arguments) : Expression;

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

public sealed record NamedTypeSyntax(string Name, IReadOnlyList<TypeSyntax> TypeArguments) : TypeSyntax;

public sealed record TupleTypeSyntax(IReadOnlyList<TypeSyntax> Elements) : TypeSyntax;

public sealed record ArrayTypeSyntax(TypeSyntax ElementType, int Depth) : TypeSyntax;

public sealed record NullableTypeSyntax(TypeSyntax InnerType) : TypeSyntax;

public sealed record SizedArrayTypeSyntax(TypeSyntax ElementType, IReadOnlyList<Expression> Dimensions) : TypeSyntax;

internal static class Immutable
{
    public static IReadOnlyList<T> List<T>(params T[] values) => Array.AsReadOnly(values);

    public static IReadOnlyList<T> ToList<T>(this IEnumerable<T> values)
        => new ReadOnlyCollection<T>(values.ToArray());
}
