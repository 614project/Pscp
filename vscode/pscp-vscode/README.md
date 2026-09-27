# PSCP VS Code Extension

이 확장은 `.pscp` 파일을 VS Code에서 편하게 다루기 위한 PSCP `v0.7`용 확장입니다.

## 편집 기능

- `.pscp` 언어 등록, v0.7 문법 하이라이팅 (`@"…"` verbatim 문자열, `new!`, 입출력 shorthand 전용 scope 포함)
- 진단: 진단 코드(`PSCP2108` 등), 스펙 링크, 선언 위치 연결, 폐기 예정 이름 취소선
- 자동완성: 문맥별 후보, 그룹 정렬(지역 → 타입 멤버 → 최상위 → intrinsic → .NET → 키워드), `labelDetails`, 스니펫
- hover: 낮아지는 C#까지 보여 줍니다. `:=`, `|>`, `<|`, `<=>`, range, `->`, 자료구조 rewrite(`+=`, `-=`, `~`, 전위 `--`), 입출력 shorthand, 바인딩의 `const` 여부
- definition / references / document symbols / signature help / rename / inlay hints
- semantic tokens: legend는 서버가 알려 주는 것을 씁니다. PSCP 전용 수식어 `mutable`, `intrinsic`, `shorthand`, `rewrite`를 테마에서 꾸밀 수 있습니다
- 빠른 수정: `mut`으로 선언, `rec` 추가/제거, `:=`로 바꾸기, `..<`/`..=` 고르기, `string.asc` 넘기기, 폐기 예정 이름 바꾸기, `foreach` → `for x in xs`

## 명령

| 명령 | 동작 |
|---|---|
| `PSCP: Restart Language Server` | 서버를 다시 시작합니다 |
| `PSCP: Show Language Server Log` | 서버 로그 채널을 엽니다 |
| `PSCP: Transpile Current File` | `pscp transpile`로 `<file>.g.cs`를 만들고 엽니다 |
| `PSCP: Run Current File` | `pscp run`. 입력은 터미널에서 받습니다 |
| `PSCP: Run with Input File` | 같은 폴더의 `.in` 파일을 골라 `pscp run --stdin-file`로 실행합니다 |
| `PSCP: Run Samples` | 샘플을 모두 실행하고 기대 출력과 비교합니다 |
| `PSCP: New Sample` | 다음 번호의 빈 `.in`/`.out` 쌍을 만들고 엽니다 |
| `PSCP: Preview Generated C#` | 생성 C#을 옆 창에 읽기 전용으로 엽니다 |

편집기 제목 표시줄의 실행 버튼은 샘플이 있으면 샘플 실행, 없으면 현재 파일 실행입니다.

## 샘플 실행

`dir/name.pscp`의 샘플은 같은 폴더의 `name.in`/`name.out`, `name.<k>.in`/`name.<k>.out`입니다. `pscp test --json`이 한 번만 빌드하고 모두 실행하며, 결과는 VS Code 테스트 패널에 나오고 실패하면 기대 출력과 실제 출력을 diff로 보여 줍니다.

비교 규칙: 줄 끝 `\r\n`은 `\n`으로, 줄 끝 공백과 끝의 빈 줄은 무시합니다. `pscp.samples.floatTolerance`를 지정하면 같은 자리의 실수 토큰을 그 오차 안에서 같다고 봅니다.

`tests/TestCodes/v0.7/`의 파일들이 이 규칙을 따르므로 그대로 실행해 볼 수 있습니다.

## 설정

| 설정 | 기본값 | 뜻 |
|---|---|---|
| `pscp.samples.configuration` | `Debug` | 샘플 실행 빌드 구성. Debug는 전제조건 위반을 예외로 드러냅니다 |
| `pscp.samples.timeoutMs` | `2000` | 샘플 하나의 실행 시간 제한 (빌드 제외) |
| `pscp.samples.floatTolerance` | 없음 | 실수 비교 허용 오차 |
| `pscp.preview.pretty` | 켜짐 | 생성 C# 미리보기를 정렬합니다 |
| `pscp.preview.explain` | 꺼짐 | 미리보기에 lowering 설명을 붙입니다 |
| `pscp.inlayHints.types` | 켜짐 | `let`/`var` 바인딩의 추론 타입 |
| `pscp.inlayHints.parameterNames` | 켜짐 | 호출 위치의 매개변수 이름 |
| `pscp.hints.rewrite` | 켜짐 | 자료구조 rewrite가 부르는 .NET 메서드 |
| `pscp.trace.server` | `off` | LSP 통신 기록 (`off` / `messages` / `verbose`) |

서버 기능 설정(`pscp.inlayHints.*`, `pscp.hints.*`)은 서버를 재시작하지 않고 `workspace/didChangeConfiguration`으로 전달합니다. 서버 실행 설정(경로, 인자)이 바뀌면 서버를 다시 시작합니다.

## 언어 서버 연결 순서

1. `pscp.server.path`
2. 호환 설정 `pscp.languageServerPath`
3. VSIX에 포함된 bundled language server
4. `pscp.sdkPath`
5. 설치된 PSCP SDK (`%LOCALAPPDATA%\Programs\Pscp\pscp.exe` 등)
6. PATH의 `pscp` 또는 `pscp.exe`
7. 저장소 개발 빌드 폴백

## 보안

실행 파일 경로 설정(`pscp.server.path`, `pscp.languageServerPath`, `pscp.sdkPath`, `pscp.transpiler.path`)은 사용자/머신 설정에서만 읽습니다. 저장소의 `.vscode/settings.json`이 임의의 실행 파일을 지정할 수 없게 하기 위해서입니다.

인자 설정(`pscp.server.args`, `pscp.transpiler.args`)은 작업 영역에서도 바꿀 수 있으므로, 확장은 **작업 영역 신뢰**를 선언합니다. 신뢰하지 않는 폴더에서는 편집 기능만 동작하고 transpile/run/샘플 실행 명령은 꺼집니다.

`PSCP: Run Current File` / `PSCP: Transpile Current File`은 `pscp`를 셸을 거치지 않는 VS Code task로 실행합니다. 따라서 PowerShell, cmd, bash 어디서든 경로 인용 문제가 없고, 실행이 끝나도 출력이 남습니다.
깊은 재귀 때문에 큰 스택이 필요하면 `pscp.transpiler.args`에 `["--large-stack"]`을 지정하세요.

## VSIX 빌드

저장소 루트에서 다음 명령을 실행합니다.

```powershell
powershell -ExecutionPolicy Bypass -File .\vscode\Build-Vsix.ps1
```

생성 위치:

```text
artifacts\vscode\local.pscp-vscode-0.7.0.vsix
```

## 설치

다음 두 방법 중 하나로 설치할 수 있습니다.

1. VS Code의 `Extensions: Install from VSIX...`
2. 터미널에서:

```powershell
code --install-extension .\artifacts\vscode\local.pscp-vscode-0.7.0.vsix
```
