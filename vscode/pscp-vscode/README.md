# PSCP VS Code Extension

이 확장은 `.pscp` 파일을 VS Code에서 편하게 다루기 위한 PSCP `v0.4`용 확장입니다.

포함 기능:

- `.pscp` 언어 등록
- v0.5 문법 하이라이팅
- 진단
- 자동완성
- hover
- definition / references
- document symbols
- semantic tokens
- signature help
- rename
- inlay hints

언어 서버 연결 순서:

1. `pscp.languageServerPath`
2. `pscp.sdkPath`
3. 설치된 PSCP SDK (`%LOCALAPPDATA%\Programs\Pscp\pscp.exe` 등)
4. PATH의 `pscp.exe`
5. 저장소 개발 빌드 폴백

## VSIX 빌드

저장소 루트에서 다음 명령을 실행합니다.

```powershell
powershell -ExecutionPolicy Bypass -File .\vscode\Build-Vsix.ps1
```

생성 위치:

```text
artifacts\vscode\local.pscp-vscode-0.5.1.vsix
```

## 설치

다음 두 방법 중 하나로 설치할 수 있습니다.

1. VS Code의 `Extensions: Install from VSIX...`
2. 터미널에서:

```powershell
code --install-extension .\artifacts\vscode\local.pscp-vscode-0.5.1.vsix
```
