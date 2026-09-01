# 제3자 구성 요소 고지

이 파일은 **현재 실제로 복원해 실행한 패키지**만 적는다 (2026-08-31, 03번 작업).
버전은 `apps/print-host/packages.lock.json`으로 고정되어 있으며, 웹 의존성은 `package-lock.json`을 따른다.

## .NET 호스트 렌더링

| 패키지 | 버전 | 라이선스 | 출처 |
| --- | --- | --- | --- |
| CrossEscPos.Rendering.Skia | 1.2.0 | MIT | https://github.com/danielmeza/CrossEscPosEmulator |
| CrossEscPos.Abstractions | 1.2.0 | MIT | 같은 저장소(전이 의존성) |
| SkiaSharp | 3.119.4 | MIT (© Microsoft Corporation) | https://github.com/mono/SkiaSharp |
| SkiaSharp.NativeAssets.Linux | 3.119.4 | MIT (© Microsoft Corporation) | 위와 같음 |
| SkiaSharp.NativeAssets.Win32 | 3.119.4 | MIT (© Microsoft Corporation) | 위와 같음 |
| SkiaSharp.NativeAssets.macOS | 3.119.4 | MIT (© Microsoft Corporation) | 위와 같음 |

- CrossEscPos 두 패키지의 배포 메타데이터가 가리키는 소스 커밋은
  `b81782566a0ec5a41f95cc2a065cc45fb5a3eafb`이며, 설계 문서에 기록된 값과 같음을 nuspec에서 확인했다.
- 이 프로젝트는 위 패키지의 **이미지·캔버스·PNG 인코더만** 사용한다.
  CrossEscPos.Core(에뮬레이터, PaperConfiguration, Receipt, FeedEscPos, 파일 로거)와
  Avalonia UI·WASM·TCP/직렬/USB 모듈은 포함하지 않는다.
- Win32/macOS 네이티브 자산은 SkiaSharp 메타패키지를 통해 함께 복원된다. Linux 실행에는
  `runtimes/linux-x64/native/libSkiaSharp.so`만 사용한다.
- **13번에서 확인**: `npm run package:win`(`dotnet publish -r win-x64 --self-contained`)의
  `dist/win-x64/`에는 `libSkiaSharp.dll`(Win32 네이티브, `SkiaSharp.NativeAssets.Win32` 3.119.4)과
  관리 어셈블리 `SkiaSharp.dll`·`CrossEscPos.Rendering.Skia.dll`·`CrossEscPos.Abstractions.dll`가
  실제로 포함되고, Linux `.so`는 0건이다(WSL에서 파일 목록만 확인, Windows 실행은 NOT_RUN).
- `dotnet list package --vulnerable --include-transitive` 결과: 알려진 취약 패키지 없음(2026-08-31 기준, nuget.org 소스).

## 한글 폰트 (09번)

| 파일 | 원본 | 라이선스 | 출처 |
| --- | --- | --- | --- |
| `apps/web/src/assets/fonts/NotoSansCJKkr-Bold-hangul-subset.otf` | Noto Sans CJK KR Bold | SIL Open Font License 1.1 | https://github.com/notofonts/noto-cjk |

- 한글 음절·호환 자모·ASCII·자주 쓰는 기호만 남긴 **부분집합**이다. 다시 만드는 명령과 포함 범위는
  `apps/web/src/assets/fonts/README.md`에 있다. 라이선스 전문은 같은 폴더의 `LICENSE-OFL-1.1.txt`다.
- Reserved Font Name이 없는 OFL 판이므로 부분집합 재배포에 이름 제한이 없다. 원본 이름을 파일명에 남겨 출처를 표시했다.
- CDN에서 내려받지 않고 앱 번들에 포함해 로컬에서만 제공한다. 한글은 폰트 로딩이 끝난 뒤
  브라우저에서 래스터로 합성해 1비트 이미지에 넣는다(프린터의 한글 코드페이지를 전제로 삼지 않는다).
- **13번에서 확인**: `npm run package:win`의 `dist/win-x64/wwwroot/assets/`에 이 폰트 파일이
  실제로 복사되어 있다(인터넷 없이 로컬 자산으로 로드 가능한 구조). Windows에서 실제 로딩·렌더링은 NOT_RUN.

## 웹

React, Vite, TypeScript, ESLint, Vitest 등 개발·런타임 의존성은 `package-lock.json`에 고정되어 있다.
외부 스크립트·CDN 폰트는 사용하지 않는다.
