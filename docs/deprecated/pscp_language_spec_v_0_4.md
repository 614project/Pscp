# PSCP Language Specification v0.4

## 1. Scope

This document defines version `v0.4` of the PSCP language syntax and source-level semantics.

PSCP is a programming language specialized for problem solving and competitive programming. It is transpiled to C# and intentionally preserves broad compatibility with ordinary C# and .NET surface syntax where that surface is already practical.

This document defines:

1. lexical structure,
2. declarations and bindings,
3. expressions and statements,
4. control flow,
5. ranges, generators, and collections,
6. intrinsic shorthand syntax,
7. user-defined type declarations,
8. operator and conversion syntax,
9. pass-through C# surface areas.

This document does not define the complete intrinsic API surface and does not define backend lowering strategy in full. Those are specified separately.

This document is normative unless otherwise stated.

---

## 2. Design Principles

1. **Braces over indentation**. Indentation is never semantically significant.
2. **Default immutability**. Bindings are immutable unless explicitly mutable.
3. **Expression-oriented where useful, imperative where necessary**.
4. **C#/.NET interop preservation**. Existing C# and .NET syntax remains available when practical.
5. **Contest-specific sugar is explicit**. Only high-value shorthand is introduced.
6. **Parsing rules must be strict enough for unambiguous transpilation**.
7. **Syntax should expose allocation vs laziness when practical**.

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
public private protected internal
```

### 4.2 Reserved Operators and Special Tokens

```txt
= += -= *= /= %=
:=
== != < <= > >= <=>
&& || ^ ! ~
++ --
.. ..< ..=
|> <|
->
..
_
?
^
```

The token `..` is context-sensitive and may denote:

1. a range operator,
2. a spread element inside a collection expression.

The token `^` is used for index-from-end and slice syntax.

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

### 5.3 Interpolated Strings

PSCP supports C#-style interpolated string syntax.

```txt
$"answer = {ans}"
$"{min a b} {sum (0..<n -> i do a[i])}"
```

Rules:

1. interpolation holes contain PSCP expressions,
2. the implementation may support format specifiers in a C#-compatible style,
3. interpolated strings are string expressions.

---

## 6. Pass-Through C# Surface

The following syntax categories are part of the language and are treated as pass-through or near-pass-through syntax.

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

### 6.3 Type Tests and Related Surface

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

### 6.6 Pass-Through Access Modifiers

The following tokens without a trailing colon are ordinary pass-through C# modifiers:

```txt
public
private
protected
internal
```

They are not reinterpreted by PSCP.

Important consequences:

1. the transpiler does not rewrite or reinterpret them as PSCP-specific section syntax,
2. if they are used incorrectly, the generated C# may fail to compile,
3. PSCP does not guarantee source-level repair of invalid pass-through modifier usage.

This is distinct from colon-suffixed section labels such as `public:` defined later in this specification.

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

#### 8.4.4 Forbidden Forms

The following are compile-time errors unless covered by Section 8.4.2 or 8.4.3:

```txt
int x;
string s;
let a;
var b;
MyType obj;
```

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

## 10. Top-Level Program Structure

A source file may contain:

- top-level statements,
- function declarations,
- type declarations,
- namespace and using declarations where supported.

Top-level statements execute in source order.

The transpiler may generate a synthetic entry-point wrapper in C#.

---

## 11. Expressions

### 11.1 Expression Categories

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
- generator expressions,
- function and method calls,
- indexing and slicing,
- member access,
- tuple projection,
- lambdas,
- pass-through expressions supported by the C# surface.

### 11.2 Parenthesized Expressions

```txt
(x)
(a + b)
(if ok then 1 else 2)
```

### 11.3 Tuple Expressions

Tuple expressions are comma-separated expressions enclosed in parentheses.

```txt
(1, 2)
(x, y, z)
```

### 11.4 Block Expressions

A brace-delimited block may be used as an expression.

```txt
{
    let x = 1
    x + 1
}
```

Block value behavior is governed by Section 24.

### 11.5 Assignment Expressions with `:=`

PSCP supports an explicit assignment-expression operator.

```txt
lhs := rhs
```

Semantics:

1. assign `rhs` to `lhs`,
2. evaluate to the assigned value.

Examples:

```txt
a = b := c
parent[x] := find(parent[x])
```

Rules:

1. `:=` is an explicit value-yielding assignment operator,
2. it is distinct from ordinary assignment `=`,
3. it is right-associative,
4. it applies only to simple assignment, not to compound assignment,
5. it is intended to remove ambiguity when an assignment expression is deliberate.

`:=` is not return syntax.

---

## 12. Collections and Generators

### 12.1 Materialized Collection Expressions `[]`

A collection expression enclosed in square brackets is the materialized collection form.

```txt
[]
[1, 2, 3]
[1, x + 1, y]
```

### 12.2 Default Result Type of `[]`

#### 12.2.1 Declaration Context with `let`

In declaration context with `let`, a collection expression defaults to an array type if element types can be inferred.

```txt
let a = [1, 2, 3]
```

#### 12.2.2 Explicit Type Context

If a declaration target type is explicit, the collection expression is converted to that type.

```txt
int[] a = [1, 2, 3]
List<int> b = [1, 2, 3]
LinkedList<int> c = [1, 2, 3]
```

### 12.3 Range Expansion in `[]`

Inside a materialized collection expression, range elements are automatically expanded.

```txt
[0..<5]
[1, 2, 0..<5]
```

### 12.4 Spread Elements

An explicit spread element expands an iterable into the surrounding collection expression.

```txt
let a = [3, 4, 5]
let b = [1, 2, ..a, 6, 7]
```

Rules:

1. spread syntax is valid only inside materialized collection expressions,
2. the spread operand must be iterable,
3. spread preserves iteration order.

### 12.5 Materialized Builder Forms

A collection expression may contain builder forms.

One-line builder:

```txt
[0..<n -> i do i * i]
[arr -> x do f(x)]
```

Block-bodied builder:

```txt
[0..<n -> i {
    let x = i * i
    x + 1
}]
```

A materialized builder yields one element per iteration into a concrete collection.

### 12.6 Generator Expressions `(...)`

A parenthesized expression is parsed as a generator expression if and only if it contains a directly parenthesized iterator form using `for` or `->`.

Examples:

```txt
(0..<n -> i do i * i)
(0..<n -> i {
    let x = i * i
    x + 1
})
(for i in 0..<n do a[i])
```

A generator expression denotes a lazy iterable/generator form rather than a materialized collection.

Rules:

1. ordinary parenthesized expressions remain ordinary expressions,
2. tuple syntax remains tuple syntax,
3. generator parsing is selected only when `for` or `->` is recognized as the direct iterator form inside the parentheses,
4. a generator expression is distinct from a materialized collection expression.

### 12.7 Intended Usage

Typical usage:

```txt
sum (0..<n -> i do score(i))
min [0..<n -> i do a[i] - i]
```

The parenthesized form emphasizes lazy/iterable behavior.
The bracketed form emphasizes concrete collection construction.

---

## 13. Range Expressions

### 13.1 Syntax

The following range forms are supported:

```txt
1..10
0..<n
0..=n
10..-1..0
```

### 13.2 Semantics

- `a..b` : inclusive range from `a` to `b`
- `a..<b` : range from `a` to `b`, excluding `b`
- `a..=b` : explicit inclusive form
- `a..step..b` : stepped range including reachable endpoint

### 13.3 Iterability

Range expressions are iterable values. They may appear in:

- `for` loops,
- fast iteration `->`,
- generator expressions,
- collection expressions,
- aggregate intrinsic calls,
- ordinary function calls.

---

## 14. Member Access, Indexing, Slicing, and Tuple Projection

### 14.1 Member Access

```txt
obj.member
obj.method
```

### 14.2 Indexing

C-style indexing is used.

```txt
arr[i]
grid[r][c]
text[i]
```

### 14.3 Index-From-End

PSCP supports C#-style index-from-end syntax.

```txt
text[^1]
arr[^2]
```

### 14.4 Slicing

PSCP supports C#-style slicing syntax with `..` and `^` inside index brackets.

Examples:

```txt
text[1..^2]
text[..^1]
arr[1..]
arr[..n]
arr[..]
```

Initial intended scope is at least:

- `string`,
- one-dimensional arrays.

### 14.5 Tuple Projection

Tuple projection is written with a dot followed by a positive integer literal.

```txt
p.1
p.2
p.3
```

Rules:

1. tuple projection is 1-based,
2. the right-hand side of the dot must be an integer literal,
3. dynamic tuple indexing is not supported.

---

## 15. Conversions in Expression Position

Built-in type keywords may act as conversion/parsing functions in expression position.

Examples:

```txt
let a = int "123"
let b = long "1000"
let c = int(3.14)
let d = int(true)
let e = bool "hello"
```

### 15.1 Intended Scope

The language intends broad conversion behavior in the style of practical competitive-programming convenience.

### 15.2 General Direction

Typical categories include:

1. string parsing,
2. numeric-to-numeric conversion,
3. bool-to-numeric conversion,
4. numeric-to-bool conversion,
5. string-to-bool conversion,
6. cast-like conversion between simple scalar types where practical.

### 15.3 Examples of Intended Semantics

Examples of intended behavior include:

- `int "123"` -> parse integer,
- `bool ""` -> `false`,
- `bool "abc"` -> `true`,
- `int true` -> `1`,
- `int false` -> `0`,
- `bool 0` -> `false`,
- `bool 5` -> `true`,
- `int 3.14` -> numeric cast/convert according to backend scalar-conversion policy.

The precise conversion table belongs to the API specification, but the surface syntax is part of the language.

---

## 16. Operators

### 16.1 Unary Operators

- `+x`
- `-x`
- `!x`
- `not x`
- `~x`
- `++x`
- `--x`

### 16.2 Postfix Operators

- `x++`
- `x--`

### 16.3 Arithmetic Operators

- `*`
- `/`
- `%`
- `+`
- `-`

### 16.4 Comparison Operators

- `<`
- `<=`
- `>`
- `>=`
- `==`
- `!=`
- `<=>`

The spaceship operator `<=>` returns one of `-1`, `0`, or `1` according to the ordering relation of its operands.

### 16.5 Logical Operators

Both symbolic and keyword forms are supported.

- conjunction: `&&`, `and`
- disjunction: `||`, `or`
- exclusive or: `^`, `xor`

The keyword form is an exact alias of the symbolic form.

### 16.6 Assignment Operators

- `=`
- `+=`
- `-=`
- `*=`
- `/=`
- `%=`
- `:=`

### 16.7 Pipe Operators

- `|>`
- `<|`

### 16.8 Comparison Provider Sugar

PSCP defines comparator sugar for ascending and descending default comparers.

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

### 16.9 Increment and Decrement

`++` and `--` are supported in both prefix and postfix forms for mutable assignable targets.

Examples:

```txt
++i
i++
--q
q--
```

This operator family is independent from known data structure rewrites described later.

---

## 17. Operator Precedence and Associativity

From highest precedence to lowest precedence:

1. postfix
   - parenthesized call
   - indexing / slicing
   - member access
   - tuple projection
   - postfix `++`, `--`

2. prefix
   - unary plus/minus
   - `!`
   - `not`
   - `~`
   - prefix `++`, `--`

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

12. ordinary assignment
   - `=`, `+=`, `-=`, `*=`, `/=`, `%=`

13. explicit assignment expression
   - `:=`

`:=` is right-associative.

Range expressions bind more weakly than additive expressions.

---

## 18. Calls and Arguments

### 18.1 Parenthesized Calls

Standard call syntax is supported.

```txt
f(x)
g(x, y)
obj.method(a, b)
```

### 18.2 Space-Separated Calls

Calls may omit parentheses and commas at the call site.

```txt
add 1 2
f x
g (x + 1) y
obj.method x y
```

Rules:

1. space-separated call is surface syntax for ordinary multi-argument invocation,
2. currying and partial application are not implied,
3. complex arguments require parentheses,
4. parsing shall prefer maximal valid argument grouping under the precedence rules.

### 18.3 Argument Modifiers

The modifiers `ref`, `out`, and `in` are supported in both parenthesized and space-separated calls.

#### 18.3.1 Parenthesized Calls

```txt
foo(ref x, out y, in z)
foo(out int a, out int b)
foo(out _, ref arr[i])
```

#### 18.3.2 Space-Separated Calls

```txt
foo ref x out y in z
foo out int a out int b
foo out _ ref arr[i]
```

Rules:

1. a modifier binds to the next argument only,
2. `ref` and `in` require an assignable expression or a backend-valid equivalent target,
3. `out` permits:
   - an assignable target,
   - an explicitly typed out-variable declaration,
   - a discard `_`,
4. modifier meaning is identical to C#.

### 18.4 Lambda Parameters with Modifiers

Lambdas may use `ref`, `out`, and `in` in parameter lists.

```txt
(ref int x, out int y) => { ... }
(in int x, int y) => x + y
```

---

## 19. Functions and Lambdas

### 19.1 Function Declaration

Functions use explicit parameter lists with comma separation.

```txt
int add(int a, int b) {
    a + b
}
```

### 19.2 Recursive Functions

Recursion is disabled by default. A function may recursively reference itself only if declared with `rec`.

```txt
rec int fact(int n) {
    if n <= 1 then 1 else n * fact(n - 1)
}
```

Self-reference in a non-`rec` function is a compile-time error.

### 19.3 Lambda Expressions

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

## 20. Statements

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

### 20.1 Assignment

```txt
x = y + 1
arr[i] = 0
x += 1
```

Assignment requires a mutable target unless otherwise specified.

### 20.2 Tuple Assignment

Tuple assignment is supported.

```txt
(a, b) = (b, a)
(arr[i], arr[j]) = (arr[j], arr[i])
```

### 20.3 Expression Statements

Any expression may appear as a statement. Its value is ignored unless it serves as the final implicit value of a block under Section 24.

---

## 21. Control Flow

### 21.1 Block If Statement

```txt
if cond {
    ...
} else {
    ...
}
```

### 21.2 Single-Statement `then` / `else`

`then` and `else` bind the next single statement.

Examples:

```txt
if x < 0 then x = -x
if bad then continue
if done then break
if ok then return ans else return -1
```

Rules:

1. `then` is valid only after an `if` condition,
2. `else` binds to the nearest unmatched `if`,
3. `then` and `else` each bind exactly one following statement,
4. that following statement may itself be a block,
5. for multiple statements, braces are required.

### 21.3 While Loop

```txt
while cond {
    ...
}
```

### 21.4 One-Statement While

```txt
while cond do x += 1
```

### 21.5 For-In Loop

```txt
for i in 0..<n {
    ...
}
```

### 21.6 One-Statement For-In Loop

```txt
for i in 0..<n do sum += a[i]
```

### 21.7 Break and Continue

```txt
break
continue
```

These statements are valid only inside loops.

### 21.8 Return

```txt
return expr
return
```

`return` exits the innermost enclosing function or lambda body.

---

## 22. Fast Iteration Syntax

The language provides a fast iteration form using `->`.

### 22.1 Statement Form

```txt
xs -> x {
    ...
}

xs -> x do expr
```

### 22.2 Indexed Form

```txt
xs -> i, x {
    ...
}

xs -> i, x do expr
```

### 22.3 Semantics

`->` iterates over an iterable source.

- the single-variable form binds each element,
- the indexed form binds zero-based index and element.

`->` is reserved for iteration and builder syntax and is not used for lambda or function type notation.

---

## 23. Intrinsic Aggregate Calls

PSCP treats common aggregates as ordinary intrinsic call families.

Examples:

```txt
min a b
max a b c
sum arr
sumBy arr (x => x * x)
minBy points (p => p.x)
maxBy items (x => score(x))
chmin ref best cand
chmax ref ans value
```

These are ordinary call forms, not special block syntax.

They may consume:

- fixed values,
- ordinary iterables,
- generator expressions,
- materialized collection expressions.

Examples:

```txt
let best = min [0..<n -> i do a[i] - i]
let total = sum (0..<n -> i {
    let x = i * i
    x + 1
})
```

---

## 24. Block Value and Return Semantics

### 24.1 Explicit Return

A `return` statement may appear inside a function, lambda block, or block expression.

### 24.2 Implicit Return

A block may produce a value without `return`.

A block performs an implicit return if and only if its final statement is an expression statement satisfying all of the following:

1. it is the last statement in the block,
2. it is not terminated by a semicolon,
3. it is not a bare invocation expression.

### 24.3 Bare Invocation

A bare invocation is an expression statement whose top-level form is a function or method call.

Examples of bare invocation:

```txt
foo x
foo(x)
obj.method x
obj.method(x)
```

A bare invocation in final position does not implicitly return its value.

### 24.4 Non-Bare Final Expressions

The following final expressions implicitly return:

```txt
a <=> b
not hashset.Add 10
foo(x) == true
if ok then 1 else 2
x + y
parent[x] := find(parent[x])
```

---

## 25. Input Syntax

The language defines declaration-based input shorthand for token-oriented reading.

### 25.1 Scalar Input

```txt
int n =
long x =
string s =
char c =
```

### 25.2 Multiple Scalar Input

```txt
int n, m =
long a, b, c =
```

### 25.3 Array Input

```txt
int[n] arr =
long[m] cost =
```

### 25.4 Nested Array Input

```txt
int[n][m] grid =
char[h][w] board =
```

### 25.5 Tuple Input

```txt
(int, int) p =
(int, int)[m] edges =
(long, long, long)[k] qs =
```

### 25.6 Explicit Input API Coexistence

Declaration shorthand coexists with explicit input APIs such as `stdin.*`.

---

## 26. Output Syntax

### 26.1 Write Statement

```txt
= expr
```

This writes the rendered value without a trailing newline.

### 26.2 WriteLine Statement

```txt
+= expr
```

This writes the rendered value followed by a newline.

### 26.3 Disambiguation

At statement start:

- `= expr` is output syntax,
- `+= expr` is line output syntax.

Elsewhere:

- `x += 1` is assignment or a compiler-known operator rewrite target depending on the receiver type and context.

---

## 27. Target-Typed Allocation Shorthand

### 27.1 `new[n]`

PSCP supports target-typed array allocation shorthand.

```txt
NodeInfo[] nodes = new[n]
int[] arr = new[n]
```

Meaning:

- allocate an array using the target type's element type.

### 27.2 `new![n]`

PSCP supports array allocation with element auto-construction for known collection element types.

```txt
List<int>[] graph = new![n]
Queue<int>[] buckets = new![m]
```

Meaning:

- allocate the array,
- initialize each element with implicit `new()` according to compiler-known collection rules.

This form is intended only for known auto-constructible collection element types.

---

## 28. Known Data Structure Operator Rewrites

The following are PSCP source-level operator rewrites for selected compiler-known data structures.

These are language-level forms, not runtime wrappers.

### 28.1 `HashSet<T>`

```txt
visited += x
visited -= x
```

Intended meaning:

- `visited += x` -> `visited.Add(x)`
- `visited -= x` -> `visited.Remove(x)`

These operations preserve the underlying method return type.

Example:

```txt
if not (visited += me) then continue
```

### 28.2 `Stack<T>`

```txt
s += x
~s
--s
```

Intended meaning:

- `s += x` -> `s.Push(x)`
- `~s` -> `s.Peek()`
- `--s` -> `s.Pop()`

Postfix `s--` is not part of this design.

### 28.3 `Queue<T>`

```txt
q += x
~q
--q
```

Intended meaning:

- `q += x` -> `q.Enqueue(x)`
- `~q` -> `q.Peek()`
- `--q` -> `q.Dequeue()`

### 28.4 `PriorityQueue<TElement, TPriority>`

```txt
pq += (item, priority)
~pq
--pq
```

Intended meaning:

- `pq += (item, priority)` -> enqueue,
- `~pq` -> item returned by peek,
- `--pq` -> item returned by dequeue.

If the priority is also needed, ordinary `TryPeek` / `TryDequeue` usage remains available.

---

## 29. User-Defined Type Declarations

The language supports nominal type declarations using near-pass-through C# syntax.

### 29.1 Class Declaration

```txt
class C {
    ...
}
```

### 29.2 Struct Declaration

```txt
struct S {
    ...
}
```

### 29.3 Record Declaration

Examples:

```txt
record R(int X, int Y)
record struct P(int X, int Y)
```

### 29.4 Base Type and Interface List

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

### 29.5 Members

The following member categories are supported:

- fields,
- constructors,
- methods,
- nested type declarations,
- pass-through member forms supported by the implementation's C# subset.

### 29.6 Default Method Accessibility

In PSCP source semantics, methods are implicitly `public` unless an explicit pass-through modifier is present or a section label supplies an intended access level.

This source-level rule is distinct from backend enforcement.

---

## 30. Access Section Labels with Colon

PSCP supports experimental access section labels with a trailing colon.

Supported section labels:

```txt
public:
private:
protected:
internal:
```

### 30.1 Important Distinction

These are distinct from pass-through C# modifiers without a colon.

- `public`  = pass-through C# modifier
- `public:` = PSCP section label

### 30.2 Scope of Section Labels

Section labels apply only to methods that:

1. appear below the section label,
2. do not already have an explicit access modifier.

They do not apply to fields or other member kinds.

### 30.3 Intended Source Semantics

The nearest preceding section label provides the intended method accessibility for PSCP source analysis.

### 30.4 Backend Policy

Section labels are currently lightweight and experimental.

The transpiler is permitted to:

- issue warnings,
- perform access-violation checks,
- but still emit methods as `public` in generated C#.

Section labels must not block transpilation.

---

## 31. Ordering Shorthand in Type Bodies

Inside a type body, PSCP supports a shorthand ordering declaration.

```txt
operator<=>(other) => this.value <=> other.value
```

Intended semantics:

1. `other` has the enclosing self type,
2. return type is implicitly `int`,
3. this defines the type's default ordering.

The long-form C#-style implementation surface remains available.

---

## 32. Static Errors

The following are compile-time errors:

1. assigning to an immutable binding,
2. using `_` as a value,
3. self-recursive reference in a non-`rec` function,
4. malformed one-statement `then` or `do` body spanning multiple statements,
5. tuple projection on a non-tuple value,
6. tuple projection with non-literal index,
7. unsupported automatic input or output shape,
8. use of `break` or `continue` outside a loop,
9. invalid spread outside materialized collection expressions,
10. uninitialized immutable binding,
11. invalid use of `ref`, `out`, or `in`,
12. invalid use of comparator sugar on a type without a default ordering,
13. invalid auto-construction form for a type not covered by known collection rules,
14. invalid use of known data structure operators on unsupported receiver types,
15. invalid `:=` left-hand side,
16. invalid generator-expression form.

---

## 33. Parsing Rules of Special Interest

### 33.1 Output Statements

`= expr` and `+= expr` are parsed as output statements only when they appear at statement start.

### 33.2 Input Declarations

A declaration of the form `T x =` with no right-hand expression is parsed as input shorthand, not as incomplete assignment.

### 33.3 Spread vs Range

Inside a materialized collection expression:

- `..expr` following a comma or collection start denotes spread,
- `a..b`, `a..<b`, `a..=b`, `a..step..b` denote range expressions.

### 33.4 Generator Detection in Parentheses

A parenthesized form is parsed as a generator expression only when the directly enclosed form contains a recognized iterator construct using `for` or `->`.

Otherwise, ordinary parenthesized-expression and tuple rules apply.

### 33.5 Space-Separated Call

The parser shall accept space-separated invocation only when argument boundaries are unambiguous under the precedence rules. Parentheses are required to disambiguate complex argument expressions.

### 33.6 Argument Modifiers

`ref`, `out`, and `in` in argument position bind to the immediately following argument only.

### 33.7 Access Labels vs Modifiers

A token sequence such as `public:` is an access section label.
A token sequence such as `public` followed by a declaration is a pass-through C# modifier.

### 33.8 Final Expression of a Block

Only the final statement of a block may participate in implicit return.

---

## 34. Examples

### 34.1 Aggregate over a Generator

```txt
let total = sum (0..<n -> i {
    let x = i * i
    x + 1
})
```

### 34.2 Materialized Builder

```txt
let squares = [0..<n -> i do i * i]
```

### 34.3 HashSet Add Returning `bool`

```txt
HashSet<int> visited;
if not (visited += me) then continue
```

### 34.4 Stack Operators

```txt
Stack<int> s;
s += 10
let top = ~s
let x = --s
```

### 34.5 Target-Typed Array Allocation

```txt
NodeInfo[] nodes = new[n]
List<int>[] graph = new![n]
```

### 34.6 Ordering Shorthand

```txt
record struct Job(int Id, int Arrival, long Time) {
    operator<=>(other) =>
        if Arrival == other.Arrival then Time <=> other.Time
        else Arrival <=> other.Arrival
}
```

### 34.7 Access Labels

```txt
class Node {
private:
    int value
public:
    Node(int value) {
        this.value = value
    }
}
```

### 34.8 Conversion Keywords

```txt
let a = int "123"
let b = int(true)
let c = bool "hello"
```

### 34.9 Slicing

```txt
let middle = text[1..^1]
let tail = arr[1..]
let last = arr[^1]
```

### 34.10 Assignment Expression

```txt
rec int find(int x) {
    if x == parent[x] then x
    else parent[x] := find(parent[x])
}
```

---

## 35. Versioning and Compatibility

This document defines syntax and source semantics for draft version `v0.4`.

Backward compatibility is not guaranteed between draft revisions.

The implementation may reject programs that depend on syntax or behavior not explicitly defined by this document.

---

## 36. Conformance

An implementation conforms to this specification if it:

1. accepts all syntactically valid programs defined herein,
2. rejects all programs required to be compile-time errors herein,
3. preserves the source-level semantics of declarations, expressions, control flow, generators, collections, ranges, input and output shorthand, assignment expressions, conversion syntax, known data structure operators, ordering shorthand, and type declarations as defined herein,
4. preserves pass-through compatibility for ordinary C#/.NET surface areas identified in this document.

Backend strategy, optimization, helper runtime design, and exact lowering are implementation-defined.

