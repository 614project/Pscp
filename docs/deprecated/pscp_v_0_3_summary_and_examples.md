# PS/CP v0.3 Summary and Examples

## 1. Scope

This document is a compact implementation-facing summary of the `v0.3` design.

It consolidates the major changes across:

- language syntax,
- intrinsic API surface,
- transpiler behavior.

It also provides example PS/CP programs written in PS/CP source style.

This document is intended as a quick reference alongside the full specifications.

---

## 2. Version Summary

Version `v0.3` shifts the language toward a more pragmatic hybrid model:

1. keep PS/CP-oriented sugar where it materially helps,
2. preserve ordinary .NET and C# surface where that is already convenient,
3. move performance-sensitive behavior out of generic runtime helpers and into compile-time specialization.

The overall direction is:

- **language-owned sugar for algorithm-writing speed**,
- **pass-through C#/.NET for nominal types and ecosystem interop**,
- **hardcoded lowering for contest-critical constructs**.

---

## 3. High-Level Changes from Earlier Drafts

### 3.1 Syntax Direction

The syntax is no longer trying to replace broad areas of C# surface syntax.

Instead:

- `.NET` naming remains unchanged,
- `class`, `struct`, `record`, `new`, `this`, `base`, `namespace`, `using`, `is`, `switch` remain near-pass-through,
- PS/CP sugar is concentrated in input/output, collections, ranges, iteration, aggregation, comparator sugar, and shorthand declarations.

### 3.2 API Direction

The standard API now distinguishes clearly between:

- ordinary `.NET` APIs that remain untouched,
- intrinsic language APIs such as `stdin`, `stdout`, `Array.zero`,
- compiler-known behaviors such as collection auto-construction and comparator sugar.

### 3.3 Transpiler Direction

The transpiler is now explicitly required to:

- separate statement lowering from expression lowering,
- eliminate sugar early,
- avoid runtime helper overuse,
- hardcode efficient backend patterns for fast I/O and fixed-shape constructs,
- preserve ordinary C#/.NET surface when possible.

---

## 4. Syntax Summary

## 4.1 Bindings and Mutability

- default bindings are immutable,
- `let` means inferred immutable,
- `var` means inferred mutable,
- `mut` means explicitly typed mutable.

Examples:

```txt
let x = 3
var y = 0
mut int z = 1
```

### New Clarification

Uninitialized declarations are now explicitly constrained:

- `mut int x;` is allowed and means default initialization,
- `int[n] arr;` is allowed and means sized array allocation,
- known collection types such as `List<int> list;` are allowed and mean implicit `new()`.

---

## 4.2 Known Collection Auto-Construction

Certain well-known collection types may be declared without an initializer and are implicitly constructed with `new()`.

Initial set:

- `List<T>`
- `LinkedList<T>`
- `Queue<T>`
- `Stack<T>`
- `HashSet<T>`
- `Dictionary<K, V>`
- `SortedSet<T>`
- `PriorityQueue<TElement, TPriority>`

Examples:

```txt
List<int> list;
Queue<int> queue;
HashSet<int> seen;
PriorityQueue<int, int> pq;
```

Equivalent meaning:

```txt
List<int> list = new()
Queue<int> queue = new()
HashSet<int> seen = new()
PriorityQueue<int, int> pq = new()
```

---

## 4.3 Collection Expressions

The `[]` collection syntax remains central.

Supported forms:

```txt
[]
[1, 2, 3]
[0..<n]
[1, 2, ..xs, 6]
[0..<n -> i do i * i]
```

Rules:

- range elements inside `[]` auto-expand,
- non-range iterable expansion requires `..spread`,
- builder forms yield one element per iteration.

---

## 4.4 Ranges

Supported range forms:

```txt
1..10
0..<n
0..=n
10..-1..0
```

Meaning:

- inclusive,
- right-exclusive,
- explicit inclusive,
- stepped range.

---

## 4.5 Calls

Two call forms are supported:

### Parenthesized calls

```txt
f(x)
g(x, y)
```

### Space-separated calls

```txt
f x
g x y
obj.method x y
```

Space-separated calls are surface sugar for ordinary multi-argument invocation.

Currying is **not** implied.

---

## 4.6 `ref`, `out`, `in`

`ref`, `out`, and `in` now follow C# surface rules and are accepted in both parenthesized and space-separated call syntax.

Examples:

```txt
foo(ref x, out y, in z)
foo out int a out int b
foo(out _, ref arr[i])
```

Lambdas may also use modified parameters:

```txt
(ref int x, out int y) => { ... }
```

---

## 4.7 Control Flow

The language remains brace-based and expression-friendly.

Supported forms include:

- block `if` / `else`,
- one-line `if ... then ... else ...`,
- `while`,
- `for ... in ...`,
- one-line `do`,
- `break`, `continue`, `return`,
- fast iteration `->`.

Examples:

```txt
if ok then a else b
for i in 0..<n do sum += a[i]
xs -> x {
    += x
}
```

---

## 4.8 Comparator Sugar

New shorthand comparator surface:

```txt
T.asc
T.desc
```

Examples:

```txt
int.asc
int.desc
string.asc
string.desc
```

Intended use:

```txt
arr.sortWith(int.asc)
arr.sortWith(int.desc)
PriorityQueue<int, int> pq = new(int.desc)
```

---

## 4.9 User-Defined Types

The language now explicitly accepts near-pass-through C# nominal type declarations.

Supported surface:

- `class`
- `struct`
- `record`
- base class and interface lists
- constructors
- methods
- fields
- `this`, `base`, `new()`

Examples:

```txt
class Dsu { ... }
struct Edge { ... }
record struct Job(int Id, int Arrival, long Time) : IComparable<Job> { ... }
```

---

## 5. API Summary

## 5.1 Ordinary .NET Preservation

Ordinary `.NET` APIs remain available exactly as written.

Examples:

```txt
Queue<int> queue = new()
HashSet<int> set = new()
Math.Max(a, b)
Array.Sort(arr)
```

No casing rewrite or language-owned aliasing is required.

---

## 5.2 `stdin`

Intrinsic input object.

Core methods:

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

Declaration shorthand remains semantic sugar over this surface.

---

## 5.3 `stdout`

Intrinsic output object.

Core methods:

```txt
stdout.write(x)
stdout.writeln(x)
stdout.flush()
stdout.lines(xs)
stdout.grid(g)
stdout.join(sep, xs)
```

Statement shorthand remains:

```txt
= expr
+= expr
```

with meanings:

- write,
- writeline.

---

## 5.4 `Array.zero`

`Array.zero(n)` remains the standard one-dimensional zero/default initialization helper.

Equivalent to:

```txt
int[n] arr;
```

for array declarations.

---

## 5.5 Collection Helper Surface

Reserved helper names include:

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

These may lower to direct code or helpers depending on backend strategy.

---

## 6. Transpiler Summary

## 6.1 Main Direction Change

The transpiler is no longer allowed to lean on broad generic runtime wrappers for language-owned sugar.

The preferred strategy is now:

1. classify sugar precisely,
2. lower it early,
3. emit direct C# loops / allocations / scanner calls / writer calls,
4. keep runtime helpers small.

---

## 6.2 Statement vs Expression Lowering

This is one of the most important clarifications in `v0.3`.

The transpiler must maintain a strict separation between:

- statement lowering,
- expression lowering.

It must **not** solve ordinary statement emission by wrapping control flow in compiler-generated lambdas or expression thunks.

This specifically applies to:

- `if`,
- `while`,
- `for`,
- `break`,
- `continue`,
- `return`,
- declaration shorthand,
- output shorthand.

---

## 6.3 Fast I/O Contract

The backend is expected to prefer buffered I/O.

Recommended baseline shapes:

```csharp
StreamReader reader = new(Console.OpenStandardInput());
StreamWriter writer = new(Console.OpenStandardOutput(), bufferSize: 1048576);
```

Output should normally flush once at program end unless source explicitly flushes earlier.

A more specialized scanner is allowed.

---

## 6.4 Compile-Time Specialization

The transpiler is expected to hardcode efficient lowering for:

- scalar input shorthand,
- fixed-size array input shorthand,
- tuple input shorthand,
- output shorthand,
- sized array allocation,
- range-based loops,
- collection builders,
- aggregations,
- comparator sugar,
- known collection auto-construction.

The goal is to move work from runtime helpers into compile-time lowering where feasible.

---

## 6.5 Preferred Lowering Patterns

### Scalar input

```txt
int n =
```

preferred meaning in backend:

```csharp
int n = __pscpIn.Int();
```

### Fixed-size array input

```txt
int[n] arr =
```

preferred lowering:

```csharp
int[] arr = new int[n];
for (int i = 0; i < n; i++) arr[i] = __pscpIn.Int();
```

### Sized array declaration

```txt
int[n] arr;
```

preferred lowering:

```csharp
int[] arr = new int[n];
```

### Known collection auto-construction

```txt
List<int> list;
```

preferred lowering:

```csharp
List<int> list = new();
```

### Comparator sugar

```txt
int.desc
```

preferred semantic lowering:

- descending comparer for `int`
- exact emitted C# shape is implementation-defined

---

## 6.6 Range Lowering

Ranges should be lowered context-sensitively.

- in `for` loops: direct C# `for` whenever possible,
- in collection builders: direct fill loops,
- in standalone expression context: helper iterable is acceptable.

---

## 6.7 Aggregation Lowering

Intrinsic aggregate calls should lower to explicit loops and accumulators in the reference implementation.

Example:

```txt
min [0..<n -> i do a[i] - b[i]]
sum [0..<n -> i do a[i]]
sumBy arr (x => x * x)
minBy points (p => p.2)
```

preferred backend style:

- accumulator local,
- explicit loop,
- compare/update,
- final value.

Not generic LINQ chains by default.

---

## 6.8 Collection Builder Lowering

Builder forms such as:

```txt
[0..<n -> i do i * i]
[arr -> x do f(x)]
```

should lower to explicit build logic.

Preferred strategy:

- direct array fill if size known,
- list builder if size unknown,
- final conversion if needed.

---

## 6.9 Pass-Through Preservation

Type declarations and ordinary `.NET` surface should remain structurally recognizable in generated C# whenever practical.

This includes:

- `class / struct / record`,
- base/interface lists,
- `new()`,
- target-typed `new`,
- tuple assignment,
- `ref/out/in`,
- `using`, `namespace`,
- ordinary .NET type/member names.

---

## 7. Compact Change List

### Syntax changes and clarifications

- formalized pass-through C# surface,
- added `ref/out/in` support in calls and lambdas,
- added near-pass-through `class/struct/record`,
- added compiler-known collection auto-construction,
- added comparator sugar `T.asc` / `T.desc`,
- preserved `[]`, range, spread, builder, `->`, one-line `then/do`, input/output shorthand.

### API changes and clarifications

- clarified `stdin` / `stdout` as intrinsic surfaces,
- clarified declaration shorthand and output shorthand semantic equivalence,
- clarified `Array.zero` equivalence for sized declarations,
- clarified collection helper surface,
- clarified comparator sugar API meaning,
- clarified known-collection auto-construction as compiler-known behavior, not generic runtime magic.

### Transpiler changes and clarifications

- sugar must disappear early,
- statement lowering and expression lowering must be separate,
- generic helper overuse is discouraged,
- hardcoded compile-time specialization is encouraged,
- fast buffered I/O is the expected backend contract,
- direct loops preferred over LINQ for shaped input/builders/aggregations,
- ordinary .NET syntax should remain visible and preserved when possible.

---

## 8. Example Programs

The following examples are intentionally written in PS/CP source style and are meant to help implementation and testing.

---

## 8.1 Example: Basic Sum Modulo

```txt
int n, m =
int[n] arr =

= arr.fold(0, (acc, x) => (acc + x) % m)
```

---

## 8.2 Example: Union-Find Initialization

```txt
int n =
int[] parent = [0..<n]
int[n] size;

for i in 0..<n do size[i] = 1

rec int find(int x) {
    if parent[x] == x then x
    parent[x] = find(parent[x])
    parent[x]
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

## 8.3 Example: Coordinate Compression

```txt
int n =
int[n] xs =

let sorted = xs.sort().distinct()
let index = sorted.index()
let compressed = [xs -> x do index[x]]

+= compressed
```

---

## 8.4 Example: BFS on Grid

```txt
int n, m =
char[][] grid = stdin.charGrid(n)
int[n][m] dist;
Queue<(int, int)> queue;

for r in 0..<n {
    for c in 0..<m {
        dist[r][c] = -1
        if grid[r][c] == 'S' {
            dist[r][c] = 0
            queue.Enqueue((r, c))
        }
    }
}

int[] dr = [-1, 1, 0, 0]
int[] dc = [0, 0, -1, 1]

while queue.Count > 0 {
    let (r, c) = queue.Dequeue()

    for k in 0..<4 {
        let nr = r + dr[k]
        let nc = c + dc[k]

        if nr < 0 or nr >= n or nc < 0 or nc >= m { continue }
        if grid[nr][nc] == '#' { continue }
        if dist[nr][nc] != -1 { continue }

        dist[nr][nc] = dist[r][c] + 1
        queue.Enqueue((nr, nc))
    }
}

stdout.grid(dist)
```

---

## 8.5 Example: Prefix-Min Aggregate Call

```txt
int n =
int[n] a =

let best = min [0..<n -> i do a[i] - i]
= best
```

---

## 8.6 Example: Comparator Sugar with PriorityQueue

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

---

## 8.7 Example: `ref / out / in`

```txt
bool tryPopMax(PriorityQueue<int, int> pq, out int value) {
    if pq.Count == 0 {
        value = 0
        return false
    }

    pq.TryDequeue(out value, out _)
    true
}

int x = 10
int y;

_ = foo(ref x, out y, in x)
```

---

## 8.8 Example: Record Struct with Interface

```txt
record struct Job(int Id, int Arrival, long Time) : IComparable<Job> {
    public readonly int CompareTo(Job other) {
        if Arrival == other.Arrival {
            return Time <=> other.Time
        }
        Arrival <=> other.Arrival
    }
}
```

---

## 8.9 Example: SRPT Scheduler Style Example

This example is adapted to the PS/CP language style from the kind of C# structure frequently used for scheduling problems.

```txt
class Srpt {
    record struct Job(int Id, int Arrival, long Time) : IComparable<Job> {
        public readonly int CompareTo(Job other) {
            if Arrival == other.Arrival {
                return Time <=> other.Time
            }
            Arrival <=> other.Arrival
        }
    }

    List<Job> jobList;

    Srpt(int capacity) {
        jobList = new(capacity: capacity)
    }

    void addJob(int arrival, int time)
        => jobList.Add(new(jobList.Count, arrival, time))

    long[] simulate() {
        jobList.Sort()

        int n = jobList.Count
        long[n] completionTime;
        PriorityQueue<int, long> queue;

        long now = 0
        long currentRemaining = 0

        for (int nextJob = 0, currentId = -1;
             nextJob < n or queue.Count > 0 or currentId >= 0;
             )
        {
            if currentId < 0 and queue.Count is 0 and nextJob < n {
                now = jobList[nextJob].Arrival
            }

            while nextJob < n and jobList[nextJob].Arrival <= now {
                let (id, _, time) = jobList[nextJob++]
                queue.Enqueue(id, time)
            }

            long finishTime = now + currentRemaining

            if queue.TryPeek(out _, out long peekTime) and peekTime < currentRemaining {
                queue.Enqueue(currentId, currentRemaining)
                queue.TryDequeue(out currentId, out currentRemaining)
                finishTime = now + currentRemaining
            }
            else if currentId < 0 {
                queue.TryDequeue(out currentId, out currentRemaining)
                finishTime = now + currentRemaining
            }

            long next = Math.Min(
                nextJob < n ? jobList[nextJob].Arrival : long.MaxValue,
                finishTime
            )

            currentRemaining -= next - now
            now = next

            if currentRemaining <= 0 {
                completionTime[currentId] = now
                currentId = -1
            }
        }

        Array.Sort(completionTime)
        completionTime
    }
}
```

---

## 8.10 Example: Suffix Array Style Example

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
            if compare(sorted[i - 1], sorted[i]) == 0 then 0 else 1
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

0..<n -> i {
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

## 8.11 Example: Path-Fixing / Prefix Difference Example

```txt
int n =
char[] a = stdin.chars()
char[] b = stdin.chars()

int size = 2 * n
int last = size - 1

char rev(char target)
    => if target == 'U' then 'R' else 'U'

int[] build(char[] path, char target) {
    path.mapFold(0, (acc, c) => {
        let next = acc + (c == target ? 1 : 0)
        (next, next)
    }).1
}

int fix(char[] p, char target) {
    var cost = 0

    if p[0] != target {
        let j =
            if p[last] == target then last
            else p.findLastIndex(c => c == target)

        (p[0], p[j]) = (p[j], p[0])
        cost += 1
    }

    if p[last] == target {
        let j = p.findIndex(c => c != target)
        (p[last], p[j]) = (p[j], p[last])
        cost += 1
    }

    cost
}

int solve(char[] p1, char[] p2, char target) {
    let fixCost = fix(p1, target) + fix(p2, rev(target))
    let x = build(p1, target)
    let y = build(p2, target)
    let d = min [0..<last -> i do x[i] - y[i]]
    
    fixCost + Math.Max(1 - d, 0)
}

+= Math.Min(
    solve(a.copy(), b.copy(), 'U'),
    solve(b.copy(), a.copy(), 'U')
)
```

## 8.12 Example: Sum with Block-Bodied Builder

```txt
int n =

let total = sum [0..<n -> i {
    let x = i * i
    let y = x + 1
    y
}]

= total
```

## 8.13 Example: `chmin` / `chmax`

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

## 9. Suggested Implementation Use

This summary document is best used as:

1. a quick checkpoint before changing the parser,
2. a frontend/backend contract cheat sheet,
3. a regression sample pool for parser and transpiler tests,
4. a short design summary for collaborators.

For exact semantics, the full `v0.3` language, API, and transpiler specifications remain authoritati