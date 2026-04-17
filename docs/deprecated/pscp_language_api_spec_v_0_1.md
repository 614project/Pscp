# PS/CP Language API Specification v0.1

## 1. Scope

This document defines the standard API surface that is considered intrinsic to the PS/CP language runtime and transpilation environment.

This document does not redefine the .NET Base Class Library. All types, members, namespaces, and conventions from `System` and ordinary C# remain available as-is unless otherwise stated. This specification defines only the additional APIs, helper surfaces, and semantic mappings that are specific to the language.

This document is normative unless otherwise stated.

---

## 2. Non-Goals

The following are explicitly outside the scope of this document:

1. renaming or canonicalizing ordinary .NET API names,
2. lowering of user-written .NET calls into alternative spellings,
3. replacement of .NET type names with language-specific aliases,
4. removal of case distinctions between types and values,
5. optimizer behavior beyond the semantic requirements of the API surface.

Accordingly, source such as the following remains valid and unchanged in principle:

```txt
Queue<int> queue = new()
HashSet<int> set = new()
Array.Sort(arr)
Math.Max(a, b)
```

The transpiler shall preserve the use of existing .NET APIs unless the program explicitly uses a language-defined intrinsic API from this document.

---

## 3. Design Principles

### 3.1 Preserve .NET Familiarity

Ordinary .NET APIs are not wrapped, renamed, or hidden.

### 3.2 Add Only Contest-Oriented Surfaces

Language-defined APIs exist only where they materially improve problem-solving ergonomics.

### 3.3 Prefer Surface-Level Intrinsics

Most language-specific APIs are specified as if they were normal methods, functions, or helper objects, even when the transpiler may eventually treat them specially.

### 3.4 Keep Semantics Stable Before Optimization

Where multiple lowerings are possible, the specification fixes the semantic meaning of the API but does not require a particular optimized implementation.

---

## 4. Runtime Surface Categories

The language-defined API is divided into the following categories:

1. `stdin` input helpers,
2. `stdout` output helpers,
3. collection helper APIs,
4. array construction and initialization helpers,
5. aggregation helpers,
6. convenience functions for contest-style common tasks.

---

## 5. Availability Model

### 5.1 Ordinary .NET APIs

All ordinary .NET APIs remain available under their original names.

### 5.2 Language-Defined Intrinsics

The following names are reserved by the language runtime surface:

- `stdin`
- `stdout`
- `Array.zero`
- standard collection helper members defined in this document

### 5.3 User Shadowing

A conforming implementation may reject or warn on user declarations that shadow reserved intrinsic names.

---

## 6. Input API

## 6.1 Overview

`stdin` is the standard input intrinsic object. It exposes contest-oriented token and line readers.

`stdin` is conceptually available without import.

The implementation may lower `stdin` to a helper class, static singleton, module instance, or equivalent backend construct.

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

### 6.2.2 Failure

Parsing failure behavior is implementation-defined, but a conforming implementation shall not silently produce an unrelated value.

---

## 6.3 Line Readers

The following line-based readers are defined:

```txt
stdin.line()
stdin.lines(n)
stdin.words()
stdin.chars()
```

### 6.3.1 `stdin.line()`

Reads one full input line as `string`.

### 6.3.2 `stdin.lines(n)`

Reads exactly `n` lines and returns `string[]`.

### 6.3.3 `stdin.words()`

Reads one full line and splits it into `string[]` according to the implementation's contest tokenization policy.

### 6.3.4 `stdin.chars()`

Reads one full line and returns its characters as `char[]`.

---

## 6.4 Shaped Readers

The following shape-aware readers are defined:

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

Read the required number of tokens and return tuples.

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

Read `n * m` tokens and return rectangular or jagged integer grids according to the implementation model. The observable indexing semantics shall be preserved.

### 6.4.7 Character Grid Reader

```txt
stdin.charGrid(n)
```

Reads `n` lines and returns `char[][]`, where each line is converted to a character array.

### 6.4.8 Word Grid Reader

```txt
stdin.wordGrid(n)
```

Reads `n` lines and splits each line into a `string[]`, returning `string[][]`.

---

## 6.5 Declaration-Based Input Shorthand Mapping

The language syntax permits declaration-based input shorthand such as:

```txt
int n =
int n, m =
int[n] arr =
(int, int)[m] edges =
```

This syntax is semantically equivalent to the `stdin` API.

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

### 6.5.4 Tuple Array Mapping

```txt
(int, int)[m] edges =
```

is equivalent in meaning to:

```txt
(int, int)[] edges = stdin.tuples2<int, int>(m)
```

The transpiler may lower declaration shorthand directly without actually materializing an explicit `stdin.*` call in the generated C#, provided semantics are preserved.

---

## 7. Output API

## 7.1 Overview

`stdout` is the standard output intrinsic object. It exposes contest-oriented rendering helpers.

The implementation may lower `stdout` to a helper class, static singleton, buffered writer, or equivalent backend construct.

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

The language syntax permits output shorthand:

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

The transpiler may lower these forms directly without introducing explicit `stdout` method calls in the generated C#, provided semantics are preserved.

---

## 7.5 Default Rendering Contract

The language runtime shall provide default rendering for:

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

Default rendering for nested collections is not required and may be rejected. `stdout.grid` or equivalent helper shall be used for structured multi-line output.

---

## 8. Array Construction and Initialization API

## 8.1 `Array.zero`

The standard array zero-initialization helper is:

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

Collection expressions are language syntax, but their semantic result type depends on context and may require helper construction.

### 8.2.1 Examples

```txt
let a = [1, 2, 3]
int[] b = [1, 2, 3]
List<int> c = [1, 2, 3]
LinkedList<int> d = [1, 2, 3]
```

The transpiler shall create the target collection in a way that preserves:

1. element order,
2. element values,
3. target collection type,
4. automatic range expansion and explicit spread behavior.

### 8.2.2 Range Expansion in Collection Expressions

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

---

## 9. Collection Helper API

## 9.1 Overview

The language defines a contest-oriented collection helper surface that may be presented as instance methods, extension methods, module functions, or transpiler intrinsics.

The following names are reserved for the standard helper surface.

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
```

Not every implementation is required to expose every helper as a true runtime method if equivalent lowering is provided.

---

## 9.2 Transformation Helpers

### 9.2.1 `map`

```txt
xs.map(f)
```

Returns a sequence or collection of mapped values according to the implementation's collection policy.

### 9.2.2 `filter`

```txt
xs.filter(pred)
```

Returns elements satisfying `pred`.

### 9.2.3 `distinct`

```txt
xs.distinct()
```

Returns elements with duplicates removed, preserving the language-defined ordering policy.

### 9.2.4 `reverse`

```txt
xs.reverse()
```

Returns the reversed sequence or collection.

### 9.2.5 `copy`

```txt
xs.copy()
```

Returns a shallow copy.

---

## 9.3 Aggregation Helpers

### 9.3.1 `fold`

```txt
xs.fold(seed, f)
```

Folds from left to right.

### 9.3.2 `scan`

```txt
xs.scan(seed, f)
```

Returns intermediate fold states.

### 9.3.3 `mapFold`

```txt
xs.mapFold(seed, f)
```

Traverses the collection, threading state and producing mapped output.

### 9.3.4 `sum`

```txt
xs.sum()
```

Computes the sum of all elements.

### 9.3.5 `sumBy`

```txt
xs.sumBy(f)
```

Maps then sums.

### 9.3.6 `min`, `max`

```txt
xs.min()
xs.max()
```

Return the minimum or maximum element.

### 9.3.7 `minBy`, `maxBy`

```txt
xs.minBy(f)
xs.maxBy(f)
```

Return an element selected by comparing keys derived from `f`.

### 9.3.8 `count`

```txt
xs.count(pred)
```

Counts elements satisfying `pred`.

### 9.3.9 `any`, `all`

```txt
xs.any(pred)
xs.all(pred)
```

Boolean existential and universal checks.

---

## 9.4 Search Helpers

### 9.4.1 `find`

```txt
xs.find(pred)
```

Returns the first element satisfying `pred`.

### 9.4.2 `findIndex`

```txt
xs.findIndex(pred)
```

Returns the index of the first matching element.

### 9.4.3 `findLastIndex`

```txt
xs.findLastIndex(pred)
```

Returns the index of the last matching element.

---

## 9.5 Sorting Helpers

### 9.5.1 `sort`

```txt
xs.sort()
```

Returns the collection sorted by default ordering.

### 9.5.2 `sortBy`

```txt
xs.sortBy(f)
```

Sorts by key.

### 9.5.3 `sortWith`

```txt
xs.sortWith(cmp)
```

Sorts using a custom comparison function.

The comparison function shall follow the contract of returning a negative value, zero, or a positive value. Use of `<=>` is appropriate for such functions.

---

## 9.6 Frequency and Indexing Helpers

### 9.6.1 `groupCount`

```txt
xs.groupCount()
```

Returns grouping counts according to the implementation's chosen result type.

### 9.6.2 `index`

```txt
xs.index()
```

Returns a structure representing element-to-index association, intended for contest-oriented lookup tasks.

### 9.6.3 `freq`

```txt
xs.freq()
```

Returns frequency information according to the implementation's chosen result type.

The exact result types of `groupCount`, `index`, and `freq` are implementation-defined in v0.1 and should be documented by the implementation.

---

## 10. Iteration and Aggregation Surface

## 10.1 Fast Iteration Syntax

The following syntax is language-level, but conceptually corresponds to iteration helpers:

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

---

## 10.2 Aggregation Syntax

The following language constructs correspond semantically to aggregation helpers:

```txt
min { for i in 0..<n do a[i] - b[i] }
sum { for x in arr do x }
count { for x in arr where x % 2 == 0 do 1 }
```

A conforming implementation may lower these constructs to direct loops, standard library calls, or helper APIs.

---

## 11. Helper Lowering Contract

## 11.1 General Rule

For every intrinsic surface defined by this document, the transpiler shall preserve the semantic contract even if the generated C# does not visibly contain the same helper name.

Accordingly:

- helper calls may lower to direct loops,
- declaration sugar may lower to helper calls,
- helper calls may lower to standard .NET methods,
- helper calls may lower to custom runtime support.

This document specifies semantic equivalence, not a mandatory lowering shape.

---

## 11.2 Stable Semantic Mappings

The following mappings are defined at the semantic level:

### 11.2.1 Input Shorthand

```txt
int n =
```

means one integer token is read from standard input.

### 11.2.2 Output Shorthand

```txt
= expr
```

means the rendered value of `expr` is written without newline.

```txt
+= expr
```

means the rendered value of `expr` is written with newline.

### 11.2.3 Sized Array Declaration

```txt
int[n] a;
```

means an integer array of length `n` is allocated and zero-initialized.

### 11.2.4 Collection Builder

```txt
[0..<n -> i do i * i]
```

means a collection is constructed from the yielded values in iteration order.

### 11.2.5 Spread

```txt
[1, 2, ..a, 6]
```

means the elements of `a` are inserted between `2` and `6` in iteration order.

---

## 12. Runtime Naming Policy

### 12.1 .NET Names Are Preserved

The transpiler shall not require users to rewrite ordinary .NET names into language-specific alternatives.

Examples of preserved style:

```txt
Queue<int> queue = new()
PriorityQueue<int, int> pq = new()
HashSet<int> set = new()
Array.Sort(arr)
Math.Max(a, b)
```

### 12.2 Language Intrinsics Use Lowercase Object Style

Language-defined intrinsic objects use lowercase names by convention:

```txt
stdin
stdout
```

This distinction is intentional and does not imply a general renaming policy for .NET APIs.

---

## 13. API Surface Summary

The following table is normative as a concise summary.

### 13.1 Input Intrinsics

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

### 13.2 Output Intrinsics

```txt
stdout.write(x)
stdout.writeln(x)
stdout.flush()
stdout.lines(xs)
stdout.grid(g)
stdout.join(sep, xs)
```

### 13.3 Collection and Array Helpers

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
```

---

## 14. Examples

### 14.1 Token Input

```txt
int n =
int m =
```

Equivalent meaning:

```txt
int n = stdin.int()
int m = stdin.int()
```

### 14.2 Line Input

```txt
string text = stdin.line()
char[] s = stdin.chars()
```

### 14.3 Array Input

```txt
int[n] arr =
```

Equivalent meaning:

```txt
int[] arr = stdin.array<int>(n)
```

### 14.4 Default-Initialized Array

```txt
int[n] lcp;
```

Equivalent meaning:

```txt
int[] lcp = Array.zero(n)
```

### 14.5 Output

```txt
= ans
+= arr
stdout.grid(board)
```

### 14.6 Custom Comparison

```txt
int compare(int a, int b) {
    a <=> b
}

let sorted = arr.sortWith(compare)
```

---

## 15. Conformance Requirements

An implementation conforms to this API specification if it:

1. preserves the availability of ordinary .NET APIs under their original names,
2. provides or correctly emulates the semantic behavior of the intrinsic surfaces defined herein,
3. preserves the specified equivalence between declaration/output shorthand and the corresponding intrinsic APIs,
4. does not silently reinterpret standard .NET APIs as language-defined intrinsics.

Implementation strategy is otherwise unconstrained.

---

## 16. Versioning

This document defines API draft version `v0.1`.

Backward compatibility is not guaranteed across draft revisions.

