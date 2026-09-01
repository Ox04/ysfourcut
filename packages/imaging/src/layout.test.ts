import { describe, expect, it } from 'vitest';
import { ImagingError } from './errors';
import {
  CUT_COUNT,
  assertLayoutWithinLimits,
  createStripLayout,
  normalizeCaption,
  type StripLayout,
} from './layout';

function codeOf(run: () => unknown): string {
  try {
    run();
  } catch (error) {
    return error instanceof ImagingError ? error.code : `${error}`;
  }
  return 'no-error';
}

describe('createStripLayout', () => {
  it('576dot 프로필은 설계 예시(1788dot)와 같은 배치를 만든다', () => {
    const layout = createStripLayout({ contentWidthDots: 576, frame: 'basic' });

    expect(layout.widthDots).toBe(576);
    expect(layout.heightDots).toBe(1788);
    expect(layout.photoWidthDots).toBe(528);
    expect(layout.photoHeightDots).toBe(396);
    expect(layout.gapDots).toBe(12);
    expect(layout.header.height).toBe(64);
    expect(layout.footer.height).toBe(104);
    expect(layout.cuts).toHaveLength(CUT_COUNT);
    expect(layout.cuts.map((cut) => cut.y)).toEqual([64, 472, 880, 1288]);
  });

  it.each([576, 384, 96, 200, 333])('폭 %ddot에서도 1열 4컷이 정확히 4:3이고 범위 안에 있다', (width) => {
    const layout = createStripLayout({ contentWidthDots: width, frame: 'receipt' });

    expect(layout.cuts).toHaveLength(4);
    expect(layout.photoWidthDots * 3).toBe(layout.photoHeightDots * 4);

    layout.cuts.forEach((cut, index) => {
      expect(cut.x).toBeGreaterThanOrEqual(1);
      expect(cut.x + cut.width).toBeLessThanOrEqual(layout.widthDots - 1);
      expect(cut.y).toBeGreaterThanOrEqual(layout.header.y);
      expect(cut.y + cut.height).toBeLessThanOrEqual(layout.footer.y);
      if (index > 0) {
        expect(cut.y - (layout.cuts[index - 1].y + layout.cuts[index - 1].height)).toBe(layout.gapDots);
      }
    });

    // 글자 영역은 구분선과 겹치지 않고 페이지 안에 있다.
    expect(layout.headerTextRect.height).toBeGreaterThan(0);
    expect(layout.headerTextRect.y + layout.headerTextRect.height).toBeLessThanOrEqual(layout.headerRuleY);
    expect(layout.footerTextRect.y).toBeGreaterThanOrEqual(layout.footerRuleY + layout.ruleDots);
    expect(layout.footerTextRect.y + layout.footerTextRect.height).toBe(layout.heightDots);
    expect(layout.footerTextRect.height).toBeGreaterThanOrEqual(layout.captionFontDots + layout.dateFontDots);
  });

  it('58mm 프리셋 폭(384dot)도 상한 안에 들어간다', () => {
    const layout = createStripLayout({ contentWidthDots: 384, frame: 'basic' });

    expect(layout.photoWidthDots).toBe(352);
    expect(layout.photoHeightDots).toBe(264);
    expect(() =>
      assertLayoutWithinLimits(layout, { maxHeightDots: 4096, maxDecodedBytes: 524288 }),
    ).not.toThrow();
  });

  it('배치할 수 없는 폭은 설명 가능한 오류다', () => {
    expect(codeOf(() => createStripLayout({ contentWidthDots: 40, frame: 'basic' }))).toBe(
      'CONTENT_WIDTH_UNSUPPORTED',
    );
    expect(codeOf(() => createStripLayout({ contentWidthDots: 2048, frame: 'basic' }))).toBe(
      'CONTENT_WIDTH_UNSUPPORTED',
    );
    expect(codeOf(() => createStripLayout({ contentWidthDots: 576.5, frame: 'basic' }))).toBe(
      'CONTENT_WIDTH_UNSUPPORTED',
    );
  });
});

describe('assertLayoutWithinLimits', () => {
  const layout = createStripLayout({ contentWidthDots: 576, frame: 'basic' });

  it('기본 프로필 상한은 통과한다', () => {
    expect(() =>
      assertLayoutWithinLimits(layout, { maxHeightDots: 4096, maxDecodedBytes: 524288 }),
    ).not.toThrow();
  });

  it('높이·데이터 상한을 넘으면 코드로 거부한다', () => {
    expect(codeOf(() => assertLayoutWithinLimits(layout, { maxHeightDots: 1000, maxDecodedBytes: 524288 }))).toBe(
      'PAGE_TOO_TALL',
    );
    expect(codeOf(() => assertLayoutWithinLimits(layout, { maxHeightDots: 4096, maxDecodedBytes: 1024 }))).toBe(
      'DECODED_TOO_LARGE',
    );
  });

  it('키가 큰 배치는 서비스 상한에서도 막힌다', () => {
    const tall: StripLayout = { ...layout, heightDots: 5000 };
    expect(codeOf(() => assertLayoutWithinLimits(tall, { maxHeightDots: 999999, maxDecodedBytes: 999999 }))).toBe(
      'PAGE_TOO_TALL',
    );
  });
});

describe('normalizeCaption', () => {
  it('24자까지 받는다', () => {
    const caption = '스물네 글자까지 들어가는 문구예요오';
    expect([...caption]).toHaveLength(19);
    expect(normalizeCaption(caption)).toBe(caption);
    expect(normalizeCaption('가'.repeat(24))).toHaveLength(24);
  });

  it('24자를 넘으면 거부한다', () => {
    expect(codeOf(() => normalizeCaption('가'.repeat(25)))).toBe('CAPTION_TOO_LONG');
  });

  it('줄바꿈·제어문자는 거부한다', () => {
    expect(codeOf(() => normalizeCaption('안녕\n하세요'))).toBe('CAPTION_INVALID');
    expect(codeOf(() => normalizeCaption('\u0007경고'))).toBe('CAPTION_INVALID');
  });

  it('뒤쪽 공백은 지운다', () => {
    expect(normalizeCaption('안녕   ')).toBe('안녕');
  });
});
