# 가상 출력 통합 검수 기록 (11번)

작성일: 2026-09-01. 실제로 실행한 명령과 그 결과만 적는다. 실행하지 않은 검수는 NOT_RUN으로 남긴다.
근거 기준은 `tasks/11.md`의 AC, `tasks/DEFINITION_OF_DONE.md`, `VIRTUAL_PRINTER_DESIGN.md` 12절,
`IMPLEMENTATION_OUTLINE.md` 8절 소프트웨어 체크리스트다.

개인 사진·base64·인증 토큰은 이 문서에 넣지 않는다. 이미지 근거는 전부 인공 fixture와
Chromium 가짜 카메라 패턴이며, 픽셀 비교는 개수·좌표·치수로만 기록한다.

## 1. 검수 환경 구분

같은 "확인했다"가 아니다. 아래 다섯 가지를 섞지 않는다.

| 구분 | 이번에 한 일 |
| --- | --- |
| A. WSL 자동 시험 | `npm run typecheck/lint/test/build`, `npm run build:host`, `npm run test:host` |
| B. WSL 헤드리스 브라우저 | Playwright 1.62.1 Chromium(**가짜 카메라** `--use-fake-device-for-media-stream`)으로 공유 dev 서버(5173/4317) 전 구간 통과 |
| C. WSL 격리 호스트 | `unshare -rn`으로 만든 별도 network namespace 안에서 실제 .NET 호스트(+ 필요 시 Vite)를 직접 시작·강제 종료·재시작. 공유 dev 서버는 건드리지 않았다 |
| D. Windows 브라우저 | **NOT_RUN** (5절) |
| E. 실물 웹캠 / 실물 프린터 | **NOT_RUN** (5절). 프린터는 90/91번 대상 |

환경 상세: WSL2(Linux 6.6.87.2) Fedora 42, Node 24.19.0 / 25.2.1, .NET SDK 10.0.400,
Chromium(Playwright 1.62.1 npx 캐시), 공유 dev 서버 `node scripts/dev.mjs`(다른 세션 소유, 재시작하지 않음).

격리 호스트는 `apps/print-host/bin/Debug/net10.0/YsFourcut.Host`를 새 network namespace에서 실행하고
`--Storage:ConfigDirectory` / `--Storage:StateDirectory`를 임시 폴더로 돌린 것이다. 포트는 안쪽 namespace의
127.0.0.1:4317이라 바깥 공유 서버와 충돌하지 않는다. 이 방식으로 **호스트 강제 종료·재시작·.NET 중단**을
공유 서버를 죽이지 않고 실제로 시험했다.

## 2. AC-11-01 — 실제 명령과 결과 (환경 A)

| 명령 | 결과 |
| --- | --- |
| `npm run typecheck` | 통과 (packages/imaging, apps/web) |
| `npm run lint` | 통과 (apps/web) |
| `npm run test` | 통과 — imaging 52 + web 77 = **129** |
| `npm run build` | 통과 — `dist/assets/index-*.js` 254.87 kB (gzip 80.66 kB) |
| `npm run build:host` | 통과 |
| `npm run test:host` | 통과 — **164** (기존 162 + 이번에 추가한 회귀 2건) |

### 간헐 실패 1건을 재현하고 고쳤다

`npm run build:host && npm run test:host`를 새로 빌드한 직후 처음 돌렸을 때
`PrinterProfileApiTests.Changing_print_critical_setting_changes_revision`이
`System.ObjectDisposedException: The CancellationTokenSource has been disposed.`로 1회 실패했다
(스택: `PrintJobManager.ShutdownAsync()` ← `JobMaintenanceService.StopAsync` ← `WebApplicationFactory.DisposeAsync`).
같은 테스트 단독 3회, 전체 스위트 3회 연속에서는 재현되지 않았다.

원인은 테스트가 아니라 종료 경로다. `PrintJobManager`는 DI 컨테이너가 소유하므로 컨테이너 정리가
`IHostedService.StopAsync`보다 먼저 끝나면 `ShutdownAsync()`가 이미 Dispose된 취소 토큰을 취소하려다 던진다.
`ShutdownAsync()`에서 `ObjectDisposedException`을 흡수하도록 고치고
`PrintJobConcurrencyTests.Shutdown_after_dispose_does_not_throw`로 결정적으로 재현·고정했다
(수정 전 실패, 수정 후 통과 확인).

05·06번 handoff가 "원인 미확인"으로 넘긴 간헐 실패도 **같은 테스트 클래스**(`PrinterProfileApiTests`)였다.
05번 기록에는 메시지가 없어 동일 원인이라고 단정하지는 않지만, 이번 수정으로 그 종료 경로는 닫혔다.

## 3. AC-11-02 — 실제 Linux .NET E2E (환경 B)

공유 dev 서버(Vite 5173 → .NET 4317)에 대해 Playwright로 12항목을 측정했다. **API stub은 쓰지 않았다.**
카메라만 Chromium 가짜 장치이며, 이것이 실물 웹캠 검수를 대체하지 않는다.

> **근거 시점 주의(독립 검수 지적 반영).** 이 절의 12항목은 **5절의 결함 1·2를 고치기 전 바이너리**로 측정했다.
> 공유 dev 호스트는 이번 작업 이전에 기동해 재시작하지 않았고(소유가 다른 프로세스), 수정은 그 뒤에 들어갔기 때문이다.
> 두 수정은 렌더링 경로를 바꾸지 않으며(종료 예외 처리와 `/pair` 응답 작성), 수정 후 .NET 근거는 2절의 164개 시험과
> 4절 격리 호스트(환경 C) 배터리가 따로 제공한다. `/pair`의 302·fragment 계약은 수정 전·후 같은 시험으로 통과가 유지된다.
> 이 절의 수치를 수정 후 실서버 근거로 쓰려면 `kill 172366` 뒤 `npm run dev`로 호스트를 새 코드로 올려 재측정해야 한다.

| 항목 | 결과 |
| --- | --- |
| 페어링 | `http://127.0.0.1:4317/pair` → `http://127.0.0.1:5173/`로 이동하고 시작 화면 표시 |
| 네 컷 | 편집 화면 진입, 컷별 `다시 찍기` 버튼 4개 |
| 재촬영 | 3컷만 다시 촬영 후 편집 화면 복귀(전체 재촬영 아님) |
| 한글·미리보기 | 문구 `가나다라 한글 문구 2026` 입력, 미리보기 blob 576×1788dot |
| 접수 | `POST /api/print-jobs` **1회**, UUID 1개 |
| 서버 상태 | `state=rendered`, `isPhysical=false`, `spoolJobId=null`, `isSimulated=true`, `appearanceRevision=a1` |
| 치수 | 지면 640×1876, 내용 576×1788, 원점 (32,24), 이송 24/64, 203.2×203.2 DPI |
| 입력 ↔ content | 브라우저가 실제로 보낸 비트맵과 서버 `content.png` **다른 픽셀 0개**, 회색 픽셀 0개(1비트 유지) |
| content ↔ paper crop | `paper.png`의 (32,24) crop이 content와 **다른 픽셀 0개**. 앞이송 3행·뒤이송 3행 전부 흰색, **마지막 내용 행 일치**, 좌우 여백(0/31/608/639열) 흰색 |
| appearance | 688×1924 = 지면 + 사방 24px 여백. 실제 서버 응답을 인증 fetch Blob으로 표시 |
| 결과 화면 | 뷰어 이미지 표시, 콘솔 오류 0 |
| 정리 | `다음 촬영` 뒤 같은 작업 결과 재조회 **410 ARTIFACT_EXPIRED** |

폭 변화도 실제 호스트에서 확인했다(환경 C). 58mm 프리셋으로 바꾼 뒤 384dot 입력 →
지면 **464×688**, 내용 384, 원점 (40,24), 이송 24/64. 같은 프로필에 576dot을 보내면
`400 PAGE_SIZE_UNSUPPORTED (limit: profile)`로 거절되고, 검사 뒤 80mm로 복구했다.

8의 배수가 아닌 폭·비트 방향·마지막 행·padding 비트는 09번이 넣은
`tests/print-host/BitmapRoundTripTests.cs`가 실제 렌더러로 계속 검사한다(`npm run test:host`에 포함).

## 4. AC-11-03 — 중복·유실·실패·재시작 (환경 C)

격리 namespace의 실제 호스트 한 대를 시작해 14항목을 순서대로 측정했다. 전부 통과.

| 항목 | 실제 결과 |
| --- | --- |
| OS 큐 미조회 | `GET /api/printers` `osQueryPerformed=false`, 두 프리셋 모두 `kind=virtual`·`hardwareVerified=false`, `printerMode=virtual` |
| UUID 연타 | 같은 UUID·같은 내용 POST 5회 동시 → 전부 202, **작업 기록 1건만 증가**, 최종 `rendered` |
| UUID 충돌 | 같은 UUID·다른 내용 → `409 JOB_ID_CONFLICT` |
| 응답 유실 | 소켓으로 요청만 보내고 응답 1바이트도 받지 않고 끊음 → 같은 UUID 조회로 `rendered` 수신, 기록 1건, 재요청의 `createdAtUtc` 동일(재렌더 없음) |
| 프로필 변경 | `cutStyle` 변경으로 revision이 바뀌고, 옛 revision 제출은 `409 PROFILE_CHANGED`. 검사 뒤 원래 revision으로 복구 |
| 부분 실패 | `fail_after_rows=60` → `virtual_failed / VIRTUAL_RENDER_FAILED`, content `available=true, complete=false`(HTTP 200), **paper 404** |
| worker 무응답 | `render_timeout` → `RENDER_TIMEOUT`으로 확정, 기록 1건, 재조회도 실패 그대로, **남은 worker 프로세스 0** |
| worker 강제 종료 | 렌더 중 `--print-worker` 프로세스를 `kill -9` → `virtual_failed / VIRTUAL_RENDER_FAILED`, 기록 1건, paper 없음. 자동 재실행 없음 |
| host 재시작 | 진행 중 작업이 있는 상태에서 호스트를 `SIGKILL` → 연결 `ECONNREFUSED` → 재시작하니 `hostInstanceId` 교체. 진행 중이던 작업은 `virtual_failed / HOST_RESTARTED`, 그 작업의 이미지는 404, **재시작 전 `rendered`였던 작업의 이미지는 410 ARTIFACT_EXPIRED**(상태 조회는 되지만 `artifacts.available=false`). 2초 뒤 재조회해도 재렌더 흔적 없음 |

어떤 실패에서도 앱·서버가 새 UUID를 자동으로 만들어 재접수하지 않았고, 잘못된 `rendered` 표시도 없었다.

## 5. AC-11-04 — 인증·보관·중단·인쇄 큐

### 5.1 세션 격리와 조회 (환경 C)

- 인증 없이 `GET /api/print-jobs/{id}` → **401**, 같은 작업의 artifact → **401**.
- **다른 세션**(같은 호스트에서 새로 페어링)으로 남의 작업 조회 → **404**, artifact → **404**. 소유 세션은 200.

### 5.2 보관 한도 (환경 C)

- **세션당 3작업**: 네 번째 작업을 끝내자 가장 오래된 결과가 `410 ARTIFACT_EXPIRED`, 최신 결과는 200.
- **명시적 clear**: 진행 중 clear는 `409 PRINTER_BUSY`. 끝난 뒤 clear는 `remainingJobs=0`이고 결과 재조회는 `410 ARTIFACT_EXPIRED`.
- **절대 TTL 10분**: 가짜 시계가 아니라 **실제 시계**로 측정했다. 같은 작업의 `content`를
  0초/301초/541초에 조회하면 200, **601초·661초에는 410 `ARTIFACT_EXPIRED`**였다(조회로 연장되지 않음).
  만료 뒤에도 작업 상태 조회는 200이지만 `artifacts.available=false`이고 반복 조회도 계속 410이다.
  브라우저를 열어 두지 않아도 호스트 타이머가 스스로 정리한다.
- **정리 뒤 늦은 게시**: 실서버에서 clear는 진행 중이면 409이므로 HTTP만으로 경합 창을 만들 수 없다.
  이 규칙은 `tests/print-host/ArtifactLifecycleTests.Publish_after_a_clear_is_rejected_so_old_images_never_come_back`가
  세대 번호로 검사한다(자동 시험 근거이며 실서버 경합 실측은 아니다).
- **호스트 전체·작업당 바이트 한도**: `Host_byte_limit_evicts_the_oldest_results`,
  `Job_over_the_per_job_byte_limit_is_not_stored`(자동 시험 근거).

### 5.3 로그·디스크 보관 (환경 C) — 결함 1건 발견·수정

격리 호스트를 기본 로그 수준(Information)으로 띄우고 전 과정을 돌린 뒤 로그와 디스크를 검사했다.

- 사진·비트맵·base64가 로그에 없음. 결과 PNG 파일이 디스크에 만들어지지 않음(이미지 파일 0건).
- 작업 기록은 `state/jobs/*.json`뿐이고 키는
  `clientJobId, createdAtUtc, failureCode, hostInstanceId, printerMode, profileId, profileRevision, requestDigest, schemaVersion, state, updatedAtUtc`
  — 사진·픽셀·인증값 없음.
- **발견**: `/pair`가 `Results.Redirect`를 쓰는 동안 프레임워크가
  `Executing RedirectResult, redirecting to …#bootstrap=<일회용 코드>`를 Information 로그로 남겼다.
  `docs/API_CONTRACT.md`의 "어떤 서버 로그에도 코드가 남지 않는다"는 사실 로그 수준을
  `Microsoft.AspNetCore: Warning`으로 낮춰 둔 설정에만 기대고 있었다.
  → `Api/BootstrapEndpoints.cs`가 Location 헤더를 직접 쓰고 상태 코드만 반환하도록 고쳤고,
  `BootstrapTests.Code_never_reaches_logs_even_with_every_filter_removed`(로그 필터를 전부 제거한 상태)로 고정했다.
  수정 뒤 같은 격리 실행에서 로그 의심 문자열 0건.

### 5.4 .NET 중단 (환경 C, 격리 namespace 안의 Vite + .NET + Chromium)

공유 dev 서버를 죽이지 않기 위해 namespace 안에 Vite와 .NET을 따로 세우고 브라우저까지 그 안에서 돌렸다.

- 네 컷 촬영 뒤 **.NET 호스트를 `SIGKILL`** → `ECONNREFUSED`.
- 그 상태에서 `가상 출력` → 12초 동안 결과 이미지 0장, 성공 문구 없음, POST 시도 1회(UUID 1개).
  **프런트 단독 성공이 없다.**
- 한국어 실패 안내와 재시도 버튼이 나오고, 재시도는 **사용자가 누를 때만** 새 UUID 1건을 만든다(자동 재시도 없음).

### 5.5 Windows 인쇄 큐 미호출

- `apps/print-host` 전체에 `winspool` / `System.Drawing.Printing` / `PrintDocument` / `OpenPrinter` /
  `StartDocPrinter` / `WritePrinter` / `lp` / `lpr` / `cups` 참조가 **0건**이다.
- 프로세스 생성은 `VirtualReceiptPrinter`의 렌더 worker 한 곳뿐이고, 실행 중 실제로 뜬 자식 프로세스는
  `YsFourcut.Host --print-worker`만 관찰됐다(강제 종료 시험에서 pid 확인).
- `GET /api/printers`는 `osQueryPerformed=false`이고 가상 프리셋만 돌려준다.
- Linux에서 `--PrinterMode=physical`로 시작하면 `PLATFORM_UNSUPPORTED`로 **시작 자체를 거부**한다(자동 virtual 전환 없음).
- 가상 작업의 `POST …/resolve`는 `409 RESOLVE_NOT_REQUIRED`(실물 확인 절차를 요구하지 않는다).

## 6. AC-11-05 — 자동 샘플 시험과 실물 검수의 구분

이 검수에서 **하지 않은 것**을 한곳에 모은다. 아래는 전부 그대로 NOT_RUN이며, 위의 자동/헤드리스 결과가
이것을 대신하지 않는다.

| 미실행 항목 | 남아 있는 번호 |
| --- | --- |
| Windows Chrome/Edge에서 화면 확인(연결 문구, fragment 제거·쿠키 저장) | 02 |
| Windows 브라우저에서 개발 확인 화면의 Blob PNG 표시·폴링 | 05 |
| Windows 브라우저에서 모의 오류 주입 → 실패 문구 표시 | 06 |
| Windows 브라우저의 07 화면·한국어 폰트 렌더링 | 07 |
| **실물 웹캠** 촬영·반전·크롭·재촬영 (AC-08-04) | 08 |
| Windows 브라우저에서 편집 미리보기·PNG 저장 | 09 |
| Windows 브라우저 + 실물 웹캠으로 시작→촬영→편집→가상 출력→결과→다음 촬영 (AC-10-01) | 10 |
| Windows 실행 폴더(SDK·인터넷 없이) 가상 출력 | 13 |
| **실물 프린터**(AHAPOS) 연결·명령·커터·농도 | 90 / 91 |

- 이번 브라우저 측정은 전부 **WSL 헤드리스 Chromium + 가짜 카메라**다. 실물 웹캠 검수로 쓰지 않는다.
- 실물 프린터는 준비되지 않았고, 가상 완료 조건에 넣지 않는다. 완료 표현은
  `웹→.NET 가상 영수증 검증 완료 / AHAPOS 실물 검수 대기`다.
- 가상 프리셋의 폭·DPI·명령·커터는 **하드웨어 검증값이 아니다**(`hardwareVerified=false`).

## 7. AC-11-06 — 발견한 결함과 조치

| # | 내용 | 조치 | 회귀 |
| --- | --- | --- | --- |
| 1 | 호스트 종료 시 `PrintJobManager.ShutdownAsync()`가 이미 Dispose된 취소 토큰 때문에 `ObjectDisposedException`을 던져 종료 경로를 깨뜨린다(전체 스위트에서 간헐 실패로 드러남) | `apps/print-host/Jobs/PrintJobManager.cs` — 취소 실패를 흡수 | `PrintJobConcurrencyTests.Shutdown_after_dispose_does_not_throw` |
| 2 | `/pair`의 일회용 코드가 프레임워크 RedirectResult 로그에 남는다(로그 수준 설정에만 의존) | `apps/print-host/Api/BootstrapEndpoints.cs` — Location 헤더 직접 작성 | `BootstrapTests.Code_never_reaches_logs_even_with_every_filter_removed` |
| 3 | 개발용 `인공 비트맵 출력` 버튼이 운영 번들에 포함돼, 누르면 제품과 무관한 작업이 서버 잠금을 차지하고 그동안 부스 출력이 거절된다 | `apps/web/src/dev/DevPrintCheck.tsx` — 그 버튼과 확인용 이미지 영역만 `import.meta.env.DEV`로 접음. 모의 오류 주입 select와 `서버 결과 정리`는 운영에 그대로 남긴다 | 운영 번들에 `인공 비트맵 출력` 문자열 0건(빌드 결과 확인), 개발 실행에서는 그대로 노출됨을 헤드리스로 확인 |

수정하지 않고 남긴 판단·관찰은 8절에 있다.

## 8. 남긴 판단과 미해결 항목

### 8.1 AC-10-02의 "재접속에서도 같은 작업을 조회" — 앱에는 **N/A**

앱은 진행 중 UUID를 어디에도 보관하지 않는다. `apps/web/src` 전체에 `localStorage` / `sessionStorage` /
`indexedDB` / `document.cookie` 사용이 **0건**임을 확인했다. 따라서 새로고침·재접속은 항상 새 촬영으로 시작한다.

그대로 두기로 판단한 이유:

1. 이 절이 막으려는 위험(자동 중복 인쇄)은 **서버의 같은 UUID 규칙**이 막고 있고, 이번 4절에서 실제로 측정했다.
   앱에는 새 UUID를 자동으로 만드는 경로가 없다(5.4절의 재시도도 사용자 조작 1회다).
2. 진행 중 UUID를 보관하면 새로고침 뒤 **이전 사용자의 영수증이 되살아난다**. 부스 제품에서 이것은
   10번 AC-10-05(다음 사용자에게 이전 결과가 보이지 않음)와 정면으로 충돌한다.
3. 11번은 검수 번호다. 복원 흐름을 새로 만드는 것은 이번 범위(재현 가능한 결함의 최소 수정)를 벗어난다.

개인정보 규칙과의 관계: 저장 대상은 사진이나 인증값이 아니라 UUID이므로 "원본은 브라우저 메모리" 규칙을
직접 위반하지는 않는다. 그러나 위 2번 때문에 **보관하지 않는 편이 규칙에 더 맞는다**고 판단했다.

10번 handoff의 "새로고침 뒤 같은 UUID 조회" 근거는 **서버에 직접 질의한 실측**이며 앱 동작이 아니다.
이 사실을 바꿔 쓰지 않는다.

### 8.2 그대로 둔 관찰

- 프로필 전환 직후 짧은 창에서 화면이 옛 revision을 들고 있을 수 있다. 서버가 `409 PROFILE_CHANGED`로
  거절하므로 섞인 결과는 생기지 않는다(4절에서 확인). 표시 문구 개선은 12번 후보.
- `applyStatus`가 실물 전용 상태(`submitted`, `outcome_unknown`)를 확정 실패로 접는다. 가상 경로에서는
  발생하지 않으며 90/91번 재설계 대상이다.
- 편집 화면의 컷별 `다시 찍기` 버튼이 좁은 스트립에서 커 보인다(09번이 남긴 12번 후보).

## 9. 재현 방법

- 자동 시험: `npm run typecheck && npm run lint && npm run test && npm run build`,
  이어서 `npm run build:host && npm run test:host`(둘은 같은 출력 폴더를 쓰므로 동시에 돌리지 않는다).
- 헤드리스 E2E: 공유 dev 서버(`npm run dev`)를 띄운 뒤 Playwright Chromium을
  `--use-fake-ui-for-media-stream --use-fake-device-for-media-stream`으로 열고
  `http://127.0.0.1:4317/pair`부터 시작한다. 검증 스크립트는 저장소에 넣지 않았다(세션 밖 임시 폴더).
- 격리 호스트: `unshare -rn bash -c 'ip link set lo up; …'` 안에서
  `apps/print-host/bin/Debug/net10.0/YsFourcut.Host --Storage:ConfigDirectory=… --Storage:StateDirectory=…`를 실행한다.
  안쪽 127.0.0.1:4317은 바깥 공유 서버와 겹치지 않으므로 강제 종료·재시작을 마음대로 시험할 수 있다.
- 서버 상태를 바꾸는 검증(모의 오류, 프로필, 결과 정리)을 했으면 반드시
  주입 `null`, 프로필 `virtual-80mm-8dpmm`, `remainingJobs=0`으로 되돌린다.
