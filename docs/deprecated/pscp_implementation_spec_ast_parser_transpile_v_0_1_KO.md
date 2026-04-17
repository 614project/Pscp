# PS/CP 언어 구현 명세 v0.1

## 1. 적용 범위

이 문서는 언어의 구현 지향적 구조를 세 가지 측면에서 정의합니다:

1. 추상 구문 트리(AST) 설계,
2. 파싱 규칙 및 파서 아키텍처,
3. C# 트랜스파일 전략.

이 문서는 언어 명세 및 API 명세와 함께 사용하도록 작성되었습니다. 충돌이 발생할 경우, 소스 수준의 의미 체계는 언어 명세가 우선하며, 이 문서는 하나의 준거 구현 전략을 정의합니다.

---

## 2. 구현 목표

참조 구현은 다음 목표를 만족해야 합니다:

1. 소스 의미 체계를 정확히 보존한다,
2. 파서를 결정론적으로 유지하고 디버깅하기 쉽게 만든다,
3. 사용자 의도를 변경하는 암묵적 재작성을 피한다,
4. 읽기 쉬운 C#을 생성한다,
5. 구문 설탕(syntax sugar)을 소수의 로우어링 단계에 격리한다,
6. 프론트엔드 의미 체계를 변경하지 않고 미래의 최적화가 가능하도록 한다.

---

## 3. 컴파일 파이프라인

준거 구현은 다음 파이프라인을 사용할 수 있습니다:

1. 어휘 분석(lexical analysis),
2. 구체 구문 파싱(concrete syntax parsing),
3. AST 구성(AST construction),
4. 이름 바인딩(name binding),
5. 타입 검사 및 형태 검사(type checking and shape checking),
6. 로우어드 AST로의 디슈가링(desugaring to a lowered AST),
7. C# 코드 생성(C# code generation),
8. 선택적 포맷팅(optional formatting).

권장 내부 단계 이름:

- `TokenStream`
- `SyntaxTree`
- `AstTree`
- `BoundTree`
- `LoweredTree`
- `CSharpTree`

---

## 4. 소스 위치 정보

모든 구문 노드와 AST 노드는 다음을 위한 소스 위치 메타데이터를 포함해야 합니다:

1. 줄 및 열 진단,
2. 범위 하이라이팅,
3. 가능한 경우 생성된 C#을 소스로 역매핑.

권장 필드:

```txt
startOffset
endOffset
startLine
startColumn
endLine
endColumn
```

---

## 5. AST 설계 원칙

### 5.1 구문 형태와 의미 형태를 분리한다

파서는 초기 AST에서 소스 구문을 충실히 보존해야 합니다. 이후의 로우어링 단계에서 구문 설탕을 제거합니다.

### 5.2 구문(Statement)과 식(Expression)을 별도로 모델링한다

언어가 식 중심(expression-oriented)이더라도, AST는 식 노드와 구문 노드를 구분해야 합니다.

### 5.3 초기에는 설탕이 입혀진 구문을 보존한다

다음 구문들은 로우어링 전에 고유한 AST 노드로 파싱된 상태로 유지되어야 합니다:

- 선언 기반 입력 단축 구문,
- 출력 단축 구문,
- 범위 확장 및 스프레드가 포함된 컬렉션 식,
- 빠른 반복 `->`,
- 한 줄 `then` 및 `do`,
- 공백으로 구분된 호출,
- 집계 식.

### 5.4 불리언 플래그보다 명시적인 노드 종류를 선호한다

의미 체계가 실질적으로 다른 경우 별도의 노드 타입을 사용합니다.

---

## 6. 핵심 AST 최상위 구조

권장 최상위 모델:

```txt
Program
  Members: List<TopLevelMember>
```

### 6.1 TopLevelMember 종류

```txt
TopLevelMember
  - FunctionDecl
  - GlobalStatement
  - UsingDirective            // 향후 선택적 확장
  - TypeDecl                  // 향후 선택적 확장
```

v0.1에서는 `FunctionDecl`과 `GlobalStatement`만 필수입니다.

---

## 7. 선언 AST

## 7.1 바인딩 가변성

가변성을 명시적으로 표현합니다:

```txt
MutabilityKind
  - Immutable
  - Mutable
```

매핑:

- `let` => `Immutable`
- `var` => `Mutable`
- `mut` => `Mutable`
- `mut` 없이 타입을 명시한 선언 => `Immutable`

## 7.2 선언 노드

```txt
Statement
  - LocalDeclStmt
  - MultiDeclStmt
  - InputDeclStmt
  - SizedArrayDeclStmt
```

### 7.2.1 `LocalDeclStmt`

초기화식이 있거나 없는 일반 선언을 나타냅니다.

필드:

```txt
mutability: MutabilityKind
explicitType: TypeSyntax?
name: Identifier or Discard
initializer: Expr?
```

예시:

```txt
let x = 1
var y = 2
mut int z = 3
mut int t;
```

### 7.2.2 `MultiDeclStmt`

하나의 구문에서 여러 이름을 선언하는 경우를 나타냅니다.

필드:

```txt
mutability: MutabilityKind
explicitType: TypeSyntax?
names: List<IdentifierOrDiscard>
initializer: Expr or TupleExpr or null
```

### 7.2.3 `InputDeclStmt`

선언 기반 입력 구문을 나타냅니다.

필드:

```txt
declaredShape: InputDeclShape
mutability: MutabilityKind
explicitElementType: TypeSyntax
namesOrPattern: InputTarget
```

예시:

```txt
int n =
int n, m =
int[n] arr =
(int, int)[m] edges =
```

### 7.2.4 `SizedArrayDeclStmt`

초기화식이 있거나 없는 크기가 지정된 배열 선언을 나타냅니다.

필드:

```txt
elementType: TypeSyntax
dimensions: List<Expr>
name: Identifier
initializer: Expr?
mutability: MutabilityKind
```

예시:

```txt
int[n] a;
int[n][m] dp;
int[n] arr =
```

---

## 8. 구문(Statement) AST

권장 구문 기본 계층:

```txt
Statement
  - BlockStmt
  - LocalDeclStmt
  - MultiDeclStmt
  - InputDeclStmt
  - SizedArrayDeclStmt
  - ExprStmt
  - AssignmentStmt
  - TupleAssignmentStmt
  - OutputStmt
  - IfStmt
  - WhileStmt
  - ForInStmt
  - FastForStmt
  - ReturnStmt
  - BreakStmt
  - ContinueStmt
  - EmptyStmt
```

## 8.1 `BlockStmt`

필드:

```txt
statements: List<Statement>
```

## 8.2 `ExprStmt`

필드:

```txt
expr: Expr
hasSemicolon: bool
```

`hasSemicolon` 필드는 암묵적 반환 분석을 지원하기 위해 필요합니다.

## 8.3 `AssignmentStmt`

필드:

```txt
target: AssignableExpr
operator: AssignOp
value: Expr
```

`AssignOp`에는 `=`, `+=`, `-=`, `*=`, `/=`, `%=`가 포함됩니다.

## 8.4 `TupleAssignmentStmt`

필드:

```txt
left: TupleAssignTarget
right: Expr
```

좌변에는 대입 가능성 규칙에 따라 식별자, 버림(discard), 인덱서, 또는 멤버 접근이 포함될 수 있습니다.

## 8.5 `OutputStmt`

필드:

```txt
kind: OutputKind   // Write 또는 WriteLine
expr: Expr
```

매핑:

```txt
= expr
+= expr
```

## 8.6 `IfStmt`

필드:

```txt
condition: Expr
thenBranch: Statement
elseBranch: Statement?
isOneLineForm: bool
```

한 줄 `if then else`는 식으로도 사용될 수 있습니다. 식 AST를 참조하세요.

## 8.7 `WhileStmt`

필드:

```txt
condition: Expr
body: Statement
isDoForm: bool
```

## 8.8 `ForInStmt`

필드:

```txt
iterator: IdentifierOrDiscard
source: Expr
body: Statement
isDoForm: bool
```

## 8.9 `FastForStmt`

`->` 구문을 나타냅니다.

필드:

```txt
source: Expr
indexName: IdentifierOrDiscard?
itemName: IdentifierOrDiscard
body: Statement or Expr
isDoForm: bool
```

예시:

```txt
xs -> x { ... }
xs -> x do expr
xs -> i, x { ... }
```

## 8.10 `ReturnStmt`

필드:

```txt
expr: Expr?
```

## 8.11 `BreakStmt` 및 `ContinueStmt`

추가 필드 없음.

---

## 9. 식(Expression) AST

권장 식 계층:

```txt
Expr
  - LiteralExpr
  - IdentifierExpr
  - DiscardExpr                 // 선택적 구문 전용 노드; 여기서 금지할 수도 있음
  - ParenExpr
  - TupleExpr
  - BlockExpr
  - IfExpr
  - UnaryExpr
  - BinaryExpr
  - RangeExpr
  - CallExpr
  - SpaceCallExpr
  - MemberAccessExpr
  - IndexExpr
  - TupleProjectionExpr
  - LambdaExpr
  - CollectionExpr
  - SpreadExpr                  // 컬렉션 식 내부에서만 유효
  - AggregationExpr
  - NewExpr                     // 일반 C# 스타일 상호운용
  - ObjectCreationExpr          // NewExpr에서 선택적으로 분리
```

## 9.1 `LiteralExpr`

필드:

```txt
kind: LiteralKind
value: object?
rawText: string
```

## 9.2 `IdentifierExpr`

필드:

```txt
name: string
```

## 9.3 `TupleExpr`

필드:

```txt
elements: List<Expr>
```

## 9.4 `BlockExpr`

필드:

```txt
block: BlockStmt
```

## 9.5 `IfExpr`

필드:

```txt
condition: Expr
thenExpr: Expr
elseExpr: Expr
```

한 줄 식 형태 및 식으로 타입 검사되는 중괄호 식 형태를 나타냅니다.

## 9.6 `UnaryExpr`

필드:

```txt
operator: UnaryOp
operand: Expr
```

## 9.7 `BinaryExpr`

필드:

```txt
left: Expr
operator: BinaryOp
right: Expr
```

산술, 논리, 파이프, 비교, 우주선(spaceship) 연산자를 포함합니다.

## 9.8 `RangeExpr`

필드:

```txt
start: Expr
step: Expr?
end: Expr
kind: RangeKind   // Inclusive, RightExclusive, ExplicitInclusive
```

예시:

```txt
1..10
0..<n
0..=n
10..-1..0
```

## 9.9 `CallExpr`

괄호를 사용한 일반 호출 형태를 나타냅니다.

필드:

```txt
callee: Expr
arguments: List<Expr>
```

## 9.10 `SpaceCallExpr`

로우어링 전 공백으로 구분된 호출을 나타냅니다.

필드:

```txt
callee: Expr
arguments: List<Expr>
```

파싱 및 검증 후 이 노드는 `CallExpr`로 로우어링될 수 있습니다.

## 9.11 `MemberAccessExpr`

필드:

```txt
receiver: Expr
memberName: string
```

## 9.12 `IndexExpr`

필드:

```txt
receiver: Expr
arguments: List<Expr>
```

초기 구현에서는 대괄호 쌍당 인수를 하나로 제한할 수 있습니다.

## 9.13 `TupleProjectionExpr`

필드:

```txt
receiver: Expr
position: int   // 1 기반
```

`p.1`, `p.2` 등을 나타냅니다.

## 9.14 `LambdaExpr`

필드:

```txt
parameters: List<LambdaParam>
body: Expr or BlockStmt
```

`LambdaParam` 필드:

```txt
name: IdentifierOrDiscard
type: TypeSyntax?
```

## 9.15 `CollectionExpr`

필드:

```txt
elements: List<CollectionElement>
```

`CollectionElement` 종류:

```txt
CollectionElement
  - ExprElement(expr)
  - RangeElement(rangeExpr)
  - SpreadElement(expr)
  - BuilderElement(builder)
```

## 9.16 `AggregationExpr`

필드:

```txt
aggregatorName: string
clauses: List<AggregationClause>
body: Expr
```

권장 절(clause) 타입:

```txt
AggregationClause
  - ForClause(name, source)
  - IndexedForClause(indexName, itemName, source)   // 향후 선택적 형태
  - WhereClause(condition)
```

예시:

```txt
min { for i in 0..<n do a[i] - b[i] }
count { for x in arr where x % 2 == 0 do 1 }
```

---

## 10. 타입 구문 AST

권장 표현:

```txt
TypeSyntax
  - NamedTypeSyntax(name, typeArgs)
  - TupleTypeSyntax(elements)
  - ArrayTypeSyntax(elementType, rankOrDepth)
  - SizedArrayTypeSyntax(elementType, dimensions)    // 선언 전용 구문
```

### 10.1 `NamedTypeSyntax`

예시:

```txt
int
string
Queue<int>
Dictionary<int, string>
```

### 10.2 `TupleTypeSyntax`

예시:

```txt
(int, int, string)
```

### 10.3 `ArrayTypeSyntax`

다음과 같은 일반 타입을 나타냅니다:

```txt
int[]
int[][]
```

### 10.4 `SizedArrayTypeSyntax`

다음과 같은 선언 전용 구문을 나타냅니다:

```txt
int[n]
int[n][m]
```

이 노드는 완전히 바인딩된 타입 모델에 남아있어서는 안 됩니다. 일반 배열 타입과 할당 의미 체계로 로우어링됩니다.

---

## 11. 파서 아키텍처

권장 프론트엔드 파서는 식 처리에 우선순위 클라이밍(precedence climbing) 또는 Pratt 파싱을 사용하는 재귀 하강(recursive-descent) 파서입니다.

### 11.1 재귀 하강을 권장하는 이유

다음과 같은 이유로 권장됩니다:

1. 구문 시작 지점의 다중 모호성,
2. `= expr`, `+= expr`, `T x =` 등의 문맥 의존적 단축 구문,
3. 한 줄 `do` 및 `then`,
4. 스프레드와 범위를 포함한 컬렉션 식,
5. 공백으로 구분된 호출 구문.

재귀 하강 방식은 이러한 결정들을 국소적으로 이해하기 쉽게 만듭니다.

### 11.2 파서 모듈

권장 파서 분해 구조:

- `ParseProgram`
- `ParseTopLevelMember`
- `ParseStatement`
- `ParseDeclarationOrExpressionStatement`
- `ParseBlock`
- `ParseExpression(precedence)`
- `ParsePrimary`
- `ParseCollectionExpression`
- `ParseFunctionDecl`
- `ParseTypeSyntax`

---

## 12. 구문 파싱 규칙

## 12.1 구문 시작 분류

구문 시작 지점에서 다음 순서로 분류합니다:

1. `{` => 블록,
2. `if` => if 구문 또는 식 문맥의 if,
3. `while` => while 구문,
4. `for` => for-in 구문,
5. `return` => return 구문,
6. `break` => break,
7. `continue` => continue,
8. `=` => write 출력 구문,
9. `+=` => writeline 출력 구문,
10. `let` / `var` / `mut` / 명시적 타입 룩어헤드 => 선언,
11. 그 외의 경우 식 또는 대입 구문으로 파싱.

이 순서는 의도적으로 엄격하게 정의되어 있습니다.

## 12.2 선언과 식의 구분

구문이 식별자 또는 알려진 타입 키워드로 시작할 때, 파서는 다음을 구분해야 합니다:

- 명시적 타입 선언,
- 식 구문,
- 대입,
- 공백으로 구분된 호출.

권장 전략:

1. 제한된 룩어헤드로 명시적 타입 파싱을 시도한다,
2. 유효한 선언 패턴이 뒤따를 때만 선언으로 확정한다,
3. 그렇지 않으면 식 파싱으로 폴백한다.

올바른 구분이 필요한 예시:

```txt
int x = 1          // 선언
int[n] arr =       // 입력 선언 또는 크기 지정 선언
foo x y            // 식 구문, 공백 호출
x = y + 1          // 대입
```

## 12.3 입력 선언 파싱

선언 헤드를 파싱한 후, `=` 다음에 구문 종료, 개행 경계, 또는 블록 끝이 바로 이어지면 `InputDeclStmt`를 생성합니다.

예시:

```txt
int n =
int n, m =
int[n] arr =
```

이 규칙은 불완전한 대입으로 오류를 보고하는 것보다 우선합니다.

## 12.4 출력 구문 파싱

구문 시작 지점에서:

- `= expr`은 `OutputStmt(Write)`로 파싱됩니다.
- `+= expr`은 `OutputStmt(WriteLine)`으로 파싱됩니다.

그 외의 위치에서 `+=`는 일반 대입으로 파싱됩니다.

## 12.5 튜플 대입 파싱

괄호로 감싼 식에 `=`가 뒤따르는 경우, 괄호 안의 좌변이 유효한 대입 패턴인 경우에만 튜플 대입을 의미합니다.

예시:

```txt
(a, b) = (b, a)
(arr[i], arr[j]) = (arr[j], arr[i])
```

좌변이 대입 불가능한 경우, 파싱은 구문적으로 식으로 성공하고 이후 단계에서 거부됩니다.

---

## 13. 식 파싱 규칙

## 13.1 식 파서 스타일

후위 형식을 명시적으로 처리하는 우선순위 클라이밍 또는 Pratt 파싱을 사용합니다.

## 13.2 우선순위 표

파서는 높은 것부터 낮은 순으로 다음 우선순위를 사용해야 합니다:

1. 후위(postfix)
   - 괄호 호출
   - 인덱싱
   - 멤버 접근
   - 튜플 프로젝션

2. 전위(prefix)
   - 단항 플러스/마이너스
   - `!`
   - `not`

3. 곱셈 계열
   - `*`, `/`, `%`

4. 덧셈 계열
   - `+`, `-`

5. 범위
   - `..`, `..<`, `..=`

6. 비교
   - `<`, `<=`, `>`, `>=`, `<=>`

7. 동등성
   - `==`, `!=`

8. 논리 AND
   - `&&`, `and`

9. 논리 XOR
   - `^`, `xor`

10. 논리 OR
    - `||`, `or`

11. 파이프
    - `|>`, `<|`

12. 대입
    - `=`, `+=`, `-=`, `*=`, `/=`, `%=`

대입은 구현에서 명시적으로 대입 식을 지원하지 않는 한 구문 문맥에서만 파싱되어야 합니다.

v0.1 권장 전략: 대입을 일반 식으로 노출하지 않는다.

## 13.3 후위 파싱 루프

기본 식을 파싱한 후, 다음 순서로 후위 연산자를 반복적으로 소비합니다:

1. `(` 인수 목록 `)` => `CallExpr`,
2. `[` 인덱스 목록 `]` => `IndexExpr`,
3. `.` 식별자 => `MemberAccessExpr`,
4. `.` 정수 리터럴 => `TupleProjectionExpr`,
5. 문맥에서 허용되는 경우 공백 호출 계속 => `SpaceCallExpr`.

이를 통해 다음과 같은 체이닝이 가능합니다:

```txt
obj.method(x)[i].name
```

---

## 14. 공백으로 구분된 호출 파싱

## 14.1 도입 배경

공백으로 구분된 호출 구문은 파이프 지향 함수형 스타일과의 호환성을 높입니다.

## 14.2 파싱 규칙

피호출자로 적합한 기본 또는 후위 식을 파싱한 후, 다음 토큰이 원자적 인수의 시작이면 `SpaceCallExpr`을 계속 파싱할 수 있습니다.

권장 원자적 인수 시작 토큰:

- 식별자,
- 리터럴,
- `(`,
- `[`,
- 현재 문맥에서 블록 식이 유효한 경우에만 `{`,
- 모호하지 않은 경우 람다 시작자.

## 14.3 제한 사항

괄호 없이 낮은 우선순위 이항 연산자를 넘어서 다음 토큰 시퀀스를 공백 호출 인수로 흡수하려 하면 파서는 이를 해서는 안 됩니다.

예시:

```txt
f x y              // 유효
f (x + y) z        // 유효
f x + y            // (f x) + y로 파싱
f x < y            // (f x) < y로 파싱
f if ok then 1 else 2   // 괄호 필요
```

## 14.4 로우어링

파싱 및 검증이 성공하면 `SpaceCallExpr`은 동일한 피호출자와 인수 목록을 가진 `CallExpr`로 로우어링됩니다.

v0.1에서는 커링이나 부분 적용을 의미하지 않습니다.

---

## 15. 컬렉션 식 파싱

## 15.1 진입

`[`를 만나면 `CollectionExpr`를 파싱합니다.

## 15.2 요소 분류

컬렉션 식 내부에서 쉼표로 구분된 각 요소를 다음 중 하나로 분류합니다:

1. 스프레드 요소,
2. 빌더 요소,
3. 범위 요소,
4. 일반 식 요소.

권장 순서:

1. 토큰이 `..`이고 스프레드가 허용되는 위치이면 `SpreadElement`로 파싱,
2. 그렇지 않으면 식을 파싱하고, 해당 식이 구문적으로 범위이면 `RangeElement`로 분류,
3. 식 다음에 `->`가 오면 빌더 형식으로 파싱,
4. 그 외의 경우 `ExprElement`로 분류.

## 15.3 자동 범위 확장

파싱 중에는 범위를 확장하지 않습니다. `RangeElement` 노드를 보존하고 로우어링 또는 코드 생성 시 확장을 수행합니다.

## 15.4 스프레드 제한

스프레드는 컬렉션 식 내부에서만 허용됩니다. 선행 `..expr`이 다른 위치에 나타나면 구문 오류입니다.

---

## 16. 집계(Aggregation) 파싱

## 16.1 형식

집계 구문의 형식은 다음과 같습니다:

```txt
name { clauses do expr }
```

예시:

```txt
min { for i in 0..<n do a[i] - b[i] }
sum { for x in arr do x }
count { for x in arr where x % 2 == 0 do 1 }
```

## 16.2 권장 파서 전략

기본 식을 파싱할 때, 식별자 다음에 `{`가 오고 블록이 `for`로 시작하면 일반 호출이나 블록 대신 `AggregationExpr`로 파싱합니다.

## 16.3 절 구조

v0.1 최소 파서는 다음만 지원하면 됩니다:

- 하나의 `for` 절,
- 선택적 `where` 절,
- 하나의 `do` 본문 식.

이후 버전에서 일반화될 수 있습니다.

---

## 17. 한 줄 `then` 및 `do`

## 17.1 파싱 규칙

`then`과 `do`는 구문 종료까지 정확히 하나의 식 또는 단순 구문 본문을 소비합니다.

## 17.2 종료 조건

한 줄 본문은 다음 중 첫 번째에서 종료됩니다:

1. 현재 구문을 종료하는 개행,
2. 세미콜론,
3. 감싸는 구문의 닫는 중괄호,
4. 파일 끝.

## 17.3 금지 형식

한 줄 본문은 중괄호로 감싸지 않는 한 여러 구문을 포함할 수 없습니다.

예시:

```txt
if x < 0 then -x else x          // 유효
for i in 0..<n do sum += a[i]    // 유효
if cond then a; b else c         // 한 줄 형식으로는 무효
```

---

## 18. 암묵적 반환 분석

## 18.1 단계

암묵적 반환은 파싱 이후, AST 검증 또는 바인딩 중에 해결되어야 합니다.

## 18.2 규칙

블록은 다음 조건을 모두 만족할 때 마지막 식 구문을 암묵적으로 반환합니다:

1. 마지막 구문이 `ExprStmt`인 경우,
2. `ExprStmt.hasSemicolon == false`인 경우,
3. 최상위 식이 단순 호출(bare invocation)이 아닌 경우.

## 18.3 단순 호출 검사

식의 최상위 노드가 다음 중 하나이면 단순 호출입니다:

- `CallExpr`,
- `SpaceCallExpr`.

선택적 구현: 의미적으로 투명한 괄호로 감싼 호출 등 특정 호출 래퍼를 여전히 단순 호출로 처리할 수 있습니다.

## 18.4 로우어링 전략

바인더 또는 로우어러는 블록에 다음 중 하나를 주석으로 달아야 합니다:

```txt
BlockResultKind
  - NoValue
  - ImplicitValue(expr)
  - ExplicitReturn
```

이는 코드 생성 중 반환 동작을 재계산하는 것을 피하게 합니다.

---

## 19. 바인딩과 이름 해석

## 19.1 스코프 모델

다음에 대해 어휘적 스코프를 도입합니다:

- 함수 본문,
- 블록 구문,
- 람다 본문,
- 루프 본문,
- 집계 스코프,
- 빠른 반복 바인딩.

## 19.2 섀도잉

구현별 정책으로 경고를 선택하지 않는 한, 일반 어휘적 섀도잉은 허용됩니다.

## 19.3 예약 내장 바인딩

`stdin`과 `stdout`은 섀도잉이 의도적으로 지원되지 않는 한 예약된 내장 바인딩으로 취급해야 합니다.

## 19.4 재귀 바인딩

`rec`로 선언된 함수는 본문을 바인딩하기 전에 자신의 스코프에 등록됩니다. `rec` 없는 함수는 그렇지 않습니다.

---

## 20. 타입 검사 참고 사항

## 20.1 최소 타입 요구 사항

v0.1 구현은 최소한 다음을 적용해야 합니다:

1. 불변 바인딩에 대한 대입 거부,
2. 튜플 프로젝션은 튜플 타입 수신자를 요구,
3. `break`와 `continue`는 루프 내부에서만 사용,
4. `return` 타입은 포함하는 함수와 일치,
5. 컬렉션 리터럴 요소는 유효한 요소 타입으로 통일,
6. 범위 끝점과 스텝은 호환되는 숫자 타입,
7. 출력 단축 구문은 렌더링 가능한 형태에서만 사용,
8. 입력 단축 구문은 지원되는 형태에서만 사용.

## 20.2 크기 지정 배열 타입

크기 지정 선언 타입은 구문 전용이며, 바인딩 전이나 바인딩 중에 일반 배열 타입과 할당 의미 체계로 소거되어야 합니다.

---

## 21. 디슈가링 전략

권장 로우어링 순서:

1. 선언 및 입출력 단축 구문 해석,
2. `SpaceCallExpr`을 `CallExpr`로 로우어링,
3. `FastForStmt`를 일반 루프 형식으로 로우어링,
4. `AggregationExpr`을 명시적 루프 또는 헬퍼 호출로 로우어링,
5. 컬렉션 식의 스프레드 및 범위 확장 로우어링,
6. 필요한 경우 암묵적 반환을 명시적 return 구문으로 로우어링,
7. 크기 지정 선언 구문 소거.

디슈가링은 가능한 경우 소스 범위를 보존해야 합니다.

---

## 22. 로우어드 AST 형태

디슈가링 이후 구현은 더 작은 코어 언어로 제한할 수 있습니다.

권장 로우어드 구문 집합:

```txt
LoweredStatement
  - BlockStmt
  - LocalDeclStmt
  - ExprStmt
  - AssignmentStmt
  - IfStmt
  - WhileStmt
  - ForInStmt or CanonicalForStmt
  - ReturnStmt
  - BreakStmt
  - ContinueStmt
```

권장 로우어드 식 집합:

```txt
LoweredExpr
  - LiteralExpr
  - IdentifierExpr
  - TupleExpr
  - UnaryExpr
  - BinaryExpr
  - CallExpr
  - MemberAccessExpr
  - IndexExpr
  - LambdaExpr
  - ConditionalExpr
  - NewExpr
  - ArrayLiteralExpr or BuilderExpr
```

이 코어는 C#으로 트랜스파일하기 더 쉽습니다.

---

## 23. C# 트랜스파일 전략

## 23.1 일반 정책

생성된 C#은 다음을 만족해야 합니다:

1. 의미 체계적 동등성,
2. 가독성,
3. 예측 가능한 지역 구조,
4. 디버깅 시 낮은 의외성.

v0.1에서 트랜스파일러는 할당 최소화가 필수는 아닙니다.

## 23.2 명명 정책

트랜스파일러는 가능한 한 사용자가 선언한 이름을 보존해야 합니다.

.NET API는 그대로 사용할 수 있으므로 자동 대소문자 변환은 수행하지 않습니다. 다음과 같은 예시들이 사용자 코드와 생성 코드 모두에서 자연스럽게 유지되어야 합니다:

```txt
Queue<int> queue = new()
PriorityQueue<int, int> pq = new()
HashSet<int> set = new()
```

임시 로우어드 이름에는 사용자 코드와 충돌하지 않을 예약 접두사를 사용해야 합니다. 예:

```txt
__tmp0
__iter1
__acc2
```

---

## 24. 런타임 지원 전략

트랜스파일러는 다음을 위한 소형 헬퍼 런타임을 생성할 수 있습니다:

- `stdin`,
- `stdout`,
- .NET에 직접 매핑되지 않는 컬렉션 헬퍼,
- 렌더링 헬퍼,
- 배열/그리드 구성 헬퍼.

권장 헬퍼 클래스 이름:

```txt
__PscpStdin
__PscpStdout
__PscpSeq
__PscpRender
```

이 이름들은 권장 사항이며, 소스 언어의 일부가 아닙니다.

---

## 25. 구문별 C# 생성 규칙

## 25.1 스칼라 및 기본 선언

예시:

```txt
let x = 1
var y = 2
mut int z = 3
```

제안 C# 출력:

```csharp
var x = 1;
var y = 2;
int z = 3;
```

생성 구문에서 가변성 구분을 보존할 필요가 없는 경우, `let`과 `var` 모두 추론된 대로 `var` 또는 명시적 타입 선언으로 로우어링할 수 있습니다.

## 25.2 초기화되지 않은 가변 스칼라

```txt
mut int x;
mut string s;
```

제안 C# 출력:

```csharp
int x = default;
string s = default;
```

## 25.3 크기 지정 배열

```txt
int[n] a;
int[n][m] dp;
```

제안 C# 출력:

```csharp
int[] a = new int[n];
int[][] dp = new int[n][];
for (int __i = 0; __i < n; __i++) dp[__i] = new int[m];
```

런타임 헬퍼를 대신 사용하는 것도 허용됩니다.

## 25.4 입력 단축 구문

```txt
int n =
int[n] arr =
(int, int)[m] edges =
```

제안 C# 출력:

```csharp
int n = stdin.Int();
int[] arr = stdin.Array<int>(n);
(int, int)[] edges = stdin.Tuples2<int, int>(m);
```

정확한 헬퍼 이름은 구현에서 정의합니다.

## 25.5 출력 단축 구문

```txt
= expr
+= expr
```

제안 C# 출력:

```csharp
stdout.Write(expr);
stdout.WriteLine(expr);
```

헬퍼는 튜플 및 컬렉션 렌더링을 구현할 수 있습니다.

## 25.6 튜플 프로젝션

```txt
p.2
```

제안 C# 출력:

```csharp
p.Item2
```

## 25.7 튜플 대입

```txt
(a, b) = (b, a)
(arr[i], arr[j]) = (arr[j], arr[i])
```

제안 C# 출력:

```csharp
(a, b) = (b, a);
(arr[i], arr[j]) = (arr[j], arr[i]);
```

백엔드 제약이 있는 경우 임시 변수 전략을 사용할 수 있습니다.

## 25.8 범위 식

독립적인 범위는 헬퍼 이터러블로 로우어링될 수 있습니다.

예시:

```txt
0..<n
1..10
10..-1..0
```

권장 전략:

1. 헬퍼 열거 객체,
2. `for`, `->`, 또는 집계에서 사용될 때 직접 루프 합성,
3. 컬렉션 식에 배치될 때 구체화.

v0.1 권장: 범위 로우어링을 전역적으로 적극 최적화하지 말고, 이미 반복이 필요한 문맥에서만 최적화한다.

## 25.9 컬렉션 식

예시:

```txt
[1, 2, 3]
[0..<n]
[1, 2, ..a, 6]
[0..<n -> i do i * i]
```

제안 로우어링 전략:

1. 선언 문맥에서 구체적인 배열/리스트 구성,
2. 모호한 r-값 문맥에서 헬퍼 빌더,
3. 빌더 형식에 대한 직접 루프 채우기,
4. 스프레드에 대한 명시적 연결 또는 추가 루프.

선택된 로우어링은 순서를 보존해야 합니다.

## 25.10 빠른 반복

```txt
xs -> x { ... }
xs -> i, x { ... }
```

제안 로우어링:

- 인덱스 형식이고 소스가 임의 접근을 지원하면 인덱스 기반 `for` 생성,
- 그렇지 않으면 `foreach`와 인덱스 카운터 생성,
- 인덱스를 요청하지 않으면 일반 `foreach`로 충분.

## 25.11 집계

```txt
min { for i in 0..<n do a[i] - b[i] }
sum { for x in arr do x }
count { for x in arr where pred(x) do 1 }
```

제안 로우어링:

1. 누산기 지역 변수 할당,
2. 명시적 반복 생성,
3. 집계자 종류에 따라 누산기 갱신,
4. 최종 누산기 식 생성.

v0.1 권장: 명확성과 제어 가능성을 위해 집계를 라이브러리 체인이 아닌 루프로 로우어링한다.

## 25.12 한 줄 형식

한 줄 `then`과 `do`는 코드 생성 시 별도의 구문으로 남아있어서는 안 됩니다. 블록 또는 식 형식과 동일한 하위 AST로 로우어링됩니다.

## 25.13 암묵적 반환

로우어링 중, return 구문이 필요한 C# 메서드나 람다를 생성할 때 암묵적 반환 블록을 명시적 `return` 구문으로 변환합니다.

예시:

```txt
int cmp(int a, int b) {
    a <=> b
}
```

제안 C# 출력:

```csharp
int cmp(int a, int b)
{
    return Compare(a, b);
}
```

여기서 `Compare`는 `<=>`에 사용된 로우어링을 나타냅니다.

## 25.14 우주선 연산자(Spaceship Operator)

```txt
a <=> b
```

제안 로우어링 전략:

1. 제네릭이거나 알 수 없을 때 `Comparer<T>.Default.Compare(a, b)`,
2. 정적으로 알려져 있고 유효한 경우 `a.CompareTo(b)`,
3. 원하는 경우 특수화된 기본 타입 비교.

v0.1 권장: 각 문맥에서 가장 읽기 쉬운 유효한 로우어링을 선호한다.

---

## 26. C# 식 vs 구문 생성

소스 언어가 식 지향 블록을 허용하므로 코드 생성기는 다음을 위한 헬퍼를 제공해야 합니다:

1. 구문 문맥 생성,
2. 식 문맥 생성,
3. 필요한 경우 임시 변수를 통한 식 리프팅.

권장 코드 생성 유틸리티:

```txt
EmitStatement(stmt)
EmitExpression(expr)
EmitBlockAsExpression(block)
EmitBlockAsStatements(block)
```

소스 식을 C#에서 직접 표현할 수 없는 경우, 복잡한 식 생성을 강제하는 대신 코드 생성 전에 로우어러가 이를 재작성해야 합니다.

---

## 27. 생성된 C#의 권장 헬퍼 런타임 인터페이스

다음은 권장 생성 헬퍼 인터페이스입니다. 이름은 규범적이지 않습니다.

```csharp
sealed class __PscpStdin
{
    public int Int();
    public long Long();
    public string Str();
    public char Char();
    public double Double();
    public decimal Decimal();
    public bool Bool();
    public string Line();
    public string[] Lines(int n);
    public string[] Words();
    public char[] Chars();
    public T[] Array<T>(int n);
    public (T1, T2)[] Tuples2<T1, T2>(int n);
    public (T1, T2, T3)[] Tuples3<T1, T2, T3>(int n);
}

sealed class __PscpStdout
{
    public void Write<T>(T value);
    public void WriteLine<T>(T value);
    public void Flush();
    public void Lines<T>(IEnumerable<T> values);
    public void Grid<T>(IEnumerable<IEnumerable<T>> grid);
}
```

필요에 따라 추가 헬퍼를 더할 수 있습니다.

---

## 28. 진단(Diagnostics) 전략

구현은 다음 프론트엔드 상황에 대해 정확한 진단을 생성해야 합니다:

1. 선언 시작과 식 시작의 모호성,
2. 공백으로 구분된 호출의 잘못된 인수 그룹화,
3. 컬렉션 식 외부의 불법 스프레드,
4. 지원되지 않는 입출력 형태,
5. 재귀 자기 참조에 `rec` 누락,
6. 정적으로 알 수 있을 때 범위를 벗어난 튜플 프로젝션 인덱스,
7. 구현이 경고를 선택한 경우, 명시적 반환이 없는 값 반환 함수의 마지막 단순 호출.

진단은 구문 트리에 캡처된 소스 범위를 참조해야 합니다.

---

## 29. 권장 개발 순서

실용적인 구현 순서:

1. 렉서(lexer),
2. 구문 설탕 없는 핵심 파서,
3. 선언 및 대입,
4. `if`, `while`, `for`, `return`, `break`, `continue`,
5. 컬렉션 식과 범위,
6. 입출력 단축 구문,
7. 튜플 프로젝션과 튜플 대입,
8. 공백으로 구분된 호출,
9. 빠른 반복 `->`,
10. 집계,
11. 암묵적 반환 분석,
12. C# 생성기,
13. 헬퍼 런타임.

이 순서는 초기에 파서 모호성을 줄이고, 안정적인 코어가 갖춰질 때까지 구문적으로 가장 섬세한 기능들을 뒤로 미룹니다.

---

## 30. 최소 실행 가능 로우어드 코어

C# 생성에 적합한 권장 MVP 로우어드 코어는 다음으로 구성됩니다:

### 구문(Statements)

```txt
BlockStmt
LocalDeclStmt
AssignmentStmt
ExprStmt
IfStmt
WhileStmt
ForEachStmt
ReturnStmt
BreakStmt
ContinueStmt
```

### 식(Expressions)

```txt
LiteralExpr
IdentifierExpr
TupleExpr
UnaryExpr
BinaryExpr
CallExpr
MemberAccessExpr
IndexExpr
LambdaExpr
ConditionalExpr
ArrayBuilderExpr
```

그 외 모든 것은 백엔드 생성 전에 이 코어로 로우어링되어야 합니다.

---

## 31. 준거성(Conformance)

구현은 다음 조건을 만족할 때 이 구현 명세에 준거합니다:

1. AST가 언어 명세에 정의된 모든 소스 구문을 표현할 수 있는 경우,
2. 파서가 명시된 모든 구문적 구분을 올바르게 해석하는 경우,
3. 디슈가링이 소스 의미 체계를 보존하는 경우,
4. 생성된 C#이 관찰 가능한 동작을 보존하는 경우,
5. 기존 .NET API 이름을 대체 대소문자나 별칭으로 재작성하지 않아도 되는 경우.

최적화 전략은 구현에서 정의합니다.
