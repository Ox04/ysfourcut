import { describe, expect, it } from 'vitest';
import { composeStripPage, createStripLayout, packMonoPage } from '@ysfourcut/imaging';
import { toPrintJobBitmap } from './printJobBitmap';

describe('toPrintJobBitmap', () => {
  it('최종 페이지의 pack 결과를 그대로 서버 계약으로 옮긴다', () => {
    const layout = createStripLayout({ contentWidthDots: 576, frame: 'basic' });
    const page = composeStripPage({
      layout,
      photoBits: layout.cuts.map((cut) => new Uint8Array(cut.width * cut.height).fill(1)),
    });
    const bitmap = packMonoPage(page);

    const payload = toPrintJobBitmap(bitmap);

    expect(payload.widthDots).toBe(576);
    expect(payload.heightDots).toBe(1788);
    expect(payload.strideBytes).toBe(72);
    expect(payload.bitOrder).toBe('msb-first');
    expect(payload.blackBit).toBe(1);

    const decoded = Uint8Array.from(Buffer.from(payload.dataBase64, 'base64'));
    expect(decoded.length).toBe(72 * 1788);
    expect(Array.from(decoded)).toEqual(Array.from(bitmap.data));
  });
});
