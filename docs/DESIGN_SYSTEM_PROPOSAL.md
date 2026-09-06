# 디자인 시스템 v2 — 확정 스펙과 데모

상태: **구체화 완료, 데모 검수 대기.** 2026-09-06, 사용자 결정 반영:
포인트 색은 **모노크롬 + 셔터 레드 유지**, 배경은 **쿨 그레이 기본**(단, 데모에서
쿨/종이색/다크 3안을 나란히 비교 후 최종 확정). 실제 화면(07/12 산출물) 적용은
데모 검수 통과 후 별도 단계로 진행하며, 그 전까지 `docs/UI_DESIGN.md`의 현행 시스템이 유효하다.

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
| `apps/web/src/ds/components.tsx` | Button/StatusBadge/Panel/SegmentedControl/Select(커스텀 드롭다운)/SliderField/TextField/CutProgress/Countdown + 확장 대비: Keypad/CodeInput(OTP 6칸)/Dialog/Notice/Spinner — 시맨틱 토큰 유틸리티만 사용 |
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
  닫힘은 즉시, 항목 48px, 키보드 조작·reduced-motion 대응). Keypad 키 60px, CodeInput 칸 56px.
- 대비: 본문 4.5:1 이상. 데모 검수와 실제 적용 시 재측정한다.

## 다음 단계 (검수 통과 후)

1. 데모에서 배경안 확정. **유력안(2026-09-06): 2 종이색 ↔ 3 다크 페어를 두고 앱에서
   전환하는 구성** — 확정되면 cool은 제거하고 전환 토글을 설계에 포함한다.
2. 실제 화면 적용: `styles/tokens.css·base.css·booth.css`와 `ui/`를 `ds/`로 교체,
   `.ds-root` 최소 리셋을 `tailwindcss/preflight.css`로 전환, `docs/UI_DESIGN.md` 갱신.
   04/07/12와 같은 절차(실화면 실측 + 독립 검수)로 진행한다.
