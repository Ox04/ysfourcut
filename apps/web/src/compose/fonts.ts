// 인쇄 원본에 글자를 그리기 전에 로컬 폰트 로딩을 끝낸다.
// 로딩이 끝나기 전에는 최종 비트맵을 확정하지 않는다(대체 글꼴로 굳어지는 것을 막는다).

/** styles/print-font.css의 @font-face 이름. */
export const PRINT_FONT_FAMILY = 'YSFourcutPrintKR';

/** 폰트가 없을 때도 글자는 그린다. 그때 쓰는 대체 스택. */
export const PRINT_FONT_FALLBACK = "'Malgun Gothic', 'Apple SD Gothic Neo', sans-serif";

export function printFontStack(): string {
  return `'${PRINT_FONT_FAMILY}', ${PRINT_FONT_FALLBACK}`;
}

export type PrintFontState = 'loaded' | 'fallback';

let pending: Promise<PrintFontState> | null = null;

/**
 * 로컬 폰트를 실제로 내려받는다. 성공하면 'loaded', 실패하면 'fallback'이다.
 * 결과와 무관하게 **끝난 뒤에** 합성을 시작하므로 미리보기와 인쇄본이 달라지지 않는다.
 */
export function ensurePrintFont(): Promise<PrintFontState> {
  if (pending) return pending;

  pending = loadPrintFont().catch(() => 'fallback' as const);
  return pending;
}

async function loadPrintFont(): Promise<PrintFontState> {
  const fonts = typeof document !== 'undefined' ? document.fonts : undefined;
  if (!fonts) return 'fallback';

  // 한글·숫자·영문을 모두 요구해 필요한 자원이 준비되었는지 확인한다.
  const faces = await fonts.load(`700 32px '${PRINT_FONT_FAMILY}'`, '네컷 2026-01-01 YS');
  await fonts.ready;

  return faces.length > 0 && fonts.check(`700 32px '${PRINT_FONT_FAMILY}'`) ? 'loaded' : 'fallback';
}

/** 시험·재시도용. 다음 호출에서 다시 로딩을 시도한다. */
export function resetPrintFontForTesting(): void {
  pending = null;
}
