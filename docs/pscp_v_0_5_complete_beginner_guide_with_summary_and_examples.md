# PSCP v0.5 완전 입문서

## 0. 이 문서는 무엇인가

이 문서는 **PSCP를 실제로 사용해서 문제를 풀 사람**을 위한, 아주 자세하고 실전적인 입문서다.

이 문서는 단순한 문법 목록이 아니다.  
대신 다음 목표를 가진다.

1. PSCP가 어떤 언어인지 직관적으로 이해하게 한다.
2. 기본 문법을 실제 문제 풀이 감각으로 익히게 한다.
3. PSCP의 문법설탕이 왜 있는지, 언제 쓰면 좋은지 알려준다.
4. `stdin`, `stdout`, aggregate family, math helper, collection helper를 어떻게 써먹는지 알려준다.
5. 뒤쪽에서는 “이 언어가 어떤 스타일의 코드를 좋아하는지”를 보여주는 큰 예제를 제공한다.

즉 이 문서를 끝까지 읽고 나면, 단순히 “문법을 안다” 수준이 아니라,

> “PSCP로 어떤 식으로 문제를 풀어야 하는지 감이 잡힌다”

상태가 되는 것이 목표다.

---

## 1. PSCP는 어떤 언어인가

PSCP는 문제 해결과 경쟁 프로그래밍을 위해 만든 언어다.

짧게 요약하면:

- C#/.NET 생태계를 유지하면서
- 실전에서 자주 반복되는 보일러플레이트를 줄이고
- range, collection, aggregate, shorthand I/O 같은 표현력을 높이며
- 최종적으로는 **직접적이고 빠른 C# 코드로 변환되는 언어**

이다.

### 1.1 왜 굳이 PSCP인가

문제를 많이 풀다 보면, 이런 아쉬움이 생긴다.

- C#은 친숙하고 imperative control flow가 좋지만, 너무 길 때가 있다.
- F#은 표현력이 좋지만, 들여쓰기 문법이나 일부 imperative 도구 부재가 불편할 수 있다.
- C++은 강력하지만, .NET 라이브러리와 자료구조를 쓰는 감각과는 또 다르다.

PSCP는 이 사이에서 다음 방향을 노린다.

- **입출력은 훨씬 짧게**
- **배열/범위/집계는 훨씬 표현력 있게**
- **자료구조는 그대로 .NET 걸 써도 되게**
- **imperative 흐름도 충분히 살리게**

즉,

> “문제 풀이에서 귀찮은 것은 줄이고, 강한 도구는 그대로 쓰자”

가 핵심이다.

---

## 2. 아주 짧은 예제로 감 잡기

먼저 아주 짧은 코드부터 보자.

```txt
int n, m =
int[n] arr =

= arr.fold(0, (acc, x) => (acc + x) % m)
```

이건 다음 뜻이다.

1. 정수 두 개를 입력받아 `n`, `m`에 넣는다.
2. 길이 `n`인 정수 배열을 입력받는다.
3. 배열을 누적해서 `m`으로 나눈 나머지를 출력한다.

C#이라면 parser와 split과 map과 출력 코드를 더 길게 적어야 한다.  
PSCP는 이 반복을 줄인다.

다른 예제도 보자.

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

이건 거의 Union-Find의 의도가 그대로 드러난다.

- `[0..<n]` 으로 `0,1,2,...,n-1` 배열 생성
- `int[n] size;` 로 길이 `n`짜리 0 배열 생성
- `rec` 으로 재귀 허용
- `:=` 로 경로 압축을 자연스럽게 표현

이런 식으로 PSCP는 **실전 알고리즘 코드를 짧고 직접적으로 쓰게 해준다**.

---

## 3. 아주 빠른 요약

먼저 정말 짧게 핵심만 정리하면 이렇다.

### 입력

```txt
int n =
int n, m =
int[n] arr =
(int, int)[m] edges =
```

### 출력

```txt
= expr      // 줄바꿈 없음
+= expr     // 줄바꿈 있음
```

### 변수 선언

```txt
let x = 10      // 추론 + 불변
var sum = 0     // 추론 + 가변
mut int ans = 0 // 명시적 타입 + 가변
```

### range

```txt
1..10
0..<n
0..=n
10..-1..0
```

### collection / generator

```txt
[1, 2, 3]                   // materialized collection
[0..<n -> i do i * i]       // materialized builder
(0..<n -> i do i * i)       // generator
```

### aggregate family

```txt
min a b
max a b c
sum arr
sumBy arr (x => x * x)
minBy points (p => p.x)
chmin ref best cand
```

### 변환

```txt
int "123"
int(true)
bool 0
string 123
```

### 자료구조 설탕

```txt
visited += x     // HashSet.Add
q += x           // Queue.Enqueue
--q              // Queue.Dequeue
~q               // Queue.Peek
pq += (x, prio)  // PriorityQueue enqueue
```

이제부터는 이 각각을 진짜 실전 감각으로 자세히 본다.

---

## 4. PSCP 코드의 기본 모양

## 4.1 들여쓰기가 아니라 중괄호를 쓴다

PSCP는 들여쓰기로 문법을 결정하지 않는다.

```txt
if x < 0 {
    x = -x
}
```

즉 Python/F# 스타일이 아니라, C계열처럼 **중괄호가 흐름을 정한다**.

이게 왜 좋냐면, 문제 풀이 중 급하게 짜다가 들여쓰기 실수로 논리가 뒤틀리는 경우를 줄여준다.

---

## 4.2 줄바꿈으로 문장을 나눈다

보통은 줄바꿈이면 충분하다.

```txt
let a = 3
let b = 5
+= a + b
```

원하면 세미콜론도 쓸 수 있다.

```txt
var x = 0; x += 1; += x
```

즉 세미콜론은 **강요되지 않지만 사용 가능**하다.

---

## 5. 변수와 상수

## 5.1 `let`

가장 자주 쓰는 선언이다.

```txt
let x = 10
let name = "jun"
```

의미:

- 타입 추론
- 불변

문제 풀이에서는 기본적으로 `let`을 많이 쓰는 편이 좋다.

---

## 5.2 `var`

```txt
var sum = 0
sum += 5
```

의미:

- 타입 추론
- 가변

루프 누적값이나 포인터처럼 재할당이 필요한 값에 쓴다.

---

## 5.3 `mut`

```txt
mut int answer = 0
mut string s = ""
```

의미:

- 타입 명시
- 가변

타입을 드러내고 싶은데 재할당도 필요할 때 쓴다.

---

## 5.4 명시적 타입 + 불변

```txt
int n = 3
string text = "hello"
```

이것도 가능하다.

---

## 5.5 상수처럼 쓰이는 값

```txt
let MOD = 1000000007
let INF = 1_000_000_000
let hello = "hello"
```

이런 값들은 source에선 그냥 immutable binding이지만, 트랜스파일러는 가능한 경우 C# `const`로 내릴 수 있다.

즉 사용자 입장에선 그냥 자연스럽게 `let`을 쓰면 된다.

---

## 6. 타입

## 6.1 기본 타입

대표적인 기본 타입:

- `int`
- `long`
- `double`
- `decimal`
- `bool`
- `char`
- `string`

---

## 6.2 튜플

```txt
(int, int)
(long, int, string)
```

값도 자연스럽게 만들 수 있다.

```txt
(1, 2)
(a, b, c)
```

---

## 6.3 배열

```txt
int[]
int[][]
(string, int)[]
```

추가로 declaration 전용 shorthand도 있다.

```txt
int[n] arr
int[n][m] grid
```

---

## 6.4 generic type

PSCP는 generic type을 C#처럼 그대로 쓴다.

```txt
List<int>
Dictionary<int, string>
PriorityQueue<int, int>
```

---

## 6.5 nullable marker `?`

`?` 는 C# nullable surface와 호환된다.

```txt
string?
NodeData?
int?
```

즉 별도 PSCP 전용 의미를 추가하는 게 아니라, **C# nullable 감각으로 이해하면 된다**.

---

## 7. 입력: PSCP의 핵심 장점

PSCP에서 가장 먼저 체감되는 장점은 입력이다.

## 7.1 정수 하나 입력받기

```txt
int n =
```

이건 “정수 하나를 읽어서 `n`에 넣는다”는 뜻이다.

다른 타입도 마찬가지다.

```txt
long x =
string s =
char c =
```

---

## 7.2 여러 값 한 번에 입력받기

```txt
int n, m =
long a, b, c =
```

예를 들어 입력이

```txt
5 7
```

이면

```txt
int n, m =
```

이후 `n = 5`, `m = 7`이 된다.

---

## 7.3 배열 입력

```txt
int[n] arr =
long[m] cost =
```

예:

```txt
int n =
int[n] arr =
```

입력이

```txt
5
1 2 3 4 5
```

이면 `arr = [1,2,3,4,5]`가 된다.

---

## 7.4 2차원 입력

```txt
int[n][m] grid =
char[h][w] board =
```

---

## 7.5 튜플 입력

```txt
(int, int) p =
(int, int)[m] edges =
```

그래프 간선 입력에서 아주 자연스럽다.

---

## 7.6 명시적 `stdin`

축약 입력이 싫거나 더 세밀하게 쓰고 싶다면 `stdin`을 직접 쓰면 된다.

```txt
int n = stdin.int()
string line = stdin.line()
char[] chars = stdin.chars()
string[] words = stdin.words()
char[][] grid = stdin.charGrid(n)
```

### 대표 `stdin` API

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

### 언제 축약 입력을 쓰고, 언제 `stdin`을 쓸까?

- 일반적인 토큰 입력은 축약 입력이 가장 짧다.
- 줄 단위 입력이나 grid helper가 필요하면 `stdin`이 편하다.
- source에서 의도를 분명히 드러내고 싶을 때도 `stdin`이 좋다.

---

## 8. 출력

## 8.1 줄바꿈 없이 출력

```txt
= expr
```

예:

```txt
= x
= (a, b)
= arr
```

---

## 8.2 줄바꿈 포함 출력

```txt
+= expr
```

예:

```txt
+= answer
+= (x, y)
+= arr
```

---

## 8.3 명시적 `stdout`

```txt
stdout.write(x)
stdout.writeln(x)
stdout.flush()
stdout.lines(xs)
stdout.grid(g)
stdout.join(sep, xs)
```

### `stdout.join(sep, xs)`는 어떻게 쓰나?

`sep`는 `string` 또는 `char`를 받는다.

예:

```txt
stdout.join(' ', arr)
stdout.join(", ", arr)
```

기본적으로 줄바꿈은 붙지 않는다.

---

## 9. range: PSCP의 기본 빌딩 블록

PSCP는 range를 정말 많이 쓴다.

## 9.1 기본 range

```txt
1..10
0..<n
0..=n
10..-1..0
```

의미:

- `1..10` : 1부터 10까지 포함
- `0..<n` : 0부터 `n-1`까지
- `0..=n` : 0부터 `n`까지 포함
- `10..-1..0` : 10, 9, 8, ..., 0

중요한 점:

- **역방향은 자동 추론하지 않는다**
- 내림차순은 반드시 explicit step을 적는다

즉:

```txt
10..0
```

같은 걸 자동 역방향 range로 기대하면 안 된다.

---

## 9.2 range를 어디에 쓰는가

- `for`
- `->`
- builder
- generator
- aggregate
- 배열 생성

예:

```txt
for i in 0..<n {
    += i
}

let parent = [0..<n]
let total = sum (0..<n -> i do i)
```

---

## 10. `[]` 와 `()` : 꼭 익혀야 하는 핵심 구분

이건 PSCP에서 정말 중요하다.

## 10.1 `[]` 는 materialized collection

```txt
[1, 2, 3]
[0..<n]
[0..<n -> i do i * i]
```

의미:

> “실제로 만들어진 컬렉션”

---

## 10.2 `()` 는 generator

```txt
(0..<n -> i do i * i)
```

의미:

> “lazy iterable / generator”

즉, 당장 배열을 만드는 게 아니다.

---

## 10.3 감각적으로 이해하기

- `[]` : 담아둔다
- `()` : 흘려보낸다

예:

```txt
let xs = [0..<n -> i do score(i)]
let total = sum (0..<n -> i do score(i))
```

첫 번째는 실제 collection을 만든다.  
두 번째는 aggregate가 바로 소비하는 generator다.

---

## 10.4 `[]` 안에서 range는 자동 전개

```txt
let a = [0..<5]
```

이건 `[0,1,2,3,4]`를 의미한다.

---

## 10.5 spread `..`

```txt
let a = [3, 4, 5]
let b = [1, 2, ..a, 6, 7]
```

이때 `b`는 `[1,2,3,4,5,6,7]` 이 된다.

중요한 점:

- range는 spread 없이 auto-expand
- 일반 iterable은 `..` 필요

---

## 11. builder: PSCP의 꽃

## 11.1 한 줄 builder

```txt
[0..<n -> i do i * i]
[arr -> x do f(x)]
```

예:

```txt
let squares = [1..10 -> x do x * x]
```

---

## 11.2 블록 builder

```txt
[0..<n -> i {
    let x = i * i
    x + 1
}]
```

마지막 식이 들어간다.

---

## 11.3 generator builder

```txt
(0..<n -> i do i * i)
```

aggregate에 바로 넣을 때 좋다.

예:

```txt
let total = sum (0..<n -> i do i * i)
```

---

## 12. 반복문과 흐름 제어

## 12.1 `if`

### 블록 형태

```txt
if cond {
    ...
} else {
    ...
}
```

### 한 문장 형태

```txt
if x < 0 then x = -x
if bad then continue
if ok then return ans else return -1
```

`then`, `else`는 다음 한 문장에만 붙는다.

---

## 12.2 `while`

```txt
while cond {
    ...
}

while cond do x += 1
```

---

## 12.3 `for ... in ...`

```txt
for i in 0..<n {
    ...
}

for i in 0..<n do sum += a[i]
```

---

## 12.4 fast iteration `->`

```txt
arr -> x {
    += x
}

arr -> i, x do ans[i] = x * x
```

### 한 변수 버전

```txt
xs -> x {
    ...
}
```

### 인덱스 포함 버전

```txt
xs -> i, x {
    ...
}
```

---

## 12.5 `break`, `continue`, `return`

```txt
break
continue
return x
return
```

PSCP는 함수형 표현력도 있지만, 경쟁 프로그래밍에서 중요한 imperative 흐름도 적극적으로 받아들인다.

---

## 13. 함수와 암묵적 반환

## 13.1 일반 함수

```txt
int add(int a, int b) {
    a + b
}
```

마지막 식이 반환값이 된다.

---

## 13.2 재귀 함수는 `rec`

```txt
rec int fact(int n) {
    if n <= 1 then 1 else n * fact(n - 1)
}
```

---

## 13.3 generic 함수

```txt
T identity<T>(T x) {
    x
}
```

C# generic 감각 그대로 이해하면 된다.

---

## 13.4 bare invocation은 자동 반환이 아니다

```txt
bool add(HashSet<int> set, int x) {
    set.Add(x)
}
```

이건 자동 반환으로 보지 않는다.

반환하려면:

```txt
return set.Add(x)
```

또는

```txt
let ok = set.Add(x)
ok
```

처럼 명시해야 한다.

---

## 14. 람다

```txt
x => x + 1
(acc, x) => acc + x
(acc, x) => {
    let y = acc + x
    y % mod
}
```

discard parameter도 가능하다.

```txt
_ => 1
(a, _, c) => a + c
```

---

## 15. 파이프 연산자 `|>` 와 `<|`

이건 기존 입문서에서 빠졌던 핵심 중 하나다.

PSCP는 파이프 연산자를 지원한다.

## 15.1 `|>`

왼쪽 값을 오른쪽 함수로 넘긴다.

```txt
value |> f
```

대략 다음 감각이다.

```txt
f(value)
```

예:

```txt
arr |> sum
arr |> (xs => xs.sort())
```

---

## 15.2 `<|`

오른쪽 값을 왼쪽 함수에 넣는 방향이다.

```txt
f <| x
```

대략:

```txt
f(x)
```

복잡한 괄호를 줄일 때 유용하다.

---

## 15.3 언제 파이프를 쓰면 좋은가

### 좋은 경우

- 변환 단계를 왼쪽에서 오른쪽으로 읽고 싶을 때
- aggregate 전에 가공 체인을 보여주고 싶을 때

예:

```txt
let ans = arr
    |> (xs => xs.sort())
    |> (xs => xs.distinct())
    |> max
```

### 굳이 안 써도 되는 경우

- 단순 호출이 더 짧을 때
- 공백 호출이 이미 충분히 읽기 쉬울 때

예:

```txt
sum arr
min a b
```

파이프는 강제 도구가 아니라 **읽기 흐름을 정리하는 선택지**로 생각하면 된다.

---

## 16. 문자열과 보간 문자열

이것도 기존 입문서에서 빠졌던 핵심이다.

## 16.1 일반 문자열

```txt
"hello"
```

## 16.2 보간 문자열

```txt
$"answer = {ans}"
$"{min a b} {sum (0..<n -> i do a[i])}"
```

PSCP expression이 중괄호 안에 들어간다.

예:

```txt
int n =
+= $"n = {n}"
```

---

## 16.3 format specifier도 가능하다

```txt
$"{value:F2}"
```

즉 C# 보간 문자열 감각으로 이해해도 된다.

---

## 16.4 언제 유용한가

문제 풀이에선 디버그 출력, 설명용 출력, 복합 상태 출력에 유용하다.

예:

```txt
+= $"i={i} dist={dist[i]}"
```

최종 제출 코드에선 자주 안 쓸 수 있지만, 실전 디버깅에서는 매우 편하다.

---

## 17. 튜플

## 17.1 튜플 만들기

```txt
(1, 2)
(a, b, c)
```

## 17.2 튜플 projection

```txt
p.1
p.2
p.3
```

PSCP는 1-based projection을 쓴다.

예:

```txt
(int, int, int) p = (6, 1, 4)
+= p.2
```

---

## 17.3 튜플 대입

```txt
(a, b) = (b, a)
(arr[i], arr[j]) = (arr[j], arr[i])
```

swap 코드가 매우 자연스러워진다.

---

## 18. conversion keyword: 타입 키워드를 함수처럼

```txt
int "123"
long "1000"
int(true)
bool 0
bool "hello"
string 123
```

이건 parse / truthiness / cast-like conversion을 짧게 쓰기 위한 기능이다.

## 18.1 문자열 파싱

```txt
let a = int "123"
let b = long "10000000000"
let c = double "3.14"
```

## 18.2 bool ↔ 숫자

```txt
let x = int(true)
let y = int(false)
let a = bool 0
let b = bool 5
```

## 18.3 string truthiness

```txt
let a = bool ""
let b = bool "hello"
```

## 18.4 왜 유용한가

예:

```txt
next[sorted[i]] = next[sorted[i - 1]] + int(compare(sorted[i - 1], sorted[i]) != 0)
```

이런 코드는 suffix array, compression, ranking 문제에서 꽤 자주 나온다.

---

## 19. `:=` : 값이 되는 대입

```txt
lhs := rhs
```

의미:

1. 대입한다.
2. 그 값을 결과로 돌려준다.

## 19.1 대표 사용처: 경로 압축

```txt
rec int find(int x) {
    if x == parent[x] then x
    else parent[x] := find(parent[x])
}
```

이건 PSCP에서 정말 아름다운 패턴이다.

---

## 19.2 일반 `=` 와 차이

```txt
x = y
```

은 statement 느낌이다.

```txt
x := y
```

는 “이 대입 결과도 값으로 쓰겠다”는 의도를 명시한다.

---

## 20. aggregate family: PSCP에서 가장 중요한 도구 중 하나

aggregate family는 그냥 helper 목록이 아니라, **PSCP 스타일 그 자체**라고 봐도 된다.

## 20.1 `min`, `max`

```txt
min a b
max a b c
min arr
max (0..<n -> i do a[i])
```

즉:

- 두 값 비교
- 여러 값 비교
- iterable 전체 비교

를 한 family로 묶는다.

---

## 20.2 `sum`

```txt
sum arr
sum (0..<n -> i do score(i))
```

중간 배열이 필요 없으면 generator와 같이 쓰는 게 자연스럽다.

---

## 20.3 `sumBy`

```txt
sumBy arr (x => x * x)
sumBy points (p => p.2)
```

“각 원소를 변환한 뒤 합산”이다.

---

## 20.4 `minBy`, `maxBy`

```txt
minBy points (p => p.x)
maxBy items (x => score(x))
```

“원소 자체를 반환하되, 비교는 key로 한다”는 뜻이다.

---

## 20.5 `chmin`, `chmax`

```txt
_ = chmin(ref best, cand)
_ = chmax(ref ans, value)
```

의미:

- 더 나은 값이면 갱신
- 갱신 여부 bool 반환

실전에서 DP, Dijkstra, 최솟값 유지에 매우 유용하다.

---

## 21. math helper

PSCP는 자주 쓰는 수학 helper를 intrinsic family로 제공한다.

핵심:

- `abs`
- `sqrt`

추가:

- `clamp`
- `gcd`
- `lcm`
- `floor`
- `ceil`
- `round`
- `pow`
- `popcount`
- `bitLength`

## 21.1 `abs`

```txt
let d = abs(a - b)
```

## 21.2 `sqrt`

```txt
let dist = sqrt(dx * dx + dy * dy)
```

## 21.3 `gcd`, `lcm`

```txt
let g = gcd(a, b)
let l = lcm(a, b)
```

## 21.4 `popcount`, `bitLength`

```txt
let bits = popcount(mask)
let len = bitLength(x)
```

비트 문제에서 매우 자주 쓴다.

---

## 22. collection helper: 반드시 예제로 익혀야 하는 부분

기존 입문서의 가장 큰 약점 중 하나가 여기였지.  
목록만 나열하면 아무 감이 안 온다. 여기서는 실제로 다 예제를 붙인다.

---

## 22.1 `map`

각 원소를 변환한다.

```txt
let squares = arr.map(x => x * x)
```

예:

```txt
int n =
int[n] arr =

let doubled = arr.map(x => x * 2)
+= doubled
```

---

## 22.2 `filter`

조건을 만족하는 원소만 남긴다.

```txt
let evens = arr.filter(x => x % 2 == 0)
```

예:

```txt
int n =
int[n] arr =

let positives = arr.filter(x => x > 0)
+= positives
```

---

## 22.3 `fold`

누적 계산의 기본이다.

```txt
let total = arr.fold(0, (acc, x) => acc + x)
```

예:

```txt
int n, m =
int[n] arr =

let modSum = arr.fold(0, (acc, x) => (acc + x) % m)
+= modSum
```

---

## 22.4 `scan`

중간 누적 상태 전체를 만든다.

```txt
let pref = arr.scan(0, (acc, x) => acc + x)
```

예:

```txt
int n =
int[n] arr =

let prefix = arr.scan(0, (acc, x) => acc + x)
+= prefix
```

`scan`은 prefix sum, prefix min/max 같은 중간 상태 배열을 만들 때 매우 좋다.

---

## 22.5 `mapFold`

상태도 누적하고, 결과 배열도 동시에 만든다.

예:

```txt
let mapped, finalState = arr.mapFold(0, (acc, x) => {
    let next = acc + x
    (next, next)
})
```

prefix sum을 만들면서 마지막 합도 같이 얻는 느낌이다.

조금 더 직관적인 예:

```txt
int n =
int[n] arr =

let pref, total = arr.mapFold(0, (acc, x) => {
    let next = acc + x
    (next, next)
})

+= pref
+= total
```

---

## 22.6 `any`

하나라도 만족하면 true.

```txt
let hasNegative = arr.any(x => x < 0)
```

예:

```txt
int n =
int[n] arr =

if arr.any(x => x == 0) then
    += "YES"
else
    += "NO"
```

---

## 22.7 `all`

모두 만족하면 true.

```txt
let allPositive = arr.all(x => x > 0)
```

예:

```txt
int n =
int[n] arr =

+= arr.all(x => x % 2 == 0)
```

---

## 22.8 `count`

조건을 만족하는 개수를 센다.

```txt
let cnt = arr.count(x => x % 2 == 0)
```

예:

```txt
int n =
int[n] arr =

+= arr.count(x => x > 0)
```

---

## 22.9 `find`

첫 번째로 만족하는 원소를 찾는다.

```txt
let x = arr.find(v => v > 10)
```

주의: 못 찾았을 때 정책은 구현에 따라 주의가 필요하다.  
즉 value type에선 특별히 조심하는 습관이 좋다.

---

## 22.10 `findIndex`, `findLastIndex`

```txt
let i = arr.findIndex(x => x == 0)
let j = arr.findLastIndex(x => x < 0)
```

예:

```txt
string text = stdin.line()
char[] chars = text.ToCharArray()

let firstA = chars.findIndex(c => c == 'a')
let lastA = chars.findLastIndex(c => c == 'a')
+= (firstA, lastA)
```

---

## 22.11 `sort`

기본 정렬이다.

```txt
let sorted = arr.sort()
```

예:

```txt
int n =
int[n] arr =

let sorted = arr.sort()
+= sorted
```

---

## 22.12 `sortBy`

key selector로 정렬한다.

```txt
let sorted = points.sortBy(p => p.2)
```

예:

```txt
int n =
(int, int)[n] points =

let sorted = points.sortBy(p => p.2)
+= sorted
```

즉 `(x, y)` 쌍들을 `y` 기준으로 정렬하는 느낌이다.

---

## 22.13 `sortWith`

명시적 comparer 또는 비교 함수를 쓴다.

```txt
let sorted = arr.sortWith(int.desc)
```

또는 사용자 비교 함수를 줄 수 있다.

```txt
let sorted = jobs.sortWith((a, b) => {
    if a.2 == b.2 then a.1 <=> b.1
    else a.2 <=> b.2
})
```

이건 정말 중요하다.  
복잡한 정렬 조건을 PSCP 스타일로 압축할 수 있다.

---

## 22.14 `distinct`

중복 제거.

```txt
let uniq = arr.distinct()
```

좌표압축에서 아주 자주 쓴다.

예:

```txt
let sorted = xs.sort().distinct()
```

---

## 22.15 `reverse`

```txt
let rev = arr.reverse()
```

예:

```txt
int n =
int[n] arr =
+= arr.reverse()
```

---

## 22.16 `copy`

```txt
let b = a.copy()
```

원본을 보존하면서 별도 배열/컬렉션을 다루고 싶을 때 좋다.

---

## 22.17 `groupCount`

값별 개수를 센다.

```txt
let cnt = arr.groupCount()
```

예:

```txt
int n =
int[n] arr =

let freq = arr.groupCount()
+= freq[3]
```

즉 `3`이 몇 번 나왔는지 바로 볼 수 있다.

---

## 22.18 `freq`

`groupCount`와 비슷하게 빈도 map을 만든다.

```txt
let freq = arr.freq()
```

사용 감각은 거의 같다.

---

## 22.19 `index`

좌표압축용으로 정말 유용하다.

```txt
let sorted = xs.sort().distinct()
let idx = sorted.index()
let comp = [xs -> x do idx[x]]
```

이 패턴은 꼭 기억할 만하다.

---

## 23. comparator sugar

## 23.1 `T.asc`, `T.desc`

```txt
int.asc
int.desc
string.desc
```

예:

```txt
let sorted = arr.sortWith(int.desc)
```

기본 정렬 기준이 있는 타입에 대해 ascending / descending comparer를 빠르게 얻는다.

---

## 23.2 `operator<=>(other)`

타입 안에서 기본 ordering을 짧게 정의하는 방법이다.

```txt
record struct Job(int Id, int Arrival, long Time) {
    operator<=>(other) =>
        if Arrival == other.Arrival then Time <=> other.Time
        else Arrival <=> other.Arrival
}
```

이렇게 정의하면 `sort()`, `min/max`, `T.asc/T.desc` 같은 ordered operation과 연결될 수 있다.

---

## 24. `new[n]`, `new![n]`

## 24.1 `new[n]`

```txt
int[] arr = new[n]
NodeInfo[] nodes = new[n]
```

target type의 element type을 보고 배열을 만든다.

---

## 24.2 `new![n]`

```txt
List<int>[] graph = new![n]
Queue<int>[] buckets = new![m]
```

이건 다음을 한 번에 한다.

1. 배열 생성
2. 각 칸 `new()`

그래프 adjacency list에서 매우 강하다.

예:

```txt
int n, m =
List<int>[] graph = new![n]

for i in 0..<m {
    int a, b =
    graph[a].Add(b)
    graph[b].Add(a)
}
```

---

## 25. known data structure operator rewrites

## 25.1 `HashSet<T>`

```txt
visited += x
visited -= x
```

의미:

- `Add`
- `Remove`

중요한 점:

**반환값도 그대로 유지한다.**

예:

```txt
if not (visited += me) then continue
```

이게 진짜 강력하다.

---

## 25.2 `Stack<T>`

```txt
s += x
~s
--s
```

의미:

- push
- peek
- pop

예:

```txt
Stack<int> s;
s += 10
s += 20
let top = ~s
let x = --s
```

---

## 25.3 `Queue<T>`

```txt
q += x
~q
--q
```

의미:

- enqueue
- peek
- dequeue

---

## 25.4 `PriorityQueue<TElement, TPriority>`

```txt
pq += (item, priority)
~pq
--pq
```

`~pq`, `--pq` 는 item만 반환한다.

priority까지 필요하면 explicit API를 쓴다.

```txt
pq.TryPeek(out item, out prio)
pq.TryDequeue(out item, out prio)
```

---

## 26. slicing과 index-from-end

```txt
text[^1]
text[1..^2]
arr[..n]
arr[1..]
arr[..]
```

문자열 문제, 부분 배열, prefix/suffix 처리에 매우 편하다.

예:

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

## 27. class / struct / record

PSCP는 C# 스타일 type declaration을 잘 받아들인다.

## 27.1 class

```txt
class Node {
    int value
}
```

## 27.2 struct

```txt
struct Edge {
    int to
    int w
}
```

## 27.3 record / record struct

```txt
record R(int X, int Y)
record struct Job(int Id, int Arrival, long Time)
```

---

## 27.4 generic class

```txt
class Box<T> {
    T value
}
```

PSCP는 generic surface를 C#처럼 그대로 이해한다.

---

## 28. 실전 스타일 팁

## 28.1 기본은 `let`

재할당이 정말 필요한 경우만 `var`, `mut`를 쓰는 편이 좋다.

## 28.2 `[]` 와 `()`를 항상 의식하라

- 저장/재사용 -> `[]`
- 바로 소비 -> `()`

## 28.3 aggregate family를 적극적으로 써라

직접 루프보다 더 빨리 의도가 보이는 경우가 많다.

## 28.4 `then`, `do`는 짧을 때만

길어지면 바로 `{}` 로 바꾸는 것이 좋다.

## 28.5 known DS rewrite는 “정말 읽기 좋을 때만”

강력하지만, 지나치게 남용하면 낯설어질 수 있다.

---

## 29. 큰 예제 1: BFS

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

이 예제에서 보이는 것:

- `stdin.charGrid`
- known collection auto-construction
- `Queue +=`, `--queue`
- `HashSet +=` bool 반환
- `then continue`
- range loop

---

## 30. 큰 예제 2: Union-Find

```txt
int n =
int[] parent = [0..<n]
int[n] size;

for i in 0..<n do size[i] = 1

rec int find(int x) {
    if x == parent[x] then x
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

이 예제에서 보이는 것:

- `[0..<n]`
- `int[n] arr;`
- `rec`
- 암묵적 반환
- `:=`
- tuple swap

---

## 31. 큰 예제 3: 좌표압축

```txt
int n =
int[n] xs =

let sorted = xs.sort().distinct()
let idx = sorted.index()
let comp = [xs -> x do idx[x]]

+= comp
```

이 예제에서 보이는 것:

- `sort`
- `distinct`
- `index`
- materialized builder

이건 PSCP helper surface의 대표적인 실전 패턴이다.

---

## 32. 큰 예제 4: generator + aggregate

```txt
int n =
int[n] a =

let best = min (0..<n -> i do a[i] - i)
let total = sum (0..<n -> i {
    let x = a[i] * a[i]
    x + 1
})

let ok = int(best < total)
+= (best, total, ok)
```

이 예제에서 보이는 것:

- generator
- `min`, `sum`
- block-bodied generator
- `int(boolExpr)`
- tuple 출력

---

## 33. 큰 예제 5: suffix-array 스타일 코드

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

이 예제에서 보이는 것:

- 문자열 처리
- `sortWith`
- range builder
- conversion keyword
- `int(bool)`
- 재귀 + tuple 반환

---

## 34. 자주 하는 실수

## 34.1 `[]`와 `()`를 헷갈리기

```txt
sum [0..<n -> i do score(i)]
```

이건 동작할 수 있지만, 굳이 intermediate collection을 만들 이유가 없다면 보통

```txt
sum (0..<n -> i do score(i))
```

가 더 자연스럽다.

---

## 34.2 역방향 range를 자동으로 기대하기

```txt
10..0
```

를 자동 descending으로 기대하면 안 된다.

명시적으로 써야 한다.

```txt
10..-1..0
```

---

## 34.3 bare invocation이 자동 반환된다고 생각하기

마지막 줄이 호출식이면 자동 반환이 아니다.

---

## 34.4 helper를 무조건 많이 쓰는 것만이 능사는 아니다

`sum`, `min`, `sort().distinct()` 처럼 의도가 더 빨리 보이는 경우는 좋다.  
하지만 루프가 더 읽기 쉬우면 그냥 루프를 쓰는 것도 좋다.

PSCP는 “모든 걸 함수형으로만 쓰라”는 언어가 아니다.

---

## 35. 마지막 요약

PSCP를 잘 쓰는 핵심은 다음이다.

1. **입출력 shorthand를 적극적으로 사용한다.**
2. **range를 기본 단위로 생각한다.**
3. **`[]` 와 `()`의 차이를 확실히 이해한다.**
4. **aggregate family를 익숙하게 만든다.**
5. **helper 메서드를 단순 열거가 아니라 패턴으로 익힌다.**
6. **.NET 자료구조를 그대로 쓰되, 필요한 곳에 PSCP 설탕을 얹는다.**
7. **짧고 읽기 좋은 코드가 가장 좋은 PSCP 코드다.**

짧게 말하면,

> C#의 실용성과 .NET의 강한 자료구조를 유지하면서,  
> 문제 풀이에 필요한 표현력과 문법설탕을 적극적으로 가져온 언어

가 PSCP다.

이 문서를 끝까지 읽었다면, 이제는 단순히 “문법을 안다”가 아니라,

> “PSCP로 실제 문제를 어떻게 써내려갈지 감이 온다”

상태여야 한다.

다음 단계는 직접 몇 문제를 골라 C++/C#/F# 코드 대신 **PSCP로 다시 써보는 것**이다.

