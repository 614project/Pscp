# PSCP v0.7 적합성 테스트

이 폴더의 프로그램들은 **0.6에서 문제가 됐고 0.7 초안에서 해결되는 코드**다. 스펙은 `docs/pscp_v_0_7_spec.md`, 항목별 해설은 `docs/pscp_v_0_7_changes.md`에 있다.

## 구성

프로그램마다 파일이 세 개씩 있다.

| 파일 | 내용 |
|---|---|
| `NN_name.pscp` | 테스트 프로그램. 머리 주석에 관련 변경 항목(A1, B4 …)과 트랜스파일러 0.6.7에서의 실제 결과를 적었다 |
| `NN_name.in` | 표준 입력 (`10_read_lines_crlf.in`은 일부러 CRLF 줄 끝을 쓴다) |
| `NN_name.out` | 0.7 스펙을 따를 때의 기대 출력 (LF 줄 끝, 마지막 줄바꿈 포함) |

## 이 폴더가 따로 있는 이유

`tests/Pscp.Transpiler.Tests`는 `tests/TestCodes/*.pscp`(하위 폴더 제외)를 **모두 현재 트랜스파일러로 변환하고 C#으로 컴파일**한다. 이 폴더의 프로그램은 모두 0.6.7에서 PSCP 오류, C# 오류, 또는 다른 출력을 내므로, 상위 폴더에 두면 CI가 실패한다.

트랜스파일러가 0.7을 구현하면 다음 중 하나를 하면 된다.

1. 하네스가 이 폴더도 읽도록 넓히고, `.in`을 넣어 실행한 결과를 `.out`과 비교한다.
2. 파일을 상위 `tests/TestCodes`로 옮긴다 (컴파일 검사만 받게 된다).

## 0.6.7 결과 요약

| 프로그램 | 0.6.7 결과 | 주요 변경 |
|---|---|---|
| 01_grid_bfs_char_board | C# 오류 (중첩 builder), 격자 입력 오독 | B1, B4 |
| 02_slice_explicit_bounds | PSCP 오류 | A3 |
| 03_implicit_return_calls | PSCP 오류 | A1, C12 |
| 04_pipe_and_line_continuation | PSCP 오류 | A2, C4, C7 |
| 05_space_call_groups | C# 오류 | C2, C3, C6 |
| 06_string_ordinal_order | 다른 출력 | B13, B12 |
| 07_output_rendering | C# 오류 (중첩 builder), 다른 렌더링 | B4, B6 |
| 08_integer_math | C# 오류 | B8, D7, B5 |
| 09_read_tokens_until_eof | PSCP 오류 | B3 |
| 10_read_lines_crlf | PSCP 오류 | B2, B3 |
| 11_types_constants_ordering | C# 오류 | B17, A6, D6 |
| 12_binary_search_and_find | PSCP 오류 | D7, B15, C8 |
| 13_patterns_and_ranges | PSCP 오류 | C8, B10 |
| 14_destructuring_iteration | PSCP 오류 | B11, C13 |
| 15_name_resolution | C# 오류 | A4, C11, A5 |
| 16_conversions | 다른 출력 | B6, B5 |
| 17_new_input_api | PSCP 오류 | D1, B16 |

기대 출력 중 BFS(01), 이분 탐색(12), 그래프 차수(14), 암묵적 반환(03), 큰 수 계산(08)은 Python 참조 계산으로 다시 확인했다.
