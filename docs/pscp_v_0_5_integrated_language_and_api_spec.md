# PSCP v0.5 통합 언어 및 API 명세

## 0. 문서 개요

이 문서는 PSCP `v0.5`의 **통합 언어 및 API 명세**다.

즉 이 문서는 다음 둘을 하나로 합친다.

1. **언어 문법과 소스 수준 의미**
2. **언어가 제공하는 intrinsic API와 표준 helper surface**

이 문서는 구현자, 도구 개발자, 고급 사용자를 모두 고려하지만, 특히 다음 목적을 가진다.

- PSCP 소스가 무엇을 의미하는지 한 문서에서 알 수 있게 한다.
- 문법과 API가 서로 어긋나지 않게 한다.
- pass-through C#/.NET surface와 PSCP 고유 문법설탕의 경계를 분명히 한다.
- shorthand가 어떤 intrinsic 의미로 매핑되는지 명확히 한다.

이 문서는 트랜스파일러 최적화 정책, LSP 정책, 문서 작성 전략 자체를 중심적으로 다루지는 않는다.  
그 대신 **소스 언어와 내장 API의 의미**를 정의하는 것을 주목적으로 한다.

---

## 1. 설계 목표

PSCP는 문제 해결과 경쟁 프로그래밍을 위해 설계된 언어다.

핵심 목표는 다음과 같다.

1. **짧게 쓴다.**
2. **의도를 빠르게 드러낸다.**
3. **반복적인 입출력 보일러플레이트를 줄인다.**
4. **collection / aggregate / range 기반 표현력을 높인다.**
5. **C#과 .NET의 실용성을 유지한다.**
6. **문법설탕의 비용은 런타임이 아니라 트랜스파일러가 감당한다.**

PSCP는 C#을 완전히 대체하는 언어가 아니다.  
오히려 다음 철학을 가진다.

- C#이 이미 잘하는 것은 그대로 쓴다.
- 문제 풀이에서 반복되는 귀찮은 부분만 줄인다.
- 트랜스파일러는 가능한 한 직접적이고 빠른 C#을 출력한다.

---

## 2. 비목표

다음은 PSCP의 직접적인 목표가 아니다.

1. 일반 목적 대형 애플리케이션 프레임워크 제공
2. .NET Base Class Library 전체를 새 이름으로 재포장하기
3. 모든 C# 기능을 새 PSCP 문법으로 치환하기
4. 모든 기능을 함수형 순수 의미로만 제한하기
5. 모든 API를 wrapper abstraction으로 감싸기

따라서 PSCP에서는 다음 같은 C# surface가 그대로 유효하다.

```txt
using System.Collections.Generic
Queue<int> queue = new()
PriorityQueue<int, int> pq = new()
HashSet<int> set = new()
Math.Max(a, b)
```

---

## 3. 용어

이 문서에서 다음 용어를 사용한다.

### 3.1 pass-through

PSCP가 재해석하지 않고, 가능한 한 C#/.NET 의미를 유지하는 문법 또는 API surface를 뜻한다.

예:

- `using`
- `namespace`
- `class`
- `struct`
- `record`
- `new()`
- `this`
- `base`
- generic type syntax

### 3.2 intrinsic

PSCP가 언어 차원에서 특별한 의미를 부여하는 이름, 문법, helper family를 뜻한다.

예:

- `stdin`
- `stdout`
- declaration-based input shorthand
- statement-based output shorthand
- `min`, `max`, `sum`, `sumBy`, ...
- conversion keyword calls (`int`, `bool`, ...)
- `new![n]`

### 3.3 materialized collection

실제로 구체화된 컬렉션 값을 의미한다.

PSCP에서는 주로 `[]`가 이를 나타낸다.

### 3.4 generator

lazy iterable / generator 의미를 가진 표현을 뜻한다.

PSCP에서는 주로 `()` 안에 `for` 또는 `->` iterator form이 직접 들어간 경우 이를 generator expression으로 본다.

---

## 4. 소스 형식과 어휘 규칙

## 4.1 문자 집합

소스 파일은 Unicode 텍스트다.

## 4.2 공백

공백은 토큰 분리에 사용된다.  
줄바꿈은 대부분 문장 구분에 사용되지만, 들여쓰기는 문법적 의미를 가지지 않는다.

## 4.3 주석

다음 주석 형태를 지원한다.

```txt
// single-line comment
/* multi-line comment */
```

## 4.4 문장 구분

문장은 다음 중 하나로 구분된다.

1. 줄바꿈
2. 세미콜론 `;`
3. 블록 경계

세미콜론은 필수가 아니다.  
한 줄에 여러 문장을 적고 싶을 때 사용할 수 있다.

```txt
var x = 0; x += 1; += x
```

---

## 5. 예약어와 예약 토큰

## 5.1 예약어

다음은 PSCP의 예약어다.

```txt
let
var
mut
rec

if
then
else
for
in
do
while
break
continue
return

true
false
null

and
or
xor
not

class
struct
record

ref
out
in

new
this
base

namespace
using
is

public
private
protected
internal
```

### 5.1.1 제거된 예약어

`v0.4`에서 예약어로 남아 있던 `match`, `when`은 `v0.5`에서 예약어 목록에서 제거한다.

이유:

- PSCP 고유의 `match` 구문을 아직 정의하지 않았고
- C#의 inline switch-expression surface를 pass-through로 허용하는 방향이 더 단순하며
- 예약만 해놓고 의미를 정의하지 않는 상태를 없애기 위함이다.

---

## 5.2 예약 연산자 / 특수 토큰

```txt
=
+=
-=
*=
/=
%=
:=

==
!=
<
<=
>
>=
<=>

&&
||
^
!
~
++
--

..
..<
..=

|>
<|
->
=>

_
?
^
```

주의:

- `..` 는 문맥에 따라 range 또는 spread로 사용된다.
- `?` 는 pass-through nullable marker / nullable type surface에 사용된다.
- `^` 는 index-from-end와 bitwise xor 두 곳에서 사용된다.

---

## 6. 식별자와 리터럴

## 6.1 식별자

식별자는 Unicode letter 또는 `_`로 시작하고, 이후 letter/digit/underscore를 포함할 수 있다.

단, 단독 `_`는 일반 식별자가 아니라 discard token이다.

## 6.2 리터럴

PSCP는 일반적인 C# 스타일 리터럴을 지원한다.

- 정수 리터럴
- long 리터럴
- 실수 리터럴
- decimal 리터럴
- 문자 리터럴
- 문자열 리터럴
- 보간 문자열
- `true`, `false`, `null`

예:

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

## 6.3 보간 문자열

PSCP는 C# 스타일 보간 문자열을 지원한다.

```txt
$"answer = {ans}"
$"{min a b} {sum (0..<n -> i do a[i])}"
```

보간 hole 안에는 PSCP expression이 들어간다.

형식 지정자(format specifier)는 C#과 호환되는 방향을 따른다.

예:

```txt
$"{value:F2}"
```

---

## 7. pass-through C#/.NET surface

PSCP는 일부 C# surface를 적극적으로 pass-through 한다.

## 7.1 namespace / using

```txt
namespace X.Y
namespace X.Y { ... }
using System
using System.Collections.Generic
using static System.Math
using Alias = Some.Type
```

## 7.2 `new`, `this`, `base`

```txt
new T(...)
new()
this
base
```

## 7.3 nullable marker `?`

`?` 는 PSCP 고유 문법이 아니라, 기본적으로 C# nullable surface와 호환되는 pass-through 토큰으로 취급한다.

예:

```txt
string?
NodeData?
int?
```

또한 nullable-aware type/member surface가 나타나는 경우, PSCP는 가능한 한 C# 의미를 유지한다.

즉 `?`는 예약되어 있지만, 별도의 독자적 PSCP 의미를 추가하지 않는다.

## 7.4 type test surface

```txt
x is T
x is not T
x is 0
```

## 7.5 switch-expression surface

C#의 inline switch-expression style surface는 pass-through 허용 대상이다.

## 7.6 generic surface

generic class / struct / record / function declaration 및 사용은 C#과 호환되는 표면을 따른다.

예:

```txt
class Box<T> {
    T value
}

T identity<T>(T x) {
    x
}

Dictionary<int, List<string>> map;
```

generic constraint는 현재 pass-through surface로 취급한다.

예:

```txt
T clone<T>(T x) where T : ICloneable {
    return (T)x.Clone()
}
```

PSCP는 generic syntax를 재해석하지 않는다.

---

## 8. 타입 시스템 개요

## 8.1 기본 스칼라 타입

PSCP는 다음 기본 타입을 가진다.

- `int`
- `long`
- `double`
- `decimal`
- `bool`
- `char`
- `string`

## 8.2 튜플 타입

튜플 타입은 괄호 안에 쉼표로 구분한다.

```txt
(int, int)
(long, int, string)
```

## 8.3 배열 타입

배열 타입은 C#과 같은 표면을 따른다.

```txt
int[]
int[][]
(string, int)[]
```

## 8.4 generic / nominal types

```txt
List<int>
Dictionary<int, string>
MyType
MyGeneric<T>
```

## 8.5 sized declaration form

다음 형태는 타입 표현식이 아니라 **선언 전용 형식**이다.

```txt
int[n] arr
int[n][m] grid
```

이 형식은 declaration position에서만 허용된다.

---

## 9. 바인딩, 선언, 가변성

## 9.1 기본은 불변

PSCP의 기본 바인딩은 불변이다.

## 9.2 선언 형식

### 9.2.1 `let`

타입 추론 + 불변

```txt
let x = 3
let s = "abc"
```

### 9.2.2 `var`

타입 추론 + 가변

```txt
var x = 0
var s = ""
```

### 9.2.3 `mut`

명시적 타입 + 가변

```txt
mut int x = 0
mut string s = ""
```

### 9.2.4 명시적 타입 + 불변

```txt
int x = 3
string s = "abc"
```

## 9.3 다중 선언

```txt
int a, b = 1, 2
let x, y = f()
var i, j = 0, 0
```

## 9.4 초기화 없는 선언

### 9.4.1 `mut` 스칼라/참조

초기화 없는 선언은 다음에 한해 허용된다.

```txt
mut int x;
mut string s;
mut MyClass obj;
```

이 경우 기본값으로 초기화된다.

### 9.4.2 sized array declaration

```txt
int[n] a;
int[n][m] dp;
```

이 경우 배열이 할당되고 원소는 default 값으로 초기화된다.

### 9.4.3 known collection auto-construction

다음 compiler-known collection type은 initializer 없이 선언 가능하며, 암묵적 `new()`로 간주된다.

- `List<T>`
- `LinkedList<T>`
- `Queue<T>`
- `Stack<T>`
- `HashSet<T>`
- `Dictionary<K, V>`
- `SortedSet<T>`
- `PriorityQueue<TElement, TPriority>`

예:

```txt
List<int> list;
Queue<int> queue;
HashSet<int> set;
PriorityQueue<int, int> pq;
```

의미:

```txt
List<int> list = new()
Queue<int> queue = new()
HashSet<int> set = new()
PriorityQueue<int, int> pq = new()
```

### 9.4.4 금지되는 초기화 없는 선언

다음은 허용되지 않는다.

```txt
int x;
string s;
let a;
var b;
MyType obj;
```

단, `mut`, sized declaration, known collection auto-construction은 예외다.

---

## 10. discard `_`

`_` 는 discard token이다.

허용 위치:

- declaration target
- assignment target
- destructuring target
- lambda parameter
- loop variable / fast iteration binding
- `out _`

예:

```txt
_ = f()
let _ = g()
(a, _, c) = foo()
_ => 1
(a, _, c) => a + c
foo(out _)
0..<t -> _ {
    ...
}
```

`_` 는 값으로 참조할 수 없다.

---

## 11. 프로그램 구조

소스 파일에는 다음이 섞여 있을 수 있다.

- top-level statements
- 함수 선언
- 타입 선언
- pass-through `namespace`, `using`

top-level statement는 source order대로 실행된다.

PSCP는 최종적으로 synthetic entry point를 생성할 수 있지만, source level에서는 이를 노출하지 않는다.

---

## 12. 식(expression) 개요

PSCP는 다음 expression category를 가진다.

- literal
- identifier
- parenthesized expression
- tuple expression
- block expression
- conditional expression
- unary / binary expression
- range expression
- materialized collection expression
- generator expression
- function/method call
- indexing / slicing
- member access
- tuple projection
- lambda
- assignment expression (`:=`)
- pass-through C# expression

---

## 13. 괄호, 튜플, 블록

## 13.1 일반 괄호 식

```txt
(x)
(a + b)
(if ok then 1 else 2)
```

## 13.2 튜플 식

```txt
(1, 2)
(a, b, c)
```

## 13.3 블록 식

중괄호 블록은 식으로도 사용될 수 있다.

```txt
{
    let x = 1
    x + 1
}
```

블록의 반환 규칙은 뒤의 “암묵적 반환” 절에서 정의한다.

---

## 14. 값이 되는 대입 `:=`

PSCP는 명시적 assignment expression을 지원한다.

```txt
lhs := rhs
```

의미:

1. `lhs`에 `rhs`를 대입한다.
2. 대입된 값을 결과로 돌려준다.

예:

```txt
a = b := c
parent[x] := find(parent[x])
```

규칙:

- `:=` 는 simple assignment에만 적용된다.
- compound assignment (`+=`, `-=`, ...) 에는 적용되지 않는다.
- `:=` 는 right-associative다.
- `:=` 는 return 문법이 아니다.

---

## 15. `[]` : materialized collection expression

`[]`는 구체화된 컬렉션을 의미한다.

```txt
[]
[1, 2, 3]
[1, x + 1, y]
```

## 15.1 contextual result type

`[]`의 concrete result type은 문맥에 따라 달라진다.

예:

```txt
let a = [1, 2, 3]          // 기본은 array
int[] b = [1, 2, 3]
List<int> c = [1, 2, 3]
LinkedList<int> d = [1, 2, 3]
```

## 15.2 range auto-expansion

`[]` 안에 range가 오면 자동으로 펼쳐진다.

```txt
[0..<5]
[1, 2, 0..<5]
```

## 15.3 spread element

일반 iterable을 펼치려면 `..expr`를 사용한다.

```txt
let a = [3, 4, 5]
let b = [1, 2, ..a, 6, 7]
```

규칙:

- spread는 `[]` 내부에서만 유효하다.
- range는 spread 없이도 auto-expand 된다.
- 일반 iterable은 `..`가 필요하다.

## 15.4 materialized builder

`[]` 안에서 `->` builder를 사용할 수 있다.

한 줄 builder:

```txt
[0..<n -> i do i * i]
[arr -> x do f(x)]
```

블록 builder:

```txt
[0..<n -> i {
    let x = i * i
    x + 1
}]
```

마지막 식이 yield 값이 된다.

---

## 16. `()` : generator expression

괄호 안의 직접 감싼 형태가 `for` 또는 `->` iterator form을 포함하면 generator expression으로 해석된다.

예:

```txt
(0..<n -> i do i * i)
(0..<n -> i {
    let x = i * i
    x + 1
})
(for i in 0..<n do a[i])
```

generator expression은 materialized collection이 아니라 **lazy iterable / generator 의미**를 가진다.

중요 규칙:

1. ordinary parenthesized expression은 그대로 ordinary expression이다.
2. tuple syntax는 tuple로 남는다.
3. generator parsing은 괄호 내부에 직접 `for` 또는 `->` iterator form이 있을 때만 일어난다.

즉:

```txt
(1 + 2)     // ordinary expression
(a, b)      // tuple
(0..<n)     // range expression wrapped in parentheses
(0..<n -> i do i * i)   // generator expression
```

---

## 17. range expression

## 17.1 문법

다음 range form을 지원한다.

```txt
1..10
0..<n
0..=n
10..-1..0
```

## 17.2 의미

- `a..b` : `a`부터 `b`까지 inclusive, step `+1`
- `a..<b` : `a`부터 `b-1`까지, step `+1`
- `a..=b` : explicit inclusive form, step `+1`
- `a..step..b` : explicit stepped range

### 17.2.1 매우 중요한 규칙

기본 range는 **자동으로 역방향 step을 추론하지 않는다.**

즉 내림차순을 원하면 반드시 explicit step을 적어야 한다.

```txt
10..-1..0
```

은 유효하지만,

```txt
10..0
```

은 “자동으로 -1 step”으로 해석되지 않는다.

이 규칙은 문법과 lowering 양쪽에서 동일하게 유지되어야 한다.

## 17.3 range의 사용처

range는 iterable이다.

사용 가능한 곳:

- `for`
- `->`
- generator
- materialized collection
- aggregate call
- 일반 iterable consumer

---

## 18. member access, indexing, slicing, tuple projection

## 18.1 member access

```txt
obj.member
obj.method
```

## 18.2 indexing

```txt
arr[i]
grid[r][c]
text[i]
```

## 18.3 index-from-end

`^` 는 index-from-end 문맥에서는 C#과 같은 의미를 가진다.

```txt
text[^1]
arr[^2]
```

## 18.4 slicing

PSCP는 C# 스타일 slicing surface를 지원한다.

```txt
text[1..^2]
text[..^1]
arr[1..]
arr[..n]
arr[..]
```

최소 scope:

- `string`
- 1차원 배열

가능한 경우 C#의 slicing/index semantics와 일치하도록 한다.

## 18.5 tuple projection

튜플 프로젝션은 `.정수리터럴` 형태다.

```txt
p.1
p.2
p.3
```

규칙:

- 1-based indexing
- dot 뒤는 positive integer literal이어야 한다
- dynamic tuple indexing은 지원하지 않는다

---

## 19. conversion keyword calls

PSCP는 built-in type keyword가 expression position에서 conversion/parsing function처럼 동작할 수 있게 한다.

핵심 집합:

- `int`
- `long`
- `double`
- `decimal`
- `bool`
- `char`
- `string`

예:

```txt
int "123"
long "1000"
double "3.14"
int(true)
bool 0
bool "hello"
string 123
```

## 19.1 의미

이 기능은 다음 practical conversion category를 포괄한다.

1. string parsing
2. numeric-to-numeric cast/convert
3. bool-to-numeric conversion
4. numeric-to-bool truthiness conversion
5. string-to-bool truthiness conversion
6. scalar-to-string conversion

## 19.2 대표 규칙

예:

- `int "123"` -> integer parse
- `bool ""` -> false
- `bool "abc"` -> true
- `int true` -> 1
- `int false` -> 0
- `bool 0` -> false
- `bool 5` -> true
- `string 123` -> scalar string conversion

정확한 conversion table은 intrinsic family semantics로 본다.

---

## 20. 연산자

## 20.1 단항 연산자

- `+x`
- `-x`
- `!x`
- `not x`
- `~x`
- `++x`
- `--x`

## 20.2 후위 연산자

- `x++`
- `x--`

## 20.3 산술 연산자

- `*`
- `/`
- `%`
- `+`
- `-`

## 20.4 비교 연산자

- `<`
- `<=`
- `>`
- `>=`
- `==`
- `!=`
- `<=>`

### 20.4.1 spaceship `<=>`

`<=>` 는 -1, 0, 1 중 하나를 반환하는 ordering operator다.

## 20.5 논리 연산자와 비트 연산자

### 논리 연산자

- `&&`, `and`
- `||`, `or`
- `!`, `not`

### xor

`^` 는 C#과 동일하게 **bitwise xor** 로 정의한다.

이는 `v0.4`에서 logical xor처럼 적혀 있던 부분을 정정한 것이다.

`xor` keyword는 `^`의 alias로 간주한다.

즉:

- 정수 문맥에서는 bitwise xor
- bool 문맥에서는 C#의 `^` 와 마찬가지로 boolean xor

으로 이해하면 된다.

핵심은 **logical family 특수 의미가 아니라 C#의 `^` 의미를 따른다**는 점이다.

## 20.6 대입 연산자

- `=`
- `+=`
- `-=`
- `*=`
- `/=`
- `%=`
- `:=`

## 20.7 pipe operators

- `|>`
- `<|`

## 20.8 comparator sugar

```txt
T.asc
T.desc
```

예:

```txt
int.asc
int.desc
string.desc
```

의미:

- `T.asc` : ascending default comparer for `T`
- `T.desc` : descending default comparer for `T`

---

## 21. 연산자 우선순위

높은 우선순위에서 낮은 우선순위 순서.

1. postfix
   - call
   - indexing / slicing
   - member access
   - tuple projection
   - postfix `++`, `--`

2. prefix
   - unary `+`, `-`
   - `!`, `not`
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

9. xor
   - `^`, `xor`

10. logical or
   - `||`, `or`

11. pipe
   - `|>`, `<|`

12. ordinary assignment
   - `=`, `+=`, `-=`, `*=`, `/=`, `%=`

13. explicit assignment expression
   - `:=`

`:=`는 right-associative다.

---

## 22. 호출과 인자

## 22.1 일반 괄호 호출

```txt
f(x)
g(x, y)
obj.method(a, b)
```

## 22.2 공백 호출

PSCP는 space-separated call surface를 지원한다.

```txt
f x
g x y
obj.method x y
```

이것은 currying이나 partial application이 아니라, ordinary multi-argument call을 짧게 쓰는 문법이다.

복잡한 인자는 괄호로 감싼다.

```txt
f (a + b) x
sum (0..<n -> i do score(i))
```

## 22.3 modified arguments

`ref`, `out`, `in`을 지원한다.

괄호 호출:

```txt
foo(ref x, out y, in z)
foo(out int a, out int b)
foo(out _, ref arr[i])
```

공백 호출:

```txt
foo ref x out y in z
foo out int a out int b
foo out _ ref arr[i]
```

규칙:

- modifier는 바로 다음 인자에만 붙는다.
- `ref`, `in`은 assignable expression이 필요하다.
- `out`은 assignable target, typed out-variable declaration, discard `_` 를 허용한다.

---

## 23. 함수와 람다

## 23.1 함수 선언

```txt
int add(int a, int b) {
    a + b
}
```

parameter list는 쉼표로 구분한다.

## 23.2 재귀 함수

재귀는 기본적으로 금지되며, `rec`를 붙여야 한다.

```txt
rec int fact(int n) {
    if n <= 1 then 1 else n * fact(n - 1)
}
```

## 23.3 generic 함수

generic 함수 선언은 C#과 같은 surface를 따른다.

```txt
T identity<T>(T x) {
    x
}

(int, int) pair<T1, T2>(T1 a, T2 b) {
    (a, b)
}
```

generic constraint는 pass-through surface다.

## 23.4 람다

```txt
x => x + 1
(acc, x) => acc + x
(acc, x) => {
    let y = acc + x
    y % mod
}
```

discard parameter도 허용한다.

```txt
_ => 1
(a, _, c) => a + c
```

modified parameter도 허용한다.

```txt
(ref int x, out int y) => { ... }
```

---

## 24. 문장(statement)

PSCP의 주요 statement category는 다음과 같다.

- block statement
- declaration
- assignment
- expression statement
- output statement
- `if`, `while`, `for`
- fast iteration `->`
- `return`, `break`, `continue`
- type declaration statement
- pass-through declaration statement

## 24.1 대입

```txt
x = y + 1
arr[i] = 0
x += 1
```

## 24.2 tuple assignment

```txt
(a, b) = (b, a)
(arr[i], arr[j]) = (arr[j], arr[i])
```

## 24.3 expression statement

모든 expression은 statement로 올 수 있다.  
값은 무시된다. 단, block의 마지막 implicit value 규칙은 별도로 적용된다.

---

## 25. 제어 흐름

## 25.1 `if`

블록 형태:

```txt
if cond {
    ...
} else {
    ...
}
```

한 문장 형태:

```txt
if x < 0 then x = -x
if bad then continue
if ok then return ans else return -1
```

`then`, `else`는 **바로 다음 한 문장**에만 결합한다.

## 25.2 `while`

```txt
while cond {
    ...
}

while cond do x += 1
```

## 25.3 `for ... in ...`

```txt
for i in 0..<n {
    ...
}

for i in 0..<n do sum += a[i]
```

## 25.4 `break`, `continue`, `return`

```txt
break
continue
return expr
return
```

---

## 26. fast iteration `->`

PSCP는 `->` 를 iterator / builder용으로 사용한다.

## 26.1 statement form

```txt
xs -> x {
    ...
}

xs -> x do expr
```

## 26.2 indexed form

```txt
xs -> i, x {
    ...
}

xs -> i, x do expr
```

의미:

- single-variable form: 각 원소를 바인딩
- indexed form: 0-based index와 원소를 둘 다 바인딩

`->`는 lambda syntax가 아니다.

---

## 27. aggregate intrinsic family

PSCP는 aggregate를 ordinary intrinsic call family로 본다.

핵심 family:

- `min`
- `max`
- `sum`
- `sumBy`
- `minBy`
- `maxBy`
- `chmin`
- `chmax`

## 27.1 `min`, `max`

두 값 비교:

```txt
min a b
max a b
```

variadic:

```txt
min a b c
max a b c d
```

iterable:

```txt
min arr
max (0..<n -> i do a[i])
```

## 27.2 `sum`

```txt
sum arr
sum (0..<n -> i do score(i))
```

## 27.3 `sumBy`

```txt
sumBy arr (x => x * x)
sumBy points (p => p.2)
```

## 27.4 `minBy`, `maxBy`

```txt
minBy points (p => p.x)
maxBy items (x => score(x))
```

## 27.5 `chmin`, `chmax`

```txt
chmin ref best cand
chmax ref ans value
```

의미:

- 더 좋은 값이면 target 갱신
- 갱신 여부를 bool로 반환

---

## 28. math intrinsic family

`v0.5`에서는 자주 쓰는 수학 convenience intrinsic을 기본 surface에 포함한다.

최소 집합:

- `abs`
- `sqrt`

확장 집합:

- `clamp`
- `gcd`
- `lcm`
- `floor`
- `ceil`
- `round`
- `pow`
- `popcount`
- `bitLength`

이 중 최소 집합은 표준 intrinsic으로 간주한다.  
확장 집합은 구현이 지원하는 범위에서 제공할 수 있으며, reference implementation에서도 적극 권장된다.

## 28.1 `abs`

### 시그니처

```txt
abs(x)
```

### 인자

- `x`: `int | long | double | decimal` 및 이에 준하는 scalar ordered numeric type

### 반환값

- 입력과 같은 수치형 또는 그에 준하는 scalar numeric type

### 의미

절댓값을 반환한다.

### 예시

```txt
let d = abs(a - b)
```

## 28.2 `sqrt`

### 시그니처

```txt
sqrt(x)
```

### 인자

- `x`: 실수형 또는 실수로 승격 가능한 numeric scalar

### 반환값

- 기본적으로 `double`

### 의미

제곱근을 반환한다.

### 예시

```txt
let dist = sqrt(dx * dx + dy * dy)
```

## 28.3 `clamp`

### 시그니처

```txt
clamp(x, lo, hi)
clamp x lo hi
```

### 의미

`x`를 `[lo, hi]` 범위로 제한한다.

## 28.4 `gcd`, `lcm`

```txt
gcd(a, b)
lcm(a, b)
```

정수형에 대해 최대공약수 / 최소공배수를 계산한다.

## 28.5 `floor`, `ceil`, `round`

```txt
floor(x)
ceil(x)
round(x)
```

### 의미

실수형 값을 적절한 정수형 또는 실수형 floor/ceil/round 결과로 변환한다.  
구체적 반환 타입 정책은 implementation-defined일 수 있으나, reference implementation에서는 .NET `Math` 계열과 호환되는 방향을 따른다.

## 28.6 `pow`

```txt
pow(x, y)
```

거듭제곱 계산.

## 28.7 `popcount`

```txt
popcount(x)
```

정수형 비트 표현에서 1의 개수를 반환한다.

## 28.8 `bitLength`

```txt
bitLength(x)
```

정수형 표현의 유효 비트 길이를 반환한다.

---

## 29. block value와 암묵적 반환

PSCP는 block의 마지막 식을 implicit value로 사용할 수 있다.

예:

```txt
int abs(int x) {
    if x < 0 then -x else x
}
```

## 29.1 explicit return

```txt
return expr
return
```

## 29.2 implicit return

block의 마지막 statement가 expression statement이고, 다음을 만족하면 implicit return value가 된다.

1. block의 마지막 문장이다.
2. 세미콜론으로 끝나지 않는다.
3. bare invocation이 아니다.

## 29.3 bare invocation

다음은 bare invocation이라 implicit return이 아니다.

```txt
foo x
foo(x)
obj.method x
obj.method(x)
```

즉 마지막 줄이 호출식이면 자동 반환으로 보지 않는다.

반면 다음은 implicit return이다.

```txt
a <=> b
not hashset.Add 10
foo(x) == true
if ok then 1 else 2
x + y
parent[x] := find(parent[x])
```

---

## 30. 입력 shorthand와 `stdin`

PSCP의 입력 시스템은 두 층으로 나뉜다.

1. declaration-based shorthand
2. explicit `stdin` API

## 30.1 declaration-based input shorthand

### scalar

```txt
int n =
long x =
string s =
char c =
```

### multiple scalar

```txt
int n, m =
long a, b, c =
```

### arrays

```txt
int[n] arr =
long[m] cost =
```

### nested arrays

```txt
int[n][m] grid =
char[h][w] board =
```

### tuples

```txt
(int, int) p =
(int, int)[m] edges =
(long, long, long)[k] qs =
```

## 30.2 explicit `stdin` API

`stdin`는 intrinsic input object다.

### 30.2.1 scalar readers

```txt
stdin.int()
stdin.long()
stdin.str()
stdin.char()
stdin.double()
stdin.decimal()
stdin.bool()
```

#### 의미

- `stdin.int()` : 토큰 하나를 읽어 `int`로 파싱
- `stdin.long()` : 토큰 하나를 읽어 `long`으로 파싱
- `stdin.str()` : 토큰 하나를 읽어 `string`으로 반환
- `stdin.char()` : 토큰 하나를 읽어 `char`로 반환
- `stdin.double()` : 토큰 하나를 읽어 `double`로 파싱
- `stdin.decimal()` : 토큰 하나를 읽어 `decimal`로 파싱
- `stdin.bool()` : 토큰 하나를 읽어 `bool`로 해석

### 30.2.2 line readers

```txt
stdin.line()
stdin.lines(n)
stdin.words()
stdin.chars()
```

#### 시그니처와 반환

- `stdin.line() -> string`
- `stdin.lines(n: int) -> string[]`
- `stdin.words() -> string[]`
- `stdin.chars() -> char[]`

#### 의미

- `line()` : 한 줄 전체를 읽는다.
- `lines(n)` : 정확히 `n`줄을 읽는다.
- `words()` : 한 줄을 읽고 단어 배열로 분리한다.
- `chars()` : 한 줄을 읽고 문자 배열로 만든다.

### 30.2.3 shaped readers

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

#### 시그니처와 반환

- `stdin.array<T>(n: int) -> T[]`
- `stdin.list<T>(n: int) -> List<T>`
- `stdin.linkedList<T>(n: int) -> LinkedList<T>`
- `stdin.tuple2<T1, T2>() -> (T1, T2)`
- `stdin.tuple3<T1, T2, T3>() -> (T1, T2, T3)`
- `stdin.tuples2<T1, T2>(n: int) -> (T1, T2)[]`
- `stdin.tuples3<T1, T2, T3>(n: int) -> (T1, T2, T3)[]`
- `stdin.gridInt(n: int, m: int) -> int[][]`
- `stdin.gridLong(n: int, m: int) -> long[][]`
- `stdin.charGrid(n: int) -> char[][]`
- `stdin.wordGrid(n: int) -> string[][]`

#### 의미

- `array<T>(n)` : `T` 값을 `n`개 읽어 배열 반환
- `list<T>(n)` : `T` 값을 `n`개 읽어 `List<T>` 반환
- `linkedList<T>(n)` : `T` 값을 `n`개 읽어 `LinkedList<T>` 반환
- `tuple2`, `tuple3` : tuple 한 개 읽기
- `tuples2`, `tuples3` : tuple 여러 개 읽기
- `gridInt`, `gridLong` : 정수 grid 읽기
- `charGrid(n)` : `n`줄을 읽어 각 줄을 `char[]`로 저장
- `wordGrid(n)` : `n`줄을 읽어 각 줄을 단어 배열로 저장

### 30.2.4 note on generic readers

`stdin.array<T>` 등은 source-level API로는 존재하지만, shorthand lowering의 기본 경로가 반드시 generic helper를 경유해야 함을 뜻하지는 않는다.  
source semantics와 transpiler lowering policy는 별개다.

---

## 31. 출력 shorthand와 `stdout`

출력도 두 층으로 나뉜다.

1. statement shorthand
2. explicit `stdout` API

## 31.1 statement shorthand

### write without newline

```txt
= expr
```

### write with newline

```txt
+= expr
```

규칙:

- statement start에서만 output shorthand로 해석된다.
- 다른 위치의 `+=`는 ordinary compound assignment 또는 known data-structure operator rewrite 후보이다.

## 31.2 explicit `stdout` API

`stdout`는 intrinsic output object다.

### 31.2.1 primitive output

```txt
stdout.write(x)
stdout.writeln(x)
stdout.flush()
```

#### 시그니처

- `stdout.write(x: printable) -> void`
- `stdout.writeln(x: printable) -> void`
- `stdout.flush() -> void`

### 31.2.2 structured output

```txt
stdout.lines(xs)
stdout.grid(g)
stdout.join(sep, xs)
```

#### 시그니처

- `stdout.lines(xs: iterable of printable) -> void`
- `stdout.grid(g: grid-like printable structure) -> void`
- `stdout.join(sep: string | char, xs: iterable of printable) -> void`

#### 의미

- `lines(xs)` : 각 원소를 한 줄씩 출력
- `grid(g)` : 2차원 구조를 줄 단위로 출력
- `join(sep, xs)` : `xs`를 `sep`로 이어 붙여 출력, 기본적으로 줄바꿈은 붙지 않음

### 31.2.3 기본 rendering contract

PSCP 기본 출력은 다음을 자연스럽게 다룬다.

- scalar value
- tuple
- 1차원 iterable / collection

기본 정책:

- tuple은 원소를 공백으로 구분
- 1차원 collection은 원소를 공백으로 구분
- nested collection은 기본 renderer에 맡기기보다 `stdout.grid` 같은 명시적 helper 사용 권장

---

## 32. array / allocation helper

## 32.1 `Array.zero`

```txt
Array.zero(n)
```

### 시그니처

- `Array.zero<T>(n: int) -> T[]`

### 의미

길이 `n`인 1차원 배열을 만들고, 각 원소를 default 값으로 초기화한다.

### 예시

```txt
int[] arr = Array.zero(10)
```

## 32.2 `new[n]`

```txt
NodeInfo[] nodes = new[n]
int[] arr = new[n]
```

의미:

- target type의 element type을 보고 배열을 할당한다.

## 32.3 `new![n]`

```txt
List<int>[] graph = new![n]
Queue<int>[] buckets = new![m]
```

의미:

1. 배열 생성
2. 각 칸을 implicit `new()`로 자동 초기화

제한:

- known auto-constructible collection element type에만 허용된다.
- arbitrary class array 전체를 자동 초기화하는 문법은 아니다.

---

## 33. known data structure operator rewrites

PSCP는 일부 compiler-known data structure에 대해 operator rewrite를 제공한다.

이건 runtime wrapper가 아니라 source-level sugar다.

## 33.1 `HashSet<T>`

```txt
set += value
set -= value
```

의미:

- `set += value` -> `set.Add(value)`
- `set -= value` -> `set.Remove(value)`

중요:

- underlying return type을 그대로 보존한다.
- `HashSet<T>.Add` / `Remove`의 bool 반환이 그대로 살아 있다.

예:

```txt
if not (visited += me) then continue
```

## 33.2 `Stack<T>`

```txt
s += value
~s
--s
```

의미:

- `s += value` -> `s.Push(value)`
- `~s` -> `s.Peek()`
- `--s` -> `s.Pop()`

postfix `s--`는 지원하지 않는다.

## 33.3 `Queue<T>`

```txt
q += value
~q
--q
```

의미:

- `q += value` -> `q.Enqueue(value)`
- `~q` -> `q.Peek()`
- `--q` -> `q.Dequeue()`

## 33.4 `PriorityQueue<TElement, TPriority>`

```txt
pq += (item, priority)
~pq
--pq
```

의미:

- `pq += (item, priority)` -> enqueue
- `~pq` -> peek한 **item만** 반환
- `--pq` -> dequeue한 **item만** 반환

priority도 필요하면 ordinary explicit API 사용:

```txt
pq.TryPeek(out item, out priority)
pq.TryDequeue(out item, out priority)
```

---

## 34. standard collection helper surface

PSCP는 collection-oriented helper surface를 가진다.  
이들은 source-level 표준 helper로 간주되며, 구현은 direct lowering 또는 helper library로 표현될 수 있다.

핵심 집합:

- `map`
- `filter`
- `fold`
- `scan`
- `mapFold`
- `sum`
- `sumBy`
- `min`
- `max`
- `minBy`
- `maxBy`
- `any`
- `all`
- `count`
- `find`
- `findIndex`
- `findLastIndex`
- `sort`
- `sortBy`
- `sortWith`
- `distinct`
- `reverse`
- `copy`
- `groupCount`
- `index`
- `freq`

이 중 aggregate family는 이미 앞에서 정의한 intrinsic family이기도 하다.

아래는 대표 helper의 의미를 설명한다.

## 34.1 `map`

```txt
xs.map(f)
```

### 시그니처

- `map<T, U>(xs: iterable<T>, f: T -> U) -> U[]` 또는 context-appropriate materialized collection

### 의미

각 원소에 `f`를 적용해 새 컬렉션을 만든다.

## 34.2 `filter`

```txt
xs.filter(pred)
```

### 시그니처

- `filter<T>(xs: iterable<T>, pred: T -> bool) -> T[]` 또는 context-appropriate collection

### 의미

predicate를 만족하는 원소만 남긴다.

## 34.3 `fold`

```txt
xs.fold(seed, f)
```

### 시그니처

- `fold<T, U>(xs: iterable<T>, seed: U, f: (U, T) -> U) -> U`

### 의미

왼쪽에서부터 누적한다.

예:

```txt
let sum = arr.fold(0, (acc, x) => acc + x)
```

## 34.4 `scan`

```txt
xs.scan(seed, f)
```

### 시그니처

- `scan<T, U>(xs: iterable<T>, seed: U, f: (U, T) -> U) -> U[]`

### 의미

중간 누적 상태 전체를 collection으로 반환한다.

## 34.5 `mapFold`

```txt
xs.mapFold(seed, f)
```

### 시그니처

- `mapFold<T, S, U>(xs: iterable<T>, seed: S, f: (S, T) -> (S, U)) -> (U[], S)` 또는 equivalent tuple ordering per implementation policy

### 의미

상태를 누적하면서 mapped result도 동시에 만든다.

## 34.6 `any`, `all`, `count`

```txt
xs.any(pred)
xs.all(pred)
xs.count(pred)
```

### 의미

- `any` : 하나라도 참이면 true
- `all` : 모두 참이면 true
- `count` : predicate를 만족하는 개수

## 34.7 `find`, `findIndex`, `findLastIndex`

```txt
xs.find(pred)
xs.findIndex(pred)
xs.findLastIndex(pred)
```

### 의미

- `find` : 첫 원소를 찾음
- `findIndex` : 첫 인덱스를 찾음
- `findLastIndex` : 마지막 인덱스를 찾음

주의:

- 못 찾았을 때의 반환 정책은 implementation-defined일 수 있으므로, reference implementation의 정책을 별도 문서에서 구체화한다.
- 사용자 문서에서는 “없을 수 있다”는 점을 항상 경고하는 것이 좋다.

## 34.8 `sort`, `sortBy`, `sortWith`

```txt
xs.sort()
xs.sortBy(f)
xs.sortWith(cmp)
```

### 의미

- `sort()` : default ordering으로 정렬
- `sortBy(f)` : key selector로 정렬
- `sortWith(cmp)` : explicit comparer로 정렬

예:

```txt
let sorted = xs.sort().distinct()
let sorted2 = arr.sortWith(int.desc)
```

## 34.9 `distinct`, `reverse`, `copy`

```txt
xs.distinct()
xs.reverse()
xs.copy()
```

### 의미

- `distinct` : 중복 제거
- `reverse` : 역순
- `copy` : shallow copy

## 34.10 `groupCount`, `index`, `freq`

### `groupCount`

```txt
xs.groupCount()
```

값별 개수를 센다.

### `index`

```txt
xs.index()
```

대표적으로 좌표압축용 rank map / index map을 만든다. 구현 policy는 stable distinct sequence 기준의 위치 map으로 잡는 것이 권장된다.

### `freq`

```txt
xs.freq()
```

빈도 맵을 만든다.

---

## 35. comparator / ordering system

## 35.1 `T.asc`, `T.desc`

```txt
T.asc
T.desc
```

기본 ordering을 가진 타입에 대해 ascending / descending comparer를 제공한다.

예:

```txt
arr.sortWith(int.asc)
arr.sortWith(int.desc)
PriorityQueue<int, int> pq = new(int.desc)
```

## 35.2 type-body ordering shorthand

PSCP는 type body 안에서 default ordering을 짧게 선언할 수 있다.

```txt
operator<=>(other) => this.value <=> other.value
```

의미:

- `other`는 enclosing self type
- return type은 암묵적으로 `int`
- 이 선언은 그 타입의 default ordering을 정의한다

예:

```txt
record struct Job(int Id, int Arrival, long Time) {
    operator<=>(other) =>
        if Arrival == other.Arrival then Time <=> other.Time
        else Arrival <=> other.Arrival
}
```

---

## 36. class / struct / record 선언

## 36.1 class

```txt
class C {
    ...
}
```

## 36.2 struct

```txt
struct S {
    ...
}
```

## 36.3 record / record struct

```txt
record R(int X, int Y)
record struct P(int X, int Y)
```

## 36.4 base class / interface list

```txt
class A : B, IFoo, IBar {
    ...
}

record struct Job(...) : IComparable<Job> {
    ...
}
```

## 36.5 generic type declaration

```txt
class Box<T> {
    T value
}

record struct Pair<T1, T2>(T1 First, T2 Second)
```

generic constraint는 pass-through surface다.

## 36.6 access modifiers

`public`, `private`, `protected`, `internal` 은 ordinary C# pass-through modifier다.

`v0.5`에서는 access section label (`public:`, `private:` 등)을 제거한다.

즉 colon-suffixed section label은 더 이상 언어 기능이 아니다.

---

## 37. 입력/출력 및 runtime 계약에 대한 언어 수준 노트

이 문서는 기본적으로 문법/API 명세지만, 입출력 intrinsic은 런타임 기대를 어느 정도 동반하므로 최소한의 언어 수준 노트를 남긴다.

## 37.1 fast I/O baseline

reference implementation은 기본 fast I/O baseline으로 다음을 사용해야 한다.

- `Console.OpenStandardInput()` + `StreamReader`
- `Console.OpenStandardOutput()` + `StreamWriter`
- 충분한 버퍼 크기
- 프로그램 종료 시 flush

이는 권장 수준이 아니라 reference baseline이다.

## 37.2 Release build 예외 정책

Release build에서 **PSCP가 추가로 주입한 artificial `throw`** 는 없어야 한다.

예외:

- 사용자가 source에서 직접 쓴 `throw`
- pass-through C# semantics로 원래 발생하는 예외

즉 language helper / transpiler generated defensive checks를 이유로 Release output에 `throw`를 남기는 것은 금지된다.

필요한 검사는 다음 중 하나여야 한다.

- compile-time에 제거
- `#if DEBUG` 로만 유지
- `Debug.Assert` 성격으로 유지

이 규칙은 언어 의미보다는 reference implementation contract에 가깝지만, PSCP source와 intrinsic helper의 기대 동작에 중요한 제약이므로 이 문서에도 기록한다.

---

## 38. parse / disambiguation rules

## 38.1 output shorthand

`= expr` 와 `+= expr` 는 **statement start** 에서만 output shorthand로 해석된다.

## 38.2 input shorthand

`T x =` 형태에서 오른쪽 식이 비어 있으면 declaration-based input shorthand로 해석된다.

## 38.3 spread vs range

`[]` 내부에서:

- `..expr` 는 spread
- `a..b`, `a..<b`, `a..=b`, `a..step..b` 는 range

## 38.4 generator detection

괄호 내부가 직접적으로 `for` 또는 `->` iterator form이면 generator expression으로 해석한다.

그 외에는 ordinary parenthesized expression 또는 tuple rule을 따른다.

## 38.5 `public:` removal note

`v0.5`에서는 `public:` / `private:` 등 colon-suffixed section label을 지원하지 않는다.  
이 토큰 조합은 더 이상 언어 기능이 아니다.

---

## 39. 대표 예시

## 39.1 generator + aggregate

```txt
int n =

let total = sum (0..<n -> i {
    let x = i * i
    x + 1
})

= total
```

## 39.2 materialized builder

```txt
let squares = [0..<n -> i do i * i]
```

## 39.3 HashSet add returning bool

```txt
HashSet<int> visited;
if not (visited += me) then continue
```

## 39.4 Union-Find

```txt
int n =
int[] parent = [0..<n]
int[n] size;

for i in 0..<n do size[i] = 1

rec int find(int x) {
    if parent[x] == x then x
    else parent[x] := find(parent[x])
}
```

## 39.5 `new![n]`

```txt
List<int>[] graph = new![n]
```

## 39.6 ordering shorthand

```txt
record struct Job(int Id, int Arrival, long Time) {
    operator<=>(other) =>
        if Arrival == other.Arrival then Time <=> other.Time
        else Arrival <=> other.Arrival
}
```

## 39.7 conversion keyword

```txt
let a = int "123"
let b = int(true)
let c = bool "hello"
```

## 39.8 slicing

```txt
let middle = text[1..^1]
let tail = arr[1..]
let last = arr[^1]
```

---

## 40. 정적 오류

다음은 compile-time error다.

1. immutable binding에 대한 대입
2. `_`를 값으로 사용
3. non-`rec` 함수에서 자기 자신 재귀 호출
4. invalid tuple projection
5. spread를 `[]` 밖에서 사용
6. malformed input shorthand
7. malformed output shorthand
8. `break`, `continue`를 loop 밖에서 사용
9. invalid use of `ref`, `out`, `in`
10. invalid comparator sugar target
11. invalid known collection auto-construction target
12. invalid `new![n]` element type
13. invalid known data-structure operator target
14. invalid `:=` left-hand side
15. invalid generator-expression form
16. use of removed access section label syntax (`public:`, `private:` 등)

---

## 41. 버전과 호환성

이 문서는 PSCP `v0.5` 통합 언어 및 API 명세다.

draft 버전 간 backward compatibility는 보장되지 않는다.

---

## 42. 적합성(conformance)

구현은 다음을 만족하면 이 명세에 적합하다.

1. 이 문서가 정의한 문법을 수용한다.
2. 이 문서가 요구하는 compile-time error를 올바르게 보고한다.
3. intrinsic input/output, aggregate, math, conversion, comparator, known DS rewrite, allocation shorthand의 의미를 보존한다.
4. pass-through C#/.NET surface를 가능한 한 원래 의미대로 유지한다.
5. `v0.5`에서 제거된 기능(`match`, `when` 예약, access section label)을 다시 암묵적으로 살려서 모호성을 만들지 않는다.

---

## 43. 맺음말

PSCP `v0.5`의 핵심은 다음과 같다.

- source는 짧아야 하고
- 의미는 명확해야 하며
- intrinsic API는 실전적으로 충분히 풍부해야 하고
- C#/.NET의 장점은 계속 사용할 수 있어야 한다.

이 통합 명세는 그 균형을 문법과 API 양쪽에서 한 번에 보여주기 위해 작성되었다.

