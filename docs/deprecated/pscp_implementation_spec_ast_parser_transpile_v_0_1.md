# PS/CP Language Implementation Specification v0.1

## 1. Scope

This document defines the implementation-oriented structure of the language in three parts:

1. abstract syntax tree design,
2. parsing rules and parser architecture,
3. C# transpilation strategy.

This document is intended to be used together with the language specification and API specification. Where conflicts arise, the language specification controls source-level semantics, and this document defines one conforming implementation strategy.

---

## 2. Implementation Goals

The reference implementation shall satisfy the following goals:

1. preserve source semantics exactly,
2. keep the parser deterministic and easy to debug,
3. avoid hidden rewriting that changes user intent,
4. emit readable C#,
5. isolate syntax sugar in a small number of lowering stages,
6. make future optimization possible without changing frontend semantics.

---

## 3. Compilation Pipeline

A conforming implementation may use the following pipeline:

1. lexical analysis,
2. concrete syntax parsing,
3. AST construction,
4. name binding,
5. type checking and shape checking,
6. desugaring to a lowered AST,
7. C# code generation,
8. optional formatting.

Recommended internal stage names:

- `TokenStream`
- `SyntaxTree`
- `AstTree`
- `BoundTree`
- `LoweredTree`
- `CSharpTree`

---

## 4. Source Locations

Every syntax node and AST node shall carry source location metadata sufficient for:

1. line and column diagnostics,
2. range highlighting,
3. mapping generated C# back to source when possible.

Recommended fields:

```txt
startOffset
endOffset
startLine
startColumn
endLine
endColumn
```

---

## 5. AST Design Principles

### 5.1 Separate Syntax Shape from Semantic Shape

The parser should preserve source syntax faithfully in the initial AST. Later lowering stages should remove sugar.

### 5.2 Model Statements and Expressions Separately

Although the language is expression-oriented, the AST shall distinguish expression nodes from statement nodes.

### 5.3 Preserve Sugared Constructs Initially

The following constructs should survive parsing as distinct AST nodes before lowering:

- declaration-based input shorthand,
- output shorthand,
- collection expressions with range expansion and spread,
- fast iteration `->`,
- one-line `then` and `do`,
- space-separated calls,
- aggregation expressions.

### 5.4 Prefer Explicit Node Kinds Over Boolean Flags

Use distinct node types where semantics differ materially.

---

## 6. Core AST Top-Level Structure

Recommended top-level model:

```txt
Program
  Members: List<TopLevelMember>
```

### 6.1 TopLevelMember Kinds

```txt
TopLevelMember
  - FunctionDecl
  - GlobalStatement
  - UsingDirective            // optional future extension
  - TypeDecl                  // optional future extension
```

For v0.1, only `FunctionDecl` and `GlobalStatement` are required.

---

## 7. Declaration AST

## 7.1 Binding Mutability

Represent mutability explicitly:

```txt
MutabilityKind
  - Immutable
  - Mutable
```

Mapping:

- `let` => `Immutable`
- `var` => `Mutable`
- `mut` => `Mutable`
- explicit typed non-`mut` declaration => `Immutable`

## 7.2 Declaration Nodes

```txt
Statement
  - LocalDeclStmt
  - MultiDeclStmt
  - InputDeclStmt
  - SizedArrayDeclStmt
```

### 7.2.1 `LocalDeclStmt`

Represents ordinary declaration with optional initializer.

Fields:

```txt
mutability: MutabilityKind
explicitType: TypeSyntax?
name: Identifier or Discard
initializer: Expr?
```

Examples:

```txt
let x = 1
var y = 2
mut int z = 3
mut int t;
```

### 7.2.2 `MultiDeclStmt`

Represents declarations of multiple names in one statement.

Fields:

```txt
mutability: MutabilityKind
explicitType: TypeSyntax?
names: List<IdentifierOrDiscard>
initializer: Expr or TupleExpr or null
```

### 7.2.3 `InputDeclStmt`

Represents declaration-based input syntax.

Fields:

```txt
declaredShape: InputDeclShape
mutability: MutabilityKind
explicitElementType: TypeSyntax
namesOrPattern: InputTarget
```

Examples:

```txt
int n =
int n, m =
int[n] arr =
(int, int)[m] edges =
```

### 7.2.4 `SizedArrayDeclStmt`

Represents sized array declaration with or without initializer.

Fields:

```txt
elementType: TypeSyntax
dimensions: List<Expr>
name: Identifier
initializer: Expr?
mutability: MutabilityKind
```

Examples:

```txt
int[n] a;
int[n][m] dp;
int[n] arr =
```

---

## 8. Statement AST

Recommended statement base hierarchy:

```txt
Statement
  - BlockStmt
  - LocalDeclStmt
  - MultiDeclStmt
  - InputDeclStmt
  - SizedArrayDeclStmt
  - ExprStmt
  - AssignmentStmt
  - TupleAssignmentStmt
  - OutputStmt
  - IfStmt
  - WhileStmt
  - ForInStmt
  - FastForStmt
  - ReturnStmt
  - BreakStmt
  - ContinueStmt
  - EmptyStmt
```

## 8.1 `BlockStmt`

Fields:

```txt
statements: List<Statement>
```

## 8.2 `ExprStmt`

Fields:

```txt
expr: Expr
hasSemicolon: bool
```

The `hasSemicolon` field is required to support implicit-return analysis.

## 8.3 `AssignmentStmt`

Fields:

```txt
target: AssignableExpr
operator: AssignOp
value: Expr
```

`AssignOp` includes `=`, `+=`, `-=`, `*=`, `/=`, `%=`.

## 8.4 `TupleAssignmentStmt`

Fields:

```txt
left: TupleAssignTarget
right: Expr
```

The left-hand side may contain identifiers, discards, indexers, or member access, subject to assignability rules.

## 8.5 `OutputStmt`

Fields:

```txt
kind: OutputKind   // Write or WriteLine
expr: Expr
```

Maps from:

```txt
= expr
+= expr
```

## 8.6 `IfStmt`

Fields:

```txt
condition: Expr
thenBranch: Statement
elseBranch: Statement?
isOneLineForm: bool
```

One-line `if then else` may also appear as an expression. See expression AST.

## 8.7 `WhileStmt`

Fields:

```txt
condition: Expr
body: Statement
isDoForm: bool
```

## 8.8 `ForInStmt`

Fields:

```txt
iterator: IdentifierOrDiscard
source: Expr
body: Statement
isDoForm: bool
```

## 8.9 `FastForStmt`

Represents `->` syntax.

Fields:

```txt
source: Expr
indexName: IdentifierOrDiscard?
itemName: IdentifierOrDiscard
body: Statement or Expr
isDoForm: bool
```

Examples:

```txt
xs -> x { ... }
xs -> x do expr
xs -> i, x { ... }
```

## 8.10 `ReturnStmt`

Fields:

```txt
expr: Expr?
```

## 8.11 `BreakStmt` and `ContinueStmt`

No additional fields.

---

## 9. Expression AST

Recommended expression hierarchy:

```txt
Expr
  - LiteralExpr
  - IdentifierExpr
  - DiscardExpr                 // optional syntax-only node; can also be prohibited here
  - ParenExpr
  - TupleExpr
  - BlockExpr
  - IfExpr
  - UnaryExpr
  - BinaryExpr
  - RangeExpr
  - CallExpr
  - SpaceCallExpr
  - MemberAccessExpr
  - IndexExpr
  - TupleProjectionExpr
  - LambdaExpr
  - CollectionExpr
  - SpreadExpr                  // valid only inside collection expressions
  - AggregationExpr
  - NewExpr                     // plain C#-style interoperability
  - ObjectCreationExpr          // optional split from NewExpr
```

## 9.1 `LiteralExpr`

Fields:

```txt
kind: LiteralKind
value: object?
rawText: string
```

## 9.2 `IdentifierExpr`

Fields:

```txt
name: string
```

## 9.3 `TupleExpr`

Fields:

```txt
elements: List<Expr>
```

## 9.4 `BlockExpr`

Fields:

```txt
block: BlockStmt
```

## 9.5 `IfExpr`

Fields:

```txt
condition: Expr
thenExpr: Expr
elseExpr: Expr
```

Represents one-line expression form and any brace-delimited expression form that type-checks as an expression.

## 9.6 `UnaryExpr`

Fields:

```txt
operator: UnaryOp
operand: Expr
```

## 9.7 `BinaryExpr`

Fields:

```txt
left: Expr
operator: BinaryOp
right: Expr
```

Includes arithmetic, logical, pipe, comparison, and spaceship operators.

## 9.8 `RangeExpr`

Fields:

```txt
start: Expr
step: Expr?
end: Expr
kind: RangeKind   // Inclusive, RightExclusive, ExplicitInclusive
```

Examples:

```txt
1..10
0..<n
0..=n
10..-1..0
```

## 9.9 `CallExpr`

Represents ordinary parenthesized invocation.

Fields:

```txt
callee: Expr
arguments: List<Expr>
```

## 9.10 `SpaceCallExpr`

Represents space-separated invocation before lowering.

Fields:

```txt
callee: Expr
arguments: List<Expr>
```

After parsing and validation, this node may lower to `CallExpr`.

## 9.11 `MemberAccessExpr`

Fields:

```txt
receiver: Expr
memberName: string
```

## 9.12 `IndexExpr`

Fields:

```txt
receiver: Expr
arguments: List<Expr>
```

The initial implementation may restrict indexing to one argument per bracket pair.

## 9.13 `TupleProjectionExpr`

Fields:

```txt
receiver: Expr
position: int   // 1-based
```

Represents `p.1`, `p.2`, etc.

## 9.14 `LambdaExpr`

Fields:

```txt
parameters: List<LambdaParam>
body: Expr or BlockStmt
```

`LambdaParam` fields:

```txt
name: IdentifierOrDiscard
type: TypeSyntax?
```

## 9.15 `CollectionExpr`

Fields:

```txt
elements: List<CollectionElement>
```

`CollectionElement` kinds:

```txt
CollectionElement
  - ExprElement(expr)
  - RangeElement(rangeExpr)
  - SpreadElement(expr)
  - BuilderElement(builder)
```

## 9.16 `AggregationExpr`

Fields:

```txt
aggregatorName: string
clauses: List<AggregationClause>
body: Expr
```

Recommended clause types:

```txt
AggregationClause
  - ForClause(name, source)
  - IndexedForClause(indexName, itemName, source)   // optional future form
  - WhereClause(condition)
```

Examples:

```txt
min { for i in 0..<n do a[i] - b[i] }
count { for x in arr where x % 2 == 0 do 1 }
```

---

## 10. Type Syntax AST

Recommended representation:

```txt
TypeSyntax
  - NamedTypeSyntax(name, typeArgs)
  - TupleTypeSyntax(elements)
  - ArrayTypeSyntax(elementType, rankOrDepth)
  - SizedArrayTypeSyntax(elementType, dimensions)    // declaration-only syntax
```

### 10.1 `NamedTypeSyntax`

Examples:

```txt
int
string
Queue<int>
Dictionary<int, string>
```

### 10.2 `TupleTypeSyntax`

Example:

```txt
(int, int, string)
```

### 10.3 `ArrayTypeSyntax`

Represents ordinary types like:

```txt
int[]
int[][]
```

### 10.4 `SizedArrayTypeSyntax`

Represents declaration-only syntax such as:

```txt
int[n]
int[n][m]
```

This node should not survive into the fully bound type model. It lowers to ordinary array types plus allocation semantics.

---

## 11. Parser Architecture

The recommended frontend parser is a recursive-descent parser with precedence climbing or Pratt parsing for expressions.

### 11.1 Why Recursive Descent

Recommended because the language contains:

1. multiple statement-start ambiguities,
2. context-sensitive shorthand such as `= expr`, `+= expr`, `T x =`,
3. one-line `do` and `then`,
4. collection expressions containing spread and range,
5. space-separated call syntax.

Recursive descent keeps such decisions locally understandable.

### 11.2 Parser Modules

Recommended parser decomposition:

- `ParseProgram`
- `ParseTopLevelMember`
- `ParseStatement`
- `ParseDeclarationOrExpressionStatement`
- `ParseBlock`
- `ParseExpression(precedence)`
- `ParsePrimary`
- `ParseCollectionExpression`
- `ParseFunctionDecl`
- `ParseTypeSyntax`

---

## 12. Statement Parsing Rules

## 12.1 Statement Start Classification

At statement start, classify in this order:

1. `{` => block,
2. `if` => if statement or expression-context if,
3. `while` => while statement,
4. `for` => for-in statement,
5. `return` => return statement,
6. `break` => break,
7. `continue` => continue,
8. `=` => write output statement,
9. `+=` => writeline output statement,
10. `let` / `var` / `mut` / explicit type lookahead => declaration,
11. otherwise parse expression or assignment statement.

This ordering is intentionally strict.

## 12.2 Distinguishing Declaration from Expression

When a statement begins with an identifier or a known type keyword, the parser must distinguish:

- explicit typed declaration,
- expression statement,
- assignment,
- space-separated call.

Recommended strategy:

1. attempt explicit type parse with bounded lookahead,
2. only commit to declaration if followed by a valid declaration pattern,
3. otherwise fall back to expression parsing.

Examples requiring correct distinction:

```txt
int x = 1          // declaration
int[n] arr =       // input declaration or sized declaration
foo x y            // expression statement, space call
x = y + 1          // assignment
```

## 12.3 Input Declaration Parsing

After parsing a declaration head, if the parser encounters `=` followed immediately by statement termination, newline boundary, or block end, it shall produce `InputDeclStmt`.

Examples:

```txt
int n =
int n, m =
int[n] arr =
```

This rule takes precedence over reporting an incomplete assignment.

## 12.4 Output Statement Parsing

At statement start:

- `= expr` parses as `OutputStmt(Write)`
- `+= expr` parses as `OutputStmt(WriteLine)`

Elsewhere, `+=` is parsed normally as assignment.

## 12.5 Tuple Assignment Parsing

A leading parenthesized expression followed by `=` may denote tuple assignment only if the parenthesized left-hand side is a valid assignment pattern.

Example:

```txt
(a, b) = (b, a)
(arr[i], arr[j]) = (arr[j], arr[i])
```

If the left-hand side is not assignable, parsing succeeds syntactically as an expression and is rejected later.

---

## 13. Expression Parsing Rules

## 13.1 Expression Parser Style

Use precedence-climbing or Pratt parsing with explicit handling of postfix forms.

## 13.2 Precedence Table

The parser shall use the following precedence order from highest to lowest:

1. postfix
   - parenthesized call
   - indexing
   - member access
   - tuple projection

2. prefix
   - unary plus/minus
   - `!`
   - `not`

3. multiplicative
   - `*`, `/`, `%`

4. additive
   - `+`, `-`

5. range
   - `..`, `..<`, `..=`

6. comparison
   - `<`, `<=`, `>`, `>=`, `<=>`

7. equality
   - `==`, `!=`

8. logical and
   - `&&`, `and`

9. logical xor
   - `^`, `xor`

10. logical or
   - `||`, `or`

11. pipe
   - `|>`, `<|`

12. assignment
   - `=`, `+=`, `-=`, `*=`, `/=`, `%=`

Assignment shall be parsed only in statement contexts unless the implementation explicitly supports assignment expressions.

Recommended v0.1 strategy: do not expose assignment as a general expression.

## 13.3 Postfix Parsing Loop

After parsing a primary expression, repeatedly consume postfix operators in this order:

1. `(` argument list `)` => `CallExpr`,
2. `[` index list `]` => `IndexExpr`,
3. `.` identifier => `MemberAccessExpr`,
4. `.` integer literal => `TupleProjectionExpr`,
5. space-call continuation if allowed by context => `SpaceCallExpr`.

This permits chaining such as:

```txt
obj.method(x)[i].name
```

---

## 14. Space-Separated Call Parsing

## 14.1 Motivation

Space-separated call syntax improves compatibility with pipe-oriented functional style.

## 14.2 Parsing Rule

After parsing a primary or postfix expression suitable as a callee, the parser may continue a `SpaceCallExpr` if the next token begins an atomic argument.

Recommended atomic argument starts:

- identifier,
- literal,
- `(`,
- `[`,
- `{` only when block expression is valid in the current context,
- lambda introducer when unambiguous.

## 14.3 Restrictions

The parser shall not absorb a following token sequence into a space-call argument if doing so would require guessing across lower-precedence binary operators without parentheses.

Examples:

```txt
f x y              // valid
f (x + y) z        // valid
f x + y            // parses as (f x) + y
f x < y            // parses as (f x) < y
f if ok then 1 else 2   // requires parentheses
```

## 14.4 Lowering

After successful parsing and validation, `SpaceCallExpr` lowers to `CallExpr` with the same callee and argument list.

No currying or partial application is implied by v0.1.

---

## 15. Collection Expression Parsing

## 15.1 Entry

Upon encountering `[`, parse a `CollectionExpr`.

## 15.2 Element Classification

Within a collection expression, classify each comma-delimited element as one of:

1. spread element,
2. builder element,
3. range element,
4. ordinary expression element.

Recommended order:

1. if token is `..` and position is valid for spread, parse `SpreadElement`,
2. else parse an expression; if that expression is syntactically a range, classify as `RangeElement`,
3. else if expression is followed by `->`, parse builder form,
4. else classify as `ExprElement`.

## 15.3 Automatic Range Expansion

Do not expand ranges during parsing. Preserve `RangeElement` nodes and perform expansion during lowering or code generation.

## 15.4 Spread Restrictions

Spread is legal only inside collection expressions. Any other occurrence of leading `..expr` is a syntax error.

---

## 16. Aggregation Parsing

## 16.1 Form

Aggregation syntax has the form:

```txt
name { clauses do expr }
```

Examples:

```txt
min { for i in 0..<n do a[i] - b[i] }
sum { for x in arr do x }
count { for x in arr where x % 2 == 0 do 1 }
```

## 16.2 Recommended Parser Strategy

When parsing a primary expression, if an identifier is followed by `{` and the block begins with `for`, parse an `AggregationExpr` rather than a plain call or block.

## 16.3 Clause Structure

A minimal v0.1 parser need support only:

- one `for` clause,
- optional `where` clause,
- one `do` body expression.

Future versions may generalize this.

---

## 17. One-Line `then` and `do`

## 17.1 Parsing Rule

`then` and `do` consume exactly one expression or simple statement body up to statement termination.

## 17.2 Termination

The one-line body ends at the first of:

1. newline that ends the current statement,
2. semicolon,
3. closing brace of the enclosing construct,
4. end of file.

## 17.3 Prohibited Forms

A one-line body may not contain multiple statements unless enclosed in braces.

Examples:

```txt
if x < 0 then -x else x          // valid
for i in 0..<n do sum += a[i]    // valid
if cond then a; b else c         // invalid as one-line form
```

---

## 18. Implicit Return Analysis

## 18.1 Phase

Implicit return shall be resolved after parsing, during AST validation or binding.

## 18.2 Rule

A block implicitly returns its final expression statement if:

1. the final statement is `ExprStmt`,
2. `ExprStmt.hasSemicolon == false`,
3. the top-level expression is not a bare invocation.

## 18.3 Bare Invocation Check

An expression is a bare invocation if its top-level node is:

- `CallExpr`,
- `SpaceCallExpr`.

Optional implementation choice: treat certain invocation wrappers such as parenthesized calls as still bare if the parentheses are semantically transparent.

## 18.4 Lowering Strategy

The binder or lowerer should annotate blocks with one of:

```txt
BlockResultKind
  - NoValue
  - ImplicitValue(expr)
  - ExplicitReturn
```

This avoids recomputing return behavior during code generation.

---

## 19. Binding and Name Resolution

## 19.1 Scope Model

Introduce a lexical scope for:

- function bodies,
- block statements,
- lambda bodies,
- loop bodies,
- aggregation scopes,
- fast-iteration bindings.

## 19.2 Shadowing

Ordinary lexical shadowing is permitted unless an implementation-specific policy chooses to warn.

## 19.3 Reserved Intrinsics

`stdin` and `stdout` should be treated as reserved intrinsic bindings unless shadowing is intentionally supported.

## 19.4 Recursive Bindings

A function declared with `rec` is entered into its own scope before binding its body. A function without `rec` is not.

---

## 20. Type Checking Notes

## 20.1 Minimal Type Requirements

A v0.1 implementation should at minimum enforce:

1. immutable assignments are rejected,
2. tuple projection requires tuple-typed receiver,
3. `break` and `continue` occur only inside loops,
4. `return` type matches containing function,
5. collection literal elements unify to a valid element type,
6. range endpoints and step are compatible numeric types,
7. output shorthand is used only with renderable shapes,
8. input shorthand is used only with supported shapes.

## 20.2 Sized Array Types

Sized declaration types are syntax-only and must be erased into ordinary array types plus allocation semantics before or during binding.

---

## 21. Desugaring Strategy

The recommended lowering sequence is:

1. resolve declarations and input/output shorthand,
2. lower `SpaceCallExpr` to `CallExpr`,
3. lower `FastForStmt` to ordinary loop form,
4. lower `AggregationExpr` to explicit loops or helper calls,
5. lower collection expression spread and range expansion,
6. lower implicit returns to explicit return statements where needed,
7. erase sized declaration syntax.

Desugaring should preserve source spans where possible.

---

## 22. Lowered AST Shape

After desugaring, the implementation may restrict itself to a smaller core language.

Recommended lowered statement set:

```txt
LoweredStatement
  - BlockStmt
  - LocalDeclStmt
  - ExprStmt
  - AssignmentStmt
  - IfStmt
  - WhileStmt
  - ForInStmt or CanonicalForStmt
  - ReturnStmt
  - BreakStmt
  - ContinueStmt
```

Recommended lowered expression set:

```txt
LoweredExpr
  - LiteralExpr
  - IdentifierExpr
  - TupleExpr
  - UnaryExpr
  - BinaryExpr
  - CallExpr
  - MemberAccessExpr
  - IndexExpr
  - LambdaExpr
  - ConditionalExpr
  - NewExpr
  - ArrayLiteralExpr or BuilderExpr
```

This core is easier to transpile to C#.

---

## 23. C# Transpilation Strategy

## 23.1 General Policy

Generated C# should satisfy:

1. semantic equivalence,
2. readability,
3. predictable local structure,
4. low surprise for debugging.

The transpiler is not required to minimize allocations in v0.1.

## 23.2 Naming Policy

The transpiler should preserve user-declared names whenever possible.

Because .NET APIs remain available as-is, no automatic casing conversion is performed. Examples such as the following should remain natural in user code and generated code:

```txt
Queue<int> queue = new()
PriorityQueue<int, int> pq = new()
HashSet<int> set = new()
```

Temporary lowered names should use a reserved prefix unlikely to collide with user code, such as:

```txt
__tmp0
__iter1
__acc2
```

---

## 24. Runtime Support Strategy

The transpiler may emit a small helper runtime for:

- `stdin`,
- `stdout`,
- collection helpers not directly mapped to .NET,
- rendering helpers,
- array/grid construction helpers.

Recommended helper class names:

```txt
__PscpStdin
__PscpStdout
__PscpSeq
__PscpRender
```

These names are recommendations, not part of the source language.

---

## 25. C# Emission Rules by Construct

## 25.1 Scalars and Basic Declarations

Examples:

```txt
let x = 1
var y = 2
mut int z = 3
```

Suggested C# output:

```csharp
var x = 1;
var y = 2;
int z = 3;
```

If preserving mutability distinctions in emitted syntax is unnecessary, both `let` and `var` may lower to `var` or explicit type declarations as inferred.

## 25.2 Uninitialized Mutable Scalars

```txt
mut int x;
mut string s;
```

Suggested C# output:

```csharp
int x = default;
string s = default;
```

## 25.3 Sized Arrays

```txt
int[n] a;
int[n][m] dp;
```

Suggested C# output:

```csharp
int[] a = new int[n];
int[][] dp = new int[n][];
for (int __i = 0; __i < n; __i++) dp[__i] = new int[m];
```

Alternative runtime helpers are permitted.

## 25.4 Input Shorthand

```txt
int n =
int[n] arr =
(int, int)[m] edges =
```

Suggested C# output:

```csharp
int n = stdin.Int();
int[] arr = stdin.Array<int>(n);
(int, int)[] edges = stdin.Tuples2<int, int>(m);
```

Exact helper names are implementation-defined.

## 25.5 Output Shorthand

```txt
= expr
+= expr
```

Suggested C# output:

```csharp
stdout.Write(expr);
stdout.WriteLine(expr);
```

The helper may implement tuple and collection rendering.

## 25.6 Tuple Projection

```txt
p.2
```

Suggested C# output:

```csharp
p.Item2
```

## 25.7 Tuple Assignment

```txt
(a, b) = (b, a)
(arr[i], arr[j]) = (arr[j], arr[i])
```

Suggested C# output:

```csharp
(a, b) = (b, a);
(arr[i], arr[j]) = (arr[j], arr[i]);
```

If backend restrictions require it, a temporary variable strategy may be used.

## 25.8 Range Expressions

Standalone ranges may lower to helper iterables.

Examples:

```txt
0..<n
1..10
10..-1..0
```

Suggested strategies:

1. helper enumerable objects,
2. direct loop synthesis when used in `for`, `->`, or aggregation,
3. materialization when placed in collection expressions.

v0.1 recommendation: do not aggressively optimize range lowering globally; optimize only when context already requires iteration.

## 25.9 Collection Expressions

Examples:

```txt
[1, 2, 3]
[0..<n]
[1, 2, ..a, 6]
[0..<n -> i do i * i]
```

Suggested lowering strategies:

1. concrete array/list construction in declaration context,
2. helper builder in ambiguous r-value context,
3. direct loop fill for builder forms,
4. explicit concatenation or append loops for spread.

The chosen lowering must preserve order.

## 25.10 Fast Iteration

```txt
xs -> x { ... }
xs -> i, x { ... }
```

Suggested lowering:

- if indexed form and source supports random access, generate indexed `for`,
- otherwise generate `foreach` plus index counter,
- if no index is requested, ordinary `foreach` is sufficient.

## 25.11 Aggregation

```txt
min { for i in 0..<n do a[i] - b[i] }
sum { for x in arr do x }
count { for x in arr where pred(x) do 1 }
```

Suggested lowering:

1. allocate accumulator local(s),
2. generate explicit iteration,
3. update accumulator according to aggregator kind,
4. emit final accumulator expression.

v0.1 recommendation: lower aggregations to loops rather than library chains, for clarity and controllability.

## 25.12 One-Line Forms

One-line `then` and `do` should not survive code generation as distinct constructs. They lower to the same underlying AST as block or expression forms.

## 25.13 Implicit Return

During lowering, convert implicit-return blocks into explicit `return` statements when generating C# methods or lambdas that require return statements.

Example:

```txt
int cmp(int a, int b) {
    a <=> b
}
```

Suggested C# output:

```csharp
int cmp(int a, int b)
{
    return Compare(a, b);
}
```

`Compare` here stands for whatever lowering is used for `<=>`.

## 25.14 Spaceship Operator

```txt
a <=> b
```

Suggested lowering strategies:

1. `Comparer<T>.Default.Compare(a, b)` when generic or unknown,
2. `a.CompareTo(b)` when statically known and valid,
3. specialized primitive comparisons when desired.

v0.1 recommendation: prefer the most readable valid lowering in each context.

---

## 26. C# Expression vs Statement Emission

Because the source language admits expression-oriented blocks, the code generator should provide helpers for:

1. statement-context emission,
2. expression-context emission,
3. expression lifting through temporary variables where necessary.

Recommended codegen utilities:

```txt
EmitStatement(stmt)
EmitExpression(expr)
EmitBlockAsExpression(block)
EmitBlockAsStatements(block)
```

Where C# cannot represent a source expression directly, the lowerer should rewrite it before code generation rather than forcing complex expression emission.

---

## 27. Suggested Helper Runtime Surface in Generated C#

The following is a recommended generated helper surface. Names are not normative.

```csharp
sealed class __PscpStdin
{
    public int Int();
    public long Long();
    public string Str();
    public char Char();
    public double Double();
    public decimal Decimal();
    public bool Bool();
    public string Line();
    public string[] Lines(int n);
    public string[] Words();
    public char[] Chars();
    public T[] Array<T>(int n);
    public (T1, T2)[] Tuples2<T1, T2>(int n);
    public (T1, T2, T3)[] Tuples3<T1, T2, T3>(int n);
}

sealed class __PscpStdout
{
    public void Write<T>(T value);
    public void WriteLine<T>(T value);
    public void Flush();
    public void Lines<T>(IEnumerable<T> values);
    public void Grid<T>(IEnumerable<IEnumerable<T>> grid);
}
```

Additional helpers may be added as needed.

---

## 28. Diagnostics Strategy

The implementation should emit precise diagnostics for the following frontend situations:

1. ambiguous declaration vs expression start,
2. invalid space-separated call argument grouping,
3. illegal spread outside collection expression,
4. unsupported input or output shape,
5. missing `rec` on recursive self-reference,
6. tuple projection index out of range when statically known,
7. final bare invocation in value-returning function when no explicit return exists, if the implementation chooses to warn.

Diagnostics should reference source spans captured in the syntax tree.

---

## 29. Suggested Development Order

A practical implementation order is:

1. lexer,
2. core parser without sugar,
3. declarations and assignments,
4. `if`, `while`, `for`, `return`, `break`, `continue`,
5. collection expressions and ranges,
6. input and output shorthand,
7. tuple projection and tuple assignment,
8. space-separated calls,
9. fast iteration `->`,
10. aggregation,
11. implicit return analysis,
12. C# emitter,
13. helper runtime.

This order reduces parser ambiguity early and postpones the most syntactically delicate features until a stable core exists.

---

## 30. Minimal Viable Lowered Core

A recommended MVP lowered core suitable for C# emission consists of:

### Statements

```txt
BlockStmt
LocalDeclStmt
AssignmentStmt
ExprStmt
IfStmt
WhileStmt
ForEachStmt
ReturnStmt
BreakStmt
ContinueStmt
```

### Expressions

```txt
LiteralExpr
IdentifierExpr
TupleExpr
UnaryExpr
BinaryExpr
CallExpr
MemberAccessExpr
IndexExpr
LambdaExpr
ConditionalExpr
ArrayBuilderExpr
```

Everything else should lower into this core before backend emission.

---

## 31. Conformance

An implementation conforms to this implementation specification if:

1. its AST can represent every source construct defined in the language specification,
2. its parser resolves all specified syntactic distinctions correctly,
3. its desugaring preserves source semantics,
4. its generated C# preserves observable behavior,
5. it does not require rewriting existing .NET API names into alternative casing or aliases.

Optimization strategy remains implementation-defined.

