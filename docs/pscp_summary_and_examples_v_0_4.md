# PSCP v0.4 Summary and Examples

## 1. Purpose

This document is a compact implementation-facing and user-facing summary of PSCP `v0.4`.

It consolidates the major changes across:

- language syntax,
- intrinsic API surface,
- transpiler expectations,
- language-server-visible semantics.

It also provides representative PSCP example programs and patterns.

This document is intentionally shorter and more practical than the full specifications.

---

## 2. Version Summary

PSCP `v0.4` strengthens the language in four directions:

1. **clearer syntax contracts** for parsing and transpilation,
2. **stronger distinction between concrete collections and lazy generators**,
3. **broader practical intrinsic surface** for aggregates and conversion,
4. **stronger compile-time lowering expectations** for performance-sensitive code.

The guiding idea of `v0.4` is:

> keep contest code short, but make the transpiler responsible for producing direct and fast C#.

---

## 3. What Changed Most in v0.4

The most important changes are:

1. `[]` is the materialized collection form,
2. `(...)` with `for` or `->` becomes a generator/lazy iterable form,
3. aggregate operations are intrinsic call families, not special block syntax,
4. `:=` is an explicit value-yielding assignment expression,
5. conversion keywords like `int`, `bool`, `string` are usable in expression position,
6. slicing/index-from-end syntax is part of the language surface,
7. known data-structure operator rewrites are formally part of the language design,
8. target-typed array allocation shorthands `new[n]` and `new![n]` are part of the model,
9. `operator<=>(other)` is the preferred shorthand for type-local ordering declaration,
10. `public:` / `private:` / `protected:` / `internal:` are now explicit experimental PSCP section labels distinct from pass-through C# modifiers.

---

## 4. Big Picture Mental Model

A useful mental model for PSCP `v0.4` is:

- **C#/.NET stays available** when already convenient,
- **PSCP adds high-value shorthand** where C# is too verbose for contests,
- **transpilation should eliminate cost**, not add runtime abstraction,
- **source syntax should expose intent**, especially around allocation and iteration.

This gives PSCP a practical identity:

- C#-backed,
- contest-oriented,
- expression-friendly,
- compile-time-optimized.

---

## 5. Syntax Summary

## 5.1 Bindings and Mutability

Bindings are immutable by default.

- `let` = inferred immutable
- `var` = inferred mutable
- `mut` = explicitly typed mutable

Examples:

```txt
let x = 3
var sum = 0
mut int answer = 0
```

---

## 5.2 Top-Level Statements

A PSCP source file may contain:

- top-level statements,
- functions,
- type declarations,
- pass-through `namespace` / `using` forms.

This keeps one-file contest solutions natural.

---

## 5.3 Input and Output Shorthand

### Input

```txt
int n =
int n, m =
int[n] arr =
(int, int)[m] edges =
```

### Output

```txt
= expr
+= expr
```

Meaning:

- `= expr`  -> write without newline
- `+= expr` -> write with newline

---

## 5.4 Materialized Collections vs Generators

This is one of the most important `v0.4` distinctions.

### `[]` = materialized collection

```txt
[1, 2, 3]
[0..<n]
[0..<n -> i do i * i]
```

### `(...)` with `for` or `->` = generator/lazy iterable

```txt
(0..<n -> i do i * i)
(0..<n -> i {
    let x = i * i
    x + 1
})
```

Practical mental model:

- `[]` means “build a concrete thing”
- `()` generator means “produce an iterable thing”

---

## 5.5 Ranges

Supported forms:

```txt
1..10
0..<n
0..=n
10..-1..0
```

Ranges are iterable and participate in loops, builders, generators, and aggregate calls.

---

## 5.6 `:=`

`:=` is an explicit value-yielding assignment expression.

```txt
a = b := c
parent[x] := find(parent[x])
```

Meaning:

1. assign,
2. evaluate to the assigned value.

It is **not** return syntax.

---

## 5.7 Conversion Keywords in Expression Position

Built-in type keywords may act like conversion/parsing functions.

Examples:

```txt
int "123"
long "1000"
int(true)
bool "hello"
bool 0
string 123
```

This is intended to cover practical parse/cast/truthiness use cases.

---

## 5.8 Slicing and Index-From-End

PSCP supports C#-style indexing and slicing surface.

Examples:

```txt
text[^1]
text[1..^2]
arr[..n]
arr[1..]
arr[..]
```

At minimum, the intended scope is strings and one-dimensional arrays.

---

## 5.9 Aggregate Intrinsic Families

Aggregate operations are ordinary intrinsic calls.

Examples:

```txt
min a b
max a b c
sum arr
sum (0..<n -> i do score(i))
sumBy arr (x => x * x)
minBy points (p => p.x)
maxBy items (x => score(x))
chmin ref best cand
chmax ref ans value
```

This replaces older special aggregation syntax.

---

## 5.10 Comparator Sugar

PSCP provides comparator sugar:

```txt
T.asc
T.desc
```

Examples:

```txt
int.asc
int.desc
arr.sortWith(int.desc)
PriorityQueue<int, int> pq = new(int.desc)
```

---

## 5.11 Target-Typed Array Allocation

### `new[n]`

```txt
NodeInfo[] nodes = new[n]
```

### `new![n]`

```txt
List<int>[] graph = new![n]
```

Meaning:

- `new[n]` = allocate array from target type
- `new![n]` = allocate array and auto-construct each element when the element type is a known collection type

---

## 5.12 Known Data-Structure Operator Rewrites

PSCP defines compiler-known operator rewrites for selected data structures.

### `HashSet<T>`

```txt
visited += x    // Add, returns bool
visited -= x    // Remove, returns bool
```

### `Stack<T>`

```txt
s += x          // Push
~s              // Peek
--s             // Pop and return
```

### `Queue<T>`

```txt
q += x          // Enqueue
~q              // Peek
--q             // Dequeue and return
```

### `PriorityQueue<TElement, TPriority>`

```txt
pq += (item, priority)
~pq             // item only
--pq            // item only
```

If priority is needed too, use explicit APIs such as `TryPeek` / `TryDequeue`.

---

## 5.13 Ordering Shorthand in Type Bodies

Inside a type body, the preferred shorthand is:

```txt
operator<=>(other) => this.value <=> other.value
```

Meaning:

- `other` is the same type as `this`,
- return type is implicitly `int`,
- this defines the type's default ordering.

---

## 5.14 Access Labels vs Pass-Through Modifiers

This distinction matters.

### Pass-through C# modifiers

```txt
public
private
protected
internal
```

These are ordinary C# modifiers.
PSCP does not reinterpret them.

### PSCP section labels

```txt
public:
private:
protected:
internal:
```

These are experimental PSCP section labels.

Important `v0.4` behavior:

- they apply only to methods,
- they affect only methods without explicit modifiers,
- they are lightweight and warning-oriented,
- transpilation is not blocked,
- generated C# may still use `public`.

---

## 6. API Summary

## 6.1 `stdin`

Intrinsic input object.

Representative members:

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

---

## 6.2 `stdout`

Intrinsic output object.

Representative members:

```txt
stdout.write(x)
stdout.writeln(x)
stdout.flush()
stdout.lines(xs)
stdout.grid(g)
stdout.join(sep, xs)
```

---

## 6.3 `Array.zero`

```txt
Array.zero(n)
```

One-dimensional default-initialized array helper.

---

## 6.4 Aggregate Family

The intrinsic aggregate family includes:

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

---

## 6.5 Conversion Family

Expression-position conversion keywords include:

```txt
int
long
double
decimal
bool
char
string
```

---

## 6.6 Comparator Sugar

```txt
T.asc
T.desc
```

---

## 6.7 Known Auto-Constructed Collections

The initial known set is:

- `List<T>`
- `LinkedList<T>`
- `Queue<T>`
- `Stack<T>`
- `HashSet<T>`
- `Dictionary<K, V>`
- `SortedSet<T>`
- `PriorityQueue<TElement, TPriority>`

This affects:

- declarations without initializer,
- `new![n]`.

---

## 7. Transpiler Summary

## 7.1 Core Direction

The transpiler must remove PSCP sugar by compile-time lowering rather than leaving meaning inside generic runtime helpers.

This is the main practical promise of PSCP.

---

## 7.2 Statement Lowering vs Expression Lowering

These must be separated clearly.

Do not encode normal statement control flow through compiler-generated lambda thunks.

---

## 7.3 Fast I/O Expectations

The reference backend is expected to prefer:

- specialized scalar scanner calls,
- direct loops for fixed-shape reads,
- buffered output,
- direct rendering for common scalar/tuple/1D collection outputs.

---

## 7.4 Range Optimization Rule

This is one of the most important `v0.4` constraints.

Simple numeric range iteration should lower directly to C# `for` loops when possible.

Examples that should not become helper-enumerable `foreach` by default:

```txt
for i in 0..<n { ... }
0..<n -> i { ... }
sum (0..<n -> i do f(i))
```

---

## 7.5 Generator vs Materialization

The transpiler is expected to preserve the semantic distinction:

- `[]` -> concrete materialized result
- `()` generator -> iterable/lazy result

For example:

```txt
sum (0..<n -> i do score(i))
```

should not imply an unnecessary intermediate array allocation.

---

## 7.6 Aggregate Lowering

Aggregate family calls should lower as follows in the reference backend:

- small fixed-arity `min/max` -> compare trees
- iterable `min/max/sum` -> direct loops
- `sumBy` -> direct loop with selector
- `minBy/maxBy` -> best-element tracking loop
- `chmin/chmax` -> direct compare-update rewrite

---

## 7.7 Conversion Lowering

Common conversion-keyword calls should lower directly.

Examples:

- string -> numeric parse
- bool -> 0/1
- numeric -> bool via `!= 0`
- scalar cast-like conversions directly

The reference backend should avoid generic reflection-like conversion in hot paths.

---

## 7.8 Known Data-Structure Operator Lowering

These should compile directly to the underlying .NET calls.

Examples:

```txt
visited += x   -> visited.Add(x)
--q            -> q.Dequeue()
~s             -> s.Peek()
```

No runtime wrapper abstraction is desired here.

---

## 7.9 `new![n]`

This should lower to:

1. one array allocation,
2. one direct initialization loop.

No LINQ-based initialization pipeline should be the default lowering.

---

## 7.10 Things the Reference Backend Should Avoid

The reference backend should avoid:

- `dynamic` in core aggregates and compare paths,
- reflection-like scalar conversion in hot paths,
- helper-enumerable lowering for simple numeric ranges,
- compiler-generated lambda thunks for ordinary statement lowering,
- forced materialization of generator-fed aggregate calls,
- blind LINQ lowering for simple shaped input/builders/aggregates.

---

## 8. LSP / Tooling Summary

## 8.1 What the Language Server Must Understand in v0.4

At minimum, the LSP should understand:

- shorthand input/output,
- aggregate family,
- conversion-keyword forms,
- `[]` vs `()` builder/generator difference,
- `:=`,
- slicing / `^`,
- `new[n]`, `new![n]`,
- known data-structure operator rewrites,
- `operator<=>(other)`,
- `public` vs `public:` distinction.

---

## 8.2 Particularly Important Hover / Completion Cases

The server should be especially strong at:

- aggregate-family hover and signatures,
- conversion-keyword docs,
- `HashSet +=` returning `bool`,
- `new![n]` meaning per-element construction,
- section-label warning behavior,
- `operator<=>(other)` ordering meaning.

---

## 9. Compact Change List from v0.3 to v0.4

### Syntax changes / clarifications

- added explicit generator expression direction using `()` with `for` / `->`,
- formalized `:=` as value-yielding assignment expression,
- expanded conversion-keyword syntax in expression position,
- added slicing/index-from-end surface,
- added `++/--`,
- added known data-structure operator rewrite surface,
- added `new[n]` and `new![n]`,
- added `operator<=>(other)` shorthand,
- formalized section-label distinction from pass-through modifiers.

### API changes / clarifications

- aggregate families formalized further,
- conversion-keyword family formalized,
- generator vs materialized collection semantics formalized,
- known DS operator semantics formalized,
- `new![n]` semantics formalized.

### Transpiler changes / clarifications

- stronger direct `for` lowering rule for numeric ranges,
- stronger no-materialization expectation for generator-fed aggregate calls,
- stronger direct rewrite expectations for `chmin/chmax` and known DS operators,
- stronger anti-pattern guidance against `dynamic`, reflection-like hot input, and helper enumerable misuse.

### Tooling changes / clarifications

- LSP expected to understand section labels,
- LSP expected to understand aggregate family / conversion family / `:=` / generator expressions,
- hover/completion/diagnostics requirements expanded accordingly.

---

## 10. Example Programs

The following examples are intentionally practical and representative of PSCP `v0.4` style.

---

## 10.1 Basic Sum Modulo

```txt
int n, m =
int[n] arr =

= arr.fold(0, (acc, x) => (acc + x) % m)
```

---

## 10.2 Sum over a Generator

```txt
int n =

let total = sum (0..<n -> i {
    let x = i * i
    x + 1
})

= total
```

---

## 10.3 Materialized Builder

```txt
int n =
let squares = [0..<n -> i do i * i]
+= squares
```

---

## 10.4 Union-Find

```txt
int n =
int[] parent = [0..<n]
int[n] size;

for i in 0..<n do size[i] = 1

rec int find(int x) {
    if parent[x] == x then x
    else parent[x] := find(parent[x])
}

bool unite(int a, int b) {
    a = find(a)
    b = find(b)
    if a == b then false

    if size[a] < size[b] {
        (a, b) = (b, a)
    }

    parent[b] = a
    size[a] += size[b]
    true
}
```

---

## 10.5 Coordinate Compression

```txt
int n =
int[n] xs =

let sorted = xs.sort().distinct()
let index = sorted.index()
let compressed = [xs -> x do index[x]]

+= compressed
```

---

## 10.6 BFS with `HashSet +=`

```txt
int n, m =
char[][] grid = stdin.charGrid(n)
Queue<(int, int)> queue;
HashSet<(int, int)> visited;

queue += (0, 0)
_ = visited += (0, 0)

int[] dr = [-1, 1, 0, 0]
int[] dc = [0, 0, -1, 1]

while queue.Count > 0 {
    let (r, c) = --queue

    for k in 0..<4 {
        let nr = r + dr[k]
        let nc = c + dc[k]

        if nr < 0 or nr >= n or nc < 0 or nc >= m then continue
        if grid[nr][nc] == '#' then continue
        if not (visited += (nr, nc)) then continue

        queue += (nr, nc)
    }
}
```

---

## 10.7 Prefix-Min Aggregate

```txt
int n =
int[n] a =

let best = min (0..<n -> i do a[i] - i)
= best
```

---

## 10.8 `chmin` / `chmax`

```txt
int n =
int[n] a =

var best = int.MaxValue
var worst = int.MinValue

a -> x {
    _ = chmin(ref best, x)
    _ = chmax(ref worst, x)
}

+= (best, worst)
```

---

## 10.9 `new![n]` Graph Initialization

```txt
int n, m =
List<int>[] graph = new![n]

for i in 0..<m {
    int a, b =
    graph[a] += b
    graph[b] += a
}
```

---

## 10.10 Stack / Queue Operators

```txt
Stack<int> s;
s += 10
s += 20
let top = ~s
let x = --s

Queue<int> q;
q += 3
q += 4
let first = ~q
let y = --q
```

---

## 10.11 Priority Queue with Comparator Sugar

```txt
int n =
PriorityQueue<int, int> pq = new(int.desc)

for i in 0..<n {
    int x =
    pq.Enqueue(x, x)
}

while pq.Count > 0 {
    pq.TryDequeue(out int x, out _)
    += x
}
```

And the planned shorthand surface is:

```txt
int n =
PriorityQueue<int, int> pq = new(int.desc)

for i in 0..<n {
    int x =
    pq += (x, x)
}

while pq.Count > 0 {
    += --pq
}
```

---

## 10.12 Conversion Keywords

```txt
let a = int "123"
let b = int(true)
let c = bool "hello"
let d = bool 0
let e = string 123
```

---

## 10.13 Slicing

```txt
string text = stdin.line()

let middle = text[1..^1]
let prefix = text[..^2]
let last = text[^1]

+= middle
+= prefix
+= last
```

---

## 10.14 Ordering Shorthand

```txt
record struct Job(int Id, int Arrival, long Time) {
    operator<=>(other) =>
        if Arrival == other.Arrival then Time <=> other.Time
        else Arrival <=> other.Arrival
}
```

---

## 10.15 Section Labels

```txt
class Node {
private:
    helper() {
        += "helper"
    }

public:
    solve() {
        helper()
    }
}
```

Remember:

- `public:` is PSCP experimental section-label syntax,
- `public` without colon is ordinary pass-through C#.

---

## 10.16 Suffix Array Style Example

```txt
int n =
string text = stdin.line()

rec (int[], int[]) makeSuffix(int[] suffix, int[] rank, int skip) {
    int compare(int a, int b) {
        if rank[a] != rank[b] {
            return rank[a] <=> rank[b]
        }

        let ra = if a + skip < n then rank[a + skip] else -1
        let rb = if b + skip < n then rank[b + skip] else -1
        ra <=> rb
    }

    if skip >= n {
        return (suffix, rank)
    }

    let sorted = suffix.sortWith(compare)
    int[n] next;

    for i in 1..<n {
        next[sorted[i]] =
            next[sorted[i - 1]] +
            int(compare(sorted[i - 1], sorted[i]) != 0)
    }

    if next[sorted[n - 1]] + 1 < n {
        return makeSuffix(sorted, next, skip * 2)
    }

    (sorted, next)
}

let suffix, rank = makeSuffix(
    [0..<n],
    [0..<n -> i do int(text[i]) - int('a')],
    1
)

int[n] lcp;
var h = 0

for i in 0..<n {
    if rank[i] + 1 >= n {
        h = 0
    } else {
        int j = suffix[rank[i] + 1]

        while i + h < n and j + h < n and text[i + h] == text[j + h] {
            h += 1
        }

        lcp[rank[i]] = h

        if h > 0 {
            h -= 1
        }
    }
}

= lcp.max()
```

---

## 11. Practical Advice

A good default PSCP `v0.4` style is:

- use top-level statements for one-file contest solutions,
- use `let` unless mutation is genuinely needed,
- use `[]` when you want a concrete collection,
- use `()` generator form when feeding aggregates or iterable consumers,
- use shorthand input/output aggressively,
- prefer aggregate-family calls over handwritten boilerplate when readability improves,
- use conversion-keyword calls for concise parse/cast/truthiness code,
- use known DS operators only when they clearly improve readability,
- use braces early when single-statement `then` / `do` starts to feel cramped,
- remember that `public:` is not the same thing as `public`.

---

## 12. Suggested Sanity Checklist for Users and Implementers

A good `v0.4` mental checklist is:

1. Did I mean a concrete collection (`[]`) or a lazy iterable (`()`) ?
2. Am I using `min/max/sum/...` as intrinsic calls rather than older special syntax?
3. Did I use `:=` only when I truly want assignment as a value?
4. Am I relying on `HashSet +=` returning `bool` intentionally?
5. Did I use `new![n]` only for known collection element types?
6. Am I accidentally confusing `public:` with pass-through `public`?
7. Is my generator-fed aggregate avoiding needless allocation?
8. Would braces make this `then` / `do` clearer?

---

## 13. Closing Note

PSCP `v0.4` is the point where the language becomes much more explicit about its real identity:

- PSCP code should be short,
- generated C# should still be direct and fast,
- the transpiler should absorb the hard work,
- and ordinary C#/.NET should remain available where it already feels right.

This summary document is intended to make that direction easy to see at a glance.

For exact semantics, the full `v0.4` language, API, transpiler, and language-server specifications remain authoritative.

