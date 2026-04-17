# PS/CP Language Transpiler Specification v0.3

## 1. Scope

This document defines version `v0.3` of the transpiler specification for the PS/CP language.

This document describes:

1. overall compilation architecture,
2. frontend-to-backend stage boundaries,
3. semantic lowering policy,
4. hardcoded compile-time specialization strategy,
5. C# backend emission rules,
6. runtime helper minimization policy,
7. code generation contracts for intrinsic language features,
8. diagnostics and recovery expectations for transpilation.

This document is normative unless otherwise stated.

---

## 2. Core Principles

### 2.1 Source Semantics Are Primary

The transpiler shall preserve the source-language semantics defined by the language specification. Optimization and backend convenience shall never change observable source behavior.

### 2.2 Sugar Must Disappear Early

Language-specific sugar shall be eliminated before final C# emission wherever possible.

This applies especially to:

- declaration-based input shorthand,
- statement-based output shorthand,
- collection builder forms,
- automatic range expansion,
- comparator sugar,
- fast iteration `->`,
- implicit return semantics,
- compiler-known auto-construction.

### 2.3 Statement and Expression Lowering Must Be Separate

The transpiler shall maintain a strong distinction between statement-context lowering and expression-context lowering.

Statement constructs must not be lowered by wrapping them in lambda expressions or expression thunks unless no other semantically correct lowering exists.

In particular, the transpiler shall not treat the following as generic expression-thunk candidates:

- `if` statements,
- `while` statements,
- `for` statements,
- `break`,
- `continue`,
- `return`,
- declaration-based input,
- statement-based output.

### 2.4 Compile-Time Specialization Is a Feature

The transpiler is permitted and encouraged to hardcode efficient backend patterns for language intrinsics and contest-oriented constructs.

This is especially important for:

- input reading,
- output writing,
- fixed-shape array allocation,
- tuple-shaped input,
- collection builders,
- aggregation lowering.

### 2.5 Preserve Ordinary .NET Surface

Ordinary .NET and C# syntax that the language accepts as pass-through shall remain structurally recognizable in generated C# when practical.

Examples:

```txt
Queue<int> queue = new()
PriorityQueue<int, int> pq = new()
HashSet<int> set = new()
record struct Job(int Id, int Arrival, long Time) : IComparable<Job>
```

The transpiler shall not attempt to rename or normalize such APIs into language-owned aliases.

---

## 3. Compilation Pipeline

A conforming transpiler shall conceptually implement the following stages.

### 3.1 Lexing

Input source text is tokenized.

### 3.2 Parsing

Tokens are parsed into a syntax tree or syntax-preserving AST.

### 3.3 Name Binding and Scope Construction

The transpiler resolves identifiers, scopes, recursion eligibility, intrinsic names, and declaration categories.

### 3.4 Type and Shape Analysis

The transpiler performs type checking, shape checking, intrinsic validation, tuple projection validation, input/output shape validation, and known-collection detection.

### 3.5 High-Level Desugaring

Language-specific sugar is lowered into a more explicit semantic representation.

### 3.6 Backend-Oriented Lowering

Constructs are rewritten into a backend-friendly intermediate form suitable for direct C# emission.

### 3.7 C# Emission

The lowered program is emitted as C#.

### 3.8 Optional Formatting

Generated C# may be formatted or normalized for readability.

---

## 4. Internal Representations

A conforming implementation may choose any internal representation, but a staged model is recommended.

### 4.1 Suggested Representation Stack

```txt
TokenStream
SyntaxTree
AstTree
BoundTree
LoweredTree
CSharpTree or TextEmitter
```

### 4.2 Responsibilities by Stage

#### 4.2.1 SyntaxTree / AST

Must preserve source distinctions such as:

- input shorthand vs explicit call,
- output shorthand vs assignment,
- collection expressions with spread and range,
- builder forms,
- fast iteration `->`,
- space-separated calls,
- one-line `then` / `do`,
- pass-through type declarations,
- argument modifiers `ref`, `out`, `in`.

#### 4.2.2 BoundTree

Must annotate:

- symbol identities,
- mutability,
- recursion legality,
- expression types,
- block value behavior,
- comparison provider meaning,
- known-collection auto-construction eligibility,
- intrinsic-vs-ordinary API meaning.

#### 4.2.3 LoweredTree

Should remove most language sugar and leave only backend-meaningful structure.

---

## 5. Source Location Preservation

All syntax and AST nodes shall preserve source range information sufficient for:

1. diagnostics,
2. mapping generated code when possible,
3. future language server integration,
4. internal rewrite debugging.

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

## 6. Mandatory Frontend Distinctions

Before lowering begins, the transpiler shall distinguish the following at the semantic level.

### 6.1 Declarations

It must distinguish:

- immutable declaration,
- mutable declaration,
- inferred declaration,
- explicit typed declaration,
- declaration-based input shorthand,
- sized array declaration,
- compiler-known collection auto-construction declaration.

### 6.2 Output Forms

It must distinguish:

- `= expr` write statement,
- `+= expr` writeline statement,
- ordinary assignment `x += y`.

### 6.3 Call Forms

It must distinguish:

- ordinary parenthesized call,
- space-separated call,
- modified arguments (`ref`, `out`, `in`),
- out-variable declarations,
- `out _` discard.

### 6.4 Collection Forms

It must distinguish:

- plain collection literal,
- range element,
- spread element,
- builder element.

### 6.5 Iteration Forms

It must distinguish:

- ordinary `for ... in ...`,
- `while`,
- fast iteration `->`,
- aggregation iteration.

### 6.6 Return Behavior

It must determine:

- explicit return,
- implicit return,
- final bare invocation with no implicit return,
- void-like final expression statements.

---

## 7. Desugaring Boundaries

The transpiler shall eliminate sugar in a controlled order.

Recommended sequence:

1. declaration-based input and output shorthand classification,
2. recursion validation,
3. implicit return analysis,
4. space-separated call normalization,
5. comparator sugar lowering,
6. compiler-known auto-construction lowering,
7. collection expression lowering,
8. fast iteration lowering,
9. intrinsic aggregate call lowering,
10. sized declaration lowering,
11. backend I/O specialization.

This order is recommended because each stage benefits from prior semantic information.

---

## 8. Runtime Helper Minimization Policy

### 8.1 General Rule

The transpiler shall avoid generating generic runtime helpers for constructs that can be lowered directly to efficient C#.

### 8.2 Preferred Direct Lowering Targets

The following should be lowered directly whenever practical:

- scalar input shorthand,
- fixed-length array input shorthand,
- tuple input shorthand,
- statement-based output shorthand,
- sized array allocation,
- collection builder loops,
- intrinsic aggregate calls,
- binary `min` / `max`,
- `chmin` / `chmax`,
- range-based `for` loops,
- tuple assignment,
- comparator sugar.

### 8.3 Acceptable Runtime Helper Areas

Runtime helpers remain acceptable for:

- central scanner/writer implementation,
- rendering of tuples and one-dimensional collections,
- optional generic collection operations when no direct lowering is chosen,
- optional utility support for non-MVP features.

### 8.4 Prohibited Helper Overuse

The transpiler should not rely on broad generic helper wrappers of the form “evaluate arbitrary source semantics inside lambda thunks” for ordinary statements or loops.

It is specifically undesirable to encode control-flow structure through generated lambda expressions solely to force expression form.

---

## 9. Backend Target Contract

### 9.1 Primary Backend

The reference backend target is C#.

### 9.2 Emission Style Goals

Generated C# should be:

1. readable,
2. structurally close to source intent,
3. easy to debug,
4. compatible with ordinary .NET library usage,
5. efficient enough for contest use.

### 9.3 Entry Point Contract

A top-level source program shall lower to a C# entry point.

The transpiler may generate a wrapper type and `Main` method even when the source uses top-level statements.

---

## 10. Standard Backend I/O Contract

### 10.1 Input Backend Recommendation

The reference backend shall use buffered standard input, with at least the following acceptable baseline:

```csharp
StreamReader reader = new(Console.OpenStandardInput());
```

More specialized scanner implementations are permitted and encouraged.

### 10.2 Output Backend Recommendation

The reference backend shall use buffered standard output, with at least the following acceptable baseline:

```csharp
StreamWriter writer = new(Console.OpenStandardOutput(), bufferSize: 1048576);
```

### 10.3 Flushing Policy

Unless explicit flush semantics are required by the source program, the generated program should flush once at the logical end of execution.

If the source uses `stdout.flush()`, earlier flush points must be preserved.

### 10.4 Fast I/O Specialization

A conforming implementation may replace ordinary helper calls with a specialized scanner/writer implementation, including token readers, byte-buffer scanners, or direct loops.

The source semantics of `stdin` and `stdout` shall remain unchanged.

---

## 11. Name Preservation Policy

### 11.1 User Names

User-written identifiers should be preserved whenever possible.

### 11.2 .NET Names

Ordinary .NET names shall remain unchanged in emitted code unless a semantic rewrite requires otherwise.

### 11.3 Generated Temporaries

Compiler-generated temporaries shall use a reserved prefix unlikely to collide with user code.

Recommended prefixes:

```txt
__pscpTmp
__pscpIter
__pscpAcc
__pscpCmp
```

### 11.4 Intrinsic Helper Names

If generated helper types are emitted, they should use clearly reserved implementation names and must not collide with user symbols.

---

## 12. Lowering of Declarations

## 12.1 Immutable and Mutable Declarations

Examples:

```txt
let x = 1
var y = 2
mut int z = 3
```

These shall lower to ordinary local declarations in C#.

The emitter may choose `var` or explicit types in generated C# according to implementation policy, provided semantics are preserved.

## 12.2 Uninitialized Mutable Scalars

```txt
mut int x;
mut string s;
```

These shall lower to default-initialized declarations.

Suggested backend shape:

```csharp
int x = default;
string s = default;
```

## 12.3 Sized Arrays

```txt
int[n] a;
int[n][m] dp;
```

These shall lower to explicit allocation, not to a generic helper if a direct C# form is straightforward.

Suggested backend shapes:

```csharp
int[] a = new int[n];
```

and for nested arrays:

```csharp
int[][] dp = new int[n][];
for (int __pscpIter0 = 0; __pscpIter0 < n; __pscpIter0++)
    dp[__pscpIter0] = new int[m];
```

Equivalent helper-based allocation is permitted only if semantics and performance remain suitable.

## 12.4 Compiler-Known Auto-Construction

Declarations such as:

```txt
List<int> list;
Queue<int> queue;
PriorityQueue<int, int> pq;
```

shall lower to explicit `new()` construction.

Suggested backend shape:

```csharp
List<int> list = new();
Queue<int> queue = new();
PriorityQueue<int, int> pq = new();
```

This rewrite shall apply only to the compiler-known collection set defined by the language/API specification.

---

## 13. Lowering of Input Syntax

## 13.1 Scalar Input Shorthand

Source:

```txt
int n =
long x =
```

Required semantic meaning:

- read one value of the requested type,
- bind it to the declared variable.

### 13.1.1 Preferred Lowering

Preferred lowering is to direct scanner calls.

Example backend shape:

```csharp
int n = __pscpIn.Int();
long x = __pscpIn.Long();
```

### 13.1.2 Prohibited Over-Generalization

A generic `read<T>()` plus reflection or `Convert.ChangeType` shall not be the default preferred lowering strategy for performance-critical generated code.

It may exist as a fallback implementation, but the reference lowering should use type-specialized scanner methods.

## 13.2 Multiple Scalar Input Shorthand

Source:

```txt
int n, m =
```

shall lower to multiple typed reads, not line splitting.

Suggested backend shape:

```csharp
int n = __pscpIn.Int();
int m = __pscpIn.Int();
```

## 13.3 Fixed-Length Array Input

Source:

```txt
int[n] arr =
```

shall lower to:

1. array allocation,
2. explicit loop fill,
3. specialized scanner call per element or equivalent optimized implementation.

Suggested backend shape:

```csharp
int[] arr = new int[n];
for (int __pscpIter0 = 0; __pscpIter0 < n; __pscpIter0++)
    arr[__pscpIter0] = __pscpIn.Int();
```

The transpiler should prefer direct loops for fixed-shape reads over generic LINQ-based construction.

## 13.4 Nested Array Input

Source:

```txt
int[n][m] grid =
```

shall lower to nested allocation and nested read loops or equivalent specialized grid readers.

## 13.5 Tuple Input

Source:

```txt
(int, int) p =
(int, int)[m] edges =
```

shall lower to:

1. scalar reads grouped into tuples,
2. loops for tuple arrays.

Suggested backend shape for tuple array:

```csharp
(int, int)[] edges = new (int, int)[m];
for (int __pscpIter0 = 0; __pscpIter0 < m; __pscpIter0++)
    edges[__pscpIter0] = (__pscpIn.Int(), __pscpIn.Int());
```

---

## 14. Lowering of Output Syntax

## 14.1 Write Shorthand

Source:

```txt
= expr
```

shall lower to a write operation without newline.

## 14.2 WriteLine Shorthand

Source:

```txt
+= expr
```

shall lower to a write operation with newline.

## 14.3 Rendering Strategy

The transpiler may emit:

1. direct `writer.Write(...)` / `writer.WriteLine(...)`,
2. helper-based rendering calls,
3. direct join formatting for tuples and one-dimensional collections.

### 14.3.1 Preferred Strategy

For scalars, emit direct writes.

For tuples and one-dimensional collections, emit compact helper-assisted rendering or explicit join code.

### 14.3.2 Nested Collections

Nested collections should not use generic “format any enumerable recursively” machinery by default in the reference backend. Structured forms such as `stdout.grid` should lower to explicit row-wise output.

---

## 15. Lowering of Calls and Arguments

## 15.1 Space-Separated Calls

A validated space-separated call shall lower to an ordinary call node before C# emission.

Example:

```txt
foo x y
```

lowers semantically to:

```txt
foo(x, y)
```

before code generation.

## 15.2 Argument Modifiers

Arguments marked with `ref`, `out`, and `in` shall survive lowering as explicit modified-argument nodes until C# emission.

Examples:

```txt
foo ref x out y in z
foo(out int a, out int b)
foo(out _, ref arr[i])
```

shall emit using ordinary C# argument modifiers.

### 15.2.1 Out Variable Declarations

`out int a` shall not be desugared into separate declaration plus argument unless required by backend constraints.

Preferred backend emission preserves C# out-variable syntax.

### 15.2.2 Out Discard

`out _` shall emit as ordinary C# `out _`.

---

## 16. Lowering of Tuples

## 16.1 Tuple Construction

Tuple expressions shall lower to ordinary C# tuple expressions when possible.

## 16.2 Tuple Projection

Tuple projection:

```txt
p.1
p.2
```

shall lower to:

```csharp
p.Item1
p.Item2
```

## 16.3 Tuple Assignment

Tuple assignment such as:

```txt
(a, b) = (b, a)
(arr[i], arr[j]) = (arr[j], arr[i])
```

shall lower directly to C# tuple assignment when valid.

Fallback temporary-variable lowering is permitted if required by backend constraints.

---

## 17. Lowering of Comparator Sugar

## 17.1 Source Forms

Comparator sugar source forms are:

```txt
T.asc
T.desc
```

## 17.2 Semantic Meaning

The lowering shall preserve the meaning of:

- ascending default comparer for `T`,
- descending default comparer for `T`.

## 17.3 Backend Strategies

The implementation may lower comparator sugar to:

1. `Comparer<T>.Default`,
2. `Comparer<T>.Create(...)`,
3. specialized comparer objects,
4. specialized direct comparison lambdas,
5. backend-specific helper caches.

## 17.4 Reference Strategy

The reference lowering should prefer readable and reusable comparer objects rather than repeatedly emitting long inline comparer factory expressions when avoidable.

Examples of acceptable backend shapes:

```csharp
Comparer<int>.Default
Comparer<int>.Create((a, b) => b.CompareTo(a))
__pscpComparers.IntDesc
```

The exact emitted shape is implementation-defined.

---

## 18. Lowering of Collection Expressions

## 18.1 Plain Collection Literals

Plain collection literals shall lower according to target context.

Examples:

```txt
let a = [1, 2, 3]
int[] b = [1, 2, 3]
List<int> c = [1, 2, 3]
```

### 18.1.1 Preferred Lowering

- target array => emit array literal or explicit allocation,
- target known collection => emit constructor or fill sequence,
- ambiguous r-value => emit a backend-defined iterable or materialized collection according to contextual needs.

## 18.2 Automatic Range Expansion

Range elements inside collection expressions shall be expanded at compile time into a concrete builder strategy.

Example:

```txt
[0..<n]
```

should not normally survive to backend as a generic runtime range concatenation when target context requires a concrete array.

Preferred lowering is direct sized allocation plus loop fill when possible.

## 18.3 Spread Elements

Spread elements shall preserve order.

Preferred lowering strategies:

1. compute total size where possible,
2. allocate target collection once if size is known,
3. append in source order.

It is acceptable in v0.3 to use simpler concatenation-based lowering if semantics are correct, but future optimization should target direct construction.

## 18.4 Builder Forms

Source:

```txt
[0..<n -> i do i * i]
[arr -> x do f(x)]
```

shall lower to explicit builders and loops, not to nested LINQ pipelines by default.

Preferred backend shape for array-targeted contexts:

1. determine size if known,
2. allocate array,
3. fill in one pass.

Fallback list-builder plus final conversion is acceptable when size is not statically available.

---

## 19. Lowering of Ranges

## 19.1 Context-Dependent Policy

Range lowering shall be context-sensitive.

### 19.1.1 Loop Context

For loop contexts such as:

```txt
for i in 0..<n { ... }
```

ranges should lower directly to C# `for` loops whenever possible.

### 19.1.2 Builder Context

For collection builders, ranges should lower to fill loops rather than generic runtime enumerables when the target result is materialized.

### 19.1.3 Standalone Expression Context

A standalone range value may lower to a helper enumerable or lazy iterable if no more specific context exists.

## 19.2 Step Handling

Stepped ranges such as:

```txt
10..-1..0
```

shall preserve source semantics exactly.

Loop emission must choose the correct comparison direction and step application.

---

## 20. Lowering of Fast Iteration `->`

## 20.1 Source Forms

```txt
xs -> x { ... }
xs -> x do expr
xs -> i, x { ... }
```

## 20.2 General Lowering

Fast iteration shall lower to ordinary loop constructs.

### 20.2.1 Preferred Cases

- if source supports random access and an index is requested, lower to indexed `for`,
- otherwise lower to `foreach`,
- if no index is requested, ordinary `foreach` is preferred unless a more efficient known shape exists.

## 20.3 One-Line `do`

One-line bodies shall be normalized to ordinary statement/block form before backend emission.

---

## 21. Lowering of Intrinsic Aggregate Calls

### 21.1 Principle

Intrinsic aggregate calls should lower to explicit loops and accumulators in the reference implementation rather than to generic library chains.

### 21.2 Binary `min` and `max`

Source:

```txt
min a b
max a b
```

Preferred lowering shape:

1. direct conditional comparison when straightforward,
2. `Math.Min` / `Math.Max`-equivalent lowering for supported primitive numeric types,
3. comparer-based comparison for other ordered types.

### 21.3 Aggregate `min`, `max`, and `sum`

Source:

```txt
min arr
max arr
sum arr
min [0..<n -> i do a[i] - b[i]]
sum [0..<n -> i {
    let x = f(i)
    x
}]
```

Preferred lowering shape:

1. allocate accumulator local(s),
2. track initialization state for `min` / `max` if necessary,
3. iterate explicitly,
4. update accumulator,
5. yield final value.

### 21.4 `minBy`, `maxBy`, and `sumBy`

Source:

```txt
minBy values keySelector
maxBy values keySelector
sumBy values selector
```

Preferred lowering shape:

1. iterate explicitly,
2. compute selector/key value once per element,
3. track current best element and best key for `minBy` / `maxBy`,
4. accumulate selector result for `sumBy`.

### 21.5 `chmin` and `chmax`

Source:

```txt
chmin ref target value
chmax ref target value
```

Preferred lowering shape:

1. lower to direct conditional assignment when the target is a simple assignable backend target,
2. return a boolean indicating whether an update occurred.

Preferred backend style:

```csharp
if (value < target) { target = value; return true; }
return false;
```

or the `chmax` equivalent.

The transpiler should avoid preserving `chmin` / `chmax` as ordinary runtime helper calls when a direct rewrite is straightforward.

### 21.6 Why Loop/Conditional Lowering Is Preferred

This strategy:

1. avoids unnecessary iterator allocation,
2. avoids unnecessary runtime helper calls,
3. avoids ref-related helper overhead for `chmin` / `chmax`,
4. preserves control over temporaries,
5. keeps generated C# readable.

---

## 22. Lowering of One-Line `then` and `do`

One-line forms shall not survive into the final lowered tree.

They are normalized into ordinary block or expression forms during desugaring.

Examples:

```txt
if x < 0 then -x else x
for i in 0..<n do sum += a[i]
```

shall lower to the same semantic structures used for multiline forms.

---

## 23. Lowering of Implicit Return

## 23.1 Analysis Phase

Implicit return behavior shall be resolved before C# emission.

## 23.2 Lowering Rule

A block that yields a value implicitly shall be rewritten into explicit return flow appropriate to its context.

### 23.2.1 Function Body Context

If a function body ends with an implicit value expression, the backend should emit an explicit `return` statement.

Example source:

```txt
int cmp(int a, int b) {
    a <=> b
}
```

Preferred backend shape:

```csharp
int cmp(int a, int b)
{
    return /* lowered compare */;
}
```

### 23.2.2 Expression Context

If a block expression appears inside another expression context, the transpiler shall lower it to a backend-compatible expression strategy. If C# cannot express it directly, the transpiler should rewrite surrounding code to statement form rather than encoding full statement logic inside lambda thunks.

## 23.3 Bare Invocation Rule

A final bare invocation shall not be treated as an implicit return. If the enclosing function requires a value, the transpiler shall surface an error or earlier diagnostic rather than silently reinterpret it.

---

## 24. Lowering of Pass-Through C# Type Declarations

## 24.1 Class, Struct, Record

Type declarations using `class`, `struct`, and `record` shall be preserved structurally and emitted as ordinary C# declarations when supported by the accepted subset.

## 24.2 Base Types and Interfaces

Base-type and interface lists shall be preserved and emitted directly.

## 24.3 Members

Fields, constructors, methods, nested declarations, `this`, `base`, and `new()` shall be preserved and emitted with minimal rewriting unless required by other language sugar.

## 24.4 Record Primary Forms

Record-like primary constructor forms shall lower to ordinary C# record or record struct syntax when supported by the backend target version.

---

## 25. Lowering of `ref`, `out`, and `in`

## 25.1 Principle

`ref`, `out`, and `in` preserve ordinary C# meaning and should survive nearly unchanged to code generation.

## 25.2 Backend Emission

Modified arguments shall emit as ordinary C# modified arguments.

Examples:

```txt
foo(ref x, out y, in z)
foo(out int a, out int b)
foo(out _, ref arr[i])
```

shall preserve those forms in emitted C# whenever valid.

## 25.3 Space-Call Normalization

Space-separated forms using modifiers shall first normalize to ordinary call nodes with modified arguments before emission.

---

## 26. Lowered Core Language Recommendation

Before final C# emission, the implementation should reduce the program to a manageable lowered core.

### 26.1 Lowered Statement Set

Recommended lowered statements:

```txt
BlockStmt
LocalDeclStmt
AssignmentStmt
ExprStmt
IfStmt
WhileStmt
ForStmt or ForEachStmt
ReturnStmt
BreakStmt
ContinueStmt
TypeDeclStmt / TypeDeclMember
```

### 26.2 Lowered Expression Set

Recommended lowered expressions:

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
ObjectCreationExpr
ArrayCreationExpr
TupleProjectionExpr
```

Collection builders, aggregations, fast iteration, shorthand I/O, and comparator sugar should not survive beyond this point except as backend-specific helper calls when deliberately chosen.

---

## 27. C# Emission Strategy

## 27.1 Emitter Architecture

A conforming implementation should separate:

1. statement emission,
2. expression emission,
3. type emission,
4. member/type declaration emission,
5. helper/runtime emission.

Recommended emitter interfaces:

```txt
EmitStatement(stmt)
EmitExpression(expr)
EmitType(type)
EmitMember(member)
EmitProgram(program)
```

## 27.2 Expression/Statement Discipline

If an expression cannot be emitted naturally in C#, the correct response is to lower it further, not to force a lambda-thunk encoding unless explicitly intended.

## 27.3 Formatting and Parentheses

The emitter shall add parentheses as needed to preserve source precedence.

## 27.4 Modern C# Preservation

The emitter may preserve modern C# syntax such as:

- target-typed `new()`,
- tuple assignment,
- records,
- collection/object constructors,
- expression-bodied members when convenient.

This is permitted but not required.

---

## 28. Helper Runtime Strategy

## 28.1 Minimal Recommended Helper Set

The generated backend may emit a small helper runtime for:

- scanner input,
- buffered output,
- rendering of tuples and one-dimensional collections,
- optional collection helpers not directly lowered.

### 28.1.1 Recommended Helper Names

```txt
__PscpInput
__PscpOutput
__PscpRender
__PscpComparers
```

These are recommendations only.

## 28.2 Avoid Generic Mega-Helpers

The implementation should avoid central mega-helper objects that attempt to encode all source semantics dynamically through:

- `dynamic`,
- reflection-based conversion for ordinary hot paths,
- generic expression wrappers,
- ubiquitous LINQ pipelines in hot or structurally simple cases.

---

## 29. Performance-Oriented Rewriting Guidance

The following rewrites are encouraged for the reference transpiler.

### 29.1 Favor Direct Loops Over LINQ for Lowered Sugar

Especially for:

- collection builders,
- fixed-size reads,
- aggregations,
- range-based fills,
- shaped input.

### 29.2 Favor Specialized Scanner Calls Over Generic Conversion

Use type-specific read methods in the reference backend.

### 29.3 Favor Buffered Writes Over Per-Element Console Calls

Use a buffered writer contract for output.

### 29.4 Avoid Capturing Lambdas Introduced Solely by the Transpiler

Do not introduce backend lambdas merely to simulate control-flow structures that can be emitted directly.

### 29.5 Preserve Ordinary User Lambdas

User-written lambdas are part of source semantics and must be preserved or equivalently lowered. This rule applies only to compiler-invented lambdas.

---

## 30. Diagnostics During Lowering and Emission

The transpiler shall produce clear diagnostics when backend lowering cannot preserve source semantics directly.

Required areas include:

1. unsupported pass-through construct in current backend subset,
2. invalid use of comparator sugar,
3. unsupported input/output shape,
4. invalid auto-construction target,
5. illegal implicit return in value-returning context,
6. unsupported block-expression lowering in expression context,
7. invalid modified argument form,
8. impossible or unsupported record/type declaration subset.

Diagnostics should reference source spans, not only generated-code spans.

---

## 31. Suggested Development Order

A practical transpiler development order is:

1. core syntax to AST,
2. binding and mutability checks,
3. sized declarations,
4. explicit `return`/control-flow lowering,
5. space-call normalization,
6. input/output shorthand lowering,
7. tuple projection and tuple assignment,
8. compiler-known collection auto-construction,
9. collection expression lowering,
10. range lowering,
11. fast iteration lowering,
12. aggregation lowering,
13. comparator sugar,
14. C# type/member emission,
15. fast I/O backend generation,
16. cleanup and formatting.

This order prioritizes correctness of core control flow and data movement before optimization-oriented rewrites.

---

## 32. Conformance

A transpiler conforms to this specification if it:

1. preserves source semantics defined by the language specification,
2. respects the intrinsic API semantics defined by the API specification,
3. lowers language sugar without changing observable behavior,
4. preserves pass-through ordinary C#/.NET surface areas accepted by the language,
5. does not rely on expression-thunk encoding for ordinary statement constructs where direct lowering is available,
6. emits valid C# for all supported source constructs,
7. maintains correct semantics for input/output shorthand, collection builders, ranges, comparator sugar, fast iteration, implicit returns, compiler-known auto-construction, and modified arguments.

Optimization strategy and exact helper implementation remain implementation-defined.

---

## 33. Versioning

This document defines transpiler draft version `v0.3`.

Backward compatibility is not guaranteed across draft revisions.

