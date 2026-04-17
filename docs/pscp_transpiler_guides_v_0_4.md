# PSCP Transpiler Guides v0.4

## Purpose

This document is split into two parts:

- **Part I — Implementation Guide**
- **Part II — Optimization Guide**

The purpose of this document is practical:

1. define a recommended structure for implementing the PSCP transpiler,
2. define where syntax and semantics must be resolved,
3. define which constructs must lower directly rather than through generic helpers,
4. define which optimizations are expected from a reference-quality backend.

This document complements the formal language and API specifications.

---

# Part I — Implementation Guide

## 1. Overall Goal

The PSCP transpiler should transform PSCP source into valid, readable, and efficient C#.

The transpiler should:

- preserve PSCP source semantics,
- preserve pass-through C#/.NET surface where appropriate,
- eliminate PSCP-specific sugar early,
- keep lowering decisions explicit and predictable,
- avoid pushing too much meaning into runtime helpers.

---

## 2. Recommended Pipeline

A practical implementation pipeline is:

1. lexing,
2. parsing,
3. syntax-preserving AST construction,
4. name binding,
5. type and shape analysis,
6. semantic desugaring,
7. backend-oriented lowering,
8. C# emission,
9. optional formatting.

Suggested internal stages:

```txt
TokenStream
SyntaxTree
AstTree
BoundTree
LoweredTree
CSharpEmitter
```

---

## 3. Mandatory Architectural Rule

The transpiler must clearly separate:

- **statement lowering**
- **expression lowering**

Do not solve ordinary statement lowering by wrapping control flow into compiler-generated lambdas or expression thunks.

The following must remain statement-lowered constructs unless there is no correct alternative:

- `if` statements,
- `while` statements,
- `for` statements,
- `break`,
- `continue`,
- `return`,
- declaration-based input shorthand,
- statement-based output shorthand.

---

## 4. Source Location Preservation

All syntax and AST nodes should preserve source locations sufficient for:

- diagnostics,
- mapping generated code when useful,
- future LSP integration,
- rewrite debugging.

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

## 5. Frontend Responsibilities

Before lowering begins, the frontend must already know the difference between at least the following categories.

### 5.1 Declarations

It must distinguish:

- immutable declaration,
- mutable declaration,
- inferred declaration,
- explicit typed declaration,
- declaration-based input shorthand,
- sized array declaration,
- known collection auto-construction declaration,
- target-typed allocation shorthand declarations.

### 5.2 Output Statements

It must distinguish:

- `= expr` write statement,
- `+= expr` writeline statement,
- ordinary `+=` assignment,
- known data-structure operator rewrite candidates.

### 5.3 Calls

It must distinguish:

- ordinary parenthesized call,
- space-separated call,
- modified arguments (`ref`, `out`, `in`),
- out-variable declarations,
- `out _` discard,
- intrinsic aggregate calls,
- conversion-keyword calls.

### 5.4 Collections and Generators

It must distinguish:

- materialized collection expression `[...]`,
- materialized builder,
- generator expression `(...)` with `for` or `->`,
- range element,
- spread element.

### 5.5 Assignment Forms

It must distinguish:

- ordinary assignment statements,
- compound assignments,
- explicit assignment expression `:=`.

### 5.6 Type-Body Shorthand

It must distinguish:

- pass-through access modifiers (`public`, `private`, ...),
- access section labels (`public:`, `private:`, ...),
- `operator<=>(other)` shorthand,
- ordinary pass-through operator/member declarations.

---

## 6. Suggested Core AST Concepts

A practical frontend should preserve distinct AST nodes for the following before lowering:

- `InputDeclStmt`
- `SizedArrayDeclStmt`
- `OutputStmt`
- `SpaceCallExpr`
- `GeneratorExpr`
- `CollectionExpr`
- `RangeExpr`
- `SpreadElement`
- `BuilderElement`
- `FastForStmt`
- `AssignmentExpr` for `:=`
- `AggregateCallExpr`
- `ConversionCallExpr`
- `KnownDsOperatorExpr`
- `SectionLabelDecl`
- `OrderingShorthandDecl`

The exact names are implementation-defined, but the semantic distinction must exist.

---

## 7. Parsing Rules That Must Be Enforced Rigorously

### 7.1 `public` vs `public:`

These are different tokens-in-context and must not be conflated.

- `public`  = pass-through C# modifier
- `public:` = PSCP section label

The same applies to:

- `private`
- `private:`
- `protected`
- `protected:`
- `internal`
- `internal:`

### 7.2 Generator Detection in Parentheses

A parenthesized form becomes a `GeneratorExpr` only if the directly enclosed form contains a recognized iterator construct using `for` or `->`.

Examples that should parse as generators:

```txt
(0..<n -> i do i * i)
(0..<n -> i { i * i })
(for i in 0..<n do a[i])
```

Examples that should remain ordinary parenthesized expressions:

```txt
(1 + 2)
(x)
(a, b)
(0..<n)
```

### 7.3 Output Shorthand at Statement Start Only

`= expr` and `+= expr` are output statements only at statement start.

### 7.4 `:=`

`:=` must parse as a dedicated value-yielding assignment operator and must not be merged with ordinary assignment.

### 7.5 Known Data-Structure Operator Candidates

Expressions such as:

```txt
visited += me
--stack
~queue
```

must first parse as ordinary operators. Known-data-structure rewriting happens later during binding/lowering based on receiver type.

---

## 8. Binding and Semantic Analysis

The binder must resolve:

- symbol identity,
- mutability,
- recursion legality,
- expression types,
- known collection auto-construction eligibility,
- known data-structure operator applicability,
- aggregate intrinsic overload family selection,
- conversion-keyword intent,
- ordering-provider availability for `T.asc` / `T.desc`,
- section-label intent for methods,
- block value behavior.

---

## 9. Discard Semantics

PSCP `_` is always a semantic discard.

Backend rule:

- emit true C# discards where C# supports them,
- otherwise emit generated unused names such as `__pscpDiscard0`,
- never allow source `_` to become an accidentally meaningful variable in generated backend semantics.

This matters especially for:

- lambda parameters,
- loop variables,
- deconstruction bindings,
- synthetic locals introduced during lowering.

---

## 10. Desugaring Order

A practical desugaring order for `v0.4` is:

1. classify input/output shorthand,
2. resolve section labels and default source-level method accessibility,
3. resolve recursion legality,
4. resolve implicit-return behavior,
5. normalize space-separated calls,
6. resolve conversion-keyword calls,
7. resolve aggregate intrinsic families,
8. resolve known data-structure operator rewrites,
9. lower comparator sugar,
10. lower target-typed allocation shorthand,
11. lower materialized collection and generator semantics,
12. lower fast iteration,
13. lower sized declarations,
14. lower ordering shorthand in type bodies,
15. lower remaining backend-specific constructs.

---

## 11. Lowering of Input Syntax

### 11.1 Scalar Input

```txt
int n =
```

Preferred lowering shape:

```csharp
int n = __pscpIn.Int();
```

### 11.2 Multiple Scalars

```txt
int n, m =
```

Preferred lowering shape:

```csharp
int n = __pscpIn.Int();
int m = __pscpIn.Int();
```

### 11.3 Fixed-Length Arrays

```txt
int[n] arr =
```

Preferred lowering shape:

```csharp
int[] arr = new int[n];
for (int __pscpIter0 = 0; __pscpIter0 < n; __pscpIter0++)
    arr[__pscpIter0] = __pscpIn.Int();
```

### 11.4 Tuple Arrays

```txt
(int, int)[m] edges =
```

Preferred lowering shape:

```csharp
(int, int)[] edges = new (int, int)[m];
for (int __pscpIter0 = 0; __pscpIter0 < m; __pscpIter0++)
    edges[__pscpIter0] = (__pscpIn.Int(), __pscpIn.Int());
```

The reference backend should avoid generic reflection-like read helpers for these hot paths.

---

## 12. Lowering of Output Syntax

### 12.1 `= expr`

Lower to a write-without-newline operation.

### 12.2 `+= expr`

Lower to a write-with-newline operation.

### 12.3 Rendering

Preferred strategy:

- scalars -> direct writer calls,
- tuples -> compact tuple rendering,
- one-dimensional collections -> join-like rendering,
- structured nested collections -> explicit structured writers such as `stdout.grid`.

---

## 13. Lowering of Materialized Collections

### 13.1 `[]` Means Concrete Result

A materialized collection expression should lower to concrete array/list/linked-list construction according to target context.

### 13.2 Array-Targeted Context

Prefer direct allocation and fill.

### 13.3 Known Collection Target

Prefer direct collection construction and append.

### 13.4 Materialized Range Use

A range inside `[]` should not survive as a generic runtime range helper if direct fill is straightforward.

---

## 14. Lowering of Generator Expressions

### 14.1 Principle

A generator expression is an iterable-producing form and should not imply materialization.

### 14.2 Aggregate Consumers

When consumed by aggregate intrinsics such as `sum`, `min`, `max`, `sumBy`, `minBy`, `maxBy`, the reference backend should prefer direct loop lowering rather than generating an intermediate iterator object if direct inlining is practical.

### 14.3 Ordinary Iterable Contexts

When used in a broader iterable context, a helper enumerable or generated iterator may be acceptable.

---

## 15. Lowering of Ranges

### 15.1 Context-Sensitive Policy

Range lowering is context-sensitive.

### 15.2 Pure Loop Context

For simple numeric loops:

```txt
for i in 0..<n { ... }
0..<n -> i { ... }
```

prefer direct C# `for` loops.

### 15.3 Materialized Builder Context

For:

```txt
[0..<n -> i do f(i)]
```

prefer direct allocation and direct fill.

### 15.4 Aggregate Generator Context

For:

```txt
sum (0..<n -> i do f(i))
```

prefer direct loop accumulation.

### 15.5 Standalone Iterable Context

A helper iterable is acceptable if no more direct lowering is appropriate.

---

## 16. Lowering of Fast Iteration `->`

### 16.1 Core Rule

The reference backend must not lower simple numeric range iteration through helper-based `foreach` if direct `for` lowering is possible.

This applies to:

- statement-form fast iteration over numeric ranges,
- indexed fast iteration over numeric ranges,
- range-based builder lowering,
- range-based aggregate generator lowering.

### 16.2 Preferred Cases

- range + index binding -> direct `for`,
- random-access collection + requested index -> direct indexed `for` where profitable,
- otherwise -> `foreach` is acceptable.

---

## 17. Lowering of Aggregate Intrinsic Families

Aggregate intrinsics should lower as regular calls at the source/API level, but as direct loops or compare trees in the reference backend.

### 17.1 Binary and Variadic `min` / `max`

- small fixed arity -> direct compare tree,
- primitive numeric cases -> direct comparison or `Math.Min`/`Math.Max` equivalent,
- ordered non-primitive cases -> comparer-driven compare tree.

### 17.2 Iterable `min` / `max`

- iterate explicitly,
- track initialization state,
- update current best.

### 17.3 `sum`

- iterate explicitly,
- accumulate directly.

### 17.4 `sumBy`

- iterate explicitly,
- evaluate selector once per element,
- accumulate selector result.

### 17.5 `minBy` / `maxBy`

- iterate explicitly,
- evaluate key once per element,
- track current best element and key.

### 17.6 `chmin` / `chmax`

Prefer direct conditional rewrites where possible.

Preferred backend style:

```csharp
if (value < target) { target = value; return true; }
return false;
```

or the `chmax` equivalent.

---

## 18. Lowering of Conversion-Keyword Calls

### 18.1 Principle

Conversion-keyword calls should be classified semantically and then lowered according to source and target type categories.

### 18.2 Preferred Strategy by Category

#### 18.2.1 String -> Numeric

Lower to parse calls.

#### 18.2.2 Numeric -> Numeric

Lower to direct cast/convert expressions.

#### 18.2.3 Bool -> Numeric

Lower to conditional `0/1` or equivalent direct conversion.

#### 18.2.4 Numeric -> Bool

Lower to `value != 0` style semantics.

#### 18.2.5 String -> Bool

Lower to broad truthiness logic such as empty/non-empty string checks according to the language/API contract.

The reference backend should avoid routing these hot scalar conversions through generic reflection-based machinery.

---

## 19. Lowering of `:=`

### 19.1 Meaning

`lhs := rhs` is a value-yielding assignment.

### 19.2 Lowering Strategy

If C# can express the assignment naturally in the surrounding expression context, emit a direct assignment expression.

Otherwise, lower by introducing temporaries in a way that preserves:

1. side-effect order,
2. assigned value result,
3. readability where practical.

---

## 20. Lowering of Known Data Structure Operator Rewrites

These rewrites are backend-integrated and must not be implemented as generic runtime wrappers.

### 20.1 `HashSet<T>`

- `set += x` -> `set.Add(x)`
- `set -= x` -> `set.Remove(x)`

The returned `bool` must be preserved.

### 20.2 `Stack<T>`

- `s += x` -> `s.Push(x)`
- `~s` -> `s.Peek()`
- `--s` -> `s.Pop()`

### 20.3 `Queue<T>`

- `q += x` -> `q.Enqueue(x)`
- `~q` -> `q.Peek()`
- `--q` -> `q.Dequeue()`

### 20.4 `PriorityQueue<TElement, TPriority>`

- `pq += (item, priority)` -> enqueue,
- `~pq` -> item returned by peek,
- `--pq` -> item returned by dequeue.

If priority is required, ordinary explicit APIs such as `TryPeek` / `TryDequeue` should remain visible and unchanged.

---

## 21. Lowering of `new[n]` and `new![n]`

### 21.1 `new[n]`

Resolve the target type from context, then emit array allocation of the correct element type.

### 21.2 `new![n]`

Resolve the target element type from context.

Then:

1. allocate the array,
2. emit initialization for each element using implicit `new()` according to known collection rules.

This rewrite must be rejected if the element type is not in the supported known collection set.

---

## 22. Lowering of Access Section Labels

### 22.1 Nature of the Feature

Access section labels are source-level intent carriers, not fully enforced backend access-control declarations.

### 22.2 Required Behavior

The transpiler should:

- recognize `public:`, `private:`, `protected:`, `internal:` labels,
- track intended accessibility for following methods lacking explicit modifiers,
- optionally emit warnings and perform access-violation checks.

### 22.3 Non-Blocking Policy

Access section labels must not block transpilation.

The backend may still emit methods as `public`.

This policy applies only to colon-suffixed section labels.
Ordinary pass-through modifiers without colon are not part of this lightweight section-label behavior.

---

## 23. Lowering of Ordering Shorthand in Type Bodies

`operator<=>(other)` shorthand should lower to a representation that preserves the type's default ordering.

Possible backend strategies include:

1. generating or wiring a `CompareTo` method,
2. generating a comparison helper used by comparer sugar,
3. emitting direct comparison members depending on backend style.

The shorthand must behave as a source-level ordering definition, not merely as a random helper method.

---

## 24. Pass-Through Surface Preservation Rules

The transpiler should preserve pass-through C#/.NET surface wherever practical.

This includes:

- `namespace`,
- `using`,
- `new`, `this`, `base`,
- class/struct/record declarations,
- generic type syntax,
- `is`, `is not`,
- switch-expression surface,
- ordinary access modifiers without colon,
- ordinary .NET type/member names.

The transpiler should not reinterpret these as PSCP-specific sugar unless explicitly required by a separate PSCP rule.

---

# Part II — Optimization Guide

## 25. Optimization Philosophy

The optimization goal of the PSCP transpiler is not abstract whole-program cleverness.

Instead, the reference backend should focus on a very specific style of optimization:

1. detect common contest-oriented source shapes early,
2. avoid generating needless runtime abstractions,
3. turn high-level PSCP sugar into direct C# control flow and storage operations,
4. preserve readability and debuggability of generated code.

The best optimization in PSCP is often an early structural decision:

- do not allocate if allocation is not semantically required,
- do not enumerate if direct indexing is possible,
- do not wrap in lambdas if statements can be emitted directly,
- do not defer fixed semantics to reflection, `dynamic`, or generic helpers.

In other words, PSCP optimization is primarily **desugaring discipline plus shape-driven lowering**, not late cleverness.

---

## 26. Optimization Legality Rules

Every optimization must preserve:

1. source evaluation order,
2. source side effects,
3. mutation timing,
4. exception behavior where observable and relevant,
5. implicit vs explicit materialization intent,
6. precise truthiness/conversion semantics defined by the API specification,
7. value-yielding behavior of `:=`,
8. return-value preservation for known data-structure operator rewrites.

Optimizations must never silently change:

- whether a collection is materialized,
- whether a generator escapes,
- whether an operation returns a value,
- whether a target is mutated before or after another side effect.

---

## 27. Optimization Tiers

A useful model is to divide optimizations into tiers.

### 27.1 Tier A — Mandatory Reference Optimizations

These should exist in any serious `v0.4` implementation.

1. direct `for` lowering for simple numeric ranges,
2. direct loop lowering for aggregate calls over ranges/builders/generators,
3. direct scanner calls for scalar input shorthand,
4. direct loop fill for shaped array input,
5. direct compare-update lowering for `chmin` / `chmax`,
6. elimination of redundant materialization for generator-fed aggregates,
7. discard-correct lowering,
8. no `dynamic` in core aggregate/reference lowering,
9. no helper-enumerable lowering for simple range loops.

### 27.2 Tier B — Strongly Recommended Optimizations

1. pre-sizing materialized builders when final length is statically known,
2. direct fill for `[0..<n -> ...]` into arrays,
3. direct construction for `new![n]`,
4. direct lowering of `HashSet +=`, `Queue +=`, `Stack +=`,
5. direct compare trees for small-arity `min` / `max`,
6. special handling for range-fed slicing and copying,
7. tuple deconstruction temporary minimization,
8. local constant folding after desugaring,
9. direct lowering of conversion-keyword calls for common scalar cases.

### 27.3 Tier C — Optional Advanced Optimizations

1. loop fusion for aggregate over generator chains,
2. range bound hoisting,
3. allocation-free tuple rendering in some output paths,
4. comparer caching for `T.asc` / `T.desc`,
5. dead temporary elimination,
6. local common-subexpression elimination inside generated hot loops,
7. pattern-driven simplification of nested compare trees.

---

## 28. Cost Model

A reference backend should treat the following as expensive by default:

1. heap allocation,
2. iterator object creation,
3. closure allocation,
4. `dynamic` dispatch,
5. reflection-like conversion,
6. repeated comparer construction,
7. repeated delegate construction,
8. repeated string allocation in tight output loops,
9. unnecessary tuple construction in hot paths,
10. repeated bounds-check-heavy enumeration where indexed access would suffice.

The backend should treat the following as cheap and preferred when semantically valid:

1. direct `for` loops,
2. simple local temporaries,
3. direct field/property/method calls on ordinary .NET types,
4. direct `if`/`while` lowering,
5. direct scalar parse/cast calls,
6. single-pass accumulation.

---

## 29. Mandatory Anti-Patterns to Avoid

The following should be treated as prohibited or strongly discouraged in the reference backend.

### 29.1 Helper Enumerable for Simple Ranges

Do **not** lower:

```txt
0..<n -> i { ... }
```

into:

```csharp
foreach (var i in SomeRangeHelper(...)) { ... }
```

when a direct `for` loop is possible.

### 29.2 Reflection-Like Scalar Conversion in Hot Paths

Do **not** rely on generic `Convert.ChangeType`-style strategies for default scalar input lowering or common conversion-keyword lowering.

### 29.3 `dynamic` for Core Numeric Aggregates

Do **not** implement core `sum`, `sumBy`, `min`, `max`, compare helpers, or compare-update helpers through `dynamic` in the reference backend.

### 29.4 Compiler-Generated Lambda Thunks for Ordinary Statements

Do **not** encode statement control flow through compiler-generated lambdas merely to force expression form.

### 29.5 Forced Materialization of Generator-Fed Aggregates

Do **not** lower:

```txt
sum (0..<n -> i do f(i))
```

into:

```csharp
ToArray(...).Sum()
```

by default.

### 29.6 Blind LINQ Lowering for Simple Shapes

Do **not** default to LINQ for:

- shaped input,
- simple builders,
- common aggregate calls,
- range loops,
- `new![n]` initialization,
- direct `HashSet` / `Queue` / `Stack` rewrites.

### 29.7 Losing Return Values of Known Data-Structure Operators

Do **not** discard the boolean result of `HashSet<T>.Add` / `Remove` when the source expression is used in a boolean context.

---

## 30. Canonical Lowered Forms

Optimization is easier if the backend lowers source constructs into a small set of canonical shapes before emission.

Recommended canonical statement shapes:

- `LocalDeclStmt`
- `AssignmentStmt`
- `ExprStmt`
- `IfStmt`
- `WhileStmt`
- `ForStmt`
- `ForEachStmt`
- `ReturnStmt`
- `BreakStmt`
- `ContinueStmt`
- `TypeDeclStmt`

Recommended canonical expression shapes:

- `LiteralExpr`
- `IdentifierExpr`
- `TupleExpr`
- `UnaryExpr`
- `BinaryExpr`
- `CallExpr`
- `MemberAccessExpr`
- `IndexExpr`
- `SliceExpr`
- `ConditionalExpr`
- `AssignmentExpr`
- `ObjectCreationExpr`
- `ArrayCreationExpr`

Anything outside this set should be aggressively lowered before final C# emission.

---

## 31. Fast Input Optimization Guide

## 31.1 Core Rule

Scalar and fixed-shape input should use specialized scanner calls.

### 31.1.1 Good

```csharp
int n = __pscpIn.Int();
```

### 31.1.2 Bad Default

```csharp
int n = Convert.ToInt32(ReadGeneric());
```

## 31.2 Specialized Reader Set

A reference backend should ideally expose specialized scanner entry points for at least:

- `Int()`
- `Long()`
- `Double()`
- `Decimal()`
- `StringToken()`
- `CharToken()`
- `BoolToken()` or equivalent

## 31.3 Fixed-Length Array Reads

Prefer:

1. allocate once,
2. fill with a direct loop,
3. call the specialized scanner per element.

Avoid:

- `Enumerable.Range(...).Select(...).ToArray()` for default shaped-input lowering.

## 31.4 Tuple Reads

Tuple arrays should lower to a single explicit loop.

If the tuple arity is known and small, inline the per-field reads directly.

## 31.5 Grid Reads

Prefer nested loops or specialized grid helpers over nested generic enumerables.

## 31.6 Line-Based Readers

`stdin.line()`, `stdin.lines(n)`, `stdin.words()`, and `stdin.chars()` may use line-based helpers, but hot token-based scalar reads should not degrade into repeated line splitting by default.

## 31.7 Input Batching

If the backend uses a scanner abstraction, it should cache buffer state and avoid per-token string splitting when a lower-level scan is available.

---

## 32. Fast Output Optimization Guide

## 32.1 Core Rule

Use buffered output.

Reference backends should prefer buffered `StreamWriter`-style output or equivalent.

## 32.2 Scalar Writes

Lower scalar writes directly.

## 32.3 Tuple and 1D Collection Writes

Prefer compact specialized rendering helpers.

Avoid recursive “format anything enumerable” machinery on the hot path.

## 32.4 Structured Grid Output

Use explicit row-wise emission for `stdout.grid` or equivalent structured writers.

## 32.5 Join Specialization

For one-dimensional arrays/lists of simple printable scalar types, specialized join rendering is preferable to general recursive formatting.

## 32.6 Interpolated String Optimization

Interpolated strings should lower to the most direct backend form available.

Avoid building many temporary concatenations when a backend-native interpolated string or efficient builder form is available.

## 32.7 Output in Tight Loops

When lowering repeated `+= expr` inside hot loops, prefer direct writer calls with minimal helper overhead.

---

## 33. Range Optimization Guide

## 33.1 Detect Direct Numeric Range Loops Early

If a range is:

- integer/long-based,
- monotonic,
- used in loop context,

prefer direct `for` lowering.

### 33.1.1 Inclusive / Exclusive Handling

The inclusive/exclusive choice should be encoded in the generated loop condition, not passed as a runtime boolean helper flag in hot simple cases.

### 33.1.2 Stepped Ranges

Stepped ranges should still prefer direct loops when step direction and endpoint test are statically clear.

### 33.1.3 Constant Step Fast Path

If the step is a compile-time constant and nonzero, emit the direct loop immediately.

### 33.1.4 Descending Range Fast Path

Descending ranges such as `n..-1..0` should lower directly to descending `for` loops when possible.

## 33.2 Range in Builder Context

For materialized builders over statically sized ranges, compute final size when practical and fill directly.

## 33.3 Range in Generator Aggregate Context

Generator-fed aggregates over ranges should lower to a single accumulator loop with no intermediate allocation.

## 33.4 Range Value in Escape Context

If the range value escapes into a general iterable context, a helper iterable may be acceptable, but the helper shape should be specialized and should not use avoidable flag-heavy signatures in hot reference cases.

---

## 34. Materialized Builder Optimization Guide

## 34.1 Statically Sized Builder

For:

```txt
[0..<n -> i do f(i)]
```

prefer:

1. compute size `n`,
2. allocate array once,
3. fill directly in a `for` loop.

## 34.2 Statically Sized Block Builder

For:

```txt
[0..<n -> i {
    let x = g(i)
    x + 1
}]
```

still prefer direct allocation and direct fill.

## 34.3 Non-Statically-Sized Builder

If final count is not known cheaply, prefer a resizable builder and final conversion where needed.

## 34.4 Spread Optimization

When materializing `[1, 2, ..xs, 6]`, precompute total length where practical. If not practical, append in order with a resizable builder.

## 34.5 Builder Shape Classification

A useful classification is:

- **fixed-size**: direct array fill,
- **cheaply countable**: pre-size list then append,
- **unknown size**: growable builder then finalize.

## 34.6 Escape-Aware Materialization

If a materialized builder result is immediately consumed by a context that can be inlined, the backend may optionally fuse the computation if and only if materialization is not semantically observable.

By default, preserve materialization intent for `[]`.

---

## 35. Generator Optimization Guide

## 35.1 Generator + Aggregate Fusion

When a generator expression feeds an aggregate intrinsic directly, prefer loop fusion.

Examples:

```txt
sum (0..<n -> i do f(i))
min (xs -> x do g(x))
sumBy (0..<n -> i do node(i)) h
```

Recommended lowering:

- no generator object,
- no intermediate array,
- no intermediate list,
- one direct loop.

## 35.2 Generator in Non-Aggregate Context

If the generator escapes into a general iterable context, a helper iterator may be acceptable.

## 35.3 Generator + `minBy` / `maxBy`

When a generator feeds `minBy` or `maxBy`, fuse generator production and best-element tracking into one loop.

## 35.4 Generator + `count` / `any` / `all`

If these are added or treated as aggregate-family intrinsics, generator-fed uses should also be fused into direct loops with early exit where possible.

## 35.5 Nested Generator Avoidance

Avoid lowering nested generators into layered enumerable objects when both layers can be flattened into nested loops.

---

## 36. Aggregate Optimization Guide

## 36.1 `min` / `max` for Small Fixed Arity

For:

```txt
max a b c d
```

prefer nested direct compare trees.

Avoid constructing temporary arrays or lists.

## 36.2 `sum`

Prefer direct accumulation with a concrete accumulator type.

### 36.2.1 Type-Specialized Sum

Where the operand type is known, use a strongly typed accumulator and avoid generic/dynamic math.

## 36.3 `sumBy`

Evaluate selector once per element, accumulate directly, avoid `Select(...).Sum()` in the reference backend.

## 36.4 `minBy` / `maxBy`

Track best element and best key simultaneously.
Avoid recomputing the key for the same element.

## 36.5 `chmin` / `chmax`

These should be lowered to direct compare-update code whenever the target is a straightforward backend lvalue.

This is both faster and clearer than runtime helper calls.

## 36.6 Empty-Check Strategy

For iterable aggregates over containers with cheap count/length, a fast empty check before entering the loop may be profitable.

## 36.7 Early-Exit Aggregates

If future aggregate intrinsics include `any`, `all`, or predicate-based `count`, prefer short-circuit or early-stop lowering where semantics permit.

---

## 37. Assignment Expression Optimization Guide

## 37.1 Direct Native Backend Form When Possible

For `:=`, use the backend's native assignment-expression capability if it preserves semantics.

## 37.2 Temporary Introduction Only When Necessary

Introduce temporaries only if required to preserve evaluation order in more complex cases.

## 37.3 Classic DSU Pattern

For:

```txt
parent[x] := find(parent[x])
```

emit the most direct readable assignment expression supported by the backend.

## 37.4 Nested Assignment Expressions

For chains such as:

```txt
a = b := c
```

preserve right-associative evaluation while minimizing redundant temporaries.

## 37.5 Side-Effect Safety

If the left-hand side contains side effects or repeated expensive indexing/property access, consider hoisting subexpressions once before the final assignment emission.

---

## 38. Conversion Optimization Guide

## 38.1 Parse vs Convert

For string parsing, lower directly to parse calls.

For scalar-to-scalar conversion, prefer casts or direct conditional rewrites.

## 38.2 Boolean Conversion Shortcuts

Examples:

- `int(boolExpr)` -> ternary or backend numeric-bool conversion,
- `bool(numExpr)` -> `numExpr != 0`,
- `bool(strExpr)` -> empty/non-empty string check.

## 38.3 Avoid Generic Conversion Helpers for Hot Scalar Cases

Generic conversion helpers may exist, but they should not be the default reference lowering for common scalar cases.

## 38.4 Constant Conversion Folding

If the source of a conversion-keyword call is a compile-time constant and the conversion is safe to evaluate statically, constant-fold it.

Examples:

- `int "123"` -> constant `123`,
- `bool ""` -> constant `false`,
- `int true` -> constant `1`.

## 38.5 Redundant Conversion Elimination

Eliminate obviously redundant conversions such as:

- `int(intExpr)` when types already match,
- `bool(boolExpr)` when types already match,

provided source semantics remain unchanged.

---

## 39. Known Data Structure Operator Optimization Guide

## 39.1 Principle

These should compile to the underlying .NET call directly.

### 39.1.1 Good

```txt
visited += x
```

->

```csharp
visited.Add(x)
```

### 39.1.2 Good

```txt
--q
```

->

```csharp
q.Dequeue()
```

### 39.1.3 Bad Default

Do not lower through language-owned wrapper objects.

## 39.2 Return Value Preservation

When the underlying method returns a value, preserve it.

This is especially important for:

- `HashSet<T>.Add`
- `HashSet<T>.Remove`
- `Stack<T>.Pop`
- `Queue<T>.Dequeue`
- `PriorityQueue<...>` pop/peek item result.

## 39.3 Receiver Type Fast Path

If the receiver type is statically known to be a supported known data structure, rewrite immediately during lowering.

If not statically known, do not speculate.

## 39.4 PriorityQueue Item Extraction

Because `~pq` and `--pq` return only the item, emit the most direct backend code that extracts only the item and avoids unnecessary tuple creation when possible.

---

## 40. `new![n]` Optimization Guide

## 40.1 Preferred Lowering

For:

```txt
List<int>[] graph = new![n]
```

prefer:

1. allocate the array,
2. emit a direct loop that fills each slot with `new()`.

Example shape:

```csharp
var graph = new List<int>[n];
for (int i = 0; i < n; i++) graph[i] = new();
```

## 40.2 Restriction Checking

Reject the form early if element type is not compiler-known auto-constructible.

## 40.3 Loop Strength Reduction

The initialization loop for `new![n]` is simple and hot; keep it flat and direct.

Do not lower through `Enumerable.Range(...).Select(...)` or similar.

---

## 41. Comparator Optimization Guide

## 41.1 `T.asc` / `T.desc`

These should resolve once semantically and lower to efficient comparer objects or direct backend constructs.

## 41.2 Caching

Caching comparer instances is a good optimization when it does not complicate semantics.

## 41.3 Type-Body Ordering Shorthand

`operator<=>(other)` should feed the same ordering system used by aggregate comparisons and comparator sugar.

## 41.4 Compare Tree Simplification

For fixed-arity `min` / `max` on primitive ordered types, prefer direct compare trees instead of comparer dispatch.

## 41.5 Repeated Compare Site Reuse

If the same comparer value is used multiple times in a local scope, reuse the lowered comparer object where profitable.

---

## 42. Slicing and Indexing Optimization Guide

## 42.1 Preserve Native Backend Slicing When Available

For strings and one-dimensional arrays, preserve backend-native slicing/index-from-end syntax when doing so is efficient and readable.

## 42.2 Bounds Logic

Avoid lowering simple slices into overly generic helper calls when direct backend slicing is available.

## 42.3 Constant Slice Folding

If slice bounds are compile-time constants and the target is a constant string or array literal with foldable semantics, constant-fold when practical.

## 42.4 Range-Reuse in Slices

If a slice is used repeatedly inside a hot loop, consider hoisting reusable bound computations, while respecting semantics.

---

## 43. Increment / Decrement Optimization Guide

## 43.1 Ordinary Scalars

For `++` / `--` on scalar mutable targets, emit the backend-native increment/decrement form.

## 43.2 Separation from Known DS Rewrites

Do not confuse ordinary scalar `--x` with known data-structure `--queue` rewrites until receiver type is known.

## 43.3 Complex LValues

For increment/decrement of complex lvalues with indexing or property access, preserve evaluation order and avoid duplicated side effects.

---

## 44. Implicit Return Optimization Guide

## 44.1 Prefer Explicit Backend `return`

After resolving implicit return semantics, emit explicit backend `return` statements whenever the context is a function or lambda body.

## 44.2 Avoid Expression-Thunks

If a block expression cannot map directly to backend expression form, rewrite surrounding structure to statement form rather than hiding block semantics inside compiler-generated lambdas.

## 44.3 Bare-Invocation Discipline

Preserve the rule that final bare invocation is not an implicit return.

Do not “optimize” this into value return.

---

## 45. Local Constant Folding and Simplification

After semantic lowering but before final emission, a lightweight local simplification pass is recommended.

Good targets include:

1. arithmetic on literals,
2. boolean simplification,
3. comparison with constants,
4. conversion-keyword calls on constants,
5. trivial compare-tree flattening,
6. elimination of no-op temporaries.

This pass should remain local and conservative.

---

## 46. Temporary-Variable Discipline

Generated temporaries are often necessary, but they should follow these principles:

1. introduce them only when needed,
2. keep scopes small,
3. preserve source evaluation order,
4. avoid clutter in common hot lowered code,
5. avoid nested temporary explosions in builder and aggregate lowering.

Suggested naming families:

```txt
__pscpTmp
__pscpIter
__pscpAcc
__pscpDiscard
__pscpCmp
```

---

## 47. Readability as an Optimization Constraint

Generated code quality matters.

Readable generated C# is easier to debug and easier to optimize further.

When two lowerings are similar in performance, prefer the one that:

- shows direct loops,
- preserves the visible structure of the source,
- avoids deeply nested helper calls,
- avoids unnecessary iterator and lambda noise,
- avoids giant monolithic helper abstractions.

---

## 48. Suggested Optimization Implementation Order

A practical order for implementing optimizations is:

1. direct scalar input lowering,
2. direct fixed-shape array input lowering,
3. direct output rendering for scalars/tuples/1D collections,
4. direct `for` lowering for simple ranges,
5. direct materialized builder fill for static ranges,
6. aggregate-over-range / aggregate-over-generator fusion,
7. `chmin` / `chmax` direct rewrites,
8. known data-structure operator rewrites,
9. `new![n]` direct fill,
10. conversion-keyword specialization,
11. slicing preservation and cleanup,
12. comparer caching and secondary cleanups.

---

## 49. Sanity Checklist for the Reference Backend

A good `v0.4` transpiler backend should be able to answer “yes” to the following questions.

1. Does `0..<n -> i { ... }` become a `for` loop in simple numeric cases?
2. Does `sum (0..<n -> i do f(i))` avoid intermediate allocation?
3. Does `int[n] arr =` lower to a loop of specialized scanner calls?
4. Does `HashSet += x` preserve the underlying `bool` return value?
5. Does `new![n]` become one allocation plus one direct initialization loop?
6. Does `_` remain a true discard semantically even when C# would otherwise treat it as an identifier?
7. Does the backend avoid `dynamic` for aggregate hot paths?
8. Does the backend avoid helper enumerables for simple range loops?
9. Are compiler-generated lambdas avoided for ordinary statement lowering?
10. Does `sumBy` avoid `Select(...).Sum()` in default hot lowering?
11. Do conversion-keyword calls lower directly for common scalar cases?
12. Is generated code still readable enough to debug when something goes wrong?

---

## 50. Closing Note

The most important idea in PSCP optimization is simple:

- make source code short,
- keep generated code direct,
- let the transpiler do structural work,
- do not pay runtime costs for syntax sugar when compile-time lowering can remove them.

That is the standard this guide is intended to enforce.

