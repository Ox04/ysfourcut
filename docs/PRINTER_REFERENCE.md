# AHAPOS 프린터 확인 기록

갱신일: 2026-09-07(90번). 이 파일은 확인 기록이며, 프린터 호환성 인증이나 구현 완료 보고서가 아니다.
2026-08-31 최초 작성분(가상 출력 설계 단계)의 기록은 4절 "확인 이력"에 보존한다.

## 1. 실물 장치 확인값 (2026-09-07, 90번)

출처: 사용자가 촬영한 **본체 하단 라벨 사진**과 **프린터 셀프테스트 출력물 사진** 2장.
사용자 실측 용지 폭 79mm(80mm 감열지 규격 79.5±0.5mm에 해당).

### 1.1 본체 라벨

| 항목 | 값 |
| --- | --- |
| 브랜드 / 종류 | AHAPOS THERMAL RECEIPT PRINTER |
| **모델** | **CPP-3000** |
| 인터페이스 | RS232 + USB + RJ45 + DK(금전함) |
| 용지 폭 | 80mm |
| 인쇄 속도 | 300mm/s |
| **명령 지원** | **ESC/POS** (라벨 표기) |
| 전원 | 24V 2.5A (금전함 24V 1A) |
| 인증번호 | R-R-nbs-CPP-3000 |
| 제조자 | Zhuhai Howbest Label Printer Co., Ltd |
| 수입자 | 주식회사 시원아이티 |
| 제조연월 | 2024-11 |

수입자가 시원아이티인 점은 2026-08-31에 확인했던 시원아이티 자료실의 `AHAPOS Printer Driver` 기록과 일치한다.
다만 그 드라이버 파일은 여전히 다운로드·서명·안전성을 검증하지 않았고, 이번 결정 경로에서는 필요하지 않다(3절).

### 1.2 셀프테스트 출력

| 항목 | 값 |
| --- | --- |
| 펌웨어 Version | 2.00QL (Modify date 2024/09/13-1) |
| Speed | 300mm(MAX)/s |
| Interface | USB & RJ45 & Serial 9600,n,8,1 |
| **Printing width** | **72mm** |
| **Char line FontA/B** | **48 / 64** |
| Cutter | Yes (Cutter with Alarm: Yes, Cutter Phase: N) |
| Beeper | Yes |
| **USB Port mode** | **Virtual COM** |
| USB VID / PID | 1FC9 / 2016 |
| Density Level | 100% |
| Image NV Download | Yes |
| Black mark mode | No |
| Queuing function | No |
| Barcode 2D | QRCODE, PDF417, DATAMATRIX |
| Resident Character | Alphanumeric, Korean KSC5601 |
| Default code page | page0 / Default ASCII FONT: FONTA |

셀프테스트에는 **RJ45의 IP 설정이 출력되지 않았다.** LAN 경로를 쓰려면 별도 확인이 필요하다(5절).

### 1.3 확인값에서 계산되는 인쇄 기하

세 값이 서로 독립적으로 같은 결과를 가리킨다.

```
인쇄 폭 72mm × 8 dot/mm = 576 dot
FontA 48자 × 12 dot     = 576 dot
FontB 64자 ×  9 dot     = 576 dot
용지 80mm × 8 dot/mm    = 640 dot
```

→ **가로 해상도 8 dot/mm (203.2 DPI), 인쇄 가능 폭 576 dot, 용지 폭 640 dot.**

기존 가상 프리셋 `virtual-80mm-8dpmm.json`의 `paperWidthDots 640 / contentWidthDots 576 /
sideMarginDots 32 / dotsPerMmX 8`과 **일치한다.** 가상 프리셋은 일반적인 예시값으로 정한 것이었으나
결과적으로 이 모델의 실제 가로 기하와 같다. 다만 아래 항목은 여전히 미확인이다.

- **세로 해상도(dotsPerMmY)**: 셀프테스트에 표기가 없다. 8 dot/mm로 가정하되 정사각 패턴 실측으로 확인해야 한다.
- **leadingFeedDots 24 / trailingFeedDots 64**: 가상 프리셋의 서비스 기본값이며 장치 사양이 아니다.
  커터 날은 인쇄 헤드보다 위쪽에 있어 마지막 내용이 잘리지 않으려면 실측한 이송량이 필요하다.

## 2. 기존 코드 확인 (2026-09-07, AC-90-02)

- 저장소: `msilot1001/astraea`, 커밋 `2a98e42bb1e0f68c704d72632a3ed2e9d7a384e4`, 파일 `app/admin/otp/page.tsx`
- 2026-08-31에 기록된 404는 **저장소가 private이기 때문**이었다. 이번에 소유자 계정(`msilot1001`)으로
  인증된 GitHub API를 통해 정상적으로 읽었다. 접근 우회나 인증정보 수집은 하지 않았다.
- **OTP·인증·관리자 업무 로직과 비밀값은 이 문서와 저장소에 복사하지 않았다.** 아래는 인쇄 경로 구조만이다.

### 2.1 확인된 인쇄 경로

| 항목 | 값 |
| --- | --- |
| 라이브러리 | `react-thermal-printer` (`Printer`, `Text`, `Br`, `Line`, `QRCode`, `Cut`, `render`) |
| 프린터 설정 | `<Printer type="epson" width={42} characterSet="korea">` |
| 인코딩 | `render(receipt)` → ESC/POS 바이트 |
| **전송** | **Web Serial** — `navigator.serial.requestPort()` → `open({ baudRate: 9600 })` → `writer.write(data)` |
| 커팅 | `<Cut />` (ESC/POS 커팅 명령) |
| WebUSB | `WebUSBReceiptPrinter` 사용 시도가 **주석 처리**되어 남아 있다 — 채택되지 않았다 |
| 별도 경로 | `react-to-print`의 `useReactToPrint`가 있으나 이는 브라우저 인쇄 대화상자 경로이며 감열 프린터 직접 전송과 무관하다 |

### 2.2 텍스트/이미지 구분 (AC-90-02 핵심)

`<Printer>` 블록에 사용된 요소는 `Text` 8회, `Br` 3회, `QRCode` 1회, `Line` 1회, `Cut` 1회다.
**`Image` 요소가 없다.**

> **기존 코드는 텍스트·괘선·QR 코드만 출력했다. 래스터 비트맵 이미지를 보낸 적이 없다.**

이 프로젝트(YS Fourcut)는 사진 네컷을 1비트 비트맵으로 인쇄하는 것이 전부이므로,
**기존 코드는 이미지 경로에 대한 근거를 제공하지 않는다.** 91번의 최대 미확인 항목이다(5절).

`type="epson"`은 라이브러리가 Epson 방언 ESC/POS를 생성했다는 뜻이고, `baudRate: 9600`은
셀프테스트의 `Serial 9600,n,8,1`과 일치한다. `width={42}`는 라이브러리의 텍스트 열 수 설정이며
장치의 FontA 48열과 다르지만, 이는 텍스트 배치 값이지 하드웨어 능력이 아니다.

## 3. 91번에서 구현할 어댑터 결정 (AC-90-03)

### 결정: `SerialEscPosPrinter` 하나만 구현한다

`SERVICE_DESIGN.md` 7절의 표에서 **"기존 코드에서 실제 COM 포트·속도·명령이 확인됨 → `SerialEscPosPrinter`"**
행을 선택한다. 전송은 **USB Virtual COM**이다.

### GDI 후보를 채택하지 않는 근거

`docs/PROJECT_RULES.md`와 `SERVICE_DESIGN.md` 6절은 **Windows 등록 큐의 GDI 비트맵 출력**을 우선 후보로
적어 두었다. 같은 규칙의 "기존 코드나 실물 검증이 다른 방식을 요구할 때만 어댑터를 교체한다"에 따라
아래 근거로 교체한다.

1. **USB가 프린터 클래스가 아니다.** 셀프테스트의 `USB Port mode: Virtual COM`이며 VID/PID는 1FC9/2016이다.
   USB로 연결하면 CDC 시리얼 포트로 잡히므로 GDI 큐가 자연스럽게 생기는 구성이 아니다.
   2026-08-31에 "Windows 프린터 목록에서 AHAPOS 큐를 찾지 못했다"고 기록된 것도 이것으로 설명된다.
2. **장치가 ESC/POS를 공식 지원한다.** 본체 라벨에 `Command Support: ESC/POS`가 표기되어 있다.
3. **기존 코드가 같은 경로를 썼다.** 2절대로 Web Serial + ESC/POS + 9600이다. 전송 계층만 브라우저에서
   .NET으로 옮기면 되고, 명령 방언(Epson)과 속도가 이미 확인되어 있다.
4. **GDI는 이 제품의 핵심 요구와 충돌한다.** 규칙상 최종 1비트 dot 배치를 보존해야 하는데,
   `SERVICE_DESIGN.md` 6절도 "드라이버가 추가로 래스터를 변환할 수 있어 GDI 경로는 프린터가 받는 최종
   바이트와 브라우저 비트맵의 동일성을 보장하지 않는다"고 적고 있다. 직접 전송은 이 문제가 없다.

WebUSB는 채택하지 않는다. Windows에서 CDC 장치는 `usbser.sys`가 점유하므로 WebUSB가 인터페이스를
claim할 수 없고, 드라이버 교체는 규칙상 하지 않는다. 기존 코드에서도 주석 처리된 채 버려져 있다.
브라우저 직접 전송(Web Serial 포함)은 `POST /api/print-jobs` 접수 경계를 깨므로 사용하지 않는다.
LAN(RJ45) 경로는 지금 구현하지 않는다 — 어댑터는 하나만 만든다(5절에 전환 조건만 기록).

### 91번에서 검수할 프로필 항목

`kind: "hardware"` 프로필 하나를 새로 만든다. 가상 프리셋에서 확정적으로 가져올 값과 실측으로 정할 값을 구분한다.

| 항목 | 값 | 근거 |
| --- | --- | --- |
| `paperWidthDots` | 640 | 80mm × 8dot/mm (라벨 + 셀프테스트) |
| `contentWidthDots` | 576 | 인쇄 폭 72mm × 8, FontA 48×12, FontB 64×9 전부 일치 |
| `sideMarginDots` | 32 | (640 − 576) / 2 |
| `dotsPerMmX` | 8 | 위와 동일 |
| `dotsPerMmY` | **실측 필요** | 셀프테스트 미표기. 정사각 패턴으로 확인 |
| `leadingFeedDots` | **실측 필요** | 가상 기본값 24는 장치 사양이 아님 |
| `trailingFeedDots` | **실측 필요** | 커터 날–헤드 거리만큼 필요. 가상 기본값 64는 근거 없음 |
| `cutStyle` | `straight` 후보 | 커터 있음(Cutter: Yes). 커팅 명령 종류 확인 후 확정 |

`ProfileVerification`은 **실제 종이 결과를 본 뒤에만** 켠다. 이번 90번에서는 켜지 않는다.

## 4. 확인 이력 (2026-08-31 최초 기록 보존)

아래는 가상 출력 설계 단계의 기록이다. 위 1~3절이 이를 갱신하지만 삭제하지 않는다.

- 사용자가 보유한 프린터의 브랜드는 **AHAPOS**라고 알려 주었다. (→ 2026-09-07 모델 CPP-3000까지 확인됨)
- 사용자는 당시 영수증 프린터가 준비되지 않았으며, 동일한 웹 → .NET 호출로 현실적인 영수증 이미지를 만드는 가상 출력 프로그램부터 설계해 달라고 요청했다.
- 개발 환경은 사용자 확인 **WSL**이다. 배포판은 Fedora Linux 42/x64.
- 같은 프린터를 사용했던 코드로 [astraea의 page.tsx](https://github.com/msilot1001/astraea/blob/2a98e42bb1e0f68c704d72632a3ed2e9d7a384e4/app/admin/otp/page.tsx)를 제공했다.
- **위 URL은 2026-08-31 확인 시 정상 URL과 raw URL 모두 404였다.** (→ 2026-09-07 원인 확인: private 저장소. 인증 경로로 읽음. 2절)
- 당시 개발 PC의 Windows 등록 프린터 목록에서 AHAPOS로 식별되는 큐를 찾지 못했다. (→ USB가 Virtual COM이므로 프린터 큐가 없는 것이 정상이다)
- 시원아이티 자료실에서 `AHAPOS Printer Driver` / `AHAPOSPrinterDriver.exe` 자료를 확인했다. 상세 페이지 열기는 실패했고 파일을 다운로드/실행하지 않았다. [자료실 항목](https://m.c1it.co.kr/myboard/st_myboard/229923)
- 가상 80/58mm 프리셋은 일반적인 예시 설정으로 정했다. (→ 80mm 프리셋의 **가로 기하**는 실물과 일치함이 확인됨. 58mm 프리셋은 이 장치와 무관하다)

## 5. 아직 확인하지 못한 항목

91번 시작 전 또는 91번 검수 중에 채운다. **이 중 어느 것도 추측으로 채우지 않는다.**

| 항목 | 상태 | 확인 방법 |
| --- | --- | --- |
| **래스터 이미지 명령** | **미확인 — 최대 리스크** | `GS v 0`을 보편 명령으로 가정하지 않는다(`PROJECT_RULES.md`). 작은 패턴을 실제로 보내 확인 |
| 이미지 전송 분할 크기 | 미확인 | 긴 네컷 스트립을 한 번에 보낼 수 있는지, 몇 줄씩 나눠야 하는지 실측 |
| Windows COM 포트 번호 | 미확인 | USB 연결 후 장치 관리자 또는 `[System.IO.Ports.SerialPort]::GetPortNames()` |
| Virtual COM의 실제 baud 동작 | 미확인 | CDC는 baud를 무시하는 경우가 많다. 기존 코드 기준 9600으로 시작 |
| 세로 해상도(dotsPerMmY) | 미확인 | 정사각 테스트 패턴 실측 |
| 이송·커팅 실동작과 필요한 trailing feed | 미확인 | 커터 날–헤드 거리. 마지막 줄이 잘리는지 실측 |
| 커팅 명령 종류 | 미확인 | 부분/전체 커팅 중 이 모델이 받는 것 |
| 사진 농도·디더링 실물 품질 | 미확인 | Density Level 100% 상태에서 네컷 실물 검수 |
| 연속 출력 안정성·발열 | 미확인 | 네컷 3회 이상 연속 |
| RJ45 IP 설정과 raw TCP 9100 | 미확인 | 이번에는 USB로 진행하므로 필요 없음. LAN 전환 시에만 확인 |
| 운영 PC의 OS/브라우저 | 미확인 | Windows 11 + Chrome/Edge를 설계 기본 가정으로 유지 |

### LAN으로 전환하고 싶어질 때

ESC/POS 바이트 생성은 전송 방식과 무관하므로 어댑터의 transport 계층만 교체하면 된다.
전환하려면 (a) 프린터 IP 확인, (b) `nc -zv <IP> 9100`으로 포트 확인, (c) 같은 ESC/POS 바이트로 실물 확인이 필요하다.
**전환 전까지 LAN 경로를 구현하지 않는다.** 두 어댑터를 동시에 유지하지 않는다.

## 6. 유지되는 원칙

- 브랜드·모델·큐 등록 정보만으로 검증 프로필을 활성화하지 않는다. 프로필 검증은 작은 패턴과 네컷 샘플의
  **실제 종이 결과**를 확인한 뒤에만 기록한다.
- 실물 검수 실패 후 다른 어댑터로 자동 전환하지 않는다(`AGENTS.md`).
- 가상 모드는 Linux/Windows 모두에서 계속 동작해야 하며 실물 큐/포트를 건드리지 않는다.
- 브라우저가 프린터로 직접 전송하지 않는다. `POST /api/print-jobs`가 공통 접수 경계다.
