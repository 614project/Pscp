# PS/CP Language API Specification v0.3

## 1. Scope

This document defines version `v0.3` of the standard API surface for the PS/CP language.

This document specifies the language-defined intrinsic API, helper surface, compiler-known collection behaviors, and semantic API mappings that exist in addition to the ordinary .NET surface.

This document does not redefine the .NET Base Class Library. All ordinary .NET namespaces, types, members, constructors, generic types, and language-compatible C# surface forms remain available as-is unless this document explicitly states otherwise.

This document is normative unless otherwise stated.

---

## 2. Design Principles

1. **Preserve .NET familiarity**. Existing .NET APIs remain available under their original names and casing.
2. **Add only contest-oriented helpers**. Language-defined APIs exist only where they materially improve algorithm-writing speed or clarity.
3. **Keep intrinsic meaning stable**. Surface sugar and helper APIs have a fixed semantic contract independent of backend optimization.
4. **Allow backend specialization**. An implementation may lower intrinsic APIs to direct loops, specialized scanners, buffered writers, or generated helper code, provided semantics are preserved.
5. **Avoid unnecessary wrapping of ordinary C#/.NET APIs**. The language does not attempt to replace ordinary .NET naming conventions with language-specific aliases.

---

## 3. Non-Goals

The following are outside the scope of this document:

1. renaming ordinary .NET types or methods,
2. replacing .NET casing conventions,
3. forcing all library usage through language-owned wrapper types,
4. prescribing a single runtime helper implementation strategy,
5. guaranteeing one unique lowering for every intrinsic.

Accordingly, the following remain ordinary valid source forms:

```txt
Queue<int> queue = new()
PriorityQueue<int, int> pq = new()
HashSet<int> set = new()
Comparer<int>.Create((a, b) => b <=> a)
Array.Sort(arr)
Math.Max(a, b)
```

---

## 4. API Surface Categories

The standard API surface is divided into the following categories:

1. intrinsic input object `stdin`,
2. intrinsic output object `stdout`,
3. declaration-based input shorthand semantic mappings,
4. statement-based output shorthand semantic mappings,
5. array initialization helpers,
6. collection helper surface,
7. aggregation helper surface,
8. compiler-known collection auto-construction behavior,
9. comparator sugar surface.

---

## 5. Availability Model

### 5.1 Ordinary .NET APIs

All ordinary .NET APIs remain available under their original spelling and case.

### 5.2 Language Intrinsics

The following names are reserved by the language runtime surface:

- `stdin`
- `stdout`
- `Array.zero`

In addition, the language reserves intrinsic meaning for:

- declaration-based input shorthand,
- statement-based output shorthand,
- compiler-known auto-construction of selected collection types,
- comparator sugar `T.asc` and `T.desc`.

### 5.3 User Shadowing

A conforming implementation may reject or warn on declarations that shadow reserved intrinsic names.

---

## 6. Input API

## 6.1 Overview

`stdin` is the intrinsic input object.

It provides contest-oriented token readers, line readers, shaped readers, and grid readers. `stdin` is conceptually available without import.

A conforming implementation may lower `stdin` to:

1. a generated helper class,
2. a static singleton,
3. a compiler-known backend object,
4. direct specialized input code in generated C#.

The surface semantics defined here are independent of the chosen lowering.

---

## 6.2 Token Readers

The following token-based readers are defined:

```txt
stdin.int()
stdin.long()
stdin.str()
stdin.char()
stdin.double()
stdin.decimal()
stdin.bool()
```

### 6.2.1 Semantics

- `stdin.int()` reads one token and parses it as `int`.
- `stdin.long()` reads one token and parses it as `long`.
- `stdin.str()` reads one token as `string`.
- `stdin.char()` reads one token and returns a `char`.
- `stdin.double()` reads one token and parses it as `double`.
- `stdin.decimal()` reads one token and parses it as `decimal`.
- `stdin.bool()` reads one token and parses it as `bool`.

### 6.2.2 Parsing Contract

The implementation shall not silently substitute unrelated values on parse failure.

Failure behavior is implementation-defined and may include throwing, compiler-emitted wrappers, or fail-fast runtime behavior.

---

## 6.3 Line Readers

The following line-oriented readers are defined:

```txt
stdin.line()
stdin.lines(n)
stdin.words()
stdin.chars()
```

### 6.3.1 `stdin.line()`

Reads one full line as `string`.

### 6.3.2 `stdin.lines(n)`

Reads exactly `n` lines and returns `string[]`.

### 6.3.3 `stdin.words()`

Reads one full line and splits it into `string[]` according to the implementation's contest tokenization policy.

### 6.3.4 `stdin.chars()`

Reads one full line and returns its characters as `char[]`.

---

## 6.4 Shaped Readers

The following shape-aware readers are part of the intrinsic API surface:

```txt
stdin.array<T>(n)
stdin.list<T>(n)
stdin.linkedList<T>(n)
stdin.tuple2<T1, T2>()
stdin.tuple3<T1, T2, T3>()
stdin.tuples2<T1, T2>(n)
stdin.tuples3<T1, T2, T3>(n)
stdin.gridInt(n, m)
stdin.gridLong(n, m)
stdin.charGrid(n)
stdin.wordGrid(n)
```

### 6.4.1 `stdin.array<T>(n)`

Reads `n` values of type `T` from token input and returns `T[]`.

### 6.4.2 `stdin.list<T>(n)`

Reads `n` values of type `T` and returns `List<T>`.

### 6.4.3 `stdin.linkedList<T>(n)`

Reads `n` values of type `T` and returns `LinkedList<T>`.

### 6.4.4 Tuple Readers

```txt
stdin.tuple2<T1, T2>()
stdin.tuple3<T1, T2, T3>()
```

Read the required number of values and return tuples.

### 6.4.5 Tuple Sequence Readers

```txt
stdin.tuples2<T1, T2>(n)
stdin.tuples3<T1, T2, T3>(n)
```

Read `n` tuples from token input and return arrays of tuples.

### 6.4.6 Numeric Grid Readers

```txt
stdin.gridInt(n, m)
stdin.gridLong(n, m)
```

Read `n * m` values and return integer grids. The exact backend representation may be jagged or otherwise implementation-defined, provided indexing semantics are preserved.

### 6.4.7 Character Grid Reader

```txt
stdin.charGrid(n)
```

Reads `n` full lines and returns `char[][]`, where each line is converted to a character array.

### 6.4.8 Word Grid Reader

```txt
stdin.wordGrid(n)
```

Reads `n` full lines and splits each line into `string[]`, returning `string[][]`.

---

## 6.5 Declaration-Based Input Shorthand Mapping

The language syntax supports declaration-based input shorthand such as:

```txt
int n =
int n, m =
int[n] arr =
(int, int)[m] edges =
```

These forms are semantic sugar over the intrinsic input API.

### 6.5.1 Scalar Mapping

```txt
int n =
```

is equivalent in meaning to:

```txt
int n = stdin.int()
```

### 6.5.2 Multiple Scalar Mapping

```txt
int n, m =
```

is equivalent in meaning to:

```txt
int n = stdin.int()
int m = stdin.int()
```

### 6.5.3 Array Mapping

```txt
int[n] arr =
```

is equivalent in meaning to:

```txt
int[] arr = stdin.array<int>(n)
```

### 6.5.4 Tuple Sequence Mapping

```txt
(int, int)[m] edges =
```

is equivalent in meaning to:

```txt
(int, int)[] edges = stdin.tuples2<int, int>(m)
```

### 6.5.5 Nested Array Mapping

```txt
int[n][m] grid =
```

is equivalent in meaning to reading values according to the declared dimensions. A conforming implementation may lower this either to `stdin.gridInt(n, m)`-style helpers or to direct generated loops.

### 6.5.6 Backend Specialization

A conforming implementation is not required to preserve the visible helper call in generated code. It may lower declaration shorthand directly into specialized scanner logic, direct loops, or generated helper calls, provided observable semantics are preserved.

---

## 7. Output API

## 7.1 Overview

`stdout` is the intrinsic output object.

It provides contest-oriented writing, line writing, structured output, joining, and flushing.

A conforming implementation may lower `stdout` to:

1. a generated helper object,
2. a buffered writer wrapper,
3. direct writes to a generated `StreamWriter`,
4. direct generated output code.

The surface semantics defined here are independent of the chosen lowering.

---

## 7.2 Primitive Output Helpers

```txt
stdout.write(x)
stdout.writeln(x)
stdout.flush()
```

### 7.2.1 `stdout.write(x)`

Writes the rendered form of `x` without a trailing newline.

### 7.2.2 `stdout.writeln(x)`

Writes the rendered form of `x` followed by a newline.

### 7.2.3 `stdout.flush()`

Flushes buffered output.

For backend-specialized generation, the implementation may ensure that flushing occurs once at program end, unless explicit `stdout.flush()` usage requires earlier flushing semantics.

---

## 7.3 Structured Output Helpers

```txt
stdout.lines(xs)
stdout.grid(g)
stdout.join(sep, xs)
```

### 7.3.1 `stdout.lines(xs)`

Writes one element per line from `xs`.

### 7.3.2 `stdout.grid(g)`

Writes a two-dimensional structure line by line according to the runtime's grid formatting policy.

### 7.3.3 `stdout.join(sep, xs)`

Writes the elements of `xs` joined by `sep` without appending a newline unless explicitly documented by the implementation.

---

## 7.4 Statement-Based Output Mapping

The language syntax supports output shorthand:

```txt
= expr
+= expr
```

### 7.4.1 Write Mapping

```txt
= expr
```

is equivalent in meaning to:

```txt
stdout.write(expr)
```

### 7.4.2 WriteLine Mapping

```txt
+= expr
```

is equivalent in meaning to:

```txt
stdout.writeln(expr)
```

A conforming implementation may lower these directly to backend-specific writes rather than materializing visible `stdout` calls.

---

## 7.5 Default Rendering Contract

The runtime or generated backend shall provide default rendering for:

1. scalar values,
2. tuples,
3. one-dimensional collections.

### 7.5.1 Scalars

Scalars are rendered by ordinary string conversion.

### 7.5.2 Tuples

Tuple elements are rendered in order and joined by a single space.

### 7.5.3 One-Dimensional Collections

Elements are rendered in order and joined by a single space.

### 7.5.4 Nested Collections

Default rendering for nested collections is not required and may be rejected. Structured helpers such as `stdout.grid` shall be used for such cases.

---

## 8. Array Construction and Initialization API

## 8.1 `Array.zero`

The standard zero-initialization helper is:

```txt
Array.zero(n)
```

### 8.1.1 Semantics

Returns a one-dimensional array of length `n` whose elements are initialized to the default value of the element type inferred from context.

Examples:

```txt
int[] a = Array.zero(n)
long[] b = Array.zero(m)
```

### 8.1.2 Declaration Equivalence

The following declaration form is semantically equivalent when used with arrays:

```txt
int[n] a;
```

Equivalent meaning:

```txt
int[] a = Array.zero(n)
```

### 8.1.3 Multi-Dimensional Sized Declarations

```txt
int[n][m] dp;
```

is semantically equivalent to allocation and default initialization of the requested nested array structure.

The exact backend representation is implementation-defined.

---

## 8.2 Collection Expression Construction

Collection expressions are syntax-level constructs, but their semantic result type depends on context and may require helper construction.

### 8.2.1 Examples

```txt
let a = [1, 2, 3]
int[] b = [1, 2, 3]
List<int> c = [1, 2, 3]
LinkedList<int> d = [1, 2, 3]
```

The implementation shall construct the target collection in a way that preserves:

1. element order,
2. element values,
3. target collection type,
4. automatic range expansion and explicit spread behavior.

### 8.2.2 Automatic Range Expansion

In collection expressions, a range element is automatically expanded.

```txt
[0..<5]
[1, 2, 0..<5]
```

### 8.2.3 Spread Elements

```txt
let a = [3, 4, 5]
let b = [1, 2, ..a, 6, 7]
```

Spread preserves iteration order and expands the iterable into the surrounding collection.

### 8.2.4 Builder Forms

```txt
[0..<n -> i do i * i]
[arr -> x do f(x)]
```

Builder forms yield elements in iteration order.

---

## 9. Compiler-Known Auto-Construction

## 9.1 Overview

Certain compiler-known collection types support implicit `new()` construction when declared without an initializer.

### 9.1.1 Supported Initial Set

The initial set is:

- `List<T>`
- `LinkedList<T>`
- `Queue<T>`
- `Stack<T>`
- `HashSet<T>`
- `Dictionary<K, V>`
- `SortedSet<T>`
- `PriorityQueue<TElement, TPriority>`

### 9.1.2 Semantics

A declaration such as:

```txt
List<int> list;
Queue<int> queue;
```

means the same as:

```txt
List<int> list = new()
Queue<int> queue = new()
```

### 9.1.3 Purpose

This behavior exists solely for contest-oriented convenience and does not apply to arbitrary class types.

### 9.1.4 Future Extension

The set of compiler-known auto-constructed collection types may be extended in future versions.

---

## 10. Collection Helper API

## 10.1 Overview

The language defines a contest-oriented collection helper surface that may be exposed as instance methods, extension methods, module functions, or compiler intrinsics.

The following names are reserved for the standard helper surface:

```txt
map
filter
fold
scan
mapFold
sum
sumBy
min
max
minBy
maxBy
any
all
count
find
findIndex
findLastIndex
sort
sortBy
sortWith
distinct
reverse
copy
groupCount
index
freq
chmin
chmax
```

Not every implementation is required to materialize each helper as a literal runtime method if equivalent lowering is provided.

---

## 10.2 Transformation Helpers

### 10.2.1 `map`

```txt
xs.map(f)
```

Returns mapped values according to the implementation's sequence/collection policy.

### 10.2.2 `filter`

```txt
xs.filter(pred)
```

Returns elements satisfying `pred`.

### 10.2.3 `distinct`

```txt
xs.distinct()
```

Returns elements with duplicates removed, preserving the language-defined ordering policy.

### 10.2.4 `reverse`

```txt
xs.reverse()
```

Returns the reversed sequence or collection.

### 10.2.5 `copy`

```txt
xs.copy()
```

Returns a shallow copy.

---

## 10.3 Aggregate and Comparison Intrinsics

The language defines the following intrinsic function families.

### 10.3.1 Binary `min` and `max`

```txt
min(left, right)
max(left, right)
min left right
max left right
````

Semantics:

* `min(left, right)` returns the smaller of the two values,
* `max(left, right)` returns the larger of the two values.

These operations require an ordinary default ordering for the operand type.

### 10.3.2 Aggregate `min` and `max`

```txt
min(values)
max(values)
min values
max values
```

Semantics:

* `min(values)` returns the minimum element of the iterable,
* `max(values)` returns the maximum element of the iterable.

Examples:

```txt
min arr
max arr
min [0..<n -> i do a[i] - b[i]]
```

### 10.3.3 Aggregate `sum`

```txt
sum(values)
sum values
```

Semantics:

* `sum(values)` returns the sum of all elements of the iterable.

Examples:

```txt
sum arr
sum [0..<n -> i do a[i]]
```

### 10.3.4 `sumBy`

```txt
sumBy(values, f)
sumBy values f
```

Semantics:

* maps each element with `f`,
* sums the resulting values.

Examples:

```txt
sumBy arr (x => x * x)
sumBy points (p => p.2)
```

### 10.3.5 `minBy` and `maxBy`

```txt
minBy(values, keySelector)
maxBy(values, keySelector)
minBy values keySelector
maxBy values keySelector
```

Semantics:

* `minBy` returns the element whose key is minimal,
* `maxBy` returns the element whose key is maximal.

Examples:

```txt
minBy points (p => p.x)
maxBy items (x => score(x))
```

### 10.3.6 `chmin` and `chmax`

```txt
chmin(ref target, value)
chmax(ref target, value)
chmin ref target value
chmax ref target value
```

Semantics:

* `chmin` updates `target` to `value` if `value` is smaller and returns whether an update occurred,
* `chmax` updates `target` to `value` if `value` is larger and returns whether an update occurred.

Examples:

```txt
_ = chmin(ref best, cand)
_ = chmax(ref ans, value)
```

### 10.3.7 Empty Iterable Behavior

For aggregate forms over iterables:

* statically empty collection literals may be rejected,
* dynamically empty iterables are runtime errors unless the implementation documents an alternative behavior.

## 10.4 Search Helpers

### 10.4.1 `find`

```txt
xs.find(pred)
```

Returns the first element satisfying `pred`.

### 10.4.2 `findIndex`

```txt
xs.findIndex(pred)
```

Returns the index of the first matching element.

### 10.4.3 `findLastIndex`

```txt
xs.findLastIndex(pred)
```

Returns the index of the last matching element.

---

## 10.5 Sorting Helpers

### 10.5.1 `sort`

```txt
xs.sort()
```

Returns the collection sorted by default ordering.

### 10.5.2 `sortBy`

```txt
xs.sortBy(f)
```

Sorts by key.

### 10.5.3 `sortWith`

```txt
xs.sortWith(cmp)
```

Sorts using a custom comparison function.

The comparison function shall follow the contract of returning a negative value, zero, or a positive value. Use of `<=>` is appropriate for such functions.

---

## 10.6 Frequency and Indexing Helpers

### 10.6.1 `groupCount`

```txt
xs.groupCount()
```

Returns grouping counts according to the implementation's chosen result type.

### 10.6.2 `index`

```txt
xs.index()
```

Returns a structure representing element-to-index association, intended for contest-oriented lookup tasks.

### 10.6.3 `freq`

```txt
xs.freq()
```

Returns frequency information according to the implementation's chosen result type.

The exact result types of `groupCount`, `index`, and `freq` are implementation-defined in v0.3 and should be documented by the implementation.

---

## 11. Aggregation and Iteration Surface

## 11.1 Fast Iteration Syntax

The following syntax is language-level, but conceptually corresponds to iteration helper behavior:

```txt
xs -> x {
    ...
}

xs -> x do expr

xs -> i, x {
    ...
}
```

This document does not require these constructs to be backed by public runtime methods. They may be lowered directly.

## 11.2 Aggregation Syntax

The following language constructs correspond semantically to aggregation helpers:

```txt
min [ 0..<n -> i do a[i] - b[i] ]
sum [ arr -> x do x * x ]
count [ arr -> x => x % 2 is 0 ]
```

A conforming implementation may lower these constructs to direct loops, standard library calls, or helper APIs.

---

## 12. Comparator Sugar API

## 12.1 Overview

The language defines comparator-producing sugar for types with ordinary default ordering.

### 12.1.1 Forms

```txt
T.asc
T.desc
```

Examples:

```txt
int.asc
int.desc
long.asc
string.desc
```

### 12.1.2 Meaning

- `T.asc` denotes the default ascending comparer for `T`.
- `T.desc` denotes the default descending comparer for `T`.

### 12.1.3 Valid Use Cases

Comparator sugar is intended for APIs that consume comparers or ordering functions.

Examples:

```txt
arr.sortWith(int.asc)
arr.sortWith(int.desc)
PriorityQueue<int, int> pq = new(int.desc)
```

### 12.1.4 Error Condition

If no default ordering exists for `T`, then `T.asc` and `T.desc` are invalid and must be rejected.

### 12.1.5 Backend Freedom

An implementation may lower comparator sugar to:

1. generated comparer instances,
2. `Comparer<T>.Default`-based wrappers,
3. specialized backend code,
4. direct sort/priority-queue strategy selection.

The semantic contract is only that `asc` and `desc` correspond to ordinary increasing and decreasing order.

---

## 13. Interop with `ref`, `out`, and `in`

## 13.1 General Rule

Argument modifiers `ref`, `out`, and `in` preserve ordinary C# meaning.

## 13.2 API Surface Consequences

A language-defined intrinsic API may use these modifiers where semantically appropriate.

For example, a conforming implementation may expose tuple deconstruction, queue-style helpers, or scanner helpers using `out` parameters if desired.

## 13.3 Pass-Through Ordinary APIs

Ordinary .NET APIs that use `ref`, `out`, or `in` remain available without renaming or wrapping.

Examples:

```txt
queue.TryDequeue(out x, out y)
foo(ref value, in other)
```

---

## 14. Helper Lowering Contract

## 14.1 General Rule

For every intrinsic surface defined by this document, the transpiler shall preserve semantic meaning even if the generated C# does not visibly contain the same helper name.

Accordingly:

- helper calls may lower to direct loops,
- declaration sugar may lower to helper calls,
- helper calls may lower to standard .NET methods,
- helper calls may lower to custom runtime support,
- input and output helpers may lower to compile-time-specialized fast I/O code.

This document specifies semantic equivalence, not a mandatory lowering shape.

---

## 14.2 Stable Semantic Mappings

The following mappings are defined at the semantic level.

### 14.2.1 Input Shorthand

```txt
int n =
```

means one integer value is read from standard input.

### 14.2.2 Output Shorthand

```txt
= expr
```

means the rendered value of `expr` is written without newline.

```txt
+= expr
```

means the rendered value of `expr` is written with newline.

### 14.2.3 Sized Array Declaration

```txt
int[n] a;
```

means an integer array of length `n` is allocated and zero-initialized.

### 14.2.4 Builder Collection

```txt
[0..<n -> i do i * i]
```

means a collection is constructed from the yielded values in iteration order.

### 14.2.5 Spread

```txt
[1, 2, ..a, 6]
```

means the elements of `a` are inserted between `2` and `6` in iteration order.

### 14.2.6 Auto-Constructed Known Collection

```txt
List<int> list;
```

means the same as:

```txt
List<int> list = new()
```

### 14.2.7 Comparator Sugar

```txt
int.desc
```

means a descending comparer for `int`.

---

## 15. Performance-Oriented Backend Expectations

This section is descriptive of intended backend behavior but remains part of the API contract in the sense that helper meaning must remain compatible with these expectations.

## 15.1 Fast Input

A conforming implementation may choose specialized backend input generation for intrinsic reading constructs, including but not limited to:

- generated scanners,
- buffered readers,
- token readers over `StreamReader`,
- direct loops for fixed-size reads.

## 15.2 Fast Output

A conforming implementation may choose buffered output generation for intrinsic writing constructs, including but not limited to:

- generated `StreamWriter` usage,
- delayed flush at program end,
- specialized join rendering.

## 15.3 No Semantic Dependence on Backend Shape

User code must not depend on whether a construct was lowered to:

- an explicit helper object,
- a direct loop,
- a generated local utility,
- a standard .NET call.

---

## 16. Runtime Naming Policy

### 16.1 .NET Names Are Preserved

The language does not require users to rewrite ordinary .NET API names into language-specific alternatives.

Examples of preserved style:

```txt
Queue<int> queue = new()
PriorityQueue<int, int> pq = new()
HashSet<int> set = new()
Array.Sort(arr)
Math.Max(a, b)
```

### 16.2 Language Intrinsics Use Lowercase Object Style

Language-defined intrinsic objects use lowercase names by convention:

```txt
stdin
stdout
```

This distinction is intentional and does not imply a general renaming policy for .NET APIs.

---

## 17. API Surface Summary

The following summary is normative.

### 17.1 Input Intrinsics

```txt
stdin.int()
stdin.long()
stdin.str()
stdin.char()
stdin.double()
stdin.decimal()
stdin.bool()
stdin.line()
stdin.lines(n)
stdin.words()
stdin.chars()
stdin.array<T>(n)
stdin.list<T>(n)
stdin.linkedList<T>(n)
stdin.tuple2<T1, T2>()
stdin.tuple3<T1, T2, T3>()
stdin.tuples2<T1, T2>(n)
stdin.tuples3<T1, T2, T3>(n)
stdin.gridInt(n, m)
stdin.gridLong(n, m)
stdin.charGrid(n)
stdin.wordGrid(n)
```

### 17.2 Output Intrinsics

```txt
stdout.write(x)
stdout.writeln(x)
stdout.flush()
stdout.lines(xs)
stdout.grid(g)
stdout.join(sep, xs)
```

### 17.3 Array and Collection Helpers

```txt
Array.zero(n)
map
filter
fold
scan
mapFold
sum
sumBy
min
max
minBy
maxBy
any
all
count
find
findIndex
findLastIndex
sort
sortBy
sortWith
distinct
reverse
copy
groupCount
index
freq
chmin
chmax
```

### 17.4 Comparator Sugar

```txt
T.asc
T.desc
```

### 17.5 Compiler-Known Auto-Construction

```txt
List<T>
LinkedList<T>
Queue<T>
Stack<T>
HashSet<T>
Dictionary<K, V>
SortedSet<T>
PriorityQueue<TElement, TPriority>
```

---

## 18. Examples

### 18.1 Token Input

```txt
int n =
int m =
```

Equivalent meaning:

```txt
int n = stdin.int()
int m = stdin.int()
```

### 18.2 Line Input

```txt
string text = stdin.line()
char[] s = stdin.chars()
```

### 18.3 Array Input

```txt
int[n] arr =
```

Equivalent meaning:

```txt
int[] arr = stdin.array<int>(n)
```

### 18.4 Default-Initialized Array

```txt
int[n] lcp;
```

Equivalent meaning:

```txt
int[] lcp = Array.zero(n)
```

### 18.5 Auto-Constructed Known Collection

```txt
List<int> list;
PriorityQueue<int, int> pq;
```

Equivalent meaning:

```txt
List<int> list = new()
PriorityQueue<int, int> pq = new()
```

### 18.6 Output

```txt
= ans
+= arr
stdout.grid(board)
```

### 18.7 Comparator Sugar

```txt
let sorted = arr.sortWith(int.asc)
let rev = arr.sortWith(int.desc)
PriorityQueue<int, int> pq = new(int.desc)
```

### 18.8 Aggregate Intrinsics

```txt
let a = min x y
let b = max arr
let c = sum [0..<n -> i do a[i]]
let d = sumBy arr (x => x * x)
let e = minBy points (p => p.2)
_ = chmin(ref best, cand)
```

---

## 19. Conformance Requirements

An implementation conforms to this API specification if it:

1. preserves the availability of ordinary .NET APIs under their original names,
2. provides or correctly emulates the semantic behavior of the intrinsic surfaces defined herein,
3. preserves the specified equivalence between declaration/output shorthand and the corresponding intrinsic APIs,
4. preserves the specified meaning of compiler-known auto-construction,
5. preserves the specified meaning of comparator sugar,
6. does not silently reinterpret standard .NET APIs as unrelated language-defined intrinsics.

Implementation strategy is otherwise unconstrained.

---

## 20. Versioning

This document defines API draft version `v0.3`.

Backward compatibility is not guaranteed across draft revisions.

