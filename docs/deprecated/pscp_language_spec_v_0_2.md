# PS/CP Language Specification v0.2

## 1. Scope

This document defines the syntax, static rules, and execution model of a programming language specialized for problem solving and competitive programming. The language is expression-oriented, block-delimited by braces, compiled by transpilation to C#, and optimized for concise algorithmic code.

This document is normative unless otherwise stated.

---

## 2. Design Goals

1. Concise expression of algorithmic intent.
2. Brace-based syntax with no semantic dependence on indentation.
3. Default immutability with explicit local mutability.
4. Practical support for imperative control flow including `break`, `continue`, and `return`.
5. First-class collection expressions, ranges, aggregation, and lightweight iteration syntax.
6. Built-in input and output shorthand suitable for contest environments.
7. Readable lowering to C# with optional helper runtime support.

---

## 3. Source Form

### 3.1 Character Set

Source files are Unicode text.

### 3.2 Whitespace

Whitespace separates tokens where necessary. Newlines are not generally semantically significant except where explicitly stated for one-line constructs.

### 3.3 Comments

The following comment forms are reserved:

```txt
// single-line comment
/* multi-line comment */
```

Comment syntax is lexical and comments do not participate in parsing.

### 3.4 Statement Separation

Statements may be separated by newlines, braces, or semicolons.

Semicolons are optional. A semicolon may be used to separate multiple statements on a single line.

```txt
var x = 0; x += 1; += x
```

A trailing semicolon is permitted.

---

## 4. Keywords and Reserved Symbols

### 4.1 Reserved Keywords

```txt
let var mut rec
if then else
for in do while
break continue return
match when
true false null
and or xor not
```

### 4.2 Special Tokens

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

The token `..` is context-sensitive and may denote a range operator or a spread element inside a collection expression.

---

## 5. Lexical Categories

### 5.1 Identifiers

Identifiers begin with a Unicode letter or underscore and may continue with letters, digits, or underscores.

The single underscore `_` is a reserved discard token and is not a normal identifier.

### 5.2 Integer Literals

Integer literals are decimal by default.

Examples:

```txt
0
1
42
1000000
1L
```

A trailing `L` denotes `long`.

### 5.3 Floating-Point Literals

Floating-point literals follow C#-style lexical rules.

Examples:

```txt
3.14
0.5
1e9
```

### 5.4 Character Literals

Character literals use single quotes.

```txt
'a'
'\n'
```

### 5.5 String Literals

String literals use double quotes.

```txt
"hello"
"a b c"
```

---

## 6. Types

### 6.1 Built-in Scalar Types

The following scalar types are built in:

- `int`
- `long`
- `double`
- `decimal`
- `bool`
- `char`
- `string`

### 6.2 Tuple Types

Tuple types are written as comma-separated type lists enclosed in parentheses.

```txt
(int, int)
(long, int, string)
```

Tuple arity is part of the type.

### 6.3 Array Types

One-dimensional and nested array types are supported.

```txt
int[]
int[][]
(string, int)[]
```

### 6.4 Generic Collection Types

The following generic collection forms are recognized by the specification:

```txt
List<int>
LinkedList<int>
```

Support for additional generic types is implementation-defined unless explicitly specified.

### 6.5 Sized Declaration Types

Sized declarations are permitted for arrays in declaration position.

```txt
int[n] arr
int[n][m] grid
```

These forms are declaration syntax, not ordinary type expressions.

---

## 7. Bindings, Mutability, and Initialization

### 7.1 Default Immutability

All bindings are immutable unless declared with `mut` or `var`.

### 7.2 Declaration Forms

#### 7.2.1 Immutable Explicitly Typed Declaration

```txt
int x = 3
string s = "abc"
```

#### 7.2.2 Immutable Inferred Declaration

```txt
let x = 3
let s = "abc"
```

#### 7.2.3 Mutable Explicitly Typed Declaration

```txt
mut int x = 0
mut string s = ""
```

#### 7.2.4 Mutable Inferred Declaration

```txt
var x = 0
var s = ""
```

### 7.3 Multiple Declarations

Multiple declarations are permitted when the number of declared names matches the number of provided values.

```txt
int a, b = 1, 2
let x, y = f()
var i, j = 0, 0
```

The right-hand side may be tuple-valued or syntactically comma-separated.

### 7.4 Uninitialized Declarations

#### 7.4.1 Mutable Scalars and References

Uninitialized declaration is permitted only for mutable explicitly typed bindings.

```txt
mut int x;
mut string s;
mut int[] arr;
```

The binding is initialized with the default value of its type.

#### 7.4.2 Sized Arrays

Sized arrays may omit an initializer.

```txt
int[n] a;
int[n][m] dp;
```

Such declarations allocate arrays and initialize all elements with the default value of the element type.

#### 7.4.3 Forbidden Forms

The following are compile-time errors:

```txt
int x;
string s;
let a;
var b;
```

### 7.5 Discards

The token `_` denotes a discard.

Allowed positions:

- left-hand side of assignment
- declaration target
- destructuring target
- lambda parameter

Examples:

```txt
_ = f()
let _ = g()
(a, _, c) = foo()
_ => 1
(a, _, c) => a + c
```

A discard cannot be referenced as a value.

```txt
let x = _      // error
```

---

## 8. Expressions

### 8.1 Categories

The language supports the following primary expression forms:

- literals
- identifiers
- parenthesized expressions
- tuple expressions
- collection expressions
- function calls
- indexing
n- member access
- unary and binary operators
- range expressions
- conditional expressions
- lambda expressions
- block expressions

### 8.2 Parenthesized Expressions

```txt
(x)
(a + b)
(if ok then 1 else 2)
```

### 8.3 Tuple Expressions

Tuple expressions are comma-separated expressions enclosed in parentheses.

```txt
(1, 2)
(x, y, z)
```

Single-element tuples are not defined.

### 8.4 Block Expressions

A brace-delimited block may be used as an expression where a value is required.

```txt
{
    let x = 1
    x + 1
}
```

The value of a block expression is determined by explicit or implicit return rules defined in Section 14.

---

## 9. Collection Expressions

### 9.1 Syntax

A collection expression is enclosed in square brackets.

```txt
[]
[1, 2, 3]
[1, x + 1, y]
```

### 9.2 Default Result Type

#### 9.2.1 Declaration Context

In declaration context with `let`, a collection expression defaults to an array type if element types can be inferred.

```txt
let a = [1, 2, 3]    // int[]
```

#### 9.2.2 Explicit Type Context

If the declaration target type is explicit, the collection expression is converted to that type.

```txt
int[] a = [1, 2, 3]
List<int> b = [1, 2, 3]
LinkedList<int> c = [1, 2, 3]
```

#### 9.2.3 R-value Context

In pure r-value context, a collection expression may be lowered as an iterable or concrete collection according to the target type or overload resolution rules. The implementation shall preserve observable semantics.

### 9.3 Automatic Expansion of Ranges

Within a collection expression, range elements are automatically expanded.

```txt
[0..<5]           // [0,1,2,3,4]
[1, 2, 0..<5]     // [1,2,0,1,2,3,4]
```

Automatic expansion applies only to range expressions.

### 9.4 Spread Elements

A spread element explicitly expands an iterable into the surrounding collection expression.

```txt
let a = [3, 4, 5]
let b = [1, 2, ..a, 6, 7]
```

Rules:

1. Spread syntax is valid only inside collection expressions.
2. The operand of spread must be iterable.
3. Spread preserves iteration order.

### 9.5 Builder Form

A collection expression may contain a comprehension-like builder.

```txt
[0..<n -> i do i * i]
[arr -> x do f(x)]
```

The builder yields a collection whose elements are the results of the `do` expression evaluated for each iteration.

---

## 10. Range Expressions

### 10.1 Syntax

The language defines the following range forms:

```txt
1..10
0..<n
0..=n
10..-1..0
```

### 10.2 Semantics

- `a..b` : inclusive range from `a` to `b`
- `a..<b` : range from `a` to `b`, excluding `b`
- `a..=b` : explicit inclusive form
- `a..step..b` : stepped range including endpoints when reachable

### 10.3 Iterability

Range expressions are iterable values. They may appear in:

- `for` loops
- `->` iteration
- collection expressions
- aggregation expressions
- function calls

---

## 11. Member Access, Indexing, and Tuple Projection

### 11.1 Member Access

```txt
obj.member
obj.method
```

### 11.2 Indexing

C-style indexing is used for arrays, strings, and indexable collections.

```txt
arr[i]
grid[r][c]
text[i]
```

### 11.3 Tuple Projection

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

Example:

```txt
(int, int, int) p = (6, 1, 4)
= p.2
```

---

## 12. Operators

### 12.1 Unary Operators

- `+x`
- `-x`
- `!x`
- `not x`

`not` is a keyword alias for boolean negation.

### 12.2 Arithmetic Operators

- `*`
- `/`
- `%`
- `+`
- `-`

### 12.3 Comparison Operators

- `<`
- `<=`
- `>`
- `>=`
- `==`
- `!=`
- `<=>`

The spaceship operator `<=>` returns one of `-1`, `0`, or `1` according to the ordering relation of its operands.

### 12.4 Logical Operators

Both symbolic and keyword forms are supported.

- conjunction: `&&`, `and`
- disjunction: `||`, `or`
- exclusive or: `^`, `xor`

The keyword form is an exact alias of the symbolic form.

### 12.5 Assignment Operators

- `=`
- `+=`
- `-=`
- `*=`
- `/=`
- `%=`

Assignment requires a mutable target unless otherwise specified by declaration syntax.

### 12.6 Pipe Operators

- `|>`
- `<|`

Pipe operators are defined for function application and sequencing in expression position.

---

## 13. Operator Precedence and Associativity

From highest precedence to lowest precedence:

1. postfix
   - `f(x)`
   - `a[i]`
   - `obj.member`
   - `p.1`

2. prefix
   - `+x`
   - `-x`
   - `!x`
   - `not x`

3. multiplicative
   - `*`
   - `/`
   - `%`

4. additive
   - `+`
   - `-`

5. range
   - `..`
   - `..<`
   - `..=`

6. comparison
   - `<`
   - `<=`
   - `>`
   - `>=`
   - `<=>`

7. equality
   - `==`
   - `!=`

8. logical and
   - `&&`
   - `and`

9. logical xor
   - `^`
   - `xor`

10. logical or
   - `||`
   - `or`

11. pipe
   - `|>`
   - `<|`

12. assignment
   - `=`
   - `+=`
   - `-=`
   - `*=`
   - `/=`
   - `%=`

Range expressions bind more weakly than additive expressions. For example:

```txt
0..<n + 1
```

parses as:

```txt
0..<(n + 1)
```

---

## 14. Return Semantics

### 14.1 Explicit Return

A `return` statement may appear inside a function, lambda block, or block expression.

```txt
return expr
return
```

`return` exits the innermost enclosing function or lambda body.

### 14.2 Implicit Return

A block may yield a value without `return`.

A block performs an implicit return if and only if its final statement is an expression statement satisfying all of the following:

1. it is the last statement in the block,
2. it is not terminated by a semicolon,
3. it is not a bare invocation expression.

### 14.3 Bare Invocation

A bare invocation is an expression statement whose top-level form is a function or method call.

Examples of bare invocation:

```txt
foo x
foo(x)
obj.method x
obj.method(x)
```

A bare invocation in final position does not implicitly return its value.

### 14.4 Non-bare Final Expressions

The following final expressions implicitly return:

```txt
a <=> b
not hashset.Add 10
foo(x) == true
if ok then 1 else 2
x + y
```

### 14.5 Value Discarding

If a value is intentionally ignored, discard assignment shall be used.

```txt
_ = hashset.Add 10
```

---

## 15. Functions and Lambdas

### 15.1 Function Declaration

Functions use explicit parameter lists with comma separation.

```txt
int add(int a, int b) {
    a + b
}
```

### 15.2 Recursive Functions

Recursion is disabled by default. A function may recursively reference itself only if declared with `rec`.

```txt
rec int fact(int n) {
    if n <= 1 then 1 else n * fact(n - 1)
}
```

Self-reference in a non-`rec` function is a compile-time error.

### 15.3 Lambda Expressions

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

### 15.4 Invocation Syntax

#### 15.4.1 Parenthesized Call

Standard call syntax is supported.

```txt
f(x)
g(x, y)
```

#### 15.4.2 Space-Separated Call

Calls may omit parentheses and commas at the call site.

```txt
add 1 2
f x
g (x + 1) y
```

Rules:

1. Space-separated call is a surface syntax for ordinary multi-argument invocation.
2. Currying and partial application are not implied by v0.2.
3. Complex arguments require parentheses.
4. The parser shall prefer maximal valid argument grouping under the precedence rules.

Recommended atomic argument forms:

- identifier
- literal
- indexing expression
- member access expression
- parenthesized expression
- lambda expression
- collection expression

---

## 16. Statements

### 16.1 Declaration Statement

Any declaration form in Section 7 may appear as a statement.

### 16.2 Assignment Statement

```txt
x = y + 1
arr[i] = 0
x += 1
```

Tuple assignment is supported.

```txt
(a, b) = (b, a)
(arr[i], arr[j]) = (arr[j], arr[i])
```

### 16.3 Expression Statement

Any expression may appear as a statement. Its value is ignored unless used as the final expression of a block under the implicit return rules.

### 16.4 Return Statement

```txt
return expr
return
```

### 16.5 Break and Continue

```txt
break
continue
```

These statements are valid only within loops.

---

## 17. Conditional Forms

### 17.1 Block If

```txt
if cond {
    ...
} else {
    ...
}
```

### 17.2 One-Line If Expression

```txt
if cond then a else b
```

Rules:

1. `then` and `else` in one-line form accept exactly one expression each.
2. Multiline use of one-line form is not permitted.
3. For multiline branches, brace-delimited blocks are required.
4. `else` binds to the nearest unmatched `if`.

---

## 18. Loops

### 18.1 While Loop

```txt
while cond {
    ...
}
```

### 18.2 For-In Loop

```txt
for i in 0..<n {
    ...
}
```

The iterated expression must be iterable.

### 18.3 One-Line Do Loop

```txt
for i in 0..<n do sum += a[i]
while cond do x += 1
```

Rules:

1. `do` accepts exactly one expression or simple statement form.
2. A newline ends the `do` body.
3. Multiline loop bodies require braces.

---

## 19. Fast Iteration Syntax

### 19.1 Statement Form

```txt
xs -> x {
    ...
}

xs -> x do expr
```

### 19.2 Indexed Form

```txt
xs -> i, x {
    ...
}

xs -> i, x do expr
```

### 19.3 Semantics

`->` introduces iteration over an iterable value.

- The single-variable form binds the element.
- The indexed form binds zero-based index and element.

The `->` construct is reserved exclusively for iteration syntax in v0.2.

---

## 20. Aggregation Expressions

Aggregation expressions compute a result from an iteration body.

### 20.1 Basic Forms

```txt
min { for i in 0..<n do a[i] - b[i] }
sum { for x in arr do x }
count { for x in arr do x % 2 == 0 }
```

### 20.2 Filtered Form

```txt
count { for x in arr where x % 2 == 0 do 1 }
sum { for x in arr where ok(x) do x }
```

The set of built-in aggregators is implementation-defined but shall include at least `min`, `max`, `sum`, and `count`.

---

## 21. One-Line Constructs

### 21.1 `then`

`then` is valid only in the one-line `if` form.

### 21.2 `do`

`do` is valid in the following positions:

- `for ... do ...`
- `while ... do ...`
- `xs -> x do ...`
- aggregation bodies
- collection builder bodies

### 21.3 Restrictions

In all one-line constructs:

1. only one expression or simple statement body is permitted,
2. the body ends at newline or statement terminator,
3. nested multiline bodies require braces.

---

## 22. Input Syntax

The language defines declaration-based input shorthand for token-oriented reading.

### 22.1 Scalar Input

```txt
int n =
long x =
string s =
char c =
```

These forms read a token from standard input and convert it to the declared type.

### 22.2 Multiple Scalar Input

```txt
int n, m =
long a, b, c =
```

Each declared name consumes one token in order.

### 22.3 Array Input

```txt
int[n] arr =
long[m] cost =
```

The declaration consumes exactly the specified number of tokens.

### 22.4 Nested Array Input

```txt
int[n][m] grid =
char[h][w] board =
```

The declaration consumes values according to its dimensions.

### 22.5 Tuple Input

```txt
(int, int) p =
(int, int)[m] edges =
(long, long, long)[k] qs =
```

A tuple declaration consumes the required number of tokens for each tuple element.

### 22.6 Input Helper API

The following helper forms are part of the standard transpilation surface:

```txt
stdin.int()
stdin.long()
stdin.str()
stdin.char()

stdin.line()
stdin.chars()
stdin.words()
stdin.lines(n)

stdin.charGrid(n)
stdin.gridInt(n, m)
stdin.gridLong(n, m)
```

The implementation may define additional helpers.

---

## 23. Output Syntax

### 23.1 Write Statement

```txt
= expr
```

This writes the rendered value without a newline.

### 23.2 WriteLine Statement

```txt
+= expr
```

This writes the rendered value followed by a newline.

### 23.3 Disambiguation

At statement start:

- `= expr` is output
- `+= expr` is line output

Elsewhere:

- `x += 1` is assignment

### 23.4 Default Rendering

The implementation shall provide default rendering for:

- scalar values: direct formatting
- tuples: elements joined by a single space
- one-dimensional collections: elements joined by a single space

Default rendering for nested collections is not defined and shall be rejected unless a helper API is used.

### 23.5 Output Helper API

```txt
stdout.write(x)
stdout.writeln(x)
stdout.lines(xs)
stdout.grid(g)
stdout.flush()
```

---

## 24. Standard Collection Operations

The following surface operations are reserved for standard library support and may be lowered to helpers or extension methods in C#:

```txt
arr.map(f)
arr.filter(pred)
arr.fold(seed, f)
arr.scan(seed, f)
arr.mapFold(seed, f)
arr.sum()
arr.sumBy(f)
arr.min()
arr.max()
arr.minBy(f)
arr.maxBy(f)
arr.any(pred)
arr.all(pred)
arr.count(pred)
arr.find(pred)
arr.findIndex(pred)
arr.findLastIndex(pred)
arr.sort()
arr.sortBy(f)
arr.sortWith(cmp)
arr.distinct()
arr.reverse()
arr.copy()
arr.groupCount()
```

### 24.1 Zero Initialization Helper

The following helper is reserved for standard array initialization:

```txt
Array.zero(n)
```

Equivalent sized declarations may also be used:

```txt
int[n] a;
```

---

## 25. Static Errors

The following are compile-time errors:

1. assigning to an immutable binding,
2. using `_` as a value,
3. self-recursive reference in a non-`rec` function,
4. malformed one-line `then` or `do` body spanning multiple statements,
5. tuple projection on a non-tuple value,
6. tuple projection with non-literal index,
7. unsupported automatic input or output shape,
8. use of `break` or `continue` outside a loop,
9. use of space-separated call with ambiguous unparenthesized complex arguments,
10. uninitialized immutable binding,
11. invalid spread operand inside a collection expression.

---

## 26. Parsing Rules of Special Interest

### 26.1 Output Statements

`= expr` and `+= expr` are parsed as output statements only when they appear at statement start.

### 26.2 Input Declarations

A declaration of the form `T x =` with no right-hand expression is parsed as input shorthand, not as incomplete assignment.

### 26.3 Spread vs Range

Inside a collection expression:

- `..expr` following a comma or collection start denotes spread,
- `a..b`, `a..<b`, `a..=b`, `a..step..b` denote range expressions.

### 26.4 Space-Separated Call

The parser shall accept space-separated invocation only when argument boundaries are unambiguous under the precedence rules. Parentheses are required to disambiguate complex argument expressions.

### 26.5 Final Expression of a Block

Only the final statement of a block may participate in implicit return.

---

## 27. Transpilation Model

The language is specified independently of any single backend, but the reference implementation targets C#.

### 27.1 Lowering Principles

1. Preserve observable semantics.
2. Prefer readable generated C#.
3. Lower sugar to helper methods when direct C# translation is awkward.
4. Use C# built-in features when semantics align.

### 27.2 Illustrative Lowerings

```txt
int[] parent = [0..<n]
```

may lower to:

```csharp
int[] parent = Enumerable.Range(0, n).ToArray();
```

```txt
let b = [1, 2, ..a, 6, 7]
```

may lower to concatenation followed by realization.

```txt
(arr[i], arr[j]) = (arr[j], arr[i])
```

may lower directly to C# tuple swap assignment.

---

## 28. Examples

### 28.1 Union-Find Initialization

```txt
int n =
int[] parent = [0..<n]
int[n] rank;
```

### 28.2 One-Line Conditional

```txt
let absx = if x < 0 then -x else x
```

### 28.3 Aggregation

```txt
let best = min { for i in 0..<n do a[i] - b[i] }
```

### 28.4 Fast Iteration

```txt
1..10 -> x {
    += x
}
```

### 28.5 Discarded Result

```txt
_ = hashset.Add 10
```

### 28.6 Tuple Projection

```txt
(int, int, int) p = (6, 1, 4)
+= p.2
```

---

## 29. Versioning and Compatibility

This document defines version `v0.2` of the language draft. Backward compatibility is not guaranteed between draft revisions.

The implementation may reject programs that depend on syntax or behavior not explicitly defined by this document.

---

## 30. Conformance

An implementation conforms to this specification if it:

1. accepts all syntactically valid programs defined herein,
2. rejects all programs required to be compile-time errors herein,
3. preserves the semantics of expressions, statements, binding, control flow, input/output shorthand, collection expressions, range expressions, tuple projection, and return behavior as defined herein.

Non-essential optimizations are implementation-defined.

