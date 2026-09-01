# 로컬 API 계약 (02번에서 고정)

작성일: 2026-08-31. 실제 구현된 코드 기준이다. 계획만 있는 내용은 "미구현"으로 표시한다.
근거 문서는 `SERVICE_DESIGN.md` 8·10절, `VIRTUAL_PRINTER_DESIGN.md` 3·5·7절, `WSL_DEVELOPMENT.md` 4절이다.

- C# 타입: `apps/print-host/Contracts/ApiContracts.cs`, 오류 코드 `Contracts/ErrorCodes.cs`
- TypeScript 타입: `apps/web/src/api/contract.ts`, 호출부 `apps/web/src/api/client.ts`
- 두 목록은 짝이다. 한쪽만 바꾸지 않는다.

## 공통 규칙

| 항목 | 계약 |
| --- | --- |
| 주소 | 웹은 상대 경로 `/api/...`만 사용. 개발은 Vite(5173)가 4317로 프록시 |
| `schemaVersion` | 요청·응답 모두 `1`. 다르면 `SCHEMA_VERSION_UNSUPPORTED` |
| Host | 호스트명이 `127.0.0.1`이어야 한다. 아니면 403 `HOST_NOT_ALLOWED` |
| 전용 헤더 | 모든 `/api` 요청에 `X-YSFourcut-Client: web`. 없으면 403 `CLIENT_HEADER_REQUIRED` |
| Origin | 변경 요청(POST/PUT/PATCH/DELETE)은 정확한 Origin 필요. 개발은 `http://127.0.0.1:5173`·`http://127.0.0.1:4317`, 운영은 후자만. 아니면 403 `ORIGIN_NOT_ALLOWED` |
| Content-Type | 변경 요청은 `application/json`. 본문이 없어도 `{"schemaVersion":1}`을 보낸다. 아니면 415 |
| 본문 상한 | 1MiB. 초과 시 413 `BODY_TOO_LARGE` |
| 캐시 | `/api`와 `/pair` 응답은 `Cache-Control: no-store` |
| CORS | 사용하지 않는다. 교차 출처 읽기를 허용하지 않으며 `*`도 쓰지 않는다 |
| 오류 형태 | `{ "schemaVersion": 1, "error": { "code", "message", "details"? } }` |

오류 응답에는 사진·비트맵·일회용 코드·쿠키 값을 넣지 않는다. 서버 로그도 마찬가지다
(`tests/print-host/BootstrapTests.cs`의 `Code_and_session_cookie_never_reach_logs`가 확인한다).

## 인증

1. 사용자가 `GET /pair`를 브라우저에서 연다(전용 헤더 불필요, 내비게이션).
2. 호스트가 256비트 난수 일회용 코드를 만들고 `302`로 앱 주소 + `#bootstrap=<code>` fragment로 돌려보낸다.
   fragment는 이후 요청에서 서버로 전송되지 않는다. 302는 `Results.Redirect` 대신 Location 헤더를
   직접 써서 만든다 — 프레임워크의 RedirectResult 로그가 Location(=코드)을 남기기 때문이며,
   로그 수준 설정과 무관하게 코드가 로그에 남지 않도록 하기 위해서다(11번 검수에서 수정).
3. 웹앱이 주소에서 코드를 즉시 제거(`history.replaceState`)하고 `POST /api/bootstrap`으로 교환한다.
4. 코드는 **60초·1회**만 유효하다. 만료/재사용/불명은 각각 `BOOTSTRAP_CODE_EXPIRED` /
   `BOOTSTRAP_CODE_ALREADY_USED` / `BOOTSTRAP_CODE_INVALID`(401)다.
5. 세션 쿠키 `ysfourcut_session`: Domain 생략, `Path=/`, `HttpOnly`, `SameSite=Strict`.
   루프백 HTTP이므로 `Secure`에 의존하지 않는다. 호스트 프로세스 수명 안에서만 유효하고 유휴 12시간에 만료된다.
6. `bootstrap`과 `health`를 제외한 모든 API는 유효 세션이 필요하다(401 `SESSION_REQUIRED` / `SESSION_INVALID`).

담당 코드: `Security/BootstrapCodeStore.cs`, `Security/SessionStore.cs`,
`Security/LocalAccessMiddleware.cs`, `Security/SessionEndpointFilter.cs`, `Api/BootstrapEndpoints.cs`.

## 경로별 구현 상태

| 메서드·경로 | 세션 | 상태 | 담당 코드 |
| --- | --- | --- | --- |
| `GET /pair` | 불필요 | 구현 | `Api/BootstrapEndpoints.cs` |
| `POST /api/bootstrap` | 불필요 | 구현 | `Api/BootstrapEndpoints.cs` |
| `GET /api/health` | 불필요 | 구현 | `Api/HealthEndpoints.cs` |
| `GET /api/printers` | 필요 | 구현 | `Api/PrinterEndpoints.cs` |
| `GET /api/printer-profile` | 필요 | 구현 | `Api/PrinterEndpoints.cs` |
| `PUT /api/printer-profile` | 필요 | 구현 | `Api/PrinterEndpoints.cs`, `Profiles/PrinterProfileStore.cs` |
| `POST /api/print-jobs` | 필요 | 구현 (202 접수) | `Api/PrintJobEndpoints.cs`, `Jobs/PrintJobManager.cs` |
| `GET /api/print-jobs/{clientJobId}` | 필요 | 구현 | `Api/PrintJobEndpoints.cs` |
| `GET /api/print-jobs/{clientJobId}/artifacts/{kind}` | 필요 | 구현 (메모리 PNG) | `Api/PrintJobEndpoints.cs`, `Artifacts/InMemoryReceiptArtifactStore.cs` |
| `POST /api/print-jobs/{clientJobId}/resolve` | 필요 | 구현 (가상은 409 `RESOLVE_NOT_REQUIRED`, 실물은 91번) | `Api/PrintJobEndpoints.cs` |
| `POST /api/session/artifacts/clear` | 필요 | 구현 | `Api/PrintJobEndpoints.cs` |
| `GET /api/virtual-printer/fault` | 필요 | 구현 (주입 상태 조회) | `Api/PrintJobEndpoints.cs` |
| `PUT /api/virtual-printer/fault` | 필요 | 구현 (모의 오류 주입) | `Api/PrintJobEndpoints.cs`, `Printing/Virtual/VirtualFault.cs` |
| `POST /api/printer-tests` | 필요 | 미구현(501) — 05·91번 | `Api/PrinterEndpoints.cs` |
| `POST /api/printer-profile/verify` | 필요 | 미구현(501) — 91번 | `Api/PrinterEndpoints.cs` |

미구현 경로는 `501` + `NOT_IMPLEMENTED` + `details.implementedIn`(담당 task)을 반환한다.
어떤 경로도 사진을 받았다며 가짜 성공을 반환하지 않는다.

## health

`GET /api/health` (인증 전 조회 가능한 유일한 API)

```json
{ "schemaVersion": 1, "status": "ok", "ready": true, "printerMode": "virtual",
  "hostInstanceId": "…", "hostVersion": "0.2.0", "environment": "Development",
  "session": { "authenticated": false } }
```

파일 경로·프로필 상세·사진 관련 정보는 넣지 않는다. `hostInstanceId`는 프로세스마다 새로 만들며,
웹은 이 값으로 호스트 재시작(=이전 결과 캐시 소멸)을 알 수 있다.

## 프린터와 프로필

- 출력 모드는 **서버 시작 설정**(`PrinterMode`, 기본 `virtual`)에서만 정한다. 클라이언트가 요청마다 지정할 수 없다.
  Linux에서 `physical`은 시작 시점에 거부한다(`PLATFORM_UNSUPPORTED`, `Printing/PrinterMode.cs`). 자동으로 virtual로 바꾸지 않는다.
- `GET /api/printers`는 `osQueryPerformed: false`와 가상 프리셋만 반환한다. Windows 프린터 열거 함수를 호출하지 않는다.
- 프리셋은 `apps/print-host/Profiles/*.json`이며 값은 `VIRTUAL_PRINTER_DESIGN.md` 5절과 같다.

| 설정 | `virtual-80mm-8dpmm`(기본) | `virtual-58mm-8dpmm` |
| --- | --- | --- |
| 용지 폭 | 640dot (80mm) | 464dot (58mm) |
| 인쇄 내용 폭 | 576dot (72mm) | 384dot (48mm) |
| 좌우 여백 | 각 32dot | 각 40dot |
| 앞/뒤 이송 | 24 / 64dot | 24 / 64dot |
| 해상도 | 8dot/mm = 203.2DPI | 동일 |
| 절취 | `straight` 기본 (`tear`, `none` 선택) | 동일 |

두 프리셋 모두 `kind=virtual`, `hardwareVerified=false`이며 notes에 **AHAPOS 미검증**을 명시한다.
가상 프로필의 사용 가능 상태를 실물 검증으로 표시하지 않는다.

### revision

`revision`은 **인쇄 핵심 설정만**의 SHA-256 앞 8바이트다(`r1-<hex16>`, `Profiles/PrinterProfile.cs`).

- 포함: kind, 용지/내용 폭, 좌우 여백, 앞뒤 이송, dot/mm, 절취 방식, 프로필 상한
- 제외: 표시 이름, 메모
- 같은 설정이면 호스트를 재시작해도 같은 값이다. 후속 작업의 작업 스냅샷과 정확히 비교할 수 있다.
- 인쇄 핵심 설정이 바뀌면 값이 바뀌고 실물 검증 기록은 해제된다.

`PUT /api/printer-profile`의 `expectedRevision`은 **클라이언트가 마지막으로 본 현재 활성 프로필의 revision**이다.
다르면 409 `PROFILE_CHANGED`(응답 details에 `currentRevision` 포함). 저장 위치는
Linux `$XDG_CONFIG_HOME/YSFourcut` 또는 `~/.config/YSFourcut`, Windows `%LOCALAPPDATA%/YSFourcut`의
`printer-profile.json`이며 임시 파일 + rename으로 원자적으로 쓴다(`Platform/AppDirectories.cs`).
선택 값과 절취 방식만 저장하고 사진·인증값은 저장하지 않는다.

## 출력 작업 요청 (`POST /api/print-jobs`)

```json
{ "schemaVersion": 1,
  "clientJobId": "8f14e45f-ceea-467a-9c2b-1e1d0a4f5a1b",
  "profileId": "virtual-80mm-8dpmm",
  "profileRevision": "r1-…",
  "bitmap": { "widthDots": 576, "heightDots": 1788, "strideBytes": 72,
              "bitOrder": "msb-first", "blackBit": 1, "dataBase64": "…" } }
```

비트 해석: `msb-first`, `blackBit=1`, 위에서 아래로·왼쪽부터, 행 우측 남는 비트는 흰색 0.
`clientJobId`는 클릭 한 번에 만드는 표준 UUID(하이픈 포함)다. 클라이언트는 큐 이름·파일 경로·RAW 명령을 보낼 수 없다.

검증 순서와 오류(`Jobs/PrintJobRequestValidator.cs`):

| 순서 | 검사 | 실패 시 |
| --- | --- | --- |
| 1 | schemaVersion | 400 `SCHEMA_VERSION_UNSUPPORTED` |
| 2 | clientJobId UUID, profileId/Revision 존재 | 400 `VALIDATION_FAILED` |
| 3 | 프로필 존재 | 404 `PRINTER_NOT_FOUND` |
| 4 | revision 일치 | 409 `PROFILE_CHANGED` |
| 5 | 모드와 프로필 kind, 실물 검증 상태 | 409 `PROFILE_KIND_MISMATCH` / `PROFILE_UNVERIFIED` |
| 6 | bitOrder·blackBit·필드 존재 | 400 `INVALID_BITMAP` (`reason`: `bit_order_unsupported` 등) |
| 7 | 크기 > 서비스 상한(1024×4096dot, 512KiB) | 400 `PAGE_SIZE_UNSUPPORTED` (`limit: service`) |
| 8 | 크기 > 프로필 상한(내용 폭 등) | 400 `PAGE_SIZE_UNSUPPORTED` (`limit: profile`) |
| 9 | `strideBytes == ceil(width/8)` | 400 `INVALID_BITMAP` (`stride_mismatch`) |
| 10 | 해제 데이터 길이 상한 | 400 `PAGE_SIZE_UNSUPPORTED` (`limit: decodedBytes`) |
| 11 | base64 해석 | 400 `INVALID_BITMAP` (`base64_invalid`) |
| 12 | 길이 == stride × height | 400 `INVALID_BITMAP` (`data_length_mismatch`) |
| 13 | 행 우측 패딩 비트가 흰색 | 400 `INVALID_BITMAP` (`padding_not_white`, `row` 포함) |

전부 통과하면 **202 Accepted** + 작업 상태(`PrintJobStatusResponse`)를 돌려준다. 접수는 렌더 완료를 기다리지 않는다.

`sourceDigest`(입력 비트 내용의 SHA-256)는 검증 단계에서 계산하며 PNG 파일 해시와 혼용하지 않는다.

## 작업 접수와 상태 (05번)

- 진행 중인 작업은 **서비스 전체에서 하나**다. 다른 작업이 진행 중이면 409 `PRINTER_BUSY`.
- 같은 `clientJobId` + 같은 내용(비트맵 digest + profileId + revision)은 **기존 작업을 그대로** 돌려준다(다시 렌더링하지 않음).
  같은 ID에 다른 내용이면 409 `JOB_ID_CONFLICT`.
- 상태 전이: `accepted → rendering → rendered | virtual_failed`. terminal 상태에서 `queueBusy=false`,
  가상은 항상 `isPhysical=false`, `spoolJobId=null`, `simulation.isSimulated=true`다. 실패는 `failure.code`로 드러낸다.
- 접수 시점에 프로필·모드·외형 revision·seed를 **스냅샷**으로 고정한다. seed는 `clientJobId`에서 결정적으로 만든다.
- 렌더링은 **별도 worker 프로세스**가 수행한다(`--print-worker`). 요청/PNG는 크기가 명시된 익명 파이프 프레임으로 주고받고,
  표준 출력에 이미지·base64를 쓰지 않는다. 늦은 결과는 host instance/job ID로 거부하고, 10초 예산을 넘기면 worker를 정리한 뒤
  `RENDER_TIMEOUT`으로 실패 확정한다. 자동 재시도는 없다.
- 세 이미지를 모두 받은 뒤 한 번에 게시한다. 하나라도 없으면 완료로 바꾸지 않는다.

### 결과 이미지 (`GET .../artifacts/{kind}`)

메모리에만 보관하며 공개 파일 경로를 만들지 않는다. 응답은 `image/png` + `Cache-Control: no-store`이고
웹은 인증 fetch로 Blob을 받아 표시한다.

| 상황 | 응답 |
| --- | --- |
| 진행 중 | 409 `ARTIFACT_NOT_READY` |
| 만료·정리·호스트 재시작 | 410 `ARTIFACT_EXPIRED` |
| 없는 작업, 현재 실행의 다른 세션 작업, 만들어진 적 없는 종류(부분 실패의 paper 등) | 404 `ARTIFACT_NOT_FOUND` |

한 번이라도 게시된 종류는 만료·정리 뒤 **반복 조회에서도 계속 410**이다(종류별로 구분한다).

보관 한도(`Artifacts/InMemoryReceiptArtifactStore.cs`): 절대 TTL **10분**(조회로 연장하지 않음),
세션당 **3작업 / 64MiB**, 호스트 전체 **128MiB**, 작업당 **32MiB**. 초과 시 오래된 완료 결과부터 만료시키고
`.NET` 타이머가 30초마다 정리한다. `POST /api/session/artifacts/clear`는 진행 중이면 409 `PRINTER_BUSY`다.

### 모의 오류 주입 (06번, 가상 모드 전용)

`PUT /api/virtual-printer/fault` `{ schemaVersion, fault, delayMs?, failAfterRows? }`.
`fault: null`이면 해제한다. **다음 작업 하나에만** 적용되고 그 뒤 자동으로 풀린다.
physical 모드에서는 409 `PLATFORM_UNSUPPORTED`다. 실제 센서·장치 상태가 아니라 **모의 오류**이며,
상태 응답의 `simulation.injectedFault`와 `GET /api/virtual-printer/fault`로 항상 확인할 수 있다.

| `fault` | 동작 | 결과 |
| --- | --- | --- |
| `delay` | 렌더 **시작 전** 지연(0.5~10초). 렌더 10초 예산에서 제외 | 정상 완료 |
| `out_of_paper` / `cover_open` / `offline` | worker를 띄우지 않고 실패 | `VIRTUAL_OUT_OF_PAPER` / `VIRTUAL_COVER_OPEN` / `VIRTUAL_PRINTER_OFFLINE` |
| `fail_before_render` | 렌더 시작 전 실패 | `VIRTUAL_RENDER_FAILED`, 이미지 없음 |
| `fail_after_rows` | N행까지만 그리고 실패 | `VIRTUAL_RENDER_FAILED` + content **부분 이미지**(`available=true, complete=false`), paper/appearance 없음 |
| `render_timeout` | worker가 응답하지 않음 | 예산 초과 시 worker 정리 후 `RENDER_TIMEOUT` |

`delayMs`는 0 또는 500~10000이어야 하고, `fail_after_rows`에는 양의 `failAfterRows`가 필요하다.
어떤 실패에서도 자동 재실행·다른 어댑터 전환·새 UUID 자동 재접수를 하지 않는다.

### 실패·정리·재시작 규칙 (06번)

- 확정 종료(terminal) 전에는 잠금을 풀지 않는다. `queueBusy=true`인 동안 새 작업은 409 `PRINTER_BUSY`다.
- 진행 중 설정 변경은 **이미 접수된 작업을 바꾸지 않는다**(접수 시점 프로필 스냅샷).
- 결과 정리(`POST /api/session/artifacts/clear`)는 세션 정리 세대를 올린다. 정리 뒤에 끝난 늦은 작업은
  게시가 거부되어 **지운 이미지가 되살아나지 않는다**. 진행 중이면 409 `PRINTER_BUSY`다.
- 정리 응답의 `remainingJobs`가 0인 것을 확인했거나, health의 `hostInstanceId`가 바뀐(=캐시가 빈 새 호스트)
  경우에만 다음 사용자 세션을 시작한다. 정리에 실패하면 화면 사진부터 지우고 TTL을 보조 수단으로 쓰되,
  위 두 조건 중 하나를 확인하기 전에는 다음 사용자를 시작하지 않는다.
- 호스트를 다시 시작하면 메모리 결과는 사라진다. 기록만 남은 작업은 상태를 그대로 보여 주되
  모든 artifact가 `available=false`다. 조회는 이전 실행에서 `rendered`였던 작업이면 410 `ARTIFACT_EXPIRED`,
  실패로 끝난 작업이면 404다(기록에는 어떤 종류를 게시했는지 남기지 않는다). 자동 재렌더링은 없다.
- 작업 조회의 세션 범위: **현재 실행이 만든 작업은 소유 세션만** 볼 수 있고 다른 세션에는 404다.
  호스트를 다시 시작하면 세션 쿠키도 함께 사라져 소유자를 대조할 수 없으므로, **이전 실행의 기록**은
  UUID를 아는 로컬 세션이 상태(사진 없는 메타데이터)만 조회할 수 있다. 이미지는 어느 경우에도 제공하지 않는다.
  기록에 `hostInstanceId`를 남겨 현재 실행의 기록은 이 복원 경로로 새지 않는다.
- 가상 실패에는 종이·OS 큐 확인을 요구하지 않는다. `resolve`는 409 `RESOLVE_NOT_REQUIRED`로 거절한다.

### 작업 기록

사진 없는 기록만 `$XDG_STATE_HOME/YSFourcut/jobs/<clientJobId>.json`(Windows는 `%LOCALAPPDATA%`)에
임시 파일 + rename으로 남긴다. 담기는 값은 clientJobId, 요청 digest, profileId/revision, 모드, 상태, spool ID,
시각, 기록을 만든 `hostInstanceId`뿐이다.
**기록에 실패하면 출력을 시작하지 않는다**(`JOB_RECORD_FAILED`). 호스트를 다시 시작하면 이전 실행의 미완료 기록을
`virtual_failed` + `HOST_RESTARTED`로 정리하며 **자동으로 다시 렌더링하지 않는다**. 완료 기록은 24시간 뒤 정리하고
해결되지 않은 기록은 자동 삭제하지 않는다.

## 렌더링 (03번에서 구현, 응답 연결은 05번)

`apps/print-host/Rendering/`이 검증된 비트맵을 `content.png`/`paper.png`로 만든다.
지금은 어떤 endpoint도 이 결과를 반환하지 않는다(artifact 경로는 아직 501).

- `ReceiptLayout`: 정수 dot 배치(`PaperWidthDots`, `PaperHeightDots`, `ContentXDots/YDots`, 이송, DPI, mm).
  `paperHeightDots = leading + contentHeight + trailing`, 내용 원점은 `(sideMarginDots, leadingFeedDots)`다.
  인쇄 가능 폭보다 좁은 내용은 왼쪽 정렬하며 자동 확대/축소를 하지 않는다.
- 한도: 이미지당 8,000,000픽셀, 작업당 인코딩 합계 32MiB. 초과는 `RENDER_LIMIT_EXCEEDED`이며
  잘라내기나 자동 축소로 숨기지 않는다. 05번이 이 코드를 HTTP 응답으로 내보낸다.
- `appearance`는 04번에서 구현했다. 캔버스는 지면 + 사방 24px 여백(투명 배경, 바닥 그림자 포함)이며
  물리 mm에는 이 여백이 들어가지 않는다. 절취 외형·질감은 `seed`와 `appearanceRevision`("a1")에만 의존해
  같은 입력이면 같은 픽셀이 나온다. 추정 효과(`ink_bleed`, `banding`)는 기본 꺼짐이며
  `content`/`paper`에는 적용하지 않는다. 상세는 `docs/RECEIPT_APPEARANCE.md`.

## 05번 이후가 채울 타입

`PrintJobStatusResponse`(state, isPhysical, spoolJobId, queueBusy, sourceDigest, layout, artifacts, simulation)와
`VirtualFaultRequest`는 C#/TS 양쪽에 **선언만** 되어 있고 아직 어떤 경로도 이 값을 만들지 않는다.
상태값은 `accepted → rendering → rendered`(가상) / `submitting → submitted`(실물) / `virtual_failed`,
`failed_before_submit`, `outcome_unknown`이다.

## 확인한 테스트

`tests/print-host/`(65개, `npm run test:host`)와 `apps/web/src/api/*.test.ts`(`npm run test`)가
위 계약을 검사한다. 세션·Origin·Host·전용 헤더·크기·stride·padding·revision 거부와
로그·응답에 코드/쿠키가 없음을 포함한다.
