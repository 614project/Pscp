# PSCP Language API Specification v0.4

## 1. Scope

This document defines version `v0.4` of the standard API surface for the PSCP language.

This document specifies:

1. language-defined intrinsic objects,
2. declaration and statement shorthand semantic mappings,
3. intrinsic aggregate function families,
4. conversion-keyword semantics,
5. comparator sugar semantics,
6. compiler-known collection behavior,
7. compiler-known data-structure operator rewrites,
8. materialized vs generator collection semantics,
9. performance-oriented API expectations.

This document does not redefine the .NET Base Class Library. Ordinary .NET namespaces, types, methods, constructors, generics, and common C# surface forms remain available as-is unless explicitly stated otherwise.

This document is normative unless otherwise stated.

---

## 2. Design Principles

1. **Preserve .NET familiarity**. Existing .NET APIs remain available under their original names and casing.
2. **Expose language-owned semantics explicitly**. Intrinsics should have stable source meaning independent of the backend shape.
3. **Prefer compile-time specialization over generic runtime helpers** when semantics are simple and performance-sensitive.
4. **Keep APIs regular**. Common competitive-programming operations should be expressible as ordinary calls where practical.
5. **Distinguish concrete vs lazy intent**. Materialized collection forms and generator forms should have distinct API-level meaning.
6. **Keep experimental convenience features bounded**. Compiler-known operator rewrites and section-label semantics should not silently replace broad language/runtime behavior.

---

## 3. Non-Goals

The following are outside the scope of this document:

1. renaming ordinary .NET types or methods,
2. replacing .NET naming conventions with PSCP aliases,
3. forcing all operations through PSCP wrapper types,
4. prescribing one unique runtime implementation strategy,
5. redefining the full semantics of arbitrary external libraries.

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

The PSCP standard API surface is divided into the following categories:

1. intrinsic input object `stdin`,
2. intrinsic output object `stdout`,
3. declaration-based input shorthand semantics,
4. statement-based output shorthand semantics,
5. array and allocation helpers,
6. materialized collection semantics,
7. generator semantics,
8. aggregate intrinsic function families,
9. conversion-keyword intrinsic families,
10. comparator sugar semantics,
11. compiler-known auto-construction semantics,
12. compiler-known data structure operator semantics.

---

## 5. Availability Model

### 5.1 Ordinary .NET APIs

All ordinary .NET APIs remain available under their original spelling and case.

### 5.2 Language Intrinsics

The following names are reserved by the language runtime surface:

- `stdin`
- `stdout`
- `Array.zero`

In addition, the language reserves semantic meaning for:

- declaration-based input shorthand,
- statement-based output shorthand,
- aggregate intrinsic families,
- conversion-keyword calls,
- comparator sugar `T.asc` and `T.desc`,
- compiler-known collection auto-construction,
- compiler-known data structure operator rewrites.

### 5.3 User Shadowing

A conforming implementation may reject or warn on declarations that shadow intrinsic names such as `stdin` and `stdout`.

---

## 6. Input API

## 6.1 Overview

`stdin` is the intrinsic input object.

It provides contest-oriented token readers, line readers, shaped readers, and grid readers.

A conforming implementation may lower `stdin` to:

1. a generated helper class,
2. a compiler-known backend object,
3. direct generated scanner code,
4. a hybrid scanner/runtime implementation.

The source-level semantics defined here are independent of the chosen lowering.

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
- `stdin.bool()` reads one token and parses it as `bool` according to the implementation's supported boolean token policy.

### 6.2.2 Parsing Contract

The implementation shall not silently substitute unrelated values on parse failure.

Failure behavior is implementation-defined and may include fail-fast behavior, exceptions, or generated helper diagnostics.

---

## 6.3 Line-Oriented Readers

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

Reads exactly `n` full lines and returns `string[]`.

### 6.3.3 `stdin.words()`

Reads one full line and splits it into `string[]` according to the implementation's contest tokenization policy.

### 6.3.4 `stdin.chars()`

Reads one full line and returns its characters as `char[]`.

---

## 6.4 Shaped Readers

The following shaped readers are part of the intrinsic API surface:

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

### 6.4.6 Grid Readers

```txt
stdin.gridInt(n, m)
stdin.gridLong(n, m)
stdin.charGrid(n)
stdin.wordGrid(n)
```

These read values according to their declared shape and return implementation-defined backend structures that preserve source-level indexing semantics.

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

is equivalent in meaning to reading values according to the declared dimensions. A conforming implementation may lower this either to grid readers or to generated nested loops.

### 6.5.6 Backend Freedom

A conforming implementation is not required to preserve visible helper calls in generated code. It may lower declaration shorthand directly into specialized scanner logic.

---

## 7. Output API

## 7.1 Overview

`stdout` is the intrinsic output object.

It provides contest-oriented writing, line writing, structured output, joining, and flushing.

A conforming implementation may lower `stdout` to:

1. a generated helper object,
2. a buffered writer wrapper,
3. direct writes to generated backend code,
4. direct `StreamWriter`-style output generation.

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

Writes a two-dimensional structure line by line according to the implementation's grid rendering policy.

### 7.3.3 `stdout.join(sep, xs)`

Writes the elements of `xs` joined by `sep` without appending a newline unless explicitly documented otherwise.

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

Default rendering for nested collections is not required and may be rejected. Structured helpers such as `stdout.grid` should be used.

---

## 8. Materialized Collections and Generators

## 8.1 Materialized Collection Semantics

A square-bracket collection expression `[...]` denotes a materialized collection.

### 8.1.1 Contextual Result Type

Its concrete result type depends on context.

Examples:

```txt
let a = [1, 2, 3]          // default array
int[] b = [1, 2, 3]
List<int> c = [1, 2, 3]
LinkedList<int> d = [1, 2, 3]
```

### 8.1.2 Range Expansion

Ranges inside `[]` are automatically expanded.

```txt
[0..<5]
[1, 2, 0..<5]
```

### 8.1.3 Spread Elements

`..expr` expands an iterable into the surrounding materialized collection.

```txt
let xs = [3, 4, 5]
let ys = [1, 2, ..xs, 6]
```

### 8.1.4 Builder Elements

A builder inside `[]` yields concrete collection elements.

```txt
[0..<n -> i do i * i]
[0..<n -> i {
    let x = i * i
    x + 1
}]
```

---

## 8.2 Generator Semantics

A parenthesized generator expression `(...)` containing a directly enclosed iterator form denotes a lazy iterable/generator rather than a materialized collection.

Examples:

```txt
(0..<n -> i do i * i)
(0..<n -> i {
    let x = i * i
    x + 1
})
(for i in 0..<n do a[i])
```

### 8.2.1 API Meaning

A generator expression is treated as an iterable source suitable for:

- aggregate intrinsic calls,
- sequence-processing helpers,
- ordinary iterable-consuming APIs.

### 8.2.2 Allocation Intent

A generator expression does not by itself imply materialization into an array or list.

### 8.2.3 Canonical Aggregate Usage

The parenthesized generator form is the preferred lazy style for aggregate calls.

Examples:

```txt
sum (0..<n -> i do score(i))
min (0..<n -> i do a[i] - i)
```

---

## 9. Array and Allocation Helpers

## 9.1 `Array.zero`

The standard zero/default initialization helper is:

```txt
Array.zero(n)
```

### 9.1.1 Semantics

Returns a one-dimensional array of length `n` whose elements are initialized to the default value of the inferred element type.

### 9.1.2 Declaration Equivalence

The declaration:

```txt
int[n] a;
```

is equivalent in meaning to:

```txt
int[] a = Array.zero(n)
```

---

## 9.2 Target-Typed Array Allocation `new[n]`

PSCP defines target-typed array allocation semantics.

```txt
int[] arr = new[n]
NodeInfo[] nodes = new[n]
```

### 9.2.1 Meaning

Allocate an array using the target type's element type and length `n`.

### 9.2.2 Scope

This is an allocation shorthand, not a general new-expression replacement.

---

## 9.3 Target-Typed Auto-Constructed Array Allocation `new![n]`

PSCP defines an array allocation form with element auto-construction.

```txt
List<int>[] graph = new![n]
Queue<int>[] buckets = new![m]
```

### 9.3.1 Meaning

1. allocate the array,
2. initialize each element with implicit `new()` according to compiler-known collection auto-construction rules.

### 9.3.2 Restriction

This is intended only for known auto-constructible collection element types, not arbitrary classes.

---

## 10. Compiler-Known Auto-Construction

## 10.1 Overview

Certain compiler-known collection types support implicit `new()` construction when declared without an initializer.

### 10.1.1 Supported Initial Set

The initial set is:

- `List<T>`
- `LinkedList<T>`
- `Queue<T>`
- `Stack<T>`
- `HashSet<T>`
- `Dictionary<K, V>`
- `SortedSet<T>`
- `PriorityQueue<TElement, TPriority>`

### 10.1.2 Semantics

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

### 10.1.3 Purpose

This behavior exists solely for contest-oriented convenience and does not apply to arbitrary class types.

---

## 11. Aggregate Intrinsic Families

PSCP defines the following aggregate and compare-update intrinsic families as ordinary intrinsic calls.

## 11.1 Binary `min` and `max`

```txt
min(left, right)
max(left, right)
min left right
max left right
```

Semantics:

- `min(left, right)` returns the smaller value,
- `max(left, right)` returns the larger value.

These operations require an ordinary default ordering for the operand type.

---

## 11.2 Variadic `min` and `max`

```txt
min(a, b, c)
max(a, b, c, d)
min a b c
max a b c d
```

Semantics:

- compare all supplied values,
- return the minimum or maximum according to ordinary ordering.

This form is intended for small fixed arity.

---

## 11.3 Aggregate `min` and `max`

```txt
min(values)
max(values)
min values
max values
```

Examples:

```txt
min arr
max (0..<n -> i do a[i])
min [0..<n -> i do a[i] - i]
```

Semantics:

- `min(values)` returns the minimum element of the iterable,
- `max(values)` returns the maximum element of the iterable.

---

## 11.4 `sum`

```txt
sum(values)
sum values
```

Examples:

```txt
sum arr
sum (0..<n -> i do a[i])
```

Semantics:

- return the sum of all elements of the iterable.

---

## 11.5 `sumBy`

```txt
sumBy(values, selector)
sumBy values selector
```

Examples:

```txt
sumBy arr (x => x * x)
sumBy points (p => p.2)
```

Semantics:

1. map each element with `selector`,
2. sum the resulting values.

---

## 11.6 `minBy` and `maxBy`

```txt
minBy(values, keySelector)
maxBy(values, keySelector)
minBy values keySelector
maxBy values keySelector
```

Examples:

```txt
minBy points (p => p.x)
maxBy items (x => score(x))
```

Semantics:

- `minBy` returns the element whose key is minimal,
- `maxBy` returns the element whose key is maximal.

---

## 11.7 `chmin` and `chmax`

```txt
chmin(ref target, value)
chmax(ref target, value)
chmin ref target value
chmax ref target value
```

Semantics:

- `chmin` updates `target` when `value` is smaller,
- `chmax` updates `target` when `value` is larger,
- both return whether an update occurred.

Examples:

```txt
_ = chmin(ref best, cand)
_ = chmax(ref ans, value)
```

---

## 11.8 Empty Iterable Behavior

For aggregate forms over iterables:

- statically empty materialized collection literals may be rejected,
- dynamically empty iterables are runtime errors unless the implementation documents an alternative policy.

---

## 12. Conversion-Keyword Intrinsic Family

PSCP defines built-in type keywords as expression-level conversion intrinsics when used in expression position.

## 12.1 Supported Keywords

The core set is:

- `int`
- `long`
- `double`
- `decimal`
- `bool`
- `char`
- `string`

An implementation may extend this set with additional numeric aliases if explicitly documented.

---

## 12.2 General Conversion Principle

A conversion-keyword call attempts a practical scalar conversion in the style of competitive-programming convenience.

Examples:

```txt
int "123"
long "10000000000"
double "3.14"
int true
bool 0
bool "hello"
string 123
```

The backend may implement these by parse calls, casts, or helper functions depending on source and target types.

---

## 12.3 String Source Behavior

When the source is `string`:

- `int s` means integer parse,
- `long s` means long parse,
- `double s` means floating-point parse,
- `decimal s` means decimal parse,
- `char s` means single-character conversion according to implementation policy,
- `bool s` means string truthiness conversion.

### 12.3.1 Boolean String Conversion

The intended broad convenience behavior is:

- `bool ""` -> `false`,
- `bool null` -> `false` when null-aware handling is supported,
- non-empty strings -> `true`.

At minimum, the language intends `bool(string)` to behave like a practical truthiness conversion rather than strict C# parsing.

---

## 12.4 Numeric Source Behavior

When the source is numeric:

- numeric-to-numeric conversion behaves like a cast/convert in the backend scalar model,
- `bool n` returns `false` only when `n == 0`, otherwise `true`,
- `string n` returns ordinary string conversion.

---

## 12.5 Boolean Source Behavior

When the source is `bool`:

- `int true` -> `1`,
- `int false` -> `0`,
- analogous numeric conversions follow the same 0/1 model,
- `string b` returns ordinary string conversion.

---

## 12.6 Purpose

This feature exists to support concise parsing and cast-like expressions such as:

```txt
let x = int "123"
let y = int(compare(a, b) != 0)
let z = bool score
```

---

## 13. Comparator Sugar API

## 13.1 Overview

PSCP defines comparator-producing sugar for types with ordinary default ordering.

### 13.1.1 Forms

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

### 13.1.2 Meaning

- `T.asc` denotes the default ascending comparer for `T`,
- `T.desc` denotes the default descending comparer for `T`.

### 13.1.3 Valid Use Cases

Examples:

```txt
arr.sortWith(int.asc)
arr.sortWith(int.desc)
PriorityQueue<int, int> pq = new(int.desc)
```

### 13.1.4 Error Condition

If no default ordering exists for `T`, then `T.asc` and `T.desc` are invalid.

---

## 14. Known Data Structure Operator Semantics

The following are compiler-known operator rewrites. They are part of PSCP source semantics, not general operator overloading for arbitrary types.

## 14.1 General Rule

These rewrites apply only when the static receiver type is a supported compiler-known data structure.

If the receiver type is not supported, ordinary operator rules apply and the use may be invalid.

---

## 14.2 `HashSet<T>`

### 14.2.1 Add

```txt
set += value
```

means:

```txt
set.Add(value)
```

and returns the same `bool` result as `Add`.

### 14.2.2 Remove

```txt
set -= value
```

means:

```txt
set.Remove(value)
```

and returns the same `bool` result as `Remove`.

### 14.2.3 Typical Use

```txt
if not (visited += me) then continue
```

---

## 14.3 `Stack<T>`

### 14.3.1 Push

```txt
s += value
```

means:

```txt
s.Push(value)
```

### 14.3.2 Peek

```txt
~s
```

means:

```txt
s.Peek()
```

### 14.3.3 Pop

```txt
--s
```

means:

```txt
s.Pop()
```

Postfix `s--` is intentionally not part of this feature.

---

## 14.4 `Queue<T>`

### 14.4.1 Enqueue

```txt
q += value
```

means:

```txt
q.Enqueue(value)
```

### 14.4.2 Peek

```txt
~q
```

means:

```txt
q.Peek()
```

### 14.4.3 Dequeue

```txt
--q
```

means:

```txt
q.Dequeue()
```

---

## 14.5 `PriorityQueue<TElement, TPriority>`

### 14.5.1 Enqueue

```txt
pq += (item, priority)
```

means enqueueing `item` with `priority`.

### 14.5.2 Peek

```txt
~pq
```

returns the same item that the ordinary peek operation would expose.

### 14.5.3 Dequeue

```txt
--pq
```

returns the same item that the ordinary dequeue operation would expose.

### 14.5.4 Priority Access

If the priority is also needed, ordinary APIs such as `TryPeek` and `TryDequeue` remain the correct explicit mechanism.

---

## 15. Ordering Shorthand in Type Bodies

PSCP defines a shorthand ordering declaration inside type bodies.

```txt
operator<=>(other) => this.value <=> other.value
```

### 15.1 Meaning

- `other` has the enclosing self type,
- return type is implicitly `int`,
- this establishes the type's default ordering.

### 15.2 Relationship to Existing APIs

This shorthand is intended to support APIs and semantics that depend on an ordinary default ordering, including comparator sugar and aggregate comparisons.

---

## 16. Access Section Labels with Colon

PSCP defines experimental access section labels with a trailing colon:

```txt
public:
private:
protected:
internal:
```

### 16.1 API-Level Meaning

These labels carry PSCP source-level intent only.

They affect only methods that:

1. appear below the label,
2. do not already have an explicit access modifier.

### 16.2 Distinction from Pass-Through Modifiers

The following without a colon are ordinary pass-through C# modifiers and are **not** part of this section-label feature:

```txt
public
private
protected
internal
```

### 16.3 Current Backend Policy

Section labels are experimental and lightweight.

A conforming implementation may:

- issue warnings,
- perform access-violation analysis,
- but still emit methods as `public` in generated C#.

Section labels must not block transpilation.

---

## 17. Performance-Oriented Backend Expectations

This section is descriptive of intended backend behavior but remains part of the API contract in the sense that helper meaning must remain compatible with these expectations.

## 17.1 Fast Input

A conforming implementation may choose specialized backend input generation, including:

- generated scanners,
- buffered readers,
- direct loops for fixed-shape reads.

## 17.2 Fast Output

A conforming implementation may choose buffered output generation, including:

- generated `StreamWriter` usage,
- delayed flush at program end,
- specialized tuple/collection rendering.

## 17.3 Direct Lowering Preferred for Aggregate and Builder Patterns

Reference implementations are expected to prefer direct loops and accumulators over generic iterator pipelines for:

- aggregate intrinsics,
- fixed-shape reads,
- materialized builders,
- generator-consuming aggregate calls,
- compare-update intrinsics.

## 17.4 Generator vs Materialization Expectations

- `[...]` implies concrete collection behavior,
- `(...)` generator forms imply iterable behavior and should not imply unnecessary allocation.

---

## 18. Runtime Naming Policy

### 18.1 .NET Names Are Preserved

The language does not require users to rewrite ordinary .NET API names into PSCP-owned aliases.

Examples:

```txt
Queue<int> queue = new()
PriorityQueue<int, int> pq = new()
HashSet<int> set = new()
Array.Sort(arr)
Math.Max(a, b)
```

### 18.2 Language Intrinsics Use Lowercase Object Style

Language-defined intrinsic objects use lowercase names by convention:

```txt
stdin
stdout
```

This distinction is intentional and does not imply a broad renaming policy for .NET APIs.

---

## 19. API Surface Summary

The following summary is normative.

### 19.1 Input Intrinsics

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

### 19.2 Output Intrinsics

```txt
stdout.write(x)
stdout.writeln(x)
stdout.flush()
stdout.lines(xs)
stdout.grid(g)
stdout.join(sep, xs)
```

### 19.3 Array and Allocation Helpers

```txt
Array.zero(n)
new[n]
new![n]
```

### 19.4 Aggregate Intrinsic Families

```txt
min
max
sum
sumBy
minBy
maxBy
chmin
chmax
```

### 19.5 Conversion-Keyword Intrinsics

```txt
int
long
double
decimal
bool
char
string
```

when used in expression position.

### 19.6 Comparator Sugar

```txt
T.asc
T.desc
```

### 19.7 Compiler-Known Auto-Construction

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

### 19.8 Compiler-Known Data Structure Operator Rewrites

```txt
HashSet<T>
Stack<T>
Queue<T>
PriorityQueue<TElement, TPriority>
```

---

## 20. Examples

### 20.1 Scalar Input

```txt
int n =
int m =
```

Equivalent meaning:

```txt
int n = stdin.int()
int m = stdin.int()
```

### 20.2 Aggregate over a Generator

```txt
let total = sum (0..<n -> i {
    let x = i * i
    x + 1
})
```

### 20.3 Materialized Builder

```txt
let squares = [0..<n -> i do i * i]
```

### 20.4 HashSet Add Returning `bool`

```txt
HashSet<int> visited;
if not (visited += me) then continue
```

### 20.5 Stack Operators

```txt
Stack<int> s;
s += 10
let top = ~s
let x = --s
```

### 20.6 Auto-Constructed Array of Collections

```txt
List<int>[] graph = new![n]
```

### 20.7 Conversion Keywords

```txt
let a = int "123"
let b = int(true)
let c = bool "hello"
```

### 20.8 Comparator Sugar

```txt
let sorted = arr.sortWith(int.asc)
let rev = arr.sortWith(int.desc)
PriorityQueue<int, int> pq = new(int.desc)
```

---

## 21. Conformance Requirements

An implementation conforms to this API specification if it:

1. preserves the availability of ordinary .NET APIs under their original names,
2. provides or correctly emulates the semantic behavior of the intrinsic surfaces defined herein,
3. preserves the specified equivalence between declaration/output shorthand and the corresponding intrinsic APIs,
4. preserves the specified meaning of compiler-known auto-construction and `new![n]`,
5. preserves the specified meaning of aggregate intrinsic families,
6. preserves the specified meaning of conversion-keyword calls,
7. preserves the specified meaning of comparator sugar,
8. preserves the specified meaning of compiler-known data structure operator rewrites,
9. does not silently reinterpret standard .NET APIs as unrelated PSCP intrinsics.

Implementation strategy is otherwise unconstrained.

---

## 22. Versioning

This document defines API draft version `v0.4`.

Backward compatibility is not guaranteed across draft revisions.

