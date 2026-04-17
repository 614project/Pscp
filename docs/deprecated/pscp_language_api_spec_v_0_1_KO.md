# PS/CP 언어 API 명세 v0.1

## 1. 범위

이 문서는 PS/CP 언어 런타임 및 트랜스파일 환경에 내재된 표준 API 표면(surface)을 정의한다.

이 문서는 .NET Base Class Library를 재정의하지 않는다. `System` 및 일반 C#에 포함된 모든 타입, 멤버, 네임스페이스, 관례는 별도 명시가 없는 한 그대로 사용 가능하다. 이 명세는 해당 언어에 고유한 추가 API, 보조 표면, 의미론적 매핑만을 정의한다.

달리 명시되지 않는 한 이 문서는 규범적(normative)이다.

---

## 2. 비목표(Non-Goals)

다음 사항은 이 문서의 범위에 명시적으로 포함되지 않는다:

1. 일반 .NET API 이름의 변경 또는 표준화,
2. 사용자가 작성한 .NET 호출을 다른 표기 방식으로 낮추기(lowering),
3. .NET 타입 이름을 언어별 별칭으로 대체,
4. 타입과 값 사이의 대소문자 구분 제거,
5. 이 API 표면의 의미론적 요건을 초과하는 최적화 동작.

따라서 아래와 같은 소스 코드는 원칙적으로 유효하며 변경되지 않는다:

```txt
Queue<int> queue = new()
HashSet<int> set = new()
Array.Sort(arr)
Math.Max(a, b)
```

트랜스파일러는 프로그램이 이 문서에 정의된 언어 내재 API를 명시적으로 사용하지 않는 한, 기존 .NET API 사용을 그대로 보존해야 한다.

---

## 3. 설계 원칙

### 3.1 .NET 친숙성 보존

일반 .NET API는 래핑, 이름 변경, 은닉하지 않는다.

### 3.2 문제 풀이에 필요한 표면만 추가

언어 정의 API는 문제 풀이의 편의성을 실질적으로 개선하는 경우에만 존재한다.

### 3.3 표면 수준의 내재 함수 선호

대부분의 언어별 API는, 트랜스파일러가 최종적으로 이를 특수하게 처리하더라도, 일반 메서드·함수·보조 객체처럼 명세한다.

### 3.4 최적화 이전에 의미론 안정화

여러 가지 낮추기(lowering) 방식이 가능한 경우, 명세는 API의 의미론적 의미를 고정하되 특정 최적화 구현 방식을 요구하지 않는다.

---

## 4. 런타임 표면 범주

언어 정의 API는 다음 범주로 구분한다:

1. `stdin` 입력 보조 함수,
2. `stdout` 출력 보조 함수,
3. 컬렉션 보조 API,
4. 배열 생성 및 초기화 보조 함수,
5. 집계(aggregation) 보조 함수,
6. 문제 풀이 스타일의 공통 작업을 위한 편의 함수.

---

## 5. 가용성 모델

### 5.1 일반 .NET API

모든 일반 .NET API는 원래 이름으로 그대로 사용 가능하다.

### 5.2 언어 정의 내재 함수

다음 이름은 언어 런타임 표면에 의해 예약된다:

- `stdin`
- `stdout`
- `Array.zero`
- 이 문서에 정의된 표준 컬렉션 보조 멤버

### 5.3 사용자에 의한 이름 가리기(Shadowing)

구현체는 예약된 내재 이름을 가리는(shadow) 사용자 선언을 거부하거나 경고를 발생시킬 수 있다.

---

## 6. 입력 API

## 6.1 개요

`stdin`은 표준 입력 내재 객체다. 문제 풀이에 특화된 토큰 및 줄 단위 읽기 기능을 제공한다.

`stdin`은 개념적으로 별도의 임포트 없이 사용 가능하다.

구현체는 `stdin`을 보조 클래스, 정적 싱글턴, 모듈 인스턴스, 또는 그에 상응하는 백엔드 구조물로 낮출 수 있다.

---

## 6.2 토큰 리더

다음 토큰 기반 리더가 정의된다:

```txt
stdin.int()
stdin.long()
stdin.str()
stdin.char()
stdin.double()
stdin.decimal()
stdin.bool()
```

### 6.2.1 의미론

- `stdin.int()` — 토큰 하나를 읽어 `int`로 파싱한다.
- `stdin.long()` — 토큰 하나를 읽어 `long`으로 파싱한다.
- `stdin.str()` — 토큰 하나를 `string`으로 읽는다.
- `stdin.char()` — 토큰 하나를 읽어 `char`로 반환한다.
- `stdin.double()` — 토큰 하나를 읽어 `double`로 파싱한다.
- `stdin.decimal()` — 토큰 하나를 읽어 `decimal`로 파싱한다.
- `stdin.bool()` — 토큰 하나를 읽어 `bool`로 파싱한다.

### 6.2.2 실패 처리

파싱 실패 동작은 구현체 정의이나, 규격을 따르는 구현체는 무관한 값을 조용히 반환해서는 안 된다.

---

## 6.3 줄 리더

다음 줄 기반 리더가 정의된다:

```txt
stdin.line()
stdin.lines(n)
stdin.words()
stdin.chars()
```

### 6.3.1 `stdin.line()`

입력 한 줄 전체를 `string`으로 읽는다.

### 6.3.2 `stdin.lines(n)`

정확히 `n`줄을 읽어 `string[]`으로 반환한다.

### 6.3.3 `stdin.words()`

한 줄 전체를 읽어 구현체의 문제 풀이 토큰화 정책에 따라 `string[]`으로 분할한다.

### 6.3.4 `stdin.chars()`

한 줄 전체를 읽어 각 문자를 `char[]`로 반환한다.

---

## 6.4 형상 인식 리더(Shaped Readers)

다음 형상 인식 리더가 정의된다:

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

토큰 입력에서 `T` 타입 값 `n`개를 읽어 `T[]`로 반환한다.

### 6.4.2 `stdin.list<T>(n)`

`T` 타입 값 `n`개를 읽어 `List<T>`로 반환한다.

### 6.4.3 `stdin.linkedList<T>(n)`

`T` 타입 값 `n`개를 읽어 `LinkedList<T>`로 반환한다.

### 6.4.4 튜플 리더

```txt
stdin.tuple2<T1, T2>()
stdin.tuple3<T1, T2, T3>()
```

필요한 수만큼 토큰을 읽어 튜플로 반환한다.

### 6.4.5 튜플 시퀀스 리더

```txt
stdin.tuples2<T1, T2>(n)
stdin.tuples3<T1, T2, T3>(n)
```

토큰 입력에서 튜플 `n`개를 읽어 튜플 배열로 반환한다.

### 6.4.6 숫자형 그리드 리더

```txt
stdin.gridInt(n, m)
stdin.gridLong(n, m)
```

`n * m`개의 토큰을 읽어, 구현 모델에 따라 직사각형 또는 가변 길이 정수 그리드로 반환한다. 관찰 가능한 인덱싱 의미론은 보존되어야 한다.

### 6.4.7 문자 그리드 리더

```txt
stdin.charGrid(n)
```

`n`줄을 읽어 각 줄을 문자 배열로 변환한 `char[][]`를 반환한다.

### 6.4.8 단어 그리드 리더

```txt
stdin.wordGrid(n)
```

`n`줄을 읽어 각 줄을 `string[]`으로 분할한 `string[][]`를 반환한다.

---

## 6.5 선언 기반 입력 단축 표기 매핑

언어 문법은 다음과 같은 선언 기반 입력 단축 표기를 허용한다:

```txt
int n =
int n, m =
int[n] arr =
(int, int)[m] edges =
```

이 문법은 의미론적으로 `stdin` API와 동일하다.

### 6.5.1 스칼라 매핑

```txt
int n =
```

는 의미론적으로 다음과 동일하다:

```txt
int n = stdin.int()
```

### 6.5.2 다중 스칼라 매핑

```txt
int n, m =
```

는 의미론적으로 다음과 동일하다:

```txt
int n = stdin.int()
int m = stdin.int()
```

### 6.5.3 배열 매핑

```txt
int[n] arr =
```

는 의미론적으로 다음과 동일하다:

```txt
int[] arr = stdin.array<int>(n)
```

### 6.5.4 튜플 배열 매핑

```txt
(int, int)[m] edges =
```

는 의미론적으로 다음과 동일하다:

```txt
(int, int)[] edges = stdin.tuples2<int, int>(m)
```

트랜스파일러는 의미론이 보존되는 한, 생성된 C# 코드에 명시적인 `stdin.*` 호출을 만들지 않고 선언 단축 표기를 직접 낮출 수 있다.

---

## 7. 출력 API

## 7.1 개요

`stdout`은 표준 출력 내재 객체다. 문제 풀이에 특화된 렌더링 보조 함수를 제공한다.

구현체는 `stdout`을 보조 클래스, 정적 싱글턴, 버퍼드 라이터, 또는 그에 상응하는 백엔드 구조물로 낮출 수 있다.

---

## 7.2 기본 출력 보조 함수

```txt
stdout.write(x)
stdout.writeln(x)
stdout.flush()
```

### 7.2.1 `stdout.write(x)`

`x`의 렌더링된 표현을 줄바꿈 없이 출력한다.

### 7.2.2 `stdout.writeln(x)`

`x`의 렌더링된 표현을 줄바꿈과 함께 출력한다.

### 7.2.3 `stdout.flush()`

버퍼에 쌓인 출력을 강제로 내보낸다.

---

## 7.3 구조화 출력 보조 함수

```txt
stdout.lines(xs)
stdout.grid(g)
stdout.join(sep, xs)
```

### 7.3.1 `stdout.lines(xs)`

`xs`의 각 원소를 한 줄씩 출력한다.

### 7.3.2 `stdout.grid(g)`

런타임의 그리드 형식화 정책에 따라 2차원 구조를 줄 단위로 출력한다.

### 7.3.3 `stdout.join(sep, xs)`

`xs`의 원소들을 `sep`으로 연결하여 출력한다. 구현체 문서에 명시되지 않는 한 줄바꿈을 추가하지 않는다.

---

## 7.4 구문 기반 출력 매핑

언어 문법은 다음과 같은 출력 단축 표기를 허용한다:

```txt
= expr
+= expr
```

### 7.4.1 쓰기 매핑

```txt
= expr
```

는 의미론적으로 다음과 동일하다:

```txt
stdout.write(expr)
```

### 7.4.2 줄 쓰기 매핑

```txt
+= expr
```

는 의미론적으로 다음과 동일하다:

```txt
stdout.writeln(expr)
```

트랜스파일러는 의미론이 보존되는 한, 생성된 C# 코드에 명시적인 `stdout` 메서드 호출을 만들지 않고 이 형태를 직접 낮출 수 있다.

---

## 7.5 기본 렌더링 계약

언어 런타임은 다음에 대한 기본 렌더링을 제공해야 한다:

1. 스칼라 값,
2. 튜플,
3. 1차원 컬렉션.

### 7.5.1 스칼라

스칼라는 일반적인 문자열 변환으로 렌더링된다.

### 7.5.2 튜플

튜플 원소는 순서대로 렌더링되며 단일 공백으로 결합된다.

### 7.5.3 1차원 컬렉션

원소는 순서대로 렌더링되며 단일 공백으로 결합된다.

### 7.5.4 중첩 컬렉션

중첩 컬렉션의 기본 렌더링은 요구 사항이 아니며 거부될 수 있다. 구조화된 다중 줄 출력에는 `stdout.grid` 또는 그에 상응하는 보조 함수를 사용해야 한다.

---

## 8. 배열 생성 및 초기화 API

## 8.1 `Array.zero`

표준 배열 0 초기화 보조 함수는 다음과 같다:

```txt
Array.zero(n)
```

### 8.1.1 의미론

컨텍스트에서 추론된 원소 타입의 기본값으로 초기화된 길이 `n`의 1차원 배열을 반환한다.

예시:

```txt
int[] a = Array.zero(n)
long[] b = Array.zero(m)
```

### 8.1.2 선언 동치

배열과 함께 사용할 경우 다음 선언 형식은 의미론적으로 동치이다:

```txt
int[n] a;
```

동치 의미:

```txt
int[] a = Array.zero(n)
```

### 8.1.3 다차원 크기 지정 선언

```txt
int[n][m] dp;
```

는 요청된 중첩 배열 구조의 할당 및 기본값 초기화와 의미론적으로 동치이다.

정확한 백엔드 표현은 구현체 정의이다.

---

## 8.2 컬렉션 표현식 생성

컬렉션 표현식은 언어 문법이지만, 결과 타입은 컨텍스트에 따라 다르며 보조 생성이 필요할 수 있다.

### 8.2.1 예시

```txt
let a = [1, 2, 3]
int[] b = [1, 2, 3]
List<int> c = [1, 2, 3]
LinkedList<int> d = [1, 2, 3]
```

트랜스파일러는 다음을 보존하는 방식으로 대상 컬렉션을 생성해야 한다:

1. 원소 순서,
2. 원소 값,
3. 대상 컬렉션 타입,
4. 자동 범위 확장 및 명시적 스프레드 동작.

### 8.2.2 컬렉션 표현식 내 범위 확장

컬렉션 표현식에서 범위 원소는 자동으로 확장된다.

```txt
[0..<5]
[1, 2, 0..<5]
```

### 8.2.3 스프레드 원소

```txt
let a = [3, 4, 5]
let b = [1, 2, ..a, 6, 7]
```

스프레드는 반복 순서를 보존하며 이터러블을 주변 컬렉션 안으로 펼친다.

---

## 9. 컬렉션 보조 API

## 9.1 개요

언어는 인스턴스 메서드, 확장 메서드, 모듈 함수, 또는 트랜스파일러 내재 함수 형태로 제공될 수 있는 문제 풀이 특화 컬렉션 보조 표면을 정의한다.

다음 이름은 표준 보조 표면을 위해 예약된다:

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

동등한 낮추기가 제공되는 경우, 모든 구현체가 모든 보조 함수를 실제 런타임 메서드로 노출할 필요는 없다.

---

## 9.2 변환 보조 함수

### 9.2.1 `map`

```txt
xs.map(f)
```

구현체의 컬렉션 정책에 따라 매핑된 값의 시퀀스 또는 컬렉션을 반환한다.

### 9.2.2 `filter`

```txt
xs.filter(pred)
```

`pred`를 만족하는 원소를 반환한다.

### 9.2.3 `distinct`

```txt
xs.distinct()
```

언어 정의 정렬 정책을 보존하면서 중복이 제거된 원소를 반환한다.

### 9.2.4 `reverse`

```txt
xs.reverse()
```

역순으로 된 시퀀스 또는 컬렉션을 반환한다.

### 9.2.5 `copy`

```txt
xs.copy()
```

얕은 복사본을 반환한다.

---

## 9.3 집계 보조 함수

### 9.3.1 `fold`

```txt
xs.fold(seed, f)
```

왼쪽에서 오른쪽으로 폴드(fold)한다.

### 9.3.2 `scan`

```txt
xs.scan(seed, f)
```

폴드의 중간 상태를 반환한다.

### 9.3.3 `mapFold`

```txt
xs.mapFold(seed, f)
```

컬렉션을 순회하면서 상태를 이어가며 매핑된 출력을 생성한다.

### 9.3.4 `sum`

```txt
xs.sum()
```

모든 원소의 합을 계산한다.

### 9.3.5 `sumBy`

```txt
xs.sumBy(f)
```

매핑 후 합산한다.

### 9.3.6 `min`, `max`

```txt
xs.min()
xs.max()
```

최솟값 또는 최댓값 원소를 반환한다.

### 9.3.7 `minBy`, `maxBy`

```txt
xs.minBy(f)
xs.maxBy(f)
```

`f`에서 파생된 키를 비교하여 선택된 원소를 반환한다.

### 9.3.8 `count`

```txt
xs.count(pred)
```

`pred`를 만족하는 원소의 수를 센다.

### 9.3.9 `any`, `all`

```txt
xs.any(pred)
xs.all(pred)
```

불리언 존재(existential) 및 전칭(universal) 검사.

---

## 9.4 탐색 보조 함수

### 9.4.1 `find`

```txt
xs.find(pred)
```

`pred`를 만족하는 첫 번째 원소를 반환한다.

### 9.4.2 `findIndex`

```txt
xs.findIndex(pred)
```

조건을 처음 만족하는 원소의 인덱스를 반환한다.

### 9.4.3 `findLastIndex`

```txt
xs.findLastIndex(pred)
```

조건을 마지막으로 만족하는 원소의 인덱스를 반환한다.

---

## 9.5 정렬 보조 함수

### 9.5.1 `sort`

```txt
xs.sort()
```

기본 순서로 정렬된 컬렉션을 반환한다.

### 9.5.2 `sortBy`

```txt
xs.sortBy(f)
```

키를 기준으로 정렬한다.

### 9.5.3 `sortWith`

```txt
xs.sortWith(cmp)
```

사용자 정의 비교 함수를 사용하여 정렬한다.

비교 함수는 음수, 0, 양수 중 하나를 반환하는 계약을 따라야 한다. 이러한 함수에는 `<=>` 사용이 적합하다.

---

## 9.6 빈도 및 인덱싱 보조 함수

### 9.6.1 `groupCount`

```txt
xs.groupCount()
```

구현체가 선택한 결과 타입에 따라 그룹별 카운트를 반환한다.

### 9.6.2 `index`

```txt
xs.index()
```

문제 풀이 지향 조회 작업을 위해 원소와 인덱스의 연관 관계를 나타내는 구조체를 반환한다.

### 9.6.3 `freq`

```txt
xs.freq()
```

구현체가 선택한 결과 타입에 따라 빈도 정보를 반환한다.

`groupCount`, `index`, `freq`의 정확한 결과 타입은 v0.1에서 구현체 정의이며 구현체 문서에 기술되어야 한다.

---

## 10. 반복 및 집계 표면

## 10.1 빠른 반복 문법

다음 문법은 언어 수준이지만 개념적으로 반복 보조 함수에 대응한다:

```txt
xs -> x {
    ...
}

xs -> x do expr

xs -> i, x {
    ...
}
```

이 문서는 이 구문들이 공개 런타임 메서드에 의해 뒷받침되도록 요구하지 않는다. 직접 낮춰질 수 있다.

---

## 10.2 집계 문법

다음 언어 구문은 의미론적으로 집계 보조 함수에 대응한다:

```txt
min { for i in 0..<n do a[i] - b[i] }
sum { for x in arr do x }
count { for x in arr where x % 2 == 0 do 1 }
```

규격을 따르는 구현체는 이 구문들을 직접 루프, 표준 라이브러리 호출, 또는 보조 API로 낮출 수 있다.

---

## 11. 보조 함수 낮추기 계약

## 11.1 일반 규칙

이 문서가 정의하는 모든 내재 표면에 대해, 트랜스파일러는 생성된 C# 코드에 동일한 보조 함수 이름이 가시적으로 나타나지 않더라도 의미론적 계약을 보존해야 한다.

따라서:

- 보조 함수 호출은 직접 루프로 낮춰질 수 있다,
- 선언 문법 설탕은 보조 함수 호출로 낮춰질 수 있다,
- 보조 함수 호출은 표준 .NET 메서드로 낮춰질 수 있다,
- 보조 함수 호출은 맞춤형 런타임 지원으로 낮춰질 수 있다.

이 문서는 의미론적 동치를 명세하는 것이지, 특정 낮추기 형태를 강제하지 않는다.

---

## 11.2 안정적 의미론 매핑

다음 매핑은 의미론 수준에서 정의된다:

### 11.2.1 입력 단축 표기

```txt
int n =
```

는 표준 입력에서 정수 토큰 하나를 읽는다는 의미이다.

### 11.2.2 출력 단축 표기

```txt
= expr
```

는 `expr`의 렌더링된 값을 줄바꿈 없이 출력한다는 의미이다.

```txt
+= expr
```

는 `expr`의 렌더링된 값을 줄바꿈과 함께 출력한다는 의미이다.

### 11.2.3 크기 지정 배열 선언

```txt
int[n] a;
```

는 길이 `n`의 정수 배열이 할당되고 0으로 초기화된다는 의미이다.

### 11.2.4 컬렉션 빌더

```txt
[0..<n -> i do i * i]
```

는 반복 순서대로 yield된 값들로부터 컬렉션이 구성된다는 의미이다.

### 11.2.5 스프레드

```txt
[1, 2, ..a, 6]
```

는 `a`의 원소들이 반복 순서대로 `2`와 `6` 사이에 삽입된다는 의미이다.

---

## 12. 런타임 명명 정책

### 12.1 .NET 이름 보존

트랜스파일러는 사용자가 일반 .NET 이름을 언어별 대안으로 재작성하도록 요구해서는 안 된다.

보존되는 스타일 예시:

```txt
Queue<int> queue = new()
PriorityQueue<int, int> pq = new()
HashSet<int> set = new()
Array.Sort(arr)
Math.Max(a, b)
```

### 12.2 언어 내재 함수는 소문자 객체 스타일 사용

언어 정의 내재 객체는 관례적으로 소문자 이름을 사용한다:

```txt
stdin
stdout
```

이 구분은 의도적인 것으로, .NET API에 대한 전반적인 이름 변경 정책을 의미하지 않는다.

---

## 13. API 표면 요약

다음 표는 간결한 요약으로서 규범적이다.

### 13.1 입력 내재 함수

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

### 13.2 출력 내재 함수

```txt
stdout.write(x)
stdout.writeln(x)
stdout.flush()
stdout.lines(xs)
stdout.grid(g)
stdout.join(sep, xs)
```

### 13.3 컬렉션 및 배열 보조 함수

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

## 14. 예시

### 14.1 토큰 입력

```txt
int n =
int m =
```

동치 의미:

```txt
int n = stdin.int()
int m = stdin.int()
```

### 14.2 줄 입력

```txt
string text = stdin.line()
char[] s = stdin.chars()
```

### 14.3 배열 입력

```txt
int[n] arr =
```

동치 의미:

```txt
int[] arr = stdin.array<int>(n)
```

### 14.4 기본값 초기화 배열

```txt
int[n] lcp;
```

동치 의미:

```txt
int[] lcp = Array.zero(n)
```

### 14.5 출력

```txt
= ans
+= arr
stdout.grid(board)
```

### 14.6 사용자 정의 비교

```txt
int compare(int a, int b) {
    a <=> b
}

let sorted = arr.sortWith(compare)
```

---

## 15. 적합성 요건

다음 조건을 충족하는 구현체는 이 API 명세에 적합하다:

1. 일반 .NET API를 원래 이름으로 사용 가능하게 보존한다,
2. 이 문서에 정의된 내재 표면의 의미론적 동작을 제공하거나 올바르게 에뮬레이션한다,
3. 선언/출력 단축 표기와 대응하는 내재 API 간의 명세된 동치를 보존한다,
4. 표준 .NET API를 언어 정의 내재 함수로 묵묵히 재해석하지 않는다.

구현 전략에 대한 그 외의 제약은 없다.

---

## 16. 버전 관리

이 문서는 API 초안 버전 `v0.1`을 정의한다.

초안 개정 간 하위 호환성은 보장되지 않는다.
