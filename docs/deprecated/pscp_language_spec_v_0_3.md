# PS/CP Language Specification v0.3

## 1. Scope

This document defines version `v0.3` of the PS/CP language syntax and source-level semantics.

This language is designed for problem solving and competitive programming. It is transpiled to C# and intentionally preserves compatibility with ordinary .NET and C# syntax wherever practical.

This document defines:

1. lexical structure,
2. declarations and bindings,
3. expression and statement syntax,
4. control flow,
5. collection and range syntax,
6. input and output shorthand,
7. user-defined type declarations,
8. pass-through compatibility areas.

This document does not define the standard API surface in full detail and does not define backend lowering strategy. Those are specified separately.

This document is normative unless otherwise stated.

---

## 2. Design Principles

1. **Brace-based syntax**. Indentation is never semantically significant.
2. **Default immutability**. Bindings are immutable unless explicitly mutable.
3. **Expression-oriented core** with practical imperative control flow.
4. **C# interop preservation**. Ordinary .NET types, members, and common C# syntax remain usable.
5. **Contest-oriented sugar**. Only syntax that materially improves algorithm-writing speed is introduced.
6. **Surface compatibility first, backend optimization later**. Source syntax is defined independently of any particular lowering strategy.

---

## 3. Source Form

### 3.1 Character Set

Source files are Unicode text.

### 3.2 Whitespace

Whitespace separates tokens where needed. Outside explicitly defined one-line constructs, newlines do not affect semantics.

### 3.3 Comments

The following comment forms are supported:

```txt
// single-line comment
/* multi-line comment */
```

### 3.4 Statement Separation

Statements may be separated by:

1. newline,
2. semicolon,
3. block boundaries.

Semicolons are optional. They may be used to place multiple statements on one line.

```txt
var x = 0; x += 1; += x
```

A trailing semicolon is permitted.

---

## 4. Keywords and Reserved Tokens

### 4.1 Reserved Keywords

```txt
let var mut rec
if then else
for in do while
break continue return
match when
true false null
and or xor not
class struct record
ref out in
new this base
namespace using
is
```

### 4.2 Reserved Operators and Special Tokens

```txt
= += -= *= /= %=
== != < <= > >= <=>
&& || ^ !
.. ..< ..=
|> <|
->
..
_
```

The token `..` is context-sensitive and may denote:

1. a range operator,
2. a spread element inside a collection expression.

---

## 5. Lexical Categories

### 5.1 Identifiers

Identifiers begin with a Unicode letter or underscore and may continue with letters, digits, or underscores.

The single underscore `_` is reserved as a discard token and is not a normal identifier.

### 5.2 Literals

The language supports ordinary C#-style literal forms for:

- integer literals,
- long literals,
- floating-point literals,
- decimal literals,
- character literals,
- string literals,
- boolean literals,
- `null`.

Examples:

```txt
0
1
42
1L
3.14
'a'
"hello"
true
false
null
```

---

## 6. Pass-Through C# Surface

The following syntax categories are part of the language and are treated as pass-through or near-pass-through syntax. They are accepted in source form and retain ordinary C# meaning unless this specification states otherwise.

### 6.1 Namespace and Using Declarations

The following forms are supported:

```txt
namespace X.Y
namespace X.Y { ... }
using System
using System.Collections.Generic
using static System.Math
using Alias = Some.Type
```

### 6.2 `new`, `this`, `base`

The following forms are supported:

```txt
new T(...)
new()
new T[n]
this
base
```

### 6.3 Type Tests and Pattern-Like Surface

The following forms are supported as pass-through syntax:

```txt
x is T
x is not T
x is 0
```

### 6.4 Switch Expressions and Inline Switch Surface

Inline switch-expression style syntax may appear as pass-through syntax and follows C# meaning.

### 6.5 Existing .NET APIs

Ordinary .NET types and members remain available under their original names and casing.

Examples:

```txt
Queue<int> queue = new()
PriorityQueue<int, int> pq = new()
HashSet<int> set = new()
Comparer<int>.Create((a, b) => b <=> a)
```

---

## 7. Types

### 7.1 Built-In Scalar Types

The following scalar types are built in:

- `int`
- `long`
- `double`
- `decimal`
- `bool`
- `char`
- `string`

### 7.2 Tuple Types

Tuple types are written as comma-separated type lists in parentheses.

```txt
(int, int)
(long, int, string)
```

### 7.3 Array Types

Ordinary array type syntax is supported.

```txt
int[]
int[][]
(string, int)[]
```

### 7.4 Generic and User Types

Generic types and user-defined nominal types use ordinary C#-style syntax.

```txt
List<int>
Dictionary<int, string>
MyType
MyGeneric<T>
```

### 7.5 Sized Declaration Types

Sized declaration forms are supported only in declaration position.

```txt
int[n] arr
int[n][m] grid
```

These are declaration forms, not ordinary reusable type expressions.

---

## 8. Bindings and Mutability

### 8.1 Default Immutability

All bindings are immutable unless declared with `mut` or `var`.

### 8.2 Declaration Forms

#### 8.2.1 Immutable Explicitly Typed Declaration

```txt
int x = 3
string s = "abc"
```

#### 8.2.2 Immutable Inferred Declaration

```txt
let x = 3
let s = "abc"
```

#### 8.2.3 Mutable Explicitly Typed Declaration

```txt
mut int x = 0
mut string s = ""
```

#### 8.2.4 Mutable Inferred Declaration

```txt
var x = 0
var s = ""
```

### 8.3 Multiple Declarations

Multiple declarations are permitted.

```txt
int a, b = 1, 2
let x, y = f()
var i, j = 0, 0
```

The right-hand side may be comma-separated syntax or tuple-valued.

### 8.4 Uninitialized Declarations

#### 8.4.1 Mutable Scalars and References

Uninitialized declaration is permitted only for mutable explicitly typed bindings.

```txt
mut int x;
mut string s;
mut MyClass obj;
```

The binding is initialized with the default value of its type.

#### 8.4.2 Sized Arrays

Sized arrays may omit an initializer.

```txt
int[n] a;
int[n][m] dp;
```

These allocate arrays and initialize all elements to the default value of the element type.

#### 8.4.3 Auto-Constructed Known Collection Types

A declaration without initializer is permitted for certain compiler-known collection types. Such a declaration is treated as implicit `new()` construction.

The initial set of such types is:

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
HashSet<int> set;
Dictionary<int, int> map;
PriorityQueue<int, int> pq;
```

These are semantically equivalent to:

```txt
List<int> list = new()
Queue<int> queue = new()
HashSet<int> set = new()
Dictionary<int, int> map = new()
PriorityQueue<int, int> pq = new()
```

#### 8.4.4 Forbidden Forms

The following are compile-time errors:

```txt
int x;
string s;
let a;
var b;
MyType obj;
```

unless the form is covered by Section 8.4.2 or 8.4.3.

---

## 9. Discards

The token `_` denotes a discard.

Allowed positions:

- declaration target,
- assignment target,
- destructuring target,
- lambda parameter,
- `out _` argument.

Examples:

```txt
_ = f()
let _ = g()
(a, _, c) = foo()
_ => 1
(a, _, c) => a + c
foo(out _)
```

A discard cannot be referenced as a value.

---

## 10. Expressions

### 10.1 Expression Categories

The language supports the following expression forms:

- literals,
- identifiers,
- parenthesized expressions,
- tuple expressions,
- block expressions,
- conditional expressions,
- unary and binary expressions,
- range expressions,
- collection expressions,
- function and method calls,
- indexing,
- member access,
- tuple projection,
- lambdas,
- pass-through expressions supported by the C# surface.

### 10.2 Parenthesized Expressions

```txt
(x)
(a + b)
(if ok then 1 else 2)
```

### 10.3 Tuple Expressions

Tuple expressions are comma-separated expressions enclosed in parentheses.

```txt
(1, 2)
(x, y, z)
```

### 10.4 Block Expressions

A brace-delimited block may be used as an expression.

```txt
{
    let x = 1
    x + 1
}
```

Block value behavior is governed by Section 21.

---

## 11. Collection Expressions

### 11.1 Syntax

A collection expression is enclosed in square brackets.

```txt
[]
[1, 2, 3]
[1, x + 1, y]
```

### 11.2 Default Result Type

#### 11.2.1 Declaration Context with `let`

In declaration context with `let`, a collection expression defaults to an array type if element types can be inferred.

```txt
let a = [1, 2, 3]
```

#### 11.2.2 Explicit Type Context

If a declaration target type is explicit, the collection expression is converted to that type.

```txt
int[] a = [1, 2, 3]
List<int> b = [1, 2, 3]
LinkedList<int> c = [1, 2, 3]
```

#### 11.2.3 Other R-Value Contexts

In pure r-value context, the result type is determined by context, overload resolution, or implementation-defined iterable lowering policy.

### 11.3 Automatic Range Expansion

Inside a collection expression, range elements are automatically expanded.

```txt
[0..<5]
[1, 2, 0..<5]
```

Automatic expansion applies only to range expressions.

### 11.4 Spread Elements

An explicit spread element expands an iterable into the surrounding collection expression.

```txt
let a = [3, 4, 5]
let b = [1, 2, ..a, 6, 7]
```

Rules:

1. Spread syntax is valid only inside collection expressions.
2. The spread operand must be iterable.
3. Spread preserves iteration order.

### 11.5 Builder Form

A collection expression may contain a builder form.

One-line form:

```txt
[0..<n -> i do i * i]
[arr -> x do f(x)]
```

Block-bodied form:

```txt
[0..<n -> i {
    let y = i * i
    y
}]

[arr -> x {
    let y = f(x)
    y
}]
```

A builder yields one element per iteration.

Rules:

1. `-> name do expr` yields `expr` once per iteration.
2. `-> name { ... }` yields the final block value once per iteration.
3. Indexed builder form is permitted wherever fast iteration supports an index binding.
4. Builder forms are valid only inside collection expressions.

---

## 12. Range Expressions

### 12.1 Syntax

The following range forms are supported:

```txt
1..10
0..<n
0..=n
10..-1..0
```

### 12.2 Semantics

- `a..b` : inclusive range from `a` to `b`
- `a..<b` : range from `a` to `b`, excluding `b`
- `a..=b` : explicit inclusive form
- `a..step..b` : stepped range including reachable endpoint

### 12.3 Iterability

Range expressions are iterable values. They may appear in:

- `for` loops,
- fast iteration `->`,
- collection expressions,
- aggregation expressions,
- function calls.

---

## 13. Member Access, Indexing, and Tuple Projection

### 13.1 Member Access

```txt
obj.member
obj.method
```

### 13.2 Indexing

C-style indexing is used.

```txt
arr[i]
grid[r][c]
text[i]
```

### 13.3 Tuple Projection

Tuple projection is written with a dot followed by a positive integer literal.

```txt
p.1
p.2
p.3
```

Rules:

1. Tuple projection is 1-based.
2. The right-hand side of the dot must be an integer literal.
3. Dynamic tuple indexing is not supported.

---

## 14. Operators

### 14.1 Unary Operators

- `+x`
- `-x`
- `!x`
- `not x`

### 14.2 Arithmetic Operators

- `*`
- `/`
- `%`
- `+`
- `-`

### 14.3 Comparison Operators

- `<`
- `<=`
- `>`
- `>=`
- `==`
- `!=`
- `<=>`

The spaceship operator `<=>` returns one of `-1`, `0`, or `1` according to the ordering relation of its operands.

### 14.4 Logical Operators

Both symbolic and keyword forms are supported.

- conjunction: `&&`, `and`
- disjunction: `||`, `or`
- exclusive or: `^`, `xor`

The keyword form is an exact alias of the symbolic form.

### 14.5 Assignment Operators

- `=`
- `+=`
- `-=`
- `*=`
- `/=`
- `%=`

### 14.6 Pipe Operators

- `|>`
- `<|`

### 14.7 Comparison Provider Sugar

The language defines comparator sugar for ascending and descending default comparers.

```txt
int.asc
int.desc
long.asc
long.desc
string.asc
string.desc
```

The general form is:

```txt
T.asc
T.desc
```

where `T` is a type expression for which an ordinary default ordering is available.

These forms denote comparator-producing expressions suitable for contexts such as custom sorting and priority-queue construction.

Examples:

```txt
arr.sortWith(int.asc)
arr.sortWith(int.desc)
PriorityQueue<int, int> pq = new(int.desc)
```

The meaning of `asc` is the ordinary increasing comparer for `T`.
The meaning of `desc` is the reversed comparer for `T`.

---

## 15. Operator Precedence and Associativity

From highest precedence to lowest precedence:

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
   - `<`, `<=`, `>`, `>=`, `<=>`, `is`, `is not`

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

Range expressions bind more weakly than additive expressions.

---

## 16. Calls and Arguments

### 16.1 Parenthesized Calls

Standard call syntax is supported.

```txt
f(x)
g(x, y)
obj.method(a, b)
```

### 16.2 Space-Separated Calls

Calls may omit parentheses and commas at the call site.

```txt
add 1 2
f x
g (x + 1) y
obj.method x y
```

Rules:

1. Space-separated call is surface syntax for ordinary multi-argument invocation.
2. Currying and partial application are not implied by v0.3.
3. Complex arguments require parentheses.
4. Parsing shall prefer maximal valid argument grouping under the precedence rules.

### 16.3 Argument Modifiers

The modifiers `ref`, `out`, and `in` are supported in both parenthesized and space-separated calls.

#### 16.3.1 Parenthesized Calls

```txt
foo(ref x, out y, in z)
foo(out int a, out int b)
foo(out _, ref arr[i])
```

#### 16.3.2 Space-Separated Calls

```txt
foo ref x out y in z
foo out int a out int b
foo out _ ref arr[i]
```

Rules:

1. A modifier binds to the next argument only.
2. `ref` and `in` require an assignable expression or any target form accepted by the implementation as a valid backend argument.
3. `out` permits:
   - an assignable target,
   - an explicitly typed out-variable declaration,
   - a discard `_`.
4. Modifier meaning is identical to C#.

### 16.4 Lambda Parameters with Modifiers

Lambdas may use `ref`, `out`, and `in` in parameter lists.

```txt
(ref int x, out int y) => { ... }
(in int x, int y) => x + y
```

---

## 17. Functions and Lambdas

### 17.1 Function Declaration

Functions use explicit parameter lists with comma separation.

```txt
int add(int a, int b) {
    a + b
}
```

### 17.2 Recursive Functions

Recursion is disabled by default. A function may recursively reference itself only if declared with `rec`.

```txt
rec int fact(int n) {
    if n <= 1 then 1 else n * fact(n - 1)
}
```

Self-reference in a non-`rec` function is a compile-time error.

### 17.3 Lambda Expressions

Lambdas use `=>`.

```txt
x => x + 1
(acc, x) => acc + x
(acc, x) => {
    let y = acc + x
    y % mod
}
```

A lambda parameter may be a discard.

```txt
_ => 1
(a, _, c) => a + c
```

---

## 18. Statements

The language defines the following statement categories:

- block statements,
- declarations,
- assignments,
- expression statements,
- output statements,
- `if`, `while`, `for`,
- fast iteration `->`,
- `return`, `break`, `continue`,
- pass-through declaration statements for namespace, using, and type declarations.

### 18.1 Assignment

```txt
x = y + 1
arr[i] = 0
x += 1
```

Assignment requires a mutable target unless otherwise specified by declaration syntax.

### 18.2 Tuple Assignment

Tuple assignment is supported.

```txt
(a, b) = (b, a)
(arr[i], arr[j]) = (arr[j], arr[i])
```

### 18.3 Expression Statements

Any expression may appear as a statement. Its value is ignored unless it serves as the final implicit value of a block under Section 21.

---

## 19. Control Flow

### 19.1 Block If Statement

```txt
if cond {
    ...
} else {
    ...
}
```

### 19.2 One-Line If Expression

```txt
if cond then a else b
```

Rules:

1. `then` and `else` in one-line form accept exactly one expression each.
2. Multiline one-line form is not permitted.
3. Multiline branches require braces.
4. `else` binds to the nearest unmatched `if`.

### 19.3 While Loop

```txt
while cond {
    ...
}
```

### 19.4 One-Line While

```txt
while cond do x += 1
```

### 19.5 For-In Loop

```txt
for i in 0..<n {
    ...
}
```

### 19.6 One-Line For-In Loop

```txt
for i in 0..<n do sum += a[i]
```

### 19.7 Break and Continue

```txt
break
continue
```

These statements are valid only inside loops.

### 19.8 Return

```txt
return expr
return
```

`return` exits the innermost enclosing function or lambda body.

---

## 20. Fast Iteration Syntax

The language provides a fast iteration form using `->`.

### 20.1 Statement Form

```txt
xs -> x {
    ...
}

xs -> x do expr
```

### 20.2 Indexed Form

```txt
xs -> i, x {
    ...
}

xs -> i, x do expr
```

### 20.3 Semantics

`->` iterates over an iterable source.

- the single-variable form binds each element,
- the indexed form binds zero-based index and element.

`->` is reserved for iteration syntax and is not used for lambda or function type notation.

---

## 21. Block Value and Return Semantics

### 21.1 Explicit Return

A `return` statement may appear inside a function, lambda block, or block expression.

### 21.2 Implicit Return

A block may produce a value without `return`.

A block performs an implicit return if and only if its final statement is an expression statement satisfying all of the following:

1. it is the last statement in the block,
2. it is not terminated by a semicolon,
3. it is not a bare invocation expression.

### 21.3 Bare Invocation

A bare invocation is an expression statement whose top-level form is a function or method call.

Examples of bare invocation:

```txt
foo x
foo(x)
obj.method x
obj.method(x)
```

A bare invocation in final position does not implicitly return its value.

### 21.4 Non-Bare Final Expressions

The following final expressions implicitly return:

```txt
a <=> b
not hashset.Add 10
foo(x) == true
if ok then 1 else 2
x + y
```

### 21.5 Value Discarding

If a value is intentionally ignored, discard assignment shall be used when necessary.

```txt
_ = hashset.Add 10
```

---

## 22. One-Line `then` and `do`

### 22.1 `then`

`then` is valid only in the one-line `if` form.

### 22.2 `do`

`do` is valid in the following positions:

- `for ... do ...`
- `while ... do ...`
- `xs -> x do ...`
- collection builder bodies.

### 22.3 Restrictions

In all one-line constructs:

1. only one expression or simple statement body is permitted,
2. the body ends at newline, semicolon, closing brace, or end of file,
3. multiline bodies require braces.

---

## 23. Intrinsic Aggregate Calls

The language defines aggregate operations such as `min`, `max`, `sum`, `minBy`, `maxBy`, and `sumBy` as ordinary intrinsic function families rather than special aggregation syntax.

Examples:

```txt
min a b
max a b

min arr
max arr
sum arr

minBy points (p => p.x)
maxBy items (x => score(x))
sumBy arr (x => x * x)

min [0..<n -> i do a[i] - b[i]]
sum [0..<n -> i {
    let x = veryLongExpression(i)
    x
}]
```

Collection builders and collection expressions may be used as arguments to aggregate calls.

The exact API surface and overload family are defined by the API specification.

## 24. Input Syntax

The language defines declaration-based input shorthand for token-oriented reading.

### 24.1 Scalar Input

```txt
int n =
long x =
string s =
char c =
```

These forms read one value from standard input according to the declared type.

### 24.2 Multiple Scalar Input

```txt
int n, m =
long a, b, c =
```

Each declared name consumes one input value in order.

### 24.3 Array Input

```txt
int[n] arr =
long[m] cost =
```

The declaration consumes exactly the specified number of input values.

### 24.4 Nested Array Input

```txt
int[n][m] grid =
char[h][w] board =
```

The declaration consumes values according to its dimensions.

### 24.5 Tuple Input

```txt
(int, int) p =
(int, int)[m] edges =
(long, long, long)[k] qs =
```

A tuple declaration consumes the required number of input values for each tuple element.

### 24.6 Explicit Input API Coexistence

Declaration shorthand coexists with explicit input APIs such as `stdin.*`. Both remain valid.

---

## 25. Output Syntax

### 25.1 Write Statement

```txt
= expr
```

This writes the rendered value without a trailing newline.

### 25.2 WriteLine Statement

```txt
+= expr
```

This writes the rendered value followed by a newline.

### 25.3 Disambiguation

At statement start:

- `= expr` is output syntax,
- `+= expr` is line output syntax.

Elsewhere:

- `x += 1` is assignment.

---

## 26. User-Defined Type Declarations

The language supports nominal type declarations using near-pass-through C# syntax.

### 26.1 Class Declaration

```txt
class C {
    ...
}
```

### 26.2 Struct Declaration

```txt
struct S {
    ...
}
```

### 26.3 Record Declaration

Both block and primary-constructor-like surface forms are supported in accordance with the implementation's supported C# subset.

Examples:

```txt
record R(int X, int Y)
record struct P(int X, int Y)
```

### 26.4 Base Type and Interface List

A type declaration may specify a base class and/or implemented interfaces using ordinary C#-style syntax.

```txt
class A : B, IFoo, IBar {
    ...
}

struct S : IFoo {
    ...
}

record struct Job(int Id, int Arrival, long Time) : IComparable<Job> {
    ...
}
```

### 26.5 Members

The following member categories are supported:

- fields,
- constructors,
- methods,
- nested type declarations,
- pass-through member forms supported by the implementation's C# subset.

### 26.6 Constructors

Constructors use ordinary C#-style syntax.

```txt
class Dsu {
    int[] parent

    Dsu(int n) {
        parent = [0..<n]
    }
}
```

### 26.7 Access Modifiers and Modifiers

C#-style access modifiers and member modifiers may be accepted as pass-through syntax according to implementation support.

---

## 27. Comparator Sugar

### 27.1 Purpose

Comparator sugar provides concise ascending and descending comparers for ordered types.

### 27.2 Forms

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

### 27.3 Intended Use

These expressions are intended for APIs that consume comparers, ordering functions, or sorting strategies.

Examples:

```txt
arr.sortWith(int.asc)
arr.sortWith(int.desc)
PriorityQueue<int, int> pq = new(int.desc)
```

### 27.4 Meaning

`T.asc` denotes the default ascending comparer for `T`.

`T.desc` denotes the reversed default ascending comparer for `T`.

If no default ordering exists for `T`, use of `T.asc` or `T.desc` is a compile-time error.

---

## 28. Static Errors

The following are compile-time errors:

1. assigning to an immutable binding,
2. using `_` as a value,
3. self-recursive reference in a non-`rec` function,
4. malformed one-line `then` or `do` body spanning multiple statements,
5. tuple projection on a non-tuple value,
6. tuple projection with non-literal index,
7. unsupported automatic input or output shape,
8. use of `break` or `continue` outside a loop,
9. invalid spread outside collection expression,
10. uninitialized immutable binding,
11. invalid use of `ref`, `out`, or `in`,
12. use of comparator sugar on a type without a default ordering,
13. invalid auto-construction form for a type not covered by Section 8.4.3.

---

## 29. Parsing Rules of Special Interest

### 29.1 Output Statements

`= expr` and `+= expr` are parsed as output statements only when they appear at statement start.

### 29.2 Input Declarations

A declaration of the form `T x =` with no right-hand expression is parsed as input shorthand, not as incomplete assignment.

### 29.3 Spread vs Range

Inside a collection expression:

- `..expr` following a comma or collection start denotes spread,
- `a..b`, `a..<b`, `a..=b`, `a..step..b` denote range expressions.

### 29.4 Space-Separated Call

The parser shall accept space-separated invocation only when argument boundaries are unambiguous under the precedence rules. Parentheses are required to disambiguate complex argument expressions.

### 29.5 Argument Modifiers

`ref`, `out`, and `in` in argument position bind to the immediately following argument only.

### 29.6 Final Expression of a Block

Only the final statement of a block may participate in implicit return.

---

## 30. Examples

### 30.1 Union-Find Initialization

```txt
int n =
int[] parent = [0..<n]
int[n] rank;
```

### 30.2 Fast Iteration

```txt
1..10 -> x {
    += x
}
```

### 30.3 Comparator Sugar

```txt
let sorted = arr.sortWith(int.asc)
let rev = arr.sortWith(int.desc)
PriorityQueue<int, int> pq = new(int.desc)
```

### 30.4 Intrinsic Aggregate Calls

```txt
let a = min x y
let b = max arr
let c = sum [0..<n -> i do a[i]]
let d = minBy points (p => p.2)
```

### 30.5 Auto-Constructed Known Collection

```txt
List<int> list;
Queue<int> queue;
HashSet<int> seen;
```

### 30.6 Modifiers in Calls

```txt
foo(ref x, out y, in z)
foo ref x out y in z
foo(out int a, out int b)
foo out int a out int b
```

### 30.7 Pass-Through Type Declaration

```txt
record struct Job(int Id, int Arrival, long Time) : IComparable<Job> {
    public readonly int CompareTo(Job other) {
        if (Arrival == other.Arrival) return Time <=> other.Time
        return Arrival <=> other.Arrival
    }
}
```

---

## 31. Versioning and Compatibility

This document defines syntax and source semantics for draft version `v0.3`.

Backward compatibility is not guaranteed between draft revisions.

The implementation may reject programs that depend on syntax or behavior not explicitly defined by this document.

---

## 32. Conformance

An implementation conforms to this specification if it:

1. accepts all syntactically valid programs defined herein,
2. rejects all programs required to be compile-time errors herein,
3. preserves the source-level semantics of declarations, expressions, control flow, collections, ranges, input and output shorthand, comparator sugar, call modifiers, and user-defined type declarations as defined herein,
4. preserves pass-through compatibility for ordinary C#/.NET surface areas identified in this document.

Backend strategy, optimization, helper runtime design, and exact lowering are implementation-defined.

