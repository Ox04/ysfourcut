import { describe, expect, it } from 'vitest';
import { composeStripPage, type TextMask } from './compose';
import { ImagingError } from './errors';
import { packMonoPage } from './index';
import { createStripLayout, type StripLayout } from './layout';
import type { MonoPage } from './page';

function photos(layout: StripLayout, value: 0 | 1): Uint8Array[] {
  return layout.cuts.map((cut) => new Uint8Array(cut.width * cut.height).fill(value));
}

function pixelAt(page: MonoPage, x: number, y: number): number {
  return page.pixels[y * page.widthDots + x];
}

function codeOf(run: () => unknown): string {
  try {
    run();
  } catch (error) {
    return error instanceof ImagingError ? error.code : `${error}`;
  }
  return 'no-error';
}

const layout576 = createStripLayout({ contentWidthDots: 576, frame: 'basic' });

describe('composeStripPage', () => {
  it('사진 비트를 배치 좌표에 그대로 옮긴다', () => {
    const page = composeStripPage({ layout: layout576, photoBits: photos(layout576, 1) });

    expect(page.widthDots).toBe(576);
    expect(page.heightDots).toBe(1788);

    layout576.cuts.forEach((cut) => {
      expect(pixelAt(page, cut.x, cut.y)).toBe(1);
      expect(pixelAt(page, cut.x + cut.width - 1, cut.y + cut.height - 1)).toBe(1);
    });
  });

  it('흰 사진이면 사진 영역은 흰색으로 남는다(넓은 검정 배경 없음)', () => {
    const page = composeStripPage({ layout: layout576, photoBits: photos(layout576, 0) });

    const black = page.pixels.reduce((sum, bit) => sum + bit, 0);
    // 프레임 선만 검정이어야 하므로 전체의 5%를 넘지 않는다.
    expect(black / page.pixels.length).toBeLessThan(0.05);
    layout576.cuts.forEach((cut) => {
      expect(pixelAt(page, cut.x + 5, cut.y + 5)).toBe(0);
    });
  });

  it('기본·영수증 프레임이 서로 다른 선을 그린다', () => {
    const basic = composeStripPage({ layout: layout576, photoBits: photos(layout576, 0) });
    const receiptLayout = createStripLayout({ contentWidthDots: 576, frame: 'receipt' });
    const receipt = composeStripPage({ layout: receiptLayout, photoBits: photos(receiptLayout, 0) });

    // 영수증 프레임만 바깥 테두리를 그린다.
    expect(pixelAt(receipt, 0, 0)).toBe(1);
    expect(pixelAt(basic, 0, 0)).toBe(0);
    expect(Array.from(basic.pixels)).not.toEqual(Array.from(receipt.pixels));
  });

  it('사진 테두리는 사진 바로 바깥에 그린다', () => {
    const page = composeStripPage({ layout: layout576, photoBits: photos(layout576, 0) });
    const cut = layout576.cuts[0];

    expect(pixelAt(page, cut.x - 1, cut.y + 10)).toBe(1);
    expect(pixelAt(page, cut.x, cut.y + 10)).toBe(0);
  });

  it('글자 마스크는 사진 위에 검정으로 더해진다(디더링 이후 합성)', () => {
    const mask: TextMask = {
      widthDots: 10,
      heightDots: 4,
      bits: new Uint8Array(40).fill(1),
      x: layout576.footerTextRect.x,
      y: layout576.footerTextRect.y,
    };

    const page = composeStripPage({ layout: layout576, photoBits: photos(layout576, 0), textMasks: [mask] });

    expect(pixelAt(page, mask.x, mask.y)).toBe(1);
    expect(pixelAt(page, mask.x + 9, mask.y + 3)).toBe(1);
    expect(pixelAt(page, mask.x + 10, mask.y)).toBe(0);
  });

  it('글자 마스크가 사진 위에 와도 사진을 지우지 않는다(검정만 더함)', () => {
    const mask: TextMask = {
      widthDots: 4,
      heightDots: 4,
      bits: Uint8Array.from([1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]),
      x: layout576.cuts[0].x,
      y: layout576.cuts[0].y,
    };

    const page = composeStripPage({ layout: layout576, photoBits: photos(layout576, 0), textMasks: [mask] });

    expect(pixelAt(page, mask.x, mask.y)).toBe(1);
    expect(pixelAt(page, mask.x + 1, mask.y)).toBe(0);
  });

  it('네 컷이 모자라거나 크기가 다르면 설명 가능한 오류다', () => {
    expect(codeOf(() => composeStripPage({ layout: layout576, photoBits: [null, null, null, null] }))).toBe(
      'CUTS_INCOMPLETE',
    );
    expect(codeOf(() => composeStripPage({ layout: layout576, photoBits: photos(layout576, 1).slice(0, 3) }))).toBe(
      'CUTS_INCOMPLETE',
    );

    const wrong = photos(layout576, 1);
    wrong[2] = new Uint8Array(10);
    expect(codeOf(() => composeStripPage({ layout: layout576, photoBits: wrong }))).toBe('PHOTO_SIZE_MISMATCH');
  });

  it('같은 최종 페이지를 pack하면 서버 계약과 맞는 비트맵이 된다', () => {
    const page = composeStripPage({ layout: layout576, photoBits: photos(layout576, 0) });
    const bitmap = packMonoPage(page);

    expect(bitmap.widthDots).toBe(576);
    expect(bitmap.heightDots).toBe(1788);
    expect(bitmap.strideBytes).toBe(72);
    expect(bitmap.data.length).toBe(72 * 1788);
  });

  it('홀수 폭에서도 행 우측 padding 비트가 흰색으로 남는다', () => {
    const odd = createStripLayout({ contentWidthDots: 333, frame: 'receipt' });
    const page = composeStripPage({ layout: odd, photoBits: photos(odd, 1) });
    const bitmap = packMonoPage(page);

    expect(bitmap.strideBytes).toBe(Math.ceil(333 / 8));
    const usedBits = 333 % 8;
    const paddingMask = 0xff >> usedBits;
    for (let y = 0; y < bitmap.heightDots; y += 1) {
      const lastByte = bitmap.data[y * bitmap.strideBytes + bitmap.strideBytes - 1];
      expect(lastByte & paddingMask).toBe(0);
    }
  });
});
