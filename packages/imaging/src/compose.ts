// 최종 1비트 페이지 합성. 순서는 흰 종이 → 사진(디더링 결과) → 프레임 → 글자다.
// 글자는 사진 디더링 뒤에 얹어 선명하게 남는다. 이 모듈은 DOM에 의존하지 않는다.

import { ImagingError } from './errors';
import { CUT_COUNT, type StripLayout } from './layout';
import { blitBits, blitMask, createMonoPage, dashedHLine, fillRect, strokeRect, type MonoPage } from './page';

/** 브라우저가 그린 글자 마스크. 1 = 검정. */
export type TextMask = {
  widthDots: number;
  heightDots: number;
  bits: Uint8Array;
  /** 페이지 좌표 */
  x: number;
  y: number;
};

/** 프레임(테두리·구분선)만 그린다. 사진 위에 얹으므로 사진 다음에 호출한다. */
export function drawStripFrame(page: MonoPage, layout: StripLayout): void {
  const t = layout.ruleDots;
  const dash = Math.max(2, t * 3);

  if (layout.frame === 'receipt') {
    // 영수증 프레임: 바깥 테두리 + 절취선 모티프. 넓은 검정 면은 만들지 않는다.
    strokeRect(page, 0, 0, layout.widthDots, layout.heightDots, t);
    dashedHLine(page, layout.header.x, layout.headerRuleY, layout.header.width, t, dash, dash);
    dashedHLine(page, layout.footer.x, layout.footerRuleY, layout.footer.width, t, dash, dash);
  } else {
    // 기본 프레임: 얇은 구분선만 둔다.
    fillRect(page, layout.header.x, layout.headerRuleY, layout.header.width, t);
    fillRect(page, layout.footer.x, layout.footerRuleY, layout.footer.width, t);
  }

  for (const cut of layout.cuts) {
    strokeRect(page, cut.x - t, cut.y - t, cut.width + t * 2, cut.height + t * 2, t);
  }
}

/**
 * 네컷 페이지를 만든다.
 * `photoBits[i]`는 `layout.cuts[i]` 크기의 0/1 배열이어야 한다(이미 목표 dot 크기에서 디더링된 결과).
 */
export function composeStripPage(input: {
  layout: StripLayout;
  photoBits: readonly (Uint8Array | null)[];
  textMasks?: readonly TextMask[];
}): MonoPage {
  const { layout, photoBits, textMasks = [] } = input;

  if (photoBits.length !== CUT_COUNT || photoBits.some((bits) => bits === null)) {
    throw new ImagingError('CUTS_INCOMPLETE', { received: photoBits.filter(Boolean).length });
  }

  const page = createMonoPage(layout.widthDots, layout.heightDots);

  layout.cuts.forEach((cut, index) => {
    const bits = photoBits[index] as Uint8Array;
    if (bits.length !== cut.width * cut.height) {
      throw new ImagingError('PHOTO_SIZE_MISMATCH', {
        cut: index + 1,
        expected: cut.width * cut.height,
        received: bits.length,
      });
    }
    blitBits(page, bits, cut.width, cut.height, cut.x, cut.y);
  });

  drawStripFrame(page, layout);

  for (const mask of textMasks) {
    if (mask.bits.length !== mask.widthDots * mask.heightDots) {
      throw new ImagingError('PHOTO_SIZE_MISMATCH', {
        expected: mask.widthDots * mask.heightDots,
        received: mask.bits.length,
      });
    }
    blitMask(page, mask.bits, mask.widthDots, mask.heightDots, mask.x, mask.y);
  }

  return page;
}
