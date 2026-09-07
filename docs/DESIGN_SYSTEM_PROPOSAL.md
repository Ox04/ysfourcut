# 디자인 시스템 v2 — 확정 스펙과 데모

상태: **확정·실제 화면 적용 완료.** 2026-09-07 사용자 확정: **쿨 그레이 단일 테마**
(warm/dark 비교안은 제거). 적용 방식: `styles/tokens.css`의 토큰 값을 v2 값으로 교체하는
브리지 — 검증된 화면 구조(booth.css)는 유지한 채 전 화면이 새 시스템을 입는다.
`src/ds/ds.css`가 시스템 원본이고 `styles/tokens.css`는 같은 값의 사본(함께 변경).
booth.css의 잔여 둥근 모서리도 0으로 정리했다(스피너 원형만 유지).

## 방향

Carbon/Amplify 계열: **모서리 0, 그림자 대신 1px 경계선, 절제된 중립 팔레트 + 포인트 색 하나.**
유지하는 장식 모티프는 영수증 절취선(굵은 점선) 하나다.

## 보는 법 (데모)

```
npm run dev  →  http://127.0.0.1:5173/#design
```

개발 전용 해시 라우트다(운영 번들 제외). 상단 스위처로 배경 3안(쿨 그레이/종이색/다크)을
전환하며 색·타이포·버튼·배지·패널·컨트롤·진행 표시를 검수한다. 색 토큰마다 실제 적용된
hex 값이 함께 표시된다.

## 구조 — 어디를 고치면 무엇이 바뀌나

| 파일 | 역할 |
| --- | --- |
| `apps/web/src/ds/ds.css` | 토큰 전부. ① 원시 팔레트(`--ds-gray-*`, `--ds-red-*`) ② 시맨틱(`--ds-bg`, `--ds-text`…, `[data-ds-theme]`별 재정의) ③ Tailwind `@theme` 매핑(`--color-*`, `--font-*`, `--text-*`) |
| `apps/web/src/ds/components.tsx` | Button/StatusBadge/Panel/SegmentedControl/Select(커스텀 드롭다운)/SliderField/TextField/CutProgress/Countdown + 확장 대비: Keypad/Keyboard(화상 QWERTY)/CodeInput(OTP 6칸)/Dialog/Notice/Spinner — 시맨틱 토큰 유틸리티만 사용 |
| `apps/web/src/ds/DesignSystemDemo.tsx` | 검수용 데모 페이지(`#design`) |

색 일괄 교체 = `ds.css`의 시맨틱 블록 값만 변경. 폰트 교체 = `@theme`의 `--font-sans` 한 줄
(+ `@font-face`). 컴포넌트는 원시 팔레트를 직접 참조하지 않는다. Tailwind 기본 팔레트·
라운드·그림자는 `initial`로 비워 시스템 밖 값 사용을 컴파일 단계에서 차단했다.

## 확정 토큰 요약

| 그룹 | 값 |
| --- | --- |
| 폰트 | **Wanted Sans Variable**(로컬 woff2, OFL) → Pretendard → 시스템 스택. 수치는 `--font-mono` |
| 타입 | display 42 / title 28 / heading 20 / body-lg 18(키오스크 본문) / body 16 / label 14 / caption 12 |
| 모서리 | 전부 0 (`--radius-*` 제거) |
| elevation | 그림자 제거, `--ds-line`(1px) / `--ds-line-strong` 경계선 |
| 중립 | **stone 웜그레이 램프** `--ds-gray-0..100` (#FFFFFF~#1C1917) — 영수증 종이·버밀리언과 온도 통일. 새 색은 반드시 램프에서 고른다 |
| 포인트 | 셔터 버밀리언 `--ds-red-60 #C73E1D`(흰 글자 5.1:1), 다크 테마는 `--ds-red-30 #FF7E5E`(짙은 글자 6.8:1) |
| 상태색 | 같은 명도 스텝에서 통일 — ok emerald-700 `#047857` / warn amber-800 `#92400E` / danger red-700 `#B91C1C` (다크: 각 400 스텝) |
| 버튼 | primary=잉크 단색, **accent=버밀리언(촬영 시작·출력 등 핵심 순간 전용)**, secondary=외곽선, ghost. md 48 / lg 60px |
| 포커스 | 2px 실선 outline + 2px offset (`--ds-focus`) |
| 간격·터치 | 기존 4px 그리드·48/60px 터치 크기 유지 (변경 없음) |

테마: `.ds-root`(cool 기본) / `[data-ds-theme='warm']`(현행 종이색 값 재사용) /
`[data-ds-theme='dark']`. 시맨틱 변수만 재정의하므로 컴포넌트 코드는 테마와 무관하다.

## 유지되는 규칙 (스타일 아님)

- 결과는 서버 PNG 원본 표시, CSS로 영수증을 다시 그리지 않음. appearance PNG는 중간톤
  받침(`--ds-viewer`) 위에 올림.
- 터치 48/60px, 포커스 링, `prefers-reduced-motion` 무효화, `word-break: keep-all`.
- 터치 우선: 슬라이더는 커스텀 28px 썸 + 48px 히트 영역(`.ds-range`), 소수 선택지는
  SegmentedControl, 목록형은 Select(커스텀 listbox 드롭다운 — 열릴 때 `ds-drop`으로 내려오고
  닫힘은 즉시, 항목 48px, 키보드 조작·reduced-motion 대응). Keypad 키 60px(지우기 ⌫ 하나),
  CodeInput 칸 56px. Keyboard(화상 QWERTY)는 OS 소프트 키보드 대체 — 대상 입력창에
  inputMode="none", 키는 pointerdown preventDefault로 포커스를 안 뺏어 물리 키보드와 병행,
  숫자열 상시 노출·원샷 Shift·이메일 기호(@ . - _). 한글 조합 입력은 범위 밖(물리 키보드용).
- 대비: 본문 4.5:1 이상. 데모 검수와 실제 적용 시 재측정한다.

## 남은 정리 (후속, 기능 영향 없음)

- booth.css 화면 스타일을 점진적으로 Tailwind 유틸리티/ds 컴포넌트로 이관
  (`ui/Button·parts` → `ds/components`), 완료 시 `.ds-root` 최소 리셋을 preflight로 전환.
- 촬영 카운트다운 링(원형 SVG)의 각형 전환 여부 — 기능(남은 초 진행 표시)이라 보류.
- Windows 브라우저·실물 웹캠 확인(기존 07/12와 동일하게 남음).
