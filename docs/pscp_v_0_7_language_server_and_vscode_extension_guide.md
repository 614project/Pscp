# PSCP v0.7 언어 서버 및 VS Code 확장 가이드

- 상태: **초안(Draft)**
- 대상 언어: PSCP v0.7 (`pscp_v_0_7_spec.md`)
- 기준 구현: 저장소의 `src/Pscp.LanguageServer`, `vscode/pscp-vscode` (도구 버전 0.6.7)
- 이전 문서: `deprecated/pscp_v_0_6_language_server_and_vscode_extension_guide.md`
- 작성일: 2026-09-26

이 문서는 v0.6 가이드를 대체한다. v0.6 가이드가 무엇이 부족했고 이 문서가 그것을 어떻게 바꿨는지는 [부록 D](#부록-d-v06-가이드-대비-개선점)에 있다.

---

## 0. 문서 안내

### 0.1 목적과 범위

이 문서는 PSCP 개발 도구 계층의 **동작 계약**을 정의한다. 대상은 다음 네 가지다.

1. **언어 서버** (`Pscp.LanguageServer`, `pscp lsp`)
2. **VS Code 확장** (`vscode/pscp-vscode`)
3. 둘이 공유하는 **트랜스파일러 front-end** (`Pscp.Transpiler`의 lexer, parser, semantic analyzer, intrinsic catalog)
4. 확장이 호출하는 **CLI 명령** (`pscp transpile`, `pscp run`, `pscp check`, 그리고 v0.7에서 추가하는 `pscp test`)

v0.6 가이드는 "이런 구조가 바람직하다"는 권장 설계 목록이었다. 그래서 현재 무엇이 동작하는지, 각 기능이 정확히 어떻게 동작해야 하는지는 알 수 없었다. 이 문서는 기능마다 다음 두 가지를 함께 적는다.

- **0.6.7 현황**: 저장소 구현을 실제로 실행해서 확인한 동작
- **0.7 요구**: 적합한 구현이 따라야 하는 동작

### 0.2 규범 용어

[스펙 §0.2](pscp_v_0_7_spec.md#02-규범-용어)와 같다.

| 표현 | 의미 |
|---|---|
| **해야 한다 / 반드시** | 필수 (MUST) |
| **해서는 안 된다 / 금지** | 금지 (MUST NOT) |
| **권장한다** | 특별한 이유가 없으면 따른다 (SHOULD) |
| **할 수 있다** | 선택 (MAY) |

표의 **0.6.7 현황** 열은 다음 세 값 중 하나다.

| 값 | 의미 |
|---|---|
| 있음 | 요구대로 동작한다 |
| 부분 | 일부만 동작하거나 요구와 다르게 동작한다 |
| 없음 | 구현되지 않았다 |

### 0.3 용어

| 용어 | 뜻 |
|---|---|
| **front-end** | 트랜스파일러의 lexer, parser, binder, semantic analyzer. C# 코드를 만들기 전까지의 단계 전부 |
| **분석 결과** | 한 문서의 한 버전에 대해 front-end를 한 번 실행해서 얻은 불변 결과. 토큰, 구문 트리, 심볼, 타입, 진단, lowering 사실을 담는다 |
| **lowering 사실** | "이 코드는 C#으로 이렇게 낮아진다"는 정보. 예: `let MOD = 7` → `const`, `visited += x` → `HashSet<int>.Add(x)` |
| **intrinsic 카탈로그** | PSCP intrinsic(입출력, aggregate, math, collection helper, 변환 키워드, 비교자)의 이름, 시그니처, 문서, 폐기 정보를 모은 단일 데이터 |
| **진단 코드** | `PSCP` + 숫자 4자리. 진단 종류마다 하나씩 붙는 안정적인 식별자([부록 B](#부록-b-진단-코드-표)) |
| **SDK** | `pscp` CLI 실행 파일과 그 설치본 |
| **샘플** | 풀이 파일과 같은 이름을 가진 입력(`.in`)/기대 출력(`.out`) 파일 쌍 ([§14.5](#145-샘플-실행)) |

### 0.4 읽는 순서

- PSCP를 **쓰는 사람**: [§1](#1-사용자-안내)만 읽으면 된다.
- 언어 서버나 확장을 **구현하는 사람**: §2–§17과 부록을 읽는다. 작업 순서는 [§17](#17-구현-우선순위)에 있다.

---

## 1. 사용자 안내

### 1.1 구성 요소

| 구성 요소 | 하는 일 | 얻는 곳 |
|---|---|---|
| **PSCP SDK** (`pscp`) | 트랜스파일, 빌드, 실행, 검사. `pscp lsp`로 언어 서버도 실행한다 | GitHub 릴리스의 Windows 설치 프로그램, Linux `.deb` / `.tar.gz` |
| **VS Code 확장** | `.pscp` 언어 등록, 구문 강조, 언어 서버 연결, 명령 | 0.6.7: 직접 빌드(`vscode/Build-Vsix.ps1`, win-x64). 0.7: GitHub 릴리스에 플랫폼별 VSIX 첨부 ([§15.2](#152-vsix)) |
| **언어 서버** | 진단, 자동완성, hover 등 | VSIX 안에 포함된 서버, 또는 SDK의 `pscp lsp` |

### 1.2 설치

1. SDK를 설치한다. 터미널에서 `pscp version`이 `pscp CLI 0.7.x (language 0.7)`처럼 나오면 된다.
2. VS Code에서 `Extensions: Install from VSIX...`로 확장을 설치한다.
3. `.pscp` 파일을 열면 상태 표시줄 왼쪽에 `PSCP 0.7`이 나타난다.

.NET SDK(`dotnet`)가 있어야 `run`, `test` 명령이 생성된 C#을 빌드할 수 있다. 편집 기능(진단, 자동완성 등)에는 필요 없다.

### 1.3 언어 서버를 찾는 순서

확장은 다음 순서로 언어 서버를 찾는다. 앞에서 찾으면 뒤는 보지 않는다(0.6.7과 같다).

1. 설정 `pscp.server.path`
2. 호환용 설정 `pscp.languageServerPath`
3. VSIX 안에 포함된 서버 (`server/Pscp.LanguageServer[.exe]`)
4. 설정 `pscp.sdkPath`
5. 기본 설치 위치의 SDK (`%LOCALAPPDATA%\Programs\Pscp\pscp.exe`, `%ProgramFiles%\Pscp\pscp.exe`)
6. `PATH`의 `pscp`
7. 저장소 개발 빌드 (`src/Pscp.LanguageServer`를 `dotnet build`해서 실행)

실행 파일 경로 설정(1, 2, 4번과 `pscp.transpiler.path`)은 **사용자/머신 설정에서만** 읽는다. 저장소의 `.vscode/settings.json`이 임의의 실행 파일을 가리킬 수 없게 하기 위해서다.

v0.7에서 더하는 규칙: 서버가 시작되면 확장은 서버가 알려 준 도구 버전과 언어 버전([§4.2](#42-initialize))을 확인한다. 확장과 다르면 상태 표시줄에 경고를 표시하고 로그에 두 버전을 남긴다.

### 1.4 자주 쓰는 흐름

| 하고 싶은 일 | 방법 |
|---|---|
| 오류와 경고 보기 | 파일을 열면 된다. 문제 패널과 밑줄로 표시된다 |
| 문법 설탕의 뜻 보기 | 마우스를 올린다. 예: `int n =` 위에서 "입력 shorthand → `stdin.readInt()`" |
| 빠른 수정 | 전구 아이콘(`Ctrl+.`). 예: 인덱서 안의 `arr[1..3]` → `arr[1..<3]` |
| 예제 입력으로 실행 | `PSCP: Run with Input File` → `.in` 파일 선택 |
| 예제 전체 채점 | `PSCP: Run Samples`, 또는 테스트 패널의 **PSCP 샘플** |
| 생성된 C# 보기 | `PSCP: Preview Generated C#` (옆 창에 열리고 편집할 때마다 갱신) |
| 그냥 실행 (키보드 입력) | `PSCP: Run Current File` |

### 1.5 문제 해결

| 증상 | 확인할 것 |
|---|---|
| 상태 표시줄이 `PSCP: stopped` | 클릭해서 로그를 연다. 서버 실행 파일 경로와 종료 코드가 기록되어 있다 |
| 기능이 느리거나 응답이 없다 | 설정 `pscp.trace.server`를 `messages`로 바꾸고 로그에서 요청 시간을 본다 |
| 편집기와 `pscp check`의 결과가 다르다 | 버그다. 두 결과는 같아야 한다([§2](#2-설계-원칙) 원칙 2). 로그와 파일을 첨부해 이슈로 보고한다 |
| 서버를 바꾼 뒤 이상하다 | `PSCP: Restart Language Server` |

---

## 2. 설계 원칙

1. **front-end는 하나다.** 언어 서버는 트랜스파일러와 **같은** lexer, parser, binder, semantic analyzer를 쓴다. 언어 서버가 PSCP 문법이나 이름 해석을 따로 구현해서는 안 된다. 편집기에서 보이는 의미는 `pscp transpile`이 실제로 쓰는 의미와 같아야 한다.
2. **편집기와 CLI의 판정은 같다.** 같은 파일에 대해 언어 서버가 publish하는 **오류와 경고**의 집합(코드, 등급, 범위, 메시지)은 `pscp check`의 출력과 같아야 한다. 정보(information)나 힌트(hint) 등급은 편집기 전용 보조 정보이므로 이 규칙에서 제외한다.
3. **intrinsic 정보는 한 곳에 있다.** intrinsic의 이름, 시그니처, 결과 타입, 전제조건, 문서, 폐기 여부는 intrinsic 카탈로그 한 곳에서 관리한다. 트랜스파일러, 언어 서버, TextMate 문법, 문서가 모두 이 카탈로그에서 나온다.
4. **확장은 해석하지 않는다.** VS Code 확장은 PSCP 코드를 직접 파싱하거나 의미를 계산하지 않는다(v0.6 원칙 유지). 확장은 표준 LSP 클라이언트로 서버와 통신하고, VS Code 전용 UI만 책임진다.
5. **단일 파일, 순차 처리.** PSCP 풀이는 한 파일에 수백~수천 줄이다. 복잡한 백그라운드 인덱싱 대신, 최신 버전만 분석하는 단순하고 예측 가능한 처리 모델을 쓴다([§5.4](#54-처리-모델)).
6. **조용히 틀리지 않는다.** 편집기가 보여 주는 lowering 설명이나 제안은 스펙과 달라서는 안 된다. 예를 들어 자동완성은 트랜스파일러가 거부할 형태를 제안해서는 안 된다.

---

## 3. 구성과 책임

### 3.1 구성도

```txt
VS Code
 └─ PSCP 확장 (JavaScript/TypeScript)
     ├─ LSP 클라이언트 (vscode-languageclient)
     ├─ 명령, 상태 표시줄, 로그, 설정
     ├─ 샘플 실행기 (테스트 패널) ─────────┐
     └─ 생성 C# 미리보기 (가상 문서)        │
          │  stdio / JSON-RPC 2.0 / LSP 3.17 │  프로세스 실행
          ▼                                   ▼
 언어 서버 (Pscp.LanguageServer)       pscp CLI (run / test / transpile)
     ├─ 프로토콜 계층                          │
     ├─ 문서 저장소 (버전별 snapshot)          │
     ├─ 분석 스케줄러 (최신 버전만)            │
     ├─ 기능 제공자 (hover, completion, …)    │
     └──────────────┬─────────────────────────┘
                    ▼
     Pscp.Transpiler front-end  +  intrinsic 카탈로그
     (lexer → parser → binder → semantic analyzer → lowering 사실)
```

### 3.2 책임 나누기

| 일 | 담당 |
|---|---|
| 파싱, 이름 해석, 타입, 진단, lowering 사실 | front-end |
| 버전 관리, 분석 스케줄, LSP 요청 응답, 결과 표현(hover 문장, 완성 정렬 등) | 언어 서버 |
| 서버 실행 파일 찾기와 실행, 재시작 | 확장 |
| 명령, 상태 표시줄, 로그 채널, 설정 전달, 작업 영역 신뢰 | 확장 |
| 샘플 실행 UI, 생성 C# 미리보기 창 | 확장 (내용은 CLI와 서버가 만든다) |
| 빌드, 실행, 샘플 채점 | CLI |

### 3.3 금지

- 확장이 `int n =`의 입력 의미, `sum arr`의 aggregate 의미, `visited += x`의 rewrite 의미 등을 스스로 판단하는 것
- 언어 서버가 front-end와 다른 규칙으로 이름을 해석하거나 진단을 만드는 것
- 언어 서버가 intrinsic 문서를 카탈로그 밖에 따로 적는 것

#### 0.6.7 현황과 이행

0.6.7 언어 서버는 원칙 1–3을 지키지 않는다.

- 이름, 심볼, hover, 자동완성은 언어 서버 자체의 **토큰 단위 분석기**(`PscpAnalyzer`)가 만든다.
- 진단은 이 분석기의 결과와 트랜스파일러 진단을 섞는다.
- intrinsic 문서는 `PscpIntrinsics.cs`에 따로 적혀 있다.

그 결과 실제 불일치가 있다([부록 D](#부록-d-v06-가이드-대비-개선점) D-12–D-14). 0.7 구현은 심볼 정보를 front-end의 binder 결과에서 가져오도록 옮겨야 하고, 언어 서버 전용 진단(`PSCP2007` 등)은 front-end로 옮기거나 없애야 한다.

---

## 4. 프로세스와 수명 주기

### 4.1 실행 형태

- 언어 서버는 **별도 프로세스**다.
- 전송은 **stdio**, 메시지는 **JSON-RPC 2.0**(`Content-Length` 헤더)이며, 프로토콜은 **LSP 3.17**이다.
- 실행 방법은 두 가지이며 같은 서버 코드를 실행한다.
  - `Pscp.LanguageServer[.exe]`: VSIX에 포함된 Native AOT 바이너리
  - `pscp lsp`: SDK에 포함된 CLI의 하위 명령
- 서버는 stdout에 LSP 메시지 외의 것을 쓰면 안 된다. 로그는 stderr로 쓴다.

### 4.2 initialize

#### 클라이언트 → 서버

클라이언트는 **실제 client capabilities**를 보내야 한다. 서버는 이를 보고 표현을 고른다. 예를 들어 markdown을 지원하지 않는 클라이언트에는 hover를 plaintext로 보내고, snippet을 지원하지 않는 클라이언트에는 snippet 없이 완성 항목을 보낸다.

`initializationOptions`에는 서버가 쓰는 설정([부록 C](#부록-c-설정-표))의 현재 값을 담는다.

```json
{
  "pscp": {
    "inlayHints": { "inferredTypes": true, "rewriteResults": true, "accumulatorTypes": true, "parameterNames": false },
    "hints": { "loweringDiagnostics": false }
  }
}
```

#### 서버 → 클라이언트

`InitializeResult`는 다음을 담아야 한다.

- `serverInfo`: `{ "name": "pscp-language-server", "version": "<도구 버전>" }`
- `capabilities`: 실제로 동작하는 기능만 광고한다. 동작하지 않는 기능을 광고해서는 안 된다(0.6.7은 `codeActionProvider: true`를 광고하지만 항상 빈 목록을 돌려준다).
- `capabilities.experimental.pscp`: PSCP 전용 정보

```json
{
  "experimental": {
    "pscp": {
      "protocolVersion": 1,
      "languageVersion": "0.7",
      "toolVersion": "0.7.0",
      "requests": ["pscp/generatedCSharp"],
      "notifications": ["pscp/status"]
    }
  }
}
```

광고할 표준 capability 전체와 0.6.7 현황은 [부록 A](#부록-a-lsp-메서드-지원표)에 있다.

### 4.3 버전 호환

| 상황 | 확장의 동작 |
|---|---|
| `protocolVersion`이 같다 | 정상 동작 |
| 서버의 `protocolVersion`이 더 낮다 | 서버가 광고하지 않은 PSCP 전용 기능(미리보기 등)을 끈다. 상태 표시줄에 경고 |
| `experimental.pscp`가 없다 (0.6.x 서버) | 표준 LSP 기능만 쓴다. 상태 표시줄에 "구버전 서버" 경고 |
| `languageVersion`이 확장이 기대하는 버전과 다르다 | 상태 표시줄 툴팁과 로그에 두 버전을 표시한다. 기능은 끄지 않는다 |

0.6.7 확장은 SDK 경로일 때만 `pscp version`의 출력을 정규식으로 읽어서 버전을 비교한다. 0.7부터는 서버가 알려 주는 값을 쓴다.

### 4.4 종료와 재시작

- `shutdown` 요청 → `exit` 알림 순서를 따른다. `shutdown` 없이 `exit`를 받으면 종료 코드 1로 끝난다(0.6.7과 같다).
- 서버 프로세스가 예기치 않게 끝나면 확장은 자동으로 다시 시작한다. 단, **3분 안에 5번** 넘게 끝나면 더 시도하지 않고, 상태 표시줄을 `PSCP: stopped`로 바꾼 뒤 로그 열기를 안내한다.
- `PSCP: Restart Language Server` 명령은 항상 다시 시작한다.

---
## 5. 문서와 분석 모델

### 5.1 문서 동기화

- `textDocumentSync`는 `openClose: true`, `change: Full(1)`이다. PSCP 파일은 작고 변경마다 전체를 다시 분석하므로 incremental 동기화의 이득이 작다. `Incremental(2)`로 바꾸는 것은 할 수 있다.
- 서버는 `file:`과 `untitled:` 문서를 모두 분석해야 한다.
- 클라이언트 쪽 디바운스는 필요 없다. 연속된 변경은 서버가 합친다([§5.4](#54-처리-모델)). 0.6.7 자체 클라이언트는 120 ms 디바운스를 쓴다.

### 5.2 위치와 줄

- 위치 인코딩은 **UTF-16**이다(`positionEncoding: "utf-16"`). 클라이언트가 `general.positionEncodings`로 다른 인코딩을 제안해도 UTF-16을 쓰면 된다.
- 줄 경계는 LSP 규정대로 `\n`, `\r\n`, `\r` 셋 다다.
  - 0.6.7의 `LineIndex`는 `\n`만 줄 경계로 본다. `\r\n` 파일은 문제없지만 `\r` 단독 줄 끝 파일에서는 위치가 어긋난다.
- 모든 범위는 front-end의 UTF-16 오프셋에서 변환한다. 서버가 따로 토큰 위치를 계산해서는 안 된다.

### 5.3 분석 결과

문서 버전마다 front-end를 **한 번** 실행해 불변 분석 결과를 만든다. 모든 기능은 이 결과를 조회만 한다. 분석 결과는 최소한 다음을 담는다.

| 항목 | 쓰는 기능 |
|---|---|
| 토큰, 구문 트리(recovery 노드 표시 포함) | semantic tokens, folding, selection range, 완성 문맥 판정 |
| 심볼 표와 참조(선언, 읽기, 쓰기) | definition, references, rename, document highlight, document symbols |
| 식의 정적 타입 | hover, 완성(수신자 타입), inlay hints |
| 진단 (코드 포함) | publishDiagnostics, code actions |
| lowering 사실 | hover, inlay hints, 생성 C# 미리보기 |
| space-call과 pipe의 **rewrite 결과** (`min (a, b) c` → `min(a, b, c)`) | hover, signature help |

구문 오류가 있어도 분석은 가능한 범위까지 진행한다([§6.4](#64-publish-규칙)).

### 5.4 처리 모델

단일 파일 편집기에 맞춘 단순한 모델이다. 다음 규칙을 지켜야 한다.

1. 메시지는 **받은 순서대로** 처리한다.
2. `didChange`를 받으면 문서 저장소의 텍스트와 버전을 즉시 바꾸고, 분석은 **예약**한다. 분석을 시작할 때 같은 문서의 더 새 버전이 이미 와 있으면 그 버전을 분석한다. 중간 버전은 분석하지 않는다(coalescing).
3. 요청(hover, completion 등)은 **그 문서의 최신 버전**의 분석 결과로 답한다. 그 버전의 분석이 아직 끝나지 않았으면 끝난 뒤에 답한다.
4. `$/cancelRequest`를 받았을 때 그 요청을 아직 처리하지 않았으면 `RequestCancelled`(-32800) 오류로 답하고 버린다. 이미 답한 요청이면 무시한다.
5. 분석 중에 더 새 버전이 도착하면 진행 중인 분석을 중단할 수 있다. 중단한 분석의 결과는 쓰지 않는다.
6. 오래된 버전의 진단을 publish해서는 안 된다. `publishDiagnostics`에는 항상 `version`을 넣는다.

v0.6 가이드가 요구했던 foreground/background 큐 분리, 공유 future, 작업 영역 심볼 인덱스는 **요구하지 않는다**. 단일 파일을 100 ms 안팎에 분석하는 규모에서는 위 규칙으로 충분하다.

0.6.7 현황:
- 메시지를 순서대로 처리하고, `didChange` 처리 안에서 분석을 동기적으로 끝낸다. 결과적으로 규칙 1, 3, 6은 지켜진다.
- 규칙 2(coalescing)와 4(`$/cancelRequest`)는 없다. `$/cancelRequest`는 무시된다.

### 5.5 열리지 않은 문서

- 요청의 URI가 열려 있지 않으면 디스크에서 읽어 버전 0으로 분석한다(0.6.7과 같다).
- `didClose`를 받으면 그 문서의 진단을 빈 목록으로 publish한다(0.6.7과 같다).

### 5.6 성능

#### 기준 측정 (0.6.7)

측정 조건: Release 빌드 `pscp lsp`(JIT, Native AOT 아님), Linux x64. 대상은 함수 260개와 builder로 만든 2,081줄 파일이다. 요청은 Python LSP 클라이언트로 보냈다.

| 항목 | 측정값 |
|---|---|
| 프로세스 시작 + `initialize` 응답 | 2.5 s |
| `didOpen` → `publishDiagnostics` | 138 ms |
| `didChange` → `publishDiagnostics` (5회) | 96–126 ms |
| `hover` | 0.3–1.9 ms |
| `completion` (후보 222개) | 1.8–2.6 ms |
| `semanticTokens/full` | 37 ms |
| `didChange` 직후의 `hover` | 96 ms (분석이 끝날 때까지 기다림) |

#### 0.7 예산

같은 조건, 2,000줄 파일, p95 기준이다.

| 항목 | 예산 |
|---|---|
| `didChange` → `publishDiagnostics` | 200 ms 이하 |
| `hover`, `completion`, `signatureHelp` (분석이 끝난 뒤) | 20 ms 이하 |
| `semanticTokens/full` | 60 ms 이하 |
| `initialize` (Native AOT 번들 서버) | 500 ms 이하 |

- 10,000줄 파일에서도 분석은 2 s 안에 끝나야 하고 서버가 죽어서는 안 된다.
- 그보다 큰 파일에서는 semantic tokens와 inlay hints를 빈 결과로 돌려줄 수 있다(축소 모드). 진단은 계속 publish한다.
- 테스트 하네스에 이 측정을 재현하는 벤치마크를 둔다([§16.2](#162-07에서-더할-테스트)). CI는 예산의 3배를 넘을 때만 실패시킨다.

### 5.7 .NET 메타데이터

pass-through .NET 코드(`Math.Max`, `list.Add`, `SortedDictionary` 등)의 완성, hover, signature help에는 .NET 타입 정보가 필요하다.

- **출처는 생성 C#이 실제로 컴파일되는 대상 프레임워크의 참조 어셈블리다.** 기본은 도구가 대상으로 하는 최신 .NET이고, `--older`를 쓰는 설정이면 net6.0이다. 서버는 .NET SDK 설치 경로의 참조 팩(`packs/Microsoft.NETCore.App.Ref/<버전>/ref/<tfm>`)을 `MetadataLoadContext`로 읽는다.
- 참조 팩을 찾지 못하면 서버 자신의 런타임을 reflection으로 읽고, 그 사실을 로그에 남긴다.
- 보이는 namespace는 트랜스파일러가 생성 코드에 넣는 기본 `using`과 사용자가 쓴 `using`이다. 이 목록은 트랜스파일러와 같은 곳에서 가져온다.
- 메타데이터는 처음 필요할 때 읽고 프로세스가 끝날 때까지 캐시한다. 처음 읽는 동안에도 PSCP 자체 기능(진단, intrinsic 완성)은 기다리지 않고 답한다.

0.6.7 현황: 서버 런타임에서 정해진 타입 24개(`Math`, `Console`, `List`, `Dictionary`, `PriorityQueue` 등)만 reflection으로 읽는다. `SortedDictionary`, `StringBuilder` 같은 타입은 완성과 hover에 나오지 않는다.

---

## 6. 진단

### 6.1 출처

- 오류와 경고는 **front-end만** 만든다. 언어 서버가 자체 규칙으로 오류나 경고를 더해서는 안 된다.
- 그래서 같은 파일에 대해 언어 서버와 `pscp check`의 오류/경고가 같다([§2](#2-설계-원칙) 원칙 2).

0.6.7 현황: 언어 서버가 자체 진단 두 가지를 더한다.

| 코드 | 내용 | 문제 |
|---|---|---|
| `PSCP2007` | Cannot assign to immutable binding (**오류**) | 트랜스파일러는 같은 상황을 **경고**로 보고한다. 편집기에는 빨간 오류와 노란 경고가 같은 자리에 함께 뜨는데, `pscp check`는 경고만 내고 컴파일도 된다 |
| `PSCP2008` | `break`/`continue` outside a loop (오류) | 언어 서버에만 있는 판정이다. front-end로 옮겨야 한다 |

```txt
int y = 3
y = 4
```

| 도구 | 결과 |
|---|---|
| 언어 서버 0.6.7 | `2:1 error PSCP2007` Cannot assign to immutable binding `y`. / `2:1 warning PSCP3001` `y` is immutable but is modified here. … |
| `pscp check` 0.6.7 | `2:1: warning:` `y` is immutable but is modified here. … (종료 코드 0) |

### 6.2 코드 체계

모든 진단은 **종류마다 고유한 코드**를 가진다. 등급은 코드의 속성이며 범주 번호와 관계없다.

| 범위 | 범주 |
|---|---|
| `PSCP1000`–`PSCP1099` | 어휘 (문자, 리터럴, 주석) |
| `PSCP1100`–`PSCP1199` | 구문 |
| `PSCP2000`–`PSCP2099` | 제어 흐름 |
| `PSCP2100`–`PSCP2199` | 이름, 바인딩, 선언 |
| `PSCP2200`–`PSCP2299` | 함수, 반환, 호출, 연산자 |
| `PSCP2300`–`PSCP2399` | range, 인덱싱, 컬렉션 식 |
| `PSCP2400`–`PSCP2499` | 입력과 출력 |
| `PSCP2500`–`PSCP2599` | intrinsic, 순서, 자료구조 rewrite |
| `PSCP2600`–`PSCP2699` | 타입과 멤버 |
| `PSCP2900`–`PSCP2999` | 폐기 예정 |
| `PSCP5000`–`PSCP5999` | 편집기 전용 정보와 힌트 (CLI에는 나오지 않는다) |

코드 전체 목록과 대응하는 스펙 절, 빠른 수정은 [부록 B](#부록-b-진단-코드-표)에 있다.

호환을 위해 0.6.7의 `PSCP1000`(어휘 일반)과 `PSCP1001`(구문 일반)은 그대로 둔다. `PSCP1001`은 구문 진단이지만 어휘 범위에 있는 예외다. 이행용 `PSCP3001`, `PSCP3002`도 범위 규칙 밖에 있다.

#### 0.6.7 코드에서 옮기기

0.6.7의 코드는 진단 종류가 아니라 **출처별 범주**였다. 그래서 빠른 수정을 특정 진단에 연결할 수 없었다.

| 0.6.7 코드 | 뜻 | 0.7 |
|---|---|---|
| `PSCP1000` | lexer 진단 전부 | 일반 어휘 오류 코드로 유지. 구체적인 경우는 `PSCP10xx` |
| `PSCP1001` | parser 진단 전부 | 일반 구문 오류 코드로 유지. 구체적인 경우는 `PSCP11xx` |
| `PSCP2007` | 불변 대입 (언어 서버, 오류) | 없앤다. front-end의 `PSCP2108`(경고)로 대체 |
| `PSCP2008` | 루프 밖 `break`/`continue` (언어 서버) | 코드는 유지하고 판정을 front-end로 옮긴다 |
| `PSCP3001` | 트랜스파일러 경고 전부 | 이행 기간의 대체 코드. 새 진단은 모두 구체 코드를 가진다 |
| `PSCP3002` | 트랜스파일러 오류 전부 | 위와 같다 |

### 6.3 LSP 표현

| 필드 | 규칙 |
|---|---|
| `range` | **문제가 일어난 위치**. 예: 불변 바인딩 수정 경고는 수정하는 식을 가리킨다. 선언은 `relatedInformation`으로 준다 |
| `severity` | `1` 오류, `2` 경고, `3` 정보, `4` 힌트 |
| `code` | `"PSCP2108"` 같은 문자열 |
| `codeDescription.href` | 해당 스펙 절의 URL (예: `…/docs/pscp_v_0_7_spec.md#92-불변성의-의미와-진단`) |
| `source` | `"pscp"` |
| `message` | CLI와 같은 문장 |
| `tags` | 폐기 예정 이름에는 `Deprecated(2)`(취소선). 필요 없는 `rec`, 결과를 쓰지 않는 단항식에는 `Unnecessary(1)`(흐리게) |
| `relatedInformation` | 선언 위치, 상호 재귀 그룹의 다른 함수 등 |
| `data` | 빠른 수정 계산에 필요한 정보 (예: `{ "replacement": "readInt" }`) |

0.6.7 현황:
- `code`, `source`, `severity`, `range`, `message`만 보낸다.
- 입력 shorthand로 선언한 바인딩을 `s -= 1`로 수정하면, 경고의 범위가 수정 위치가 아니라 선언(`int n, m, s, t =`의 `s`)을 가리킨다. 메시지는 "is modified here"다. `tests/TestCodes/09_dinic_maxflow.pscp`에서 언어 서버와 CLI 모두 `6:11`을 가리킨다.

### 6.4 publish 규칙

- 한 번의 publish는 그 버전의 진단 **전체**다. 이전 목록에 더하는 방식이 아니다.
- 진단은 위치 순서로 정렬한다.
- **구문 오류가 있어도 의미 진단은 계속한다.** recovery 노드가 걸린 문장만 의미 분석에서 제외한다. 구문 오류 범위와 겹치는 의미 진단은 억제한다(연쇄 오류 방지).

0.6.7 현황: lexer나 parser 오류가 하나라도 있으면 트랜스파일러 의미 분석 전체를 건너뛴다. 그래서 긴 파일에서 오타 하나를 치는 순간 다른 모든 경고가 사라졌다가, 오타를 고치면 다시 나타난다.

실제 예: 2, 3번째 줄에 경고(`PSCP3001`)가 있는 파일 끝에 `let z = (1 +`을 입력하면 두 경고가 모두 사라진다. 대신 같은 위치에 똑같은 `PSCP1001` 구문 오류가 두 개 뜬다. 언어 서버 자체 진단인 `PSCP2007`만 계속 남는다.

### 6.5 편집기 전용 진단

`PSCP5xxx`는 편집기에만 나오는 정보/힌트다. CLI 출력과 [§2](#2-설계-원칙) 원칙 2의 비교 대상이 아니다.

| 코드 | 등급 | 내용 | 기본값 |
|---|---|---|---|
| `PSCP5001` | 정보 | 사용자 선언이 intrinsic 이름을 가린다 (`let stdin = 3`) | 켜짐 |
| `PSCP5101` | 정보 | 컴파일 시점 상수라 C# `const`로 낮아진다 | 꺼짐 |
| `PSCP5102` | 정보 | generator를 aggregate가 바로 소비해 fused loop로 낮아진다 | 꺼짐 |

- 꺼짐으로 표시한 lowering 정보는 설정 `pscp.hints.loweringDiagnostics`로 켠다. 같은 정보를 hover에서는 항상 보여 준다.
- 0.6.7 트랜스파일러는 `PSCP5001`에 해당하는 상황을 **경고**로 낸다. 0.7 스펙은 이를 경고로 정하지 않았으므로 CLI 경고에서 빼고 편집기 정보로 옮긴다.

### 6.6 CLI 출력 형식

`pscp check`, `pscp transpile`, `pscp build`, `pscp run`은 진단을 다음 형식으로 출력한다. 코드가 추가된다.

```txt
2:1: warning PSCP2108: `y` is immutable but is modified here. Declare it with `mut` or `var` to make the mutation explicit.
```

도구 연동을 위해 `pscp check --json`을 둔다. LSP `Diagnostic`과 같은 필드(`range`는 0부터 시작)를 가진 배열을 출력한다.

---
## 7. hover

### 7.1 형식

hover는 **PSCP 코드가 무슨 뜻이고 C#으로 어떻게 낮아지는지**를 보여 준다. 내용은 다음 순서다. 해당하지 않는 줄은 뺀다.

````txt
```pscp
<대상의 모양 또는 시그니처>
```
<의미 한두 문장>

→ C#: `<lowering 결과>`
전제조건: <있으면>
[스펙 §x.y](<스펙 절 URL>)
````

- 클라이언트가 markdown을 지원하면 `MarkupContent`(`kind: "markdown"`)로, 아니면 같은 내용을 plaintext로 보낸다.
- 10줄 안팎으로 쓴다. 긴 설명은 스펙 링크로 넘긴다.
- 문장은 intrinsic 카탈로그와 front-end의 lowering 사실에서 만든다. 서버에 문장을 하드코딩해서는 안 된다.
- `range`는 hover 대상 토큰(또는 연산자)의 범위다.

### 7.2 대상별 내용

| 대상 | 보여 줄 것 | 0.6.7 |
|---|---|---|
| 입력 shorthand의 `=` | 읽는 방법(토큰/문자), 개수 식, lowering (`stdin.readInt()` × `n`) | 부분 (고정 문장 "lowers to the corresponding `stdin.*` helper") |
| 출력 shorthand `=` / `+=` | write/writeln, 값의 정적 타입에 해당하는 렌더링 규칙 (예: `char[]` → 구분자 없이, 2차원 → 줄 단위) | 부분 (고정 문장) |
| 바인딩 이름 | 선언 형태(`let`/`var`/`mut`/명시 타입), 타입, 불변 여부, `const`/`readonly`로 낮아지는지, 최상위 변수가 static 필드로 옮겨지는지 | 부분 (타입과 가변성만) |
| 함수 이름 | 시그니처, 최상위/로컬, `rec` 여부와 상호 재귀 그룹 | 부분 |
| 자료구조 rewrite (`+=`, `-=`, `~`, 전위 `--`) | 대상 메서드와 반환 타입 (`HashSet<int>.Add(x)` → `bool`) | 없음 |
| `:=` | 값을 내는 대입, 결과 타입 | 없음 |
| 식 안의 `=` | 호환용 대입이며 경고 대상, `:=` 권장 | 없음 |
| space-call head | 인자 결합 결과 (`min (a, b) c` → `min(a, b, c)`), 고른 오버로드 | 없음 |
| `\|>`, `<\|` | rewrite 결과 (`xs \|> filter(p)` → `xs.filter(p)`) | 없음 |
| range, 슬라이스 | 원소 타입, 끝 포함 여부, 인덱서 안에서는 C# lowering (`a..=b` → `a..(b+1)`) | 없음 |
| `[]`, builder, generator | materialized/lazy, 결과 타입, 즉시 소비되어 fused loop가 되는지 | 없음 |
| 변환 키워드 | 변환 표의 해당 행 (예: `int 3.7` → 0 방향 버림) | 없음 |
| aggregate, math, collection helper | 고른 오버로드의 시그니처, 결과 타입, 전제조건, 누산 타입 규칙 | 부분 (고정 문장) |
| `T.asc`, `T.desc` | `IComparer<T>`, 기본 순서 요약 (문자열은 ordinal) | 부분 |
| `operator<=>` | 생성되는 멤버 (`IComparable<T>`, `CompareTo`, `<` `<=` `>` `>=`) | 없음 |
| intrinsic을 가린 사용자 이름 | 사용자 심볼 설명 + "intrinsic `sum`을 가린다" | 부분 (사용자 심볼만) |
| 폐기 예정 이름 | "폐기 예정 (v0.8 제거)" + 대체 이름 | 없음 |

0.6.7에서 직접 확인한 예:
- `visited += 3`의 `+=`, `parent[x] := …`의 `:=`, `a.sum()`의 `sum` 위에서는 hover가 **없다**.
- v0.6 가이드가 대표 예로 든 "`visited += x` 위에 올리면 HashSet.Add returning bool이 보인다"는 구현되지 않았다.

### 7.3 예시

`HashSet<int> visited`가 있을 때 `if not (visited += x) then continue`의 `+=`:

````txt
```pscp
visited += x      // HashSet<int>
```
자료구조 rewrite: 원소를 추가하고, 새로 추가됐으면 `true`를 돌려준다.

→ C#: `visited.Add(x)` : `bool`
[스펙 §26 자료구조 연산자 rewrite](…)
````

`int[n][m] grid =`의 `=`:

````txt
```pscp
int[n][m] grid =
```
입력 shorthand: `int` 값 `n × m`개를 토큰 단위로, 행 우선 순서로 읽는다.

→ C#: `grid[i][j] = stdin.readInt()` (이중 루프)
[스펙 §17.1 선언 기반 입력 shorthand](…)
````

`let MOD = 1_000_000_007`의 `MOD`:

````txt
```pscp
let MOD: int = 1000000007
```
불변 바인딩. 컴파일 시점 상수라 타입 선언 안에서도 쓸 수 있다.

→ C#: `const int MOD = 1000000007;`
[스펙 §9.6 const / readonly lowering](…)
````

`xs |> map(f) |> sum`의 두 번째 `|>`:

````txt
```pscp
sum (xs.map(f))
```
pipe: 왼쪽 값을 `sum`의 첫 번째 인자로 넣는다.
[스펙 §13.3 pipe](…)
````

---

## 8. 자동완성

### 8.1 문맥

| 문맥 | 판정 | 후보 |
|---|---|---|
| 문장 시작 | 줄의 첫 토큰, 또는 `;` `{` `then` `else` `do` 바로 뒤 | 문장 키워드(`let` `var` `mut` `rec` `if` `for` `while` `return` `break` `continue` `class` `struct` `record` `using`), 출력 shorthand(`= `, `+= `), 식 시작 후보 전부 |
| 식 위치 | 그 밖에 식이 올 자리 | 지역 변수·매개변수·함수, 최상위 심볼, aggregate·math·변환 키워드, `stdin`·`stdout`, 타입 이름, .NET 타입, `true` `false` `null` `new` `not` `if` |
| 멤버 (`x.`) | 수신자의 **정적 타입**으로 결정 | 실제 멤버(.NET 필드·속성·메서드), 확장 메서드, PSCP member alias(aggregate `sum` `min` `max` `sumBy` `minBy` `maxBy`, collection helper 전부, 배열·`List<T>`에서는 `lowerBound` `upperBound`) |
| `stdin.` | `stdin`이 가려지지 않았을 때 | 정식 `read*` 이름, `hasNext`, `hasNextLine` |
| `stdout.` | 같음 | `write` `writeln` `flush` `lines` `grid` `join` |
| 타입 이름 뒤 `.` | `int.`, `Point.` 등 | `asc`, `desc`, 정적 멤버(`MaxValue`, `Parse` …) |
| pipe 대상 | `\|>` 바로 뒤 | 함수, aggregate, **collection helper 이름**(이 자리에서만), 변환 키워드 |
| 타입 자리 | 선언의 타입 부분, `<` `>` 안, `new` 뒤 | 타입만 |
| 패턴 문맥 | `is` 뒤, switch arm 패턴 | 타입, 상수, `not` `and` `or` `null` |
| 보간 hole | `$"…{` 안 | 식 위치와 같음 |
| `using` 뒤 | | namespace |
| 주석, 문자열 리터럴 안 | | 없음 |

### 8.2 제안하지 않는 것

- **collection helper의 자유 함수 형태.** `map(xs, f)`처럼 쓰는 형태는 스펙에 없다([스펙 §24.1](pscp_v_0_7_spec.md#241-호출-형태)). pipe 대상 자리를 빼고는 제안하지 않는다.
  - 0.6.7은 식 위치에서 `map` `filter` `fold` `scan` `mapFold` `sort` `sortBy` `sortWith` `distinct` `reverse` `copy`를 자유 함수로 제안한다. 이렇게 입력한 코드는 트랜스파일러가 `Undefined name`으로 거부한다.
- **가려진 intrinsic.** 사용자 선언이 이름을 가리면 사용자 심볼만 제안한다.
- **폐기 예정 이름** ([스펙 부록 C](pscp_v_0_7_spec.md#부록-c-폐기-예정-목록)).
- **그 문맥에 올 수 없는 키워드.** 0.6.7은 식 위치에서도 `class` `namespace` `using` `operator` `where`를 제안한다.

### 8.3 정렬

`sortText`로 다음 그룹 순서를 만든다. 같은 그룹 안에서는 이름 순서다.

| 순서 | 그룹 |
|---|---|
| 0 | 지역 변수, 매개변수, 로컬 함수 (가까운 scope가 먼저) |
| 1 | 현재 타입의 멤버 |
| 2 | 최상위 함수, 변수, 타입 |
| 3 | PSCP intrinsic (aggregate, math, 변환 키워드, `stdin`, `stdout`) |
| 4 | .NET 타입과 namespace |
| 5 | 키워드와 스니펫 |

멤버 완성에서는 필드·속성 → PSCP member alias → .NET 메서드 → 확장 메서드 순서다.

0.6.7은 모든 후보(식 위치에서 122개)를 이름 사전순으로 섞는다. 지역 변수 `a`, `xs`가 키워드 `and`, `base`, `break` 사이에 흩어진다.

### 8.4 항목 형식

| 필드 | 규칙 |
|---|---|
| `label` | 이름 |
| `labelDetails.detail` | 매개변수 모양. 예: `(seed, f)` |
| `labelDetails.description` | 결과 타입. 예: `int[]` |
| `kind` | 아래 표 |
| `detail` | 사람이 읽는 시그니처. **snippet 문법(`${1:x}`)을 넣어서는 안 된다** |
| `documentation` | intrinsic 카탈로그의 문서 (markdown) |
| `insertText` / `insertTextFormat` | 클라이언트가 snippet을 지원할 때만 snippet(`2`) |
| `sortText` | [§8.3](#83-정렬) |

0.6.7은 `detail`에 insert용 snippet 문자열을 그대로 넣는다. 그래서 VS Code 목록에 `map(${1:values}, ${2:selector})`가 글자 그대로 보인다.

| 심볼 | `kind` (LSP 번호) |
|---|---|
| 지역 변수, 매개변수, 최상위 변수, `stdin`, `stdout` | Variable (6) |
| 최상위 컴파일 시점 상수 (`let MOD = …`) | Constant (21) |
| 함수, 로컬 함수, aggregate, math, 변환 키워드 | Function (3) |
| 메서드, collection helper | Method (2) |
| 속성 | Property (10) |
| 필드 | Field (5) |
| class, record | Class (7) |
| struct, record struct | Struct (22) |
| interface | Interface (8) |
| enum | Enum (13) |
| 타입 매개변수 | TypeParameter (25) |
| namespace | Module (9) |
| 키워드 | Keyword (14) |
| 스니펫 | Snippet (15) |

### 8.5 배열과 컬렉션 수신자

- **배열(`T[]`)에서도 멤버 완성이 나와야 한다.** 후보는 `Length` 등 .NET 멤버, collection helper, aggregate alias, `lowerBound`/`upperBound`다.
  - 0.6.7에서 `int[n] a =`로 선언한 `a` 뒤에 `.`을 찍으면 후보가 **0개**다. `List<int>` 수신자에서는 후보가 나온다.
- .NET의 **제자리** 메서드와 PSCP의 **새 배열을 돌려주는** helper는 설명으로 구별한다([스펙 §24.4](pscp_v_0_7_spec.md#244-결과를-버리는-호출)).

| 후보 | `labelDetails.description` | `documentation` 첫 줄 |
|---|---|---|
| `Sort` (`List<T>`) | `void` | 리스트를 **제자리에서** 정렬한다 |
| `sort` | `T[]` | 정렬한 **새 배열**을 돌려준다. 원본은 바뀌지 않는다 |
| `Reverse` (`List<T>`) | `void` | 제자리에서 뒤집는다 |
| `reverse` | `T[]` | 뒤집은 새 배열을 돌려준다 |

### 8.6 트리거와 스니펫

- 트리거 문자는 `.`뿐이다. 공백은 완성의 트리거가 아니다.
- 문장 시작 문맥에서 다음 스니펫을 제공할 수 있다: 테스트 케이스 루프(`int t =` + `for _ in 0..<t { }`), `rec` 함수, `record struct`, `->` 순회. 타입 본문에서는 `operator<=>`.

---

## 9. signature help

### 9.1 대상

괄호 호출, **space-call**, 멤버 호출, `stdin`·`stdout` 멤버, 사용자 함수, intrinsic 전부.

### 9.2 트리거

- 트리거 문자: `(` `,` 그리고 **공백**
- 재트리거 문자: `,` 공백
- 공백으로 트리거됐을 때는 커서 바로 앞이 호출 가능한 head이거나 application chain의 중간일 때만 결과를 준다. 아니면 `null`이다.

### 9.3 활성 매개변수

space-call에서는 [스펙 §11.2](pscp_v_0_7_spec.md#112-space-call)의 인자 결합 규칙으로 현재 인자 번호를 센다. 괄호 그룹 안의 원소는 각각 인자 하나다.

| 입력 중인 코드 (`│`는 커서) | 활성 매개변수 |
|---|---|
| `clamp x lo │` | 2 (`hi`) |
| `max (a, b) │` | 2 (세 번째 값) |
| `sumBy edges │` | 1 (`selector`) |

### 9.4 오버로드

오버로드가 있는 intrinsic은 모든 시그니처를 보여 주고, 인자 개수와 타입으로 `activeSignature`를 고른다.

| 이름 | 시그니처 |
|---|---|
| `min`, `max` | `(a, b, …)` / `(xs)` |
| `find` | `(pred)` / `(pred, fallback)` |
| `pow` | `(a, e)` / `(a, e, m)` |
| `floor`, `ceil` | `(x)` / `(a, b)` |
| `round` | `(x)` / `(x, d)` |
| `lowerBound`, `upperBound` | `(x)` / `(x, cmp)` |
| `sortWith` | `(IComparer<T>)` / `((T, T) → int)` |
| `stdout.join` | `(string sep, xs)` / `(char sep, xs)` |
| `any`, `count` | `()` / `(pred)` |

`ParameterInformation.label`은 시그니처 문자열 안의 `[시작, 끝]` 오프셋으로 준다. 그래야 VS Code가 현재 매개변수를 강조한다.

0.6.7 현황:
- 괄호 호출만 지원한다. `min a[0] │`에서는 결과가 없다.
- intrinsic마다 시그니처가 하나다. `min`도 `min(left, right)` 하나뿐이다.
- 시그니처 표에 같은 키가 두 번 들어 있어 뒤의 것이 앞의 것을 덮는다. 예: 자유 함수 `sort(values)`를 멤버 `xs.sort()`가 덮는다.

---

## 10. 탐색과 편집

### 10.1 정의로 이동

- 사용자 심볼은 선언 위치로 간다.
- 스펙의 가시성 규칙대로 해석해야 한다([스펙 §7.3](pscp_v_0_7_spec.md#73-프로그램-scope), [§10.2](pscp_v_0_7_spec.md#102-함수의-가시성)). **앞에서 호출한 로컬 함수**와 **뒤에 선언된 최상위 변수**도 찾아야 한다.
- intrinsic과 .NET 심볼은 `null`이다. 설명은 hover에서 보여 준다.

0.6.7 현황: 다음 두 경우 hover와 정의로 이동이 모두 동작하지 않는다. 트랜스파일러는 두 경우를 모두 정상 처리한다.

```txt
rec int find(int x) {
    if x == 0 then x
    else parent[x] := find(parent[x])   // parent: 뒤에 선언된 최상위 변수
}
int[] parent = [0..<n]

int solve(int x) {
    int inner(int w) { deep(w) + x }     // deep: 뒤에 선언된 로컬 함수
    int deep(int z) { z * 2 }
    inner(x)
}
```

### 10.2 참조와 강조

- `textDocument/references`는 심볼 표의 모든 참조를 돌려준다. `includeDeclaration`을 지킨다(0.6.7 있음).
- `textDocument/documentHighlight`를 새로 제공한다. 읽기 참조는 `Read(2)`, 쓰기 참조는 `Write(3)`로 표시한다.

### 10.3 이름 바꾸기

- `prepareRename`은 다음을 거부한다: intrinsic, `_`, 튜플 projection(`.1`), 키워드, .NET 멤버.
- 거부할 때는 `null`이 아니라 **오류 응답**(`message`에 이유)을 보낸다. 그래야 VS Code가 이유를 보여 준다.
- 새 이름은 다음을 모두 만족해야 한다.
  - 식별자 규칙([스펙 §3.2](pscp_v_0_7_spec.md#32-식별자))을 따르고, 예약 키워드([§3.3](pscp_v_0_7_spec.md#33-키워드))가 아니며, `__pscp`로 시작하지 않는다.
  - 같은 scope의 다른 선언과 겹치지 않는다.
  - 바꾼 뒤에 **다른 심볼로 해석이 바뀌는 참조가 없다**(가리기 충돌).
- 새 이름이 intrinsic을 새로 가리는 것은 허용한다. 바꾼 뒤 `PSCP5001` 정보가 나온다.
- 한 파일 안의 모든 참조를 바꾼다. 타입 멤버는 `obj.member` 형태의 참조까지 바꾼다.

0.6.7 현황:
- 충돌 검사가 없다. `n`이 이미 있는 파일에서 `sum`을 `n`으로 바꿔도 편집을 돌려준다.
- 키워드(`then`)로 바꾸려 하면 이유 없이 `null`을 돌려준다.

### 10.4 문서 기호 (개요)

계층형 `DocumentSymbol`을 돌려준다.

| 수준 | 포함하는 것 |
|---|---|
| 최상위 | 타입, 함수, 최상위 변수 (입력 선언 포함) |
| 타입 아래 | 필드, 속성, 생성자, 메서드, `operator<=>` |
| 함수 아래 | 로컬 함수 (지역 변수는 넣지 않는다) |

`detail`에는 타입이나 시그니처를 넣고, 입력 선언에는 "입력"을 넣는다.

| 심볼 | `kind` (LSP 번호) |
|---|---|
| class, record | Class (5) |
| struct, record struct | Struct (23) |
| interface | Interface (11) |
| enum | Enum (10) |
| 함수, 로컬 함수 | Function (12) |
| 메서드 | Method (6) |
| 생성자 | Constructor (9) |
| 필드 | Field (8) |
| 속성 | Property (7) |
| `operator<=>` | Operator (25) |
| 최상위 컴파일 시점 상수 | Constant (14) |
| 그 밖의 최상위 변수 | Variable (13) |

0.6.7 현황:
- 목록이 평면이다(`children`이 항상 비어 있다).
- 일부 선언이 빠진다. `tests/TestCodes/09_dinic_maxflow.pscp`의 개요는 `Edge, level, it, bfs, dfs, maxFlow`뿐이다. 입력 선언 `int n, m, s, t =`, `List<Edge>[] g = new![n]`, 함수 `void addEdge(…)`가 없다.
- 확장 쪽 변환 버그 때문에 VS Code에서는 아이콘도 틀린다([§14.1](#141-lsp-클라이언트)).

### 10.5 접기와 선택 범위

새로 제공한다.

- `textDocument/foldingRange`: `{ }` 블록, 여러 줄에 걸친 괄호·컬렉션 식, 연속된 `using`, 연속된 주석, `// #region` ~ `// #endregion`
- `textDocument/selectionRange`: 토큰 → 식 → 문장 → 블록 → 함수 → 파일

### 10.6 포매팅

0.7에서는 제공하지 않는다. 스펙에 PSCP 포매팅 규칙이 없기 때문이다. `documentFormattingProvider`를 광고하지 않는다.

---
## 11. semantic tokens

### 11.1 legend는 서버가 정한다

- 서버는 `initialize` 응답의 `semanticTokensProvider.legend`로 토큰 종류와 수식어 목록을 알린다.
- 클라이언트는 **서버가 알린 legend**로 결과를 해석해야 한다. 클라이언트에 legend를 따로 적어서는 안 된다.
  - 0.6.7 확장은 서버와 같은 목록을 `extension.js`에 하드코딩한다. 서버가 목록을 하나만 바꿔도 모든 색이 조용히 어긋난다.
- PSCP 전용 수식어는 확장의 `package.json`에 `contributes.semanticTokenModifiers`로 선언한다. 그래야 테마와 사용자 설정(`editor.semanticTokenColorCustomizations`)에서 이름으로 꾸밀 수 있다.

### 11.2 legend

**토큰 종류** (표준): `namespace` `type` `class` `enum` `interface` `struct` `typeParameter` `parameter` `variable` `property` `enumMember` `function` `method` `keyword` `modifier` `comment` `string` `number` `operator`

**수식어**

| 수식어 | 종류 | 뜻 |
|---|---|---|
| `declaration` | 표준 | 선언 위치 |
| `readonly` | 표준 | 불변 바인딩, 불변 필드 |
| `static` | 표준 | static 멤버 |
| `deprecated` | 표준 | 폐기 예정 이름 |
| `defaultLibrary` | 표준 | intrinsic과 .NET 기본 라이브러리 |
| `modification` | 표준 | 값을 바꾸는 위치 |
| `mutable` | PSCP | `var`, `mut` 바인딩과 가변 필드 |
| `intrinsic` | PSCP | PSCP intrinsic (`stdin`, `sum`, `map`, 변환 키워드 …) |
| `shorthand` | PSCP | 입출력 shorthand의 `=`, `+=` |
| `rewrite` | PSCP | 자료구조 rewrite가 적용된 연산자 |

`package.json` 선언 예:

```json
"semanticTokenModifiers": [
  { "id": "mutable",   "description": "PSCP var/mut binding or mutable field" },
  { "id": "intrinsic", "description": "PSCP intrinsic (stdin, sum, map, ...)" },
  { "id": "shorthand", "description": "PSCP input/output shorthand operator" },
  { "id": "rewrite",   "description": "Operator rewritten to a .NET collection call" }
]
```

### 11.3 분류 표

| 대상 | 종류 | 수식어 |
|---|---|---|
| `let`·명시 타입의 불변 바인딩 | `variable` | `readonly` |
| `var`·`mut` 바인딩 | `variable` | `mutable` |
| 바인딩을 수정하는 위치 | `variable` | `modification` (+ `readonly` 또는 `mutable`) |
| 매개변수 | `parameter` | |
| 최상위 함수, 로컬 함수 | `function` | |
| 메서드 | `method` | |
| 필드, 속성 | `property` | 불변이면 `readonly`, 가변이면 `mutable` |
| class, record | `class` | |
| struct, record struct | `struct` | |
| interface, enum, 타입 매개변수 | `interface`, `enum`, `typeParameter` | |
| `stdin`, `stdout` | `variable` | `defaultLibrary` `intrinsic` |
| aggregate, math, 식 위치의 변환 키워드 | `function` | `defaultLibrary` `intrinsic` |
| collection helper, member alias | `method` | `defaultLibrary` `intrinsic` |
| 입력 shorthand의 `=` | `operator` | `shorthand` |
| 출력 shorthand의 `=`, `+=` | `operator` | `shorthand` |
| 자료구조 rewrite 연산자 (`+=`, `-=`, `~`, 전위 `--`) | `operator` | `rewrite` |
| 폐기 예정 이름 | 원래 종류 | `deprecated` 추가 |
| intrinsic 이름을 가린 사용자 심볼 | 사용자 심볼의 분류 | (`intrinsic`, `defaultLibrary` 없음) |
| .NET 타입과 멤버 | 해당 종류 | `defaultLibrary` |

같은 `+=`라도 문장 시작이면 `shorthand`, `List<T>`에 쓰면 `rewrite`, `int`에 쓰면 수식어가 없다. TextMate 문법은 이 차이를 알 수 없으므로 semantic tokens가 구별해 준다.

### 11.4 요청

| 요청 | 요구 | 0.6.7 |
|---|---|---|
| `textDocument/semanticTokens/full` | 필수 | 있음 |
| `textDocument/semanticTokens/range` | 권장 (큰 파일에서 보이는 부분만) | 없음 |
| `textDocument/semanticTokens/full/delta` | 선택 | 없음 |

---

## 12. inlay hints와 code actions

### 12.1 inlay hints

- 요청의 `range` 안에 있는 힌트만 돌려준다.
  - 0.6.7은 `range`를 무시하고 문서 전체의 힌트를 돌려준다. `0–5`줄을 요청해도 48줄의 힌트가 온다.
- 힌트 종류마다 설정으로 켜고 끈다([부록 C](#부록-c-설정-표)).
- 힌트에는 `tooltip`으로 설명을 붙인다. 타입 힌트의 label은 label parts로 주고, 사용자 타입 part에는 `location`을 붙여서 클릭하면 정의로 가게 한다.

| 종류 | 위치와 모양 | 설정 (기본값) | 0.6.7 |
|---|---|---|---|
| 추론 타입 | `let x` 이름 뒤에 `: int` | `inferredTypes` (켜짐) | 있음 |
| rewrite 결과 | **값으로 쓰인** 자료구조 rewrite 뒤에 `: bool`. 예: `if not (visited += x)`. 문장으로만 쓰이면 표시하지 않는다 | `rewriteResults` (켜짐) | 없음 |
| 넓힌 누산 타입 | target 타입 때문에 `sum`/`sumBy`/정수 `pow`가 넓게 계산될 때 `(long 누산)`. [스펙 §22.6](pscp_v_0_7_spec.md#226-정수-누산-타입) | `accumulatorTypes` (켜짐) | 없음 |
| 매개변수 이름 | 인자가 3개 이상인 호출에서 리터럴 인자 앞에 `lo:` | `parameterNames` (꺼짐) | 없음 |

### 12.2 code actions

- 요청의 `range`와 `context.diagnostics`에 해당하는 action만 돌려준다.
- 빠른 수정은 **진단 코드에 묶는다**. 코드별 목록은 [부록 B](#부록-b-진단-코드-표)에 있다.
- action의 `diagnostics` 필드에 고치는 진단을 넣는다. 안전한 수정이 하나뿐이면 `isPreferred: true`로 표시한다.
- 편집은 `documentChanges`(버전이 붙은 `TextDocumentEdit`)로 보낸다. 그래야 오래된 버전의 문서에 적용되지 않는다.
- `kind`는 진단 수정이면 `quickfix`, 진단과 관계없는 변환이면 `refactor.rewrite`다.

0.6.7 현황:
- `codeActionProvider: true`를 광고하지만 action을 만드는 코드가 없어서 **항상 빈 목록**이다.
- action을 담는 구조도 요청 `range`를 보지 않고 문서 전체의 action을 돌려주게 되어 있다.

#### 빠른 수정

| 진단 | 제목 | 편집 |
|---|---|---|
| `PSCP2108` 불변 바인딩 수정 | "`mut`으로 선언" | 명시 타입 선언 앞에 `mut `, `let`이면 `var`로 |
| `PSCP2104` 초기화 없는 불변 선언 | "`mut`으로 선언" | 같음 |
| `PSCP2201` `rec` 누락 | "`rec` 추가" | 순환에 속한 **모든** 함수 선언 앞에 `rec ` |
| `PSCP2208` 필요 없는 `rec` | "`rec` 제거" | `rec ` 삭제 |
| `PSCP2202` 끝이 `x = e`, `PSCP2209` 식 안의 `=` | "`:=`로 바꾸기" | `=` → `:=` |
| `PSCP2303` 인덱서 안의 `..` | "끝 제외(`..<`)" / "끝 포함(`..=`)" | 두 action. 어느 쪽도 `isPreferred`가 아니다 |
| `PSCP2204` 함수에서 뺄셈 | "음수 인자로: `f (-1)`" | 오른쪽 피연산자를 괄호로 감싼다 |
| `PSCP2210` `not a == b` | "`not (a == b)`" / "`a != b`" | |
| `PSCP2505` 결과를 버린 helper | "제자리 정렬" / "결과를 다시 대입" | 배열이면 `Array.Sort(x)`, `List<T>`면 `x.Sort()`. 재대입은 `x`가 가변일 때만 |
| `PSCP2506` 비교자 없는 문자열 정렬 구조 | "`string.asc` 넘기기" | `new()` → `new(string.asc)` |
| `PSCP2900` 폐기 예정 이름 | "`<대체>`로 바꾸기" | 진단 `data.replacement`로 바꾼다 |
| `PSCP1102` 지원하지 않는 C# 문장 | "`for x in xs`로 바꾸기" | `foreach`인 경우만 |

#### 변환 (진단 없음)

| 제목 | 예 |
|---|---|
| 입력 shorthand ↔ 명시 호출 | `int n =` ↔ `int n = stdin.readInt()` |
| .NET 호출 → PSCP intrinsic | `Math.Max(a, b)` → `max a b`, `Math.Abs(x)` → `abs x` |
| 초기화 루프 → `new![n]` | `for i in 0..<n do g[i] = new()` → 선언의 `new![n]` |

---

## 13. PSCP 전용 프로토콜

### 13.1 원칙

- 표준 LSP로 표현할 수 있는 것은 표준으로 보낸다.
- PSCP 전용 메시지는 이름이 `pscp/`로 시작한다. `experimental.pscp.requests`, `experimental.pscp.notifications`로 광고한다([§4.2](#42-initialize)).
- 서버가 모르는 `pscp/` 요청에는 `MethodNotFound`(-32601)로 답한다.

### 13.2 `pscp/generatedCSharp` (요청, 클라이언트 → 서버)

생성 C# 미리보기([§14.6](#146-생성-c-미리보기))에 쓴다. `pscp transpile`과 같은 옵션과 결과다.

요청:

```json
{
  "textDocument": { "uri": "file:///work/a.pscp" },
  "pretty": true,
  "explain": false,
  "verbose": false
}
```

응답:

```json
{
  "uri": "file:///work/a.pscp",
  "version": 12,
  "languageVersion": "0.7",
  "toolVersion": "0.7.0",
  "csharp": "// <auto-generated> ...",
  "diagnostics": [],
  "elapsedMs": 41
}
```

- 그 문서의 최신 분석 결과로 C#만 생성한다. 다시 분석하지 않는다.
- 오류가 있으면 `csharp`는 `null`이고 `diagnostics`에 오류가 들어 있다.
- `pretty`, `explain`, `verbose`는 CLI의 `--pretty`, `--explain`, `--verbose`와 같다.

### 13.3 `pscp/status` (알림, 서버 → 클라이언트)

상태 표시줄([§14.8](#148-상태-표시줄과-로그))에 쓴다.

```json
{ "state": "idle", "uri": "file:///work/a.pscp", "version": 12, "analysisMs": 104 }
```

- `state`는 `analyzing`, `idle`, `error` 중 하나다.
- 서버는 분석을 시작할 때 `analyzing`, 끝나면 `idle`을 보낸다. 분석이 예외로 실패하면 `error`와 `message`를 보낸다.
- 클라이언트는 `analyzing`이 300 ms 넘게 계속될 때만 진행 표시를 한다. 짧은 분석마다 깜빡이지 않게 하기 위해서다.

### 13.4 v0.6 가이드의 전용 요청 정리

| v0.6 가이드 | 0.7 |
|---|---|
| `pscp/previewGenerated` | `pscp/generatedCSharp`로 이름을 바꾸고 스키마를 정했다 |
| `pscp/getLoweringFacts` | 없앤다. lowering 정보는 hover, inlay hints, `PSCP51xx` 정보 진단으로 표준 LSP에 담는다 |
| `pscp/getSemanticDebugTree` | 개발용. 서버를 `--dev`로 실행했을 때만 `pscp/dev/syntaxTree`를 제공할 수 있다 |
| `pscp/runTranspiler` | 없앤다. 빌드와 실행은 확장이 CLI를 호출해서 한다 |

v0.6 가이드는 이름만 적고 매개변수와 결과를 정하지 않았다. 0.6.7에는 네 요청 모두 구현되어 있지 않다.

---
## 14. VS Code 확장

### 14.1 LSP 클라이언트

0.7 확장은 표준 라이브러리 **`vscode-languageclient`**로 서버와 통신하는 것을 권장한다. 이 라이브러리는 LSP 타입과 VS Code API 사이의 변환, capability 협상, 서버 → 클라이언트 요청, trace, 재시작을 모두 처리한다.

0.6.7 확장은 JSON-RPC 클라이언트를 `extension.js`에 직접 구현했다(약 1,250줄). 그 과정에서 다음 문제가 생겼다. 자체 클라이언트를 계속 쓴다면 아래 **변환 규칙을 모두 지켜야 한다**.

| 항목 | 0.6.7 동작 | 결과 | 지켜야 할 규칙 |
|---|---|---|---|
| `DocumentSymbol.kind` | LSP 번호(1부터)를 VS Code `SymbolKind`(0부터)에 그대로 넣는다 | 개요 아이콘이 한 칸씩 밀린다. 함수(12)는 Variable, 변수(13)는 Constant, struct(23)는 Event로 보인다 | `kind - 1` |
| `CodeAction.kind` | 서버가 준 문자열을 `vscode.CodeAction` 생성자에 그대로 넘긴다 | `CodeActionKind` 객체가 아니어서, action이 오기 시작하면 분류와 필터링이 깨질 수 있다(지금은 action이 비어 있어 드러나지 않는다) | `CodeActionKind.Empty.append(kind)` |
| `initialize`의 `capabilities` | 빈 객체 `{}`를 보낸다 | 서버가 markdown, snippet, 진행 표시 지원 여부를 알 수 없다 | 실제 client capabilities를 보낸다 |
| 서버 capability | 읽지 않고 모든 provider를 무조건 등록한다 | 서버가 지원하지 않는 기능에도 요청을 보낸다 | 광고된 기능만 등록 |
| `Diagnostic` | `tags`, `relatedInformation`, `codeDescription`을 버린다 | 폐기 이름 취소선, 선언 위치 연결, 스펙 링크를 쓸 수 없다 | 모두 변환 |
| `CompletionItem` | `textEdit`, `filterText`, `labelDetails`, `tags`, 문자열 `documentation`을 버린다 | | 모두 변환 |
| `Hover` | `MarkedString` 배열 형태를 버린다 | | 모두 변환 |
| `InlayHint` | label parts, `tooltip`, `textEdits`를 버린다 | | 모두 변환 |
| 서버 → 클라이언트 요청 | 응답하지 않는다 (`workspace/configuration`, `window/workDoneProgress/create` 등) | 서버가 이런 요청을 보내면 응답을 기다리다 멈춘다 | 응답하거나, 모르는 요청이면 `MethodNotFound`로 답한다 |
| 설정 변경 | 서버를 재시작한다 | | 서버 기능 설정은 `workspace/didChangeConfiguration`으로 보낸다 |

### 14.2 활성화

- `onLanguage:pscp`, `workspaceContains:**/*.pscp`
- VS Code 1.74부터 `contributes.commands`의 명령은 자동으로 활성화 이벤트가 된다. 확장의 최소 버전이 1.90이므로 `onCommand:` 목록은 지워도 된다.

### 14.3 서버 실행

- 찾는 순서는 [§1.3](#13-언어-서버를-찾는-순서)와 같다.
- 전송은 stdio다. `.dll`이면 `dotnet <dll>`, SDK면 `pscp lsp`, 그 밖에는 실행 파일을 직접 실행한다(0.6.7과 같다).
- 시작한 뒤 `serverInfo`와 `experimental.pscp`로 버전을 확인한다([§4.3](#43-버전-호환)).
- 서버가 죽으면 [§4.4](#44-종료와-재시작)의 규칙대로 다시 시작한다.

### 14.4 명령

| 명령 ID | 제목 | 동작 | 0.6.7 |
|---|---|---|---|
| `pscp.restartLanguageServer` | PSCP: Restart Language Server | 서버를 다시 시작한다 | 있음 |
| `pscp.showServerLog` | PSCP: Show Language Server Log | 로그 채널을 연다 | 있음 |
| `pscp.transpileCurrentFile` | PSCP: Transpile Current File | 저장 후 `pscp transpile <file>`로 `<file>.g.cs`를 만들고 **연다** | 부분 (만든 파일을 열지 않는다) |
| `pscp.runCurrentFile` | PSCP: Run Current File | `pscp run <file>`. 입력은 터미널에서 받는다 | 있음 |
| `pscp.runWithInput` | PSCP: Run with Input File | 같은 폴더의 `.in` 파일(또는 직접 고른 파일)을 입력으로 `pscp run <file> --stdin-file <in>` | 없음 |
| `pscp.runSamples` | PSCP: Run Samples | 샘플을 모두 실행하고 기대 출력과 비교한다([§14.5](#145-샘플-실행)) | 없음 |
| `pscp.previewGeneratedCSharp` | PSCP: Preview Generated C# | 옆 창에 생성 C#을 연다([§14.6](#146-생성-c-미리보기)) | 없음 |
| `pscp.newSample` | PSCP: New Sample | 다음 번호의 빈 `.in`/`.out` 파일 쌍을 만들고 연다 | 없음 |

공통 규칙 (0.6.7에서 이미 지키는 것 포함):
- 파일을 다루는 명령은 먼저 저장한다. 저장한 적 없는 `untitled` 문서면 저장하라고 안내한다.
- CLI는 셸을 거치지 않는 VS Code Task(`ProcessExecution`)로 실행한다. 그래서 경로 인용 문제가 없고 실행이 끝나도 출력이 남는다.
- 편집기 제목 표시줄의 실행 버튼(`editor/title/run`)은 샘플이 있으면 `pscp.runSamples`, 없으면 `pscp.runCurrentFile`을 실행한다.

### 14.5 샘플 실행

PS에서 가장 흔한 흐름인 "예제 입력으로 돌려 보고 기대 출력과 비교"를 편집기 안에서 한다.

#### 샘플 찾기

`dir/name.pscp`의 샘플은 같은 폴더의 다음 파일이다.

| 입력 | 기대 출력 |
|---|---|
| `name.in` | `name.out` |
| `name.<k>.in` | `name.<k>.out` |

- `<k>`는 점이 없는 이름이다. 예: `a.1.in`, `a.sample2.in`
- `.out`이 없는 `.in`은 "기대 출력 없음"이다. 실행하고 출력만 보여 준다.
- `tests/TestCodes/v0.7/`의 파일들(`01_grid_bfs_char_board.pscp`, `.in`, `.out`)이 이 규칙을 따른다. 확장으로 그대로 실행할 수 있다.

#### CLI: `pscp test` (새 명령)

```txt
pscp test [file.pscp] [--json] [-c Debug|Release] [--timeout <ms>]
```

- 한 번만 빌드하고 모든 샘플을 실행해 비교한다. 샘플마다 `pscp run`을 부르면 매번 빌드해서 느리다.
- 종료 코드: 모두 통과하면 0, 하나라도 실패하면 1, 빌드 실패면 2
- `--json` 출력:

```json
{
  "build": { "ok": true, "diagnostics": [], "elapsedMs": 2140 },
  "samples": [
    {
      "name": "a.1",
      "input": "a.1.in",
      "expected": "a.1.out",
      "status": "passed",
      "actual": "5\n",
      "stderr": "",
      "exitCode": 0,
      "elapsedMs": 38
    }
  ]
}
```

`status`는 `passed`, `failed`, `error`(비정상 종료), `timeout`, `noExpected` 중 하나다.

#### 비교 규칙

`pscp test`와 확장은 같은 규칙으로 비교한다.

1. 줄 끝 `\r\n`을 `\n`으로 바꾼다.
2. 각 줄 끝의 공백을 무시한다.
3. 끝에 있는 빈 줄을 무시한다.
4. 설정 `pscp.samples.floatTolerance`를 지정하면, 같은 위치의 두 토큰이 모두 실수로 읽힐 때 절대 오차나 상대 오차가 그 값 이하이면 같다고 본다. 기본값은 없음(정확히 비교)이다.

#### 실행 조건

| 설정 | 기본값 | 이유 |
|---|---|---|
| `pscp.samples.configuration` | `Debug` | 전제조건 위반([스펙 §29.2](pscp_v_0_7_spec.md#292-전제조건-위반))이 예외로 바로 드러난다 |
| `pscp.samples.timeoutMs` | `2000` | 샘플 하나의 실행 시간 제한 (빌드 제외) |

#### UI

- VS Code **테스트 패널**(Testing API)에 "PSCP 샘플" 컨트롤러를 둔다. 샘플이 있는 `.pscp` 파일이 항목이고, 샘플 하나하나가 그 아래 항목이다.
- 실패하면 `TestMessage.diff`로 기대 출력과 실제 출력을 나란히 보여 준다.
- 파일 맨 위에 code lens로 "▶ 샘플 3개 실행 · 마지막 결과 2/3 통과"를 보여 줄 수 있다.

### 14.6 생성 C# 미리보기

- `pscp/generatedCSharp`([§13.2](#132-pscpgeneratedcsharp-요청-클라이언트--서버))로 받은 C#을 가상 문서로 연다.
  - 스킴: `pscp-generated:` (예: `pscp-generated:/work/a.g.cs`)
  - 언어: `csharp`, 읽기 전용, 편집기 옆(`ViewColumn.Beside`)
- 원본을 편집하면 분석이 끝난 뒤(`pscp/status`가 `idle`) 300 ms 디바운스로 갱신한다.
- 원본에 오류가 있으면 마지막으로 성공한 C#을 그대로 두고, 맨 위에 "현재 코드에 오류가 있어 버전 N의 결과를 보여 준다"는 주석을 붙인다.
- 설정 `pscp.preview.pretty`(기본 켜짐), `pscp.preview.explain`(기본 꺼짐)은 CLI 옵션과 같다.
- v0.6 가이드가 권장했지만 0.6.7에는 없다.

### 14.7 설정 전달과 작업 영역 신뢰

- 설정 전체 목록은 [부록 C](#부록-c-설정-표)에 있다.
- **서버 기능 설정**(`pscp.inlayHints.*`, `pscp.hints.*`)은 시작할 때 `initializationOptions`로, 바뀌면 `workspace/didChangeConfiguration`으로 보낸다. 서버를 재시작하지 않는다.
- **서버 실행 설정**(경로, 인자)이 바뀌면 서버를 재시작한다(0.6.7과 같다).
- 실행 파일 경로 설정은 `machine` 범위라 작업 영역이 바꿀 수 없다(0.6.7과 같다).
- 인자 설정(`pscp.server.args`, `pscp.transpiler.args`)은 0.6.7에서 작업 영역 설정으로도 바꿀 수 있다. 예를 들어 신뢰할 수 없는 저장소가 `pscp.transpiler.args`에 `-o <경로>`를 넣으면, 사용자가 Transpile을 실행할 때 임의 경로에 파일이 써진다.
- 그래서 확장은 **작업 영역 신뢰**를 선언한다.

```json
"capabilities": {
  "untrustedWorkspaces": {
    "supported": "limited",
    "description": "신뢰하지 않는 작업 영역에서는 편집 기능만 동작하고 실행 명령은 꺼집니다.",
    "restrictedConfigurations": ["pscp.server.args", "pscp.transpiler.args"]
  }
}
```

- 제한 모드에서 편집 기능(진단, 완성 등)은 동작한다. 실행 계열 명령(transpile, run, runWithInput, runSamples)은 끈다.

### 14.8 상태 표시줄과 로그

| 상태 | 표시 | 뜻 |
|---|---|---|
| 준비 | `$(check) PSCP 0.7` | 서버 동작 중, 언어 버전 0.7 |
| 분석 중 | `$(sync~spin) PSCP` | `analyzing`이 300 ms 넘게 계속됨 |
| 경고 | `$(warning) PSCP 0.7` | 버전 불일치나 구버전 서버 ([§4.3](#43-버전-호환)) |
| 중지 | `$(error) PSCP` | 서버가 멈췄다 |

- 툴팁에는 서버 경로, 도구 버전, 언어 버전, 마지막 분석 시간을 보여 준다.
- 클릭하면 빠른 선택 메뉴(재시작, 로그 열기, 생성 C# 보기, 샘플 실행)를 연다.
- 로그 채널은 둘로 나눈다.
  - **PSCP**: 확장 동작과 CLI 실행 기록
  - **PSCP Language Server**: 서버 stderr와 LSP trace
- trace는 표준 설정 `pscp.trace.server`(`off` / `messages` / `verbose`)로 켠다.

0.6.7 현황: 상태 표시줄은 `PSCP: starting` / `ready` / `stopped` 텍스트이고 클릭하면 로그를 연다. 로그 채널은 하나에 모두 섞인다. trace는 `pscp.server.trace`(켜고 끄기)로 메시지 이름만 남긴다.

### 14.9 TextMate 문법

TextMate 문법은 서버가 준비되기 전, 그리고 semantic 강조를 끈 테마에서 쓰는 **기본 강조**다. 의미에 따른 구별(가려진 intrinsic, rewrite 연산자 등)은 semantic tokens가 맡는다([§11](#11-semantic-tokens)).

0.7에서 고칠 것:

| 항목 | 0.6.7 | 0.7 |
|---|---|---|
| 키워드 | `try` `catch` `finally` `throw` `as` `default` `typeof` `params` `static` `readonly` `const` `interface` `enum`이 없다 | [스펙 §3.3](pscp_v_0_7_spec.md#33-키워드)의 목록과 같게. 문맥 키워드 `when` `with`도 추가 |
| 제거된 문법 | `public:` 같은 section label을 여전히 별도 scope로 강조한다 | 규칙을 지우거나 `invalid.illegal`로 표시 |
| verbatim 문자열 | `@"…"`이 없다. `\`를 이스케이프로 읽어 `@"C:\dir\"`에서 강조가 깨진다 | `@"…"`, `$@"…"` 추가 |
| intrinsic 이름 | 손으로 쓴 목록. `groupCount` 포함, `lowerBound` `upperBound` 없음 | intrinsic 카탈로그에서 생성. 폐기 이름은 뺀다 |
| 출력 shorthand | 일반 연산자 | 줄 시작의 `=`, `+=`는 `keyword.operator.output.pscp` |
| 입력 shorthand | 일반 연산자 | 선언 줄 끝의 `=`는 `keyword.operator.input.pscp` |
| `new!` | 없음 | 추가 |

### 14.10 편집 설정 (language configuration)

0.6.7은 주석, 괄호, 자동 닫기 쌍만 정의한다. 0.7에서는 다음을 더한다.

- `onEnterRules`: 줄이 `{`, `then`, `else`, `do`, `->`로 끝나면 다음 줄을 한 단계 들여 쓴다([스펙 §4.2](pscp_v_0_7_spec.md#42-줄-이어짐) 줄 이어짐과 맞춘다).
- `indentationRules`: `{`에서 늘리고 `}`에서 줄인다.
- `folding.markers`: `// #region`, `// #endregion`
- `wordPattern`: 식별자 규칙([스펙 §3.2](pscp_v_0_7_spec.md#32-식별자))과 같게

---

## 15. 패키징, 배포, 버전

### 15.1 버전

| 버전 | 예 | 공유하는 곳 |
|---|---|---|
| 도구 버전 | `0.7.0` | CLI, 언어 서버, 확장. 세 곳이 항상 같다 |
| 언어 버전 | `0.7` | 스펙. `pscp version`과 `experimental.pscp.languageVersion`으로 알린다 |
| 프로토콜 버전 | `1` | `experimental.pscp.protocolVersion`. PSCP 전용 메시지가 호환되지 않게 바뀔 때만 올린다 |

세 값은 한 곳(현재 `Syntax.cs`의 상수)에서 나와야 한다. CI는 확장 `package.json`의 버전이 도구 버전과 같은지 검사한다.

### 15.2 VSIX

- **플랫폼별 VSIX**를 만든다. `vsce package --target <대상>`으로 대상마다 그 플랫폼의 Native AOT 서버를 넣는다.
  - 대상: `win32-x64`, `win32-arm64`, `linux-x64`, `linux-arm64`, `darwin-x64`, `darwin-arm64`
- 서버를 넣지 않은 범용 VSIX도 만든다. 이 VSIX는 SDK의 `pscp lsp`를 쓴다.
- 빌드 스크립트는 모든 CI 환경에서 돌아가야 한다.

0.6.7 현황: `vscode/Build-Vsix.ps1`(PowerShell)이 기본값으로 win-x64 서버를 넣은 VSIX 하나만 만든다. Linux와 macOS에서는 포함된 `.exe`를 쓸 수 없어서 확장이 SDK를 찾아간다.

### 15.3 릴리스

- GitHub 릴리스 워크플로가 VSIX를 Windows 설치 프로그램, Linux 패키지와 함께 첨부해야 한다. 0.6.7 릴리스 워크플로에는 VSIX가 없다.
- Windows 설치 프로그램은 `code`가 `PATH`에 있으면 확장도 설치하는 선택 단계를 둘 수 있다.
- Marketplace나 Open VSX에 올릴 때는 `publisher`를 `local`에서 실제 게시자 ID로 바꾼다.

---

## 16. 테스트

### 16.1 현황

`tests/Pscp.Transpiler.Tests`에 언어 서버 시나리오 테스트 6개가 있다. 모두 `LspProbeSession`으로 실제 서버를 띄워 요청을 보낸다.

1. 진단과 `stdin` 멤버 완성
2. .NET 멤버 완성
3. 컬렉션 완성, 이름 바꾸기, 진단 갱신
4. 루프 블록과 인덱서 진단
5. 컬렉션 변경과 nullable 루프
6. record 컬렉션 추론과 이름 바꾸기

확장 테스트는 없다.

### 16.2 0.7에서 더할 테스트

| 테스트 | 내용 |
|---|---|
| 진단 동일성 | `tests/TestCodes/*.pscp` 각각에서 언어 서버의 오류/경고가 `pscp check --json`과 같다(코드, 등급, 범위, 메시지). 0.7 구현 뒤에는 `tests/TestCodes/v0.7/`도 포함한다 |
| 진단 코드 | [부록 B](#부록-b-진단-코드-표)의 코드마다 그 진단을 내는 소스와 기대 코드 |
| 빠른 수정 | 빠른 수정마다: 적용 → 해당 진단이 사라짐 → 트랜스파일 성공 |
| hover | [§7.2](#72-대상별-내용)의 대상마다 대표 소스와 기대 markdown (golden 파일) |
| 완성 | 배열 수신자에 `Length`, `sort`, `sum`이 있다. 식 위치에 자유 함수 `map`이 없다. `stdin.` 뒤에 폐기 이름이 없다. `detail`에 `${`가 없다 |
| 이름 바꾸기 | 충돌 거부, 키워드 거부 메시지, intrinsic 가리기 허용 |
| 문서 기호 | 계층과 `kind` 번호 |
| semantic tokens | 서버 legend 사용, `+=`의 세 가지 분류(`shorthand`, `rewrite`, 없음) |
| 프로토콜 | 잘못된 JSON, 모르는 메서드(-32601), `$/cancelRequest`(-32800), coalescing(연속 `didChange` 뒤 publish는 최신 버전 하나) |
| 성능 | [§5.6](#56-성능)의 2,000줄 벤치마크 |

### 16.3 확장 테스트

`@vscode/test-electron`으로 다음 스모크 테스트를 둔다.

- 활성화, 서버 시작과 재시작
- 명령 등록
- 개요의 `SymbolKind` 변환 (0.6.7의 한 칸 밀림 회귀 방지)
- 샘플 실행기 (가짜 `pscp test --json` 출력을 주입)
- 제한 모드에서 실행 명령이 꺼지는지

### 16.4 릴리스 전 수동 점검

1. 설치 직후 `.pscp`를 열면 상태 표시줄에 `PSCP 0.7`이 뜬다.
2. 입력 shorthand, `visited += x`, `:=`, `|>` 위에 hover가 뜬다.
3. 배열 뒤 `.`에서 `sort`, `sum`, `Length`가 나온다.
4. `arr[1..3]`에 오류와 두 가지 빠른 수정이 뜬다.
5. 개요에서 함수, 변수, 타입 아이콘이 맞다.
6. `PSCP: Run Samples`가 `tests/TestCodes/v0.7`의 샘플을 통과시킨다(0.7 구현 뒤).
7. 생성 C# 미리보기가 편집을 따라 갱신된다.
8. Windows, Linux, macOS VSIX가 각각 포함된 서버로 시작한다.

---

## 17. 구현 우선순위

| 순위 | 항목 | 이유 |
|---|---|---|
| **P0** 정확성 | 심볼·hover·완성을 front-end binder 결과로 옮기기 ([§3.3](#33-금지)) | 편집기와 컴파일러가 다른 말을 하면 다른 기능이 모두 신뢰를 잃는다 |
| | 진단 동일성, 진단 코드 체계, CLI의 코드 출력 ([§6](#6-진단)) | |
| | 언어 서버 전용 진단 제거 (`PSCP2007`) | |
| | 확장의 `SymbolKind` 변환 수정, 또는 `vscode-languageclient`로 이행 ([§14.1](#141-lsp-클라이언트)) | |
| | 0.7 스펙의 진단([스펙 부록 B](pscp_v_0_7_spec.md#부록-b-진단-목록))과 폐기 태그 | |
| **P1** 핵심 UX | 배열 수신자 멤버 완성, 완성 문맥·정렬·`detail` ([§8](#8-자동완성)) | 매일 쓰는 기능이다 |
| | hover 대상 표 ([§7.2](#72-대상별-내용)) | |
| | 빠른 수정 ([§12.2](#122-code-actions)) | |
| | `pscp test`와 샘플 실행기 ([§14.5](#145-샘플-실행)) | PS의 기본 작업 흐름 |
| | 생성 C# 미리보기 ([§14.6](#146-생성-c-미리보기)) | |
| **P2** | space-call signature help, 문서 기호 계층, folding·selection·document highlight | |
| | inlay hints 범위 준수와 새 종류, semantic token 수식어 | |
| | coalescing과 `$/cancelRequest` ([§5.4](#54-처리-모델)) | |
| | 플랫폼별 VSIX, 릴리스 첨부, 작업 영역 신뢰 | |
| **P3** | semantic tokens range, 스니펫, code lens, incremental 동기화, `--dev` 디버그 요청 | |

---
## 부록 A. LSP 메서드 지원표

| 메서드 | 방향 | 0.6.7 | 0.7 요구 | 절 |
|---|---|---|---|---|
| `initialize` | C→S | 부분 (클라이언트 capabilities를 쓰지 않음, `experimental.pscp` 없음) | 필수 | §4.2 |
| `initialized`, `shutdown`, `exit` | C→S | 있음 | 필수 | §4.4 |
| `$/cancelRequest` | C→S | 없음 (무시) | 필수 | §5.4 |
| `textDocument/didOpen`, `didChange`, `didClose` | C→S | 있음 (전체 동기화) | 필수 | §5.1 |
| `textDocument/publishDiagnostics` | S→C | 부분 (범주 코드, 언어 서버 전용 진단, `tags` 등 없음) | 필수 | §6 |
| `workspace/didChangeConfiguration` | C→S | 없음 | 필수 | §14.7 |
| `textDocument/hover` | C→S | 부분 | 필수 | §7 |
| `textDocument/completion` | C→S | 부분 (배열 수신자 없음, 정렬, `detail`) | 필수 | §8 |
| `completionItem/resolve` | C→S | 없음 | 선택 | — |
| `textDocument/signatureHelp` | C→S | 부분 (괄호 호출만) | 필수 | §9 |
| `textDocument/definition` | C→S | 부분 (앞 참조 실패) | 필수 | §10.1 |
| `textDocument/references` | C→S | 부분 (앞 참조 실패) | 필수 | §10.2 |
| `textDocument/documentHighlight` | C→S | 없음 | 권장 | §10.2 |
| `textDocument/prepareRename`, `rename` | C→S | 부분 (충돌 검사 없음) | 필수 | §10.3 |
| `textDocument/documentSymbol` | C→S | 부분 (평면, 일부 누락) | 필수 | §10.4 |
| `textDocument/foldingRange` | C→S | 없음 | 권장 | §10.5 |
| `textDocument/selectionRange` | C→S | 없음 | 선택 | §10.5 |
| `textDocument/semanticTokens/full` | C→S | 있음 | 필수 | §11 |
| `textDocument/semanticTokens/range` | C→S | 없음 | 권장 | §11.4 |
| `textDocument/semanticTokens/full/delta` | C→S | 없음 | 선택 | §11.4 |
| `textDocument/inlayHint` | C→S | 부분 (`range` 무시, 한 종류) | 필수 | §12.1 |
| `textDocument/codeAction` | C→S | 없음 (광고만 하고 빈 목록) | 필수 | §12.2 |
| `textDocument/formatting` | C→S | 없음 | 제공하지 않음 | §10.6 |
| `workspace/symbol` | C→S | 없음 | 제공하지 않음 (단일 파일) | — |
| `pscp/generatedCSharp` | C→S | 없음 | 필수 | §13.2 |
| `pscp/status` | S→C | 없음 | 권장 | §13.3 |

---

## 부록 B. 진단 코드 표

"스펙" 열은 `pscp_v_0_7_spec.md`의 절이다. "빠른 수정" 열은 [§12.2](#122-code-actions)의 수정이다.

### B.1 어휘와 구문

| 코드 | 등급 | 조건 | 스펙 | 빠른 수정 |
|---|---|---|---|---|
| `PSCP1000` | 오류 | 어휘 오류 (잘못된 문자, 닫히지 않은 문자열 등) | §3 | |
| `PSCP1001` | 오류 | 그 밖의 구문 오류 | 부록 A | |
| `PSCP1101` | 오류 | 선언이 아닌 곳의 줄 끝 `=` | §4.3 | |
| `PSCP1102` | 오류 | 지원하지 않는 C# 문장 (`foreach`, `do-while`, `switch` 문, `goto` 등) | §6.3 | `foreach` → `for … in` |
| `PSCP1103` | 오류 | 식 위치 `if`에 `else`가 없다 | §12.1 | |
| `PSCP1104` | 오류 | `\|>`와 `<\|`를 괄호 없이 섞었다 | §13.3 | |
| `PSCP1105` | 오류 | 제거된 access section label (`public:` 등). v0.6 스펙 §6.3에서 제거된 문법 | — | |

### B.2 제어 흐름, 이름, 선언

| 코드 | 등급 | 조건 | 스펙 | 빠른 수정 |
|---|---|---|---|---|
| `PSCP2008` | 오류 | 루프 밖의 `break`, `continue` | §12.5 | |
| `PSCP2101` | 오류 | `__pscp`/`__Pscp`로 시작하는 사용자 식별자 | §3.2 | |
| `PSCP2102` | 오류 | 같은 scope에서 변수와 함수의 이름이 같다 | §5.1 | |
| `PSCP2103` | 오류 | 정의되지 않은 이름 | §5.1 | |
| `PSCP2104` | 오류 | 초기화 없는 불변 지역 선언 | §9.3 | `mut`으로 선언 |
| `PSCP2105` | 오류 | 한 괄호 안에서 선언과 기존 변수를 섞은 구조 분해 | §9.5 | |
| `PSCP2106` | 오류 | 문장 시작 `A b`에서 `A`가 타입도 호출 가능한 것도 아니다 | §11.5 | |
| `PSCP2107` | 경고 | 최상위 문장이 부른 함수가 아직 초기화되지 않은 최상위 변수를 읽는다 | §7.3 | |
| `PSCP2108` | 경고 | 불변 바인딩이나 불변 필드를 수정한다 | §9.2, §28.2 | `mut`으로 선언 |
| `PSCP2109` | 오류 | 타입 선언 안에서 상수가 아닌 최상위 변수나 최상위 함수를 참조한다 | §7.4 | |

### B.3 함수, 반환, 호출, 연산자

| 코드 | 등급 | 조건 | 스펙 | 빠른 수정 |
|---|---|---|---|---|
| `PSCP2201` | 오류 | 재귀 순환에 속한 함수에 `rec`이 없다 | §10.3 | `rec` 추가 |
| `PSCP2202` | 오류 | 값을 반환하는 함수의 경로 끝이 값이 아니다. tail 식 타입이 `void`다 | §10.5 | 끝이 `x = e`면 `:=`로 |
| `PSCP2203` | 오류 | space-call head나 pipe 대상이 호출할 수 없는 값이다 | §11.2, §13.3 | |
| `PSCP2204` | 오류 | 함수(메서드 그룹)나 인자가 모자란 호출에서 뺄셈을 한다 (`f -1`) | §11.3 | `f (-1)` |
| `PSCP2207` | 경고 | 결과를 쓰지 않는 단항식 문장 (`- b`). `Unnecessary` 태그 | §4.3 | |
| `PSCP2208` | 경고 | 재귀하지 않는 함수의 `rec`, 타입 멤버의 `rec`. `Unnecessary` 태그 | §10.3 | `rec` 제거 |
| `PSCP2209` | 경고 | 식 안의 `=`, 연쇄 `=` | §13.4 | `:=`로 |
| `PSCP2210` | 경고 | 관계·동등·`is` 연산자 왼쪽의 `not X` | §13.7 | `not (…)` / `!=` |

### B.4 range, 인덱싱, 컬렉션 식, 배열 생성

| 코드 | 등급 | 조건 | 스펙 | 빠른 수정 |
|---|---|---|---|---|
| `PSCP2301` | 오류 | range의 상수 step이 0이다 | §14.3 | |
| `PSCP2302` | 오류 | `char` bound와 정수 bound를 섞었다 | §14.2 | |
| `PSCP2303` | 오류 | 인덱서 안의 `a..b`, `..b` | §15.2 | `..<` / `..=` |
| `PSCP2304` | 오류 | 인덱서 안의 stepped range | §15.2 | |
| `PSCP2305` | 오류 | 슬라이스를 지원하지 않는 대상 | §15.3 | |
| `PSCP2306` | 오류 | target type이 없는 빈 `[]` | §16.1 | |
| `PSCP2307` | 오류 | `[]` 원소들의 공통 타입이 없다 | §16.1 | |
| `PSCP2308` | 오류 | `[]` 안의 `..<b` | §16.1 | |
| `PSCP2309` | 오류 | target type이 없는 `new[n]` | §27.1 | |
| `PSCP2310` | 오류 | `new![n]`의 원소 타입에 매개변수 없는 생성자가 없다 (배열 포함) | §27.2 | |

### B.5 입력과 출력

| 코드 | 등급 | 조건 | 스펙 | 빠른 수정 |
|---|---|---|---|---|
| `PSCP2401` | 오류 | 타입이 없는 입력 shorthand (`let x =`) | §17.1 | |
| `PSCP2402` | 오류 | token-readable이 아닌 입력 타입 | §17.6 | |
| `PSCP2403` | 오류 | 3단 이상 중첩 컬렉션의 자동 렌더링 | §18.3 | |
| `PSCP2404` | 오류 | 알 수 없는 `stdin`/`stdout` 멤버 | §17.2, §18.2 | |

### B.6 intrinsic, 순서, 자료구조 rewrite

| 코드 | 등급 | 조건 | 스펙 | 빠른 수정 |
|---|---|---|---|---|
| `PSCP2501` | 오류 | 스칼라 하나만 받은 `min`/`max` | §22.2 | |
| `PSCP2502` | 오류 | 기본 순서가 없는 타입의 순서 연산 | §25.1 | |
| `PSCP2503` | 오류 | known DS `+=`/`-=`의 피연산자 타입이 맞지 않는다 | §26.2 | |
| `PSCP2504` | 오류 | known DS에 후위 `++`/`--` | §13.5, §26.2 | |
| `PSCP2505` | 경고 | 결과를 쓰지 않는 collection helper 호출 | §24.4 | 제자리 정렬 / 재대입 |
| `PSCP2506` | 경고 | 비교자 없이 만든 문자열 key 정렬 구조, 비교자 없는 문자열 배열/리스트 정렬 | §25.4 | `string.asc` 넘기기 |

### B.7 타입과 멤버, 폐기, 이행

| 코드 | 등급 | 조건 | 스펙 | 빠른 수정 |
|---|---|---|---|---|
| `PSCP2600` | 오류 | 그 밖의 PSCP 고유 표면의 타입 오류 (C#으로 넘기지 않는다) | §34 | |
| `PSCP2601` | 경고 | 어디서도 대입하지 않는 불변 필드 | §28.2 | |
| `PSCP2900` | 경고 | 폐기 예정 이름. `Deprecated` 태그 | 부록 C | 대체 이름으로 |
| `PSCP3001` | 경고 | (이행 기간) 아직 코드를 배정하지 않은 트랜스파일러 경고 | — | |
| `PSCP3002` | 오류 | (이행 기간) 아직 코드를 배정하지 않은 트랜스파일러 오류 | — | |

### B.8 편집기 전용

| 코드 | 등급 | 조건 | 기본값 |
|---|---|---|---|
| `PSCP5001` | 정보 | 사용자 선언이 intrinsic 이름을 가린다 | 켜짐 |
| `PSCP5101` | 정보 | 컴파일 시점 상수라 C# `const`로 낮아진다 | 꺼짐 |
| `PSCP5102` | 정보 | generator를 aggregate가 바로 소비해 fused loop로 낮아진다 | 꺼짐 |

---

## 부록 C. 설정 표

"전달" 열: **재시작**은 바뀌면 서버를 다시 시작한다. **알림**은 `workspace/didChangeConfiguration`으로 보낸다. **확장**은 서버와 관계없이 확장이 쓴다.

| 설정 | 타입 | 기본값 | 범위 | 전달 | 0.6.7 |
|---|---|---|---|---|---|
| `pscp.server.path` | string | `""` | machine | 재시작 | 있음 |
| `pscp.languageServerPath` | string | `""` | machine | 재시작 | 있음 (호환용) |
| `pscp.sdkPath` | string | `""` | machine | 재시작 | 있음 (호환용) |
| `pscp.server.args` | string[] | `[]` | window. 제한 모드에서는 작업 영역 값을 무시 | 재시작 | 있음 (작업 영역 값도 읽음) |
| `pscp.transpiler.path` | string | `""` | machine | 확장 | 있음 |
| `pscp.transpiler.args` | string[] | `[]` | window. 제한 모드에서는 작업 영역 값을 무시 | 확장 | 있음 (작업 영역 값도 읽음) |
| `pscp.trace.server` | `off` \| `messages` \| `verbose` | `off` | window | 즉시 | 없음 |
| `pscp.server.trace` | boolean | `false` | window | 즉시 | 있음. 0.7에서 폐기 (`messages`로 대응) |
| `pscp.server.requestTimeoutMs` | number | `8000` | window | 확장 | 있음. 0.7에서 폐기 (표준 클라이언트는 편집기의 취소를 따른다) |
| `pscp.inlayHints.inferredTypes` | boolean | `true` | resource | 알림 | 없음 |
| `pscp.inlayHints.rewriteResults` | boolean | `true` | resource | 알림 | 없음 |
| `pscp.inlayHints.accumulatorTypes` | boolean | `true` | resource | 알림 | 없음 |
| `pscp.inlayHints.parameterNames` | boolean | `false` | resource | 알림 | 없음 |
| `pscp.hints.loweringDiagnostics` | boolean | `false` | resource | 알림 | 없음 |
| `pscp.samples.configuration` | `Debug` \| `Release` | `Debug` | resource | 확장 | 없음 |
| `pscp.samples.timeoutMs` | number | `2000` | resource | 확장 | 없음 |
| `pscp.samples.floatTolerance` | number \| null | `null` | resource | 확장 | 없음 |
| `pscp.preview.pretty` | boolean | `true` | resource | 확장 | 없음 |
| `pscp.preview.explain` | boolean | `false` | resource | 확장 | 없음 |

---

## 부록 D. v0.6 가이드 대비 개선점

v0.6 가이드를 저장소 구현(도구 0.6.7)과 대조하고, 언어 서버를 직접 실행해 요청을 보내며 확인한 결과다. "확인한 사실" 열은 실제로 관찰한 동작이다.

### D.1 문서의 성격

| # | v0.6 가이드의 문제 | 확인한 사실 | 이 문서 |
|---|---|---|---|
| D-01 | 권장 설계 목록이라 무엇이 구현됐는지 알 수 없다 | 가이드의 전용 요청 4개, 미리보기 명령, 백그라운드 인덱서는 구현되지 않았다. code action은 광고만 한다 | 기능마다 "0.6.7 현황"과 "0.7 요구"를 함께 적었다. 부록 A에 메서드 지원표 (§0.1) |
| D-02 | 규범 수준이 없다 | "권장", "가능", "강하게 권장"이 섞여 있다 | 스펙과 같은 MUST/SHOULD/MAY (§0.2) |
| D-03 | 사용자 안내가 없다 | 설치, 서버 탐색 순서, 문제 해결은 확장 README에만 일부 있다 | §1 |
| D-04 | 목차 수준이 평평하다 | `## 4.1`이 `## 4.`와 같은 수준이다 | 절 `##`, 하위 절 `###` |

### D.2 계약의 구체성

| # | v0.6 가이드의 문제 | 확인한 사실 | 이 문서 |
|---|---|---|---|
| D-05 | capability와 메서드별 동작이 없다 | 동기화 방식(전체), 트리거 문자(`.`), 위치 인코딩이 문서에 없다 | 부록 A, §5.1–§5.2 |
| D-06 | 진단 코드 체계가 없다 | 코드가 출처별 범주다 (`PSCP1000` lexer 전체, `PSCP3001` 트랜스파일러 경고 전체 등). 특정 진단에 빠른 수정을 연결할 수 없다 | 종류별 코드와 스펙 대응, CLI 출력에도 코드 (§6.2, 부록 B) |
| D-07 | semantic token legend가 없다 | legend를 서버와 확장이 각각 하드코딩한다 | 서버가 legend를 정하고, PSCP 수식어 4개와 분류 표를 정했다 (§11) |
| D-08 | hover 형식과 내용이 없다 | 몇 가지 고정 문장뿐이다 | 형식, 대상별 표, 예시 (§7) |
| D-09 | 자동완성의 문맥, 정렬, 항목 형식이 없다 | 식 위치 후보 122개를 이름 사전순으로 섞는다. `detail`에 `map(${1:values}, ${2:selector})`가 그대로 보인다 | §8 |
| D-10 | 전용 요청의 스키마가 없다 | 이름 4개만 있고 구현은 없다 | `pscp/generatedCSharp`, `pscp/status` 스키마 (§13) |
| D-11 | 빠른 수정과 진단의 연결이 없다 | 수정 이름 목록만 있다 | 진단 코드별 편집 규칙 (§12.2, 부록 B) |

### D.3 일관성

| # | v0.6 가이드의 문제 | 확인한 사실 | 이 문서 |
|---|---|---|---|
| D-12 | 언어 서버가 트랜스파일러와 같은 front-end를 써야 한다는 원칙이 없다 | 언어 서버 자체의 토큰 단위 분석기(`PscpAnalyzer`)가 심볼, hover, 완성을 만들고, 진단만 트랜스파일러에서 가져온다 | 원칙 1과 이행 계획 (§2, §3.3) |
| D-13 | 편집기와 CLI의 판정이 같아야 한다는 원칙이 없다 | `int y = 3` / `y = 4`에 언어 서버는 **오류** `PSCP2007`과 경고 `PSCP3001`을 함께 낸다. `pscp check`는 경고만 내고 성공한다 | 원칙 2, 동일성 테스트 (§2, §6.1, §16.2) |
| D-14 | intrinsic 문서의 출처가 없다 | 문서가 `PscpIntrinsics.cs`에 따로 적혀 있고 0.7 스펙과 맞지 않는다 (`round`: "Math-compatible lowering", `readChar`: "Reads one character token") | 원칙 3, intrinsic 카탈로그 (§2, §7.1) |
| D-15 | .NET 메타데이터의 출처가 없다 | 서버 런타임의 타입 24개 화이트리스트를 reflection으로 읽는다. `SortedDictionary`, `StringBuilder`는 목록에 없다 | 대상 프레임워크의 참조 어셈블리 (§5.7) |
| D-16 | 0.6 언어 규칙이 남아 있다 | "invalid mutual recursion grouping"(연속 `rec` 규칙), `groupCount` 완성 등 | 0.7 스펙의 진단과 폐기 태그 (§6, 부록 B) |

### D.4 기능의 공백

| # | v0.6 가이드의 문제 | 확인한 사실 | 이 문서 |
|---|---|---|---|
| D-17 | 가이드의 대표 hover 예시가 구현되지 않았다 | `visited += x`, `:=`, `a.sum()` 위에 hover가 없다 | 대상별 표 (§7.2) |
| D-18 | 배열 수신자 완성이 없다 | `int[n] a =` 뒤 `a.`에서 후보 0개 | §8.5 |
| D-19 | 자동완성이 틀린 형태를 제안한다 | 자유 함수 `map`, `filter`, `sort` 등을 제안한다. 트랜스파일러는 `Undefined name`으로 거부한다 | §8.2 |
| D-20 | 앞 참조를 해석하지 못한다 | 뒤에 선언된 로컬 함수(`deep`), 뒤에 선언된 최상위 변수(`parent`)에서 정의로 이동과 hover가 안 된다 | §10.1 |
| D-21 | 이름 바꾸기에 충돌 검사가 없다 | 이미 있는 `n`으로 `sum`을 바꿔도 편집을 돌려준다. 키워드로 바꾸려 하면 이유 없이 `null` | §10.3 |
| D-22 | 개요가 평면이고 일부 선언이 빠진다 | `09_dinic_maxflow.pscp`의 개요에 `int n, m, s, t =`, `g`, `addEdge`가 없다 | §10.4 |
| D-23 | inlay hints가 요청 범위를 무시한다 | 0–5줄을 요청해도 48줄의 힌트가 온다 | §12.1 |
| D-24 | code action을 광고만 한다 | 항상 빈 목록 | §12.2 |
| D-25 | signature help가 space-call을 모르고 오버로드가 하나다 | `min a[0] │`에서 결과 없음. `min`은 `(left, right)` 하나 | §9 |
| D-26 | 진단 범위가 문제 위치가 아니다 | `s -= 1`의 불변 경고("is modified here")가 선언 `int n, m, s, t =`를 가리킨다 | `range`는 문제 위치, 선언은 `relatedInformation` (§6.3) |
| D-27 | 구문 오류 하나로 의미 진단이 모두 사라진다 | lexer/parser 오류가 있으면 트랜스파일러 의미 분석 전체를 건너뛴다 | §6.4 |

### D.5 VS Code 확장

| # | v0.6 가이드의 문제 | 확인한 사실 | 이 문서 |
|---|---|---|---|
| D-28 | LSP 클라이언트 구현에 대한 지침이 없다 | 자체 클라이언트가 `SymbolKind`를 한 칸씩 밀리게 변환하고, `CodeActionKind`를 문자열로 넘기며, `capabilities: {}`를 보낸다 | `vscode-languageclient` 권장, 변환 규칙 표 (§14.1) |
| D-29 | PS 작업 흐름을 위한 명령이 없다 | 명령은 재시작, 로그, transpile, run 넷. `run`은 터미널 입력만 받는다. 미리보기는 권장만 하고 없다 | 입력 파일 실행, 샘플 실행, 미리보기, `pscp test` (§14.4–§14.6) |
| D-30 | 설정 전달과 보안 규칙이 없다 | 설정이 바뀌면 서버를 재시작만 한다. 인자 설정은 작업 영역에서도 읽는다. 작업 영역 신뢰를 선언하지 않았다 | §14.7 |
| D-31 | TextMate 문법의 기준이 없다 | 키워드 13개가 빠졌고, 제거된 `public:` label을 강조하고, `@"…"`를 모른다 | §14.9 |

### D.6 배포와 버전

| # | v0.6 가이드의 문제 | 확인한 사실 | 이 문서 |
|---|---|---|---|
| D-32 | 버전 협상이 없다 | `serverInfo`에 언어 버전이 없다. 확장은 SDK일 때만 `pscp version` 출력을 정규식으로 읽는다 | `experimental.pscp` (§4.2, §4.3, §15.1) |
| D-33 | 배포 전략이 선택지 나열이다 | VSIX 빌드 스크립트가 win-x64 서버를 넣은 VSIX 하나만 만들고, 릴리스 워크플로에 VSIX가 없다 | 플랫폼별 VSIX, 릴리스 첨부 (§15.2, §15.3) |

### D.7 성능, 동시성, 테스트

| # | v0.6 가이드의 문제 | 확인한 사실 | 이 문서 |
|---|---|---|---|
| D-34 | 구현되지 않은 복잡한 동시성을 요구하고, 실제 모델은 설명하지 않는다 | 메시지를 한 루프에서 순서대로 처리하고 `didChange` 안에서 전체를 동기 분석한다. `$/cancelRequest`는 무시한다. 가이드는 백그라운드 큐와 공유 future를 요구한다 | 단순한 모델과 규칙 6개 (§5.4) |
| D-35 | 성능 목표에 측정 근거와 방법이 없다 | 2,081줄 파일에서 변경 → 진단 96–126 ms, hover 0.3–1.9 ms, 완성 1.8–2.6 ms | 기준 측정, 예산, 벤치마크 (§5.6) |
| D-36 | 테스트 전략이 일반론이다 | 저장소의 언어 서버 테스트 6개를 언급하지 않았고, 확장 테스트는 없다 | 구체적인 테스트 목록 (§16) |
