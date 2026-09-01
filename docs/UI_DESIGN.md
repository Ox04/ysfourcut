# UI 디자인 (07번에서 확정)

작성일: 2026-08-31. 실제 구현된 코드 기준이다. **후속 작업(08~12)은 이 방향을 임의 재디자인하지 않고
토큰·컴포넌트를 재사용한다.** 세부 조정·마감은 12번에서 이 문서를 이어 쓴다.

## 방향 (하나로 확정)

**영수증 티켓 키오스크.** 밝은 종이색 배경 + 따뜻한 검정 + 포인트 색 하나(셔터 레드).
사진과 서버가 만든 영수증이 화면의 중심이고, 장식 모티프는 절취선(점선) 하나만 쓴다.
관리자 대시보드식 카드 나열, 긴 소개 문구, 외부 폰트/이미지 자산은 쓰지 않는다.

## 토큰 — `apps/web/src/styles/tokens.css`

| 그룹 | 주요 값 |
| --- | --- |
| 색 | `--paper #F4EFE4`(배경), `--paper-raised #FDFBF5`(패널), `--ink #211D16`, `--ink-soft #5C564B`, `--accent #BC3A14`(흰 글자 대비 ≈5.6:1), `--viewer-bg #57524A`(영수증 받침) |
| 타이포 | 시스템 한국어 스택(`--font-sans`), 수치는 `--font-mono`. display 40 / title 24 / body-lg 18 / small 14 |
| 간격 | `--sp-1..8` = 4/8/12/16/24/32/48/64 |
| 터치 | `--touch-min 48px`(모든 주요 동작), `--touch-lg 60px`(촬영 시작·가상 출력) |
| 포커스 | `--focus-ring` — 종이색+포인트색 이중 링, `base.css`의 `:focus-visible` 전역 적용 |
| 동작 | `--dur-quick 140ms`, `--dur-slide 420ms`. `prefers-reduced-motion`에서 base.css가 전부 무효화 |

본문 한국어는 `word-break: keep-all`(base.css). appearance PNG는 배경이 투명하므로 반드시
`--viewer-bg` 같은 중간톤 받침 위에 올린다(`docs/RECEIPT_APPEARANCE.md`).

## 컴포넌트 — `apps/web/src/ui/`, 스타일 `styles/booth.css`

| 컴포넌트 | 용도 |
| --- | --- |
| `Button` (`.btn`) | primary(포인트)/secondary(외곽선)/quiet, md=48px·lg=60px |
| `StatusBadge` (`.badge`) | 가상 출력 상시 배지(virtual), ok/warn/danger/neutral |
| `TicketPanel` (`.panel`) | 종이 패널, `perforated`=절취선 윗변 |
| `CutProgress` | `2 / 4` + 점 4개 |
| `Countdown` | 카운트다운 표시(aria-hidden 장식). 남은 초 낭독은 촬영 화면의 안내 문구(role=status) 하나가 담당 |
| `SegmentedControl` | 프레임 선택, 결과 보기 모드. `aria-pressed` 버튼 그룹 |
| `SliderField` | 밝기/대비 행 |

## 화면 — `apps/web/src/booth/`

| 파일 | 내용 | 연결 상태 |
| --- | --- | --- |
| `BoothShell` | 로고·단계(시작/촬영/편집/결과)·**가상 출력 상시 배지** | 완료 |
| `StartScreen` | 티켓 카드 + 촬영 시작(lg) + 장치 배지 | `onStart` → 10번 |
| `CaptureScreen` | 프리뷰 자리(`preview` prop)·카운트다운·플래시·슬롯 4개·촬영 오류 문구 | **08번 연결 완료** (`camera/CameraPreview`) |
| `EditScreen` | 스트립 **편집 미리보기**(CSS 필터)·프레임 2종·밝기/대비·문구(24자)·가상 출력 | 실제 픽셀 처리 09번, 제출 10번 |
| `ResultScreen` | 상태별(만드는 중/완료/실패·부분 진단) + `ReceiptViewer` + 저장/다시 출력/다음 촬영 | 데이터 10번 |
| `ReceiptViewer` | 서버 PNG 4모드: 실물 느낌(화면 맞춤)·용지 그대로(1:1)·원본(1:1)·픽셀 확대(4배 정수) + dot/mm 치수 | 완료 |
| `types.ts` | `CutSlot`·`EditSettings`·`DeviceReadiness`·`ResultView` — **props/hooks 경계** | 08~10이 채움 |
| `fixtures.ts` | 인공 SVG 샘플 컷·긴 문구 샘플 (개발 미리보기 전용) | 제품 미사용 |
| `BoothPreview` | **개발 전용** 장면 드라이버(`#scene=` 해시). rendered 상태만은 fixture 금지 — 실제 서버 PNG를 받아야 보인다 | 디자인 확인용으로 유지 |
| `BoothFlow` | **실제 흐름**(08번): 카메라 준비 → 시작 → 네 컷 촬영 → 편집. 기본 화면이며 `#scene=`이 있을 때만 미리보기로 전환 | 출력 제출은 10번 |

원칙:
- CSS로 영수증을 다시 그리지 않는다. 결과는 서버 PNG 원본을 표시한다. 실물 느낌만 축소 허용,
  용지/원본은 1:1(픽셀레이티드, 넘치면 스크롤), 픽셀 확대는 4배 정수(`zoom: 4`).
- 서버가 `rendered`를 주기 전에는 어떤 성공 표현도 없다. 배출 효과(`viewer__stage--eject`)는
  rendered 뒤 1회의 장식이고 reduced-motion에서 꺼진다.
- 촬영 화면은 스크롤 없이 한 화면(프리뷰 `min(56vh, 540px)`).
- 운영 도구(프로필·모의 오류 주입·개발 확인)는 `<details class="device-panel">`로 방문자 흐름과 분리.

## 12번 마감에서 조정한 것 (2026-09-01, Fable 메인 세션)

실제 앱(가짜 카메라, 실서버)을 1280×800·390×844에서 전 흐름으로 직접 보고 조정했다. 토큰·방향은 그대로다.

- **시작 화면 카메라 미리보기** — 라벨 없는 원본 `<video>` 블록이 시작 티켓과 경쟁하던 것을
  320px 카드(`booth-flow__camera-frame`, radius/테두리) + "카메라 확인용 미리보기" 캡션으로 정리.
  스트림은 촬영 예열을 위해 계속 켜 둔다. 오류 시에는 기존 `camera-error`(role=alert)가 그대로 보인다.
- **컷 다시 찍기 버튼**(09번이 넘긴 후보) — 프레임 선을 넘던 -8px 겹침을 없애고 컷 안쪽 모서리에
  8px 여백으로 앉힘. 배경 78%→62% + 흰 hairline(35%)로 사진 가림을 줄였다. 터치 48px는 유지.
- **정리 패널 제목** — blocked 상태에서 "정리 중"이던 제목을 "정리하지 못했어요"로 상태에 맞춤
  (running은 "정리 중" 유지, 본문 role=alert 그대로).
- **편집 안내 문구** — "왼쪽 미리보기가…"를 "미리보기 스트립이…"로. 좁은 화면에서는 미리보기가 위에 있다.

## 실제 확인한 것 (2026-09-01)

환경: **WSL headless Chromium(Playwright)**. Windows Chrome/Edge에서의 확인(폰트 렌더링 차이 포함)은 남아 있다.

- 1280×800·390×844 × 장면 9종(시작 정상/출력 불가, 촬영 대기/카운트다운/셔터, 편집 기본/전송 중,
  결과 만드는 중/실패)을 스크린샷과 **수치 측정**(가로 넘침 px, 터치 영역 px)으로 확인 — 전부 넘침 0,
  48px 미만 터치 0.
- 결과 화면은 실제 `POST /api/print-jobs` → 서버 PNG로 채웠고(실측 배율: 용지/원본 1.000, 픽셀 확대 4.000,
  실물 느낌 균일 축소), 가짜 성공 이미지는 쓰지 않았다.
- 키보드 Tab 순회·포커스 이중 링, reduced-motion(링·스피너·플래시 무효화)도 실측.
- 독립 검수(별도 Opus 검수자)가 390 폭에서 촬영 256px·시작(출력 불가) 52px·편집 15px **가로 넘침을
  발견**했고(초기 자체 확인의 누락), 프리뷰 크기 계산·배지 줄바꿈·좁은 화면 필드 배치로 수정한 뒤
  위의 전 장면 수치 측정으로 재검증했다. 접근성 지적(진행 표시 미노출, 재촬영 버튼 이름 중복,
  라이브 영역 중복, 대비 미달 4곳, busy 중 슬라이더 활성, 포커스 링 잘림, h1 부재 등)도 반영했다.

스크린샷 재생성: 세션 스크래치의 임시 도구(저장소 밖) 또는 Windows 브라우저에서 `#scene=<장면>` 해시로 이동.

## 후속 연결 위치 요약

- ~~08 카메라~~ 완료: `camera/`(순수 상태 머신 + 훅)와 `BoothFlow`가 연결했다. 크롭은 `object-fit: cover` ↔ `coverCropRect`, 반전은 CSS `scaleX(-1)` ↔ canvas `scale(-1,1)`로 대응한다.
- 09 합성: `EditSettings`를 실제 픽셀 처리(밝기/대비/프레임/문구/디더링)로 반영해 1비트 비트맵 생성.
- 10 연결: `BoothFlow`에 제출·폴링·결과·정리를 잇는다(현재 편집 화면의 `가상 출력`은 비활성).
  `ResultView`는 기존 `api.printJob`/`fetchArtifactBlobUrl` 결과로 채운다.
- ~~12 마감~~ 완료: 위 "12번 마감에서 조정한 것" 절. 토큰 값 변경은 없었다. Windows 브라우저·실물 웹캠 확인은 남아 있다.
