import { describe, expect, it } from 'vitest';
import { packMonoBitmap, strideBytesFor, toBase64 } from './index';

describe('packMonoBitmap', () => {
  it('x=0은 0x80, x=7은 0x01에 들어간다', () => {
    const pixels = new Uint8Array(16);
    pixels[0] = 1; // (0,0)
    pixels[7] = 1; // (7,0)

    const bitmap = packMonoBitmap(pixels, 16, 1);

    expect(bitmap.strideBytes).toBe(2);
    expect(bitmap.data[0]).toBe(0b1000_0001);
    expect(bitmap.data[1]).toBe(0);
  });

  it('행 우측 남는 비트는 0으로 남는다', () => {
    const pixels = new Uint8Array(9 * 2).fill(1);

    const bitmap = packMonoBitmap(pixels, 9, 2);

    expect(bitmap.strideBytes).toBe(2);
    expect(bitmap.data[0]).toBe(0xff);
    expect(bitmap.data[1]).toBe(0b1000_0000); // 폭 9 → 다음 7비트는 흰색
    expect(bitmap.data[3]).toBe(0b1000_0000);
  });

  it('픽셀 수가 맞지 않으면 거부한다', () => {
    expect(() => packMonoBitmap(new Uint8Array(10), 4, 4)).toThrow(RangeError);
    expect(() => packMonoBitmap(new Uint8Array(0), 0, 4)).toThrow(RangeError);
  });
});

describe('strideBytesFor', () => {
  it('ceil(widthDots / 8)', () => {
    expect(strideBytesFor(576)).toBe(72);
    expect(strideBytesFor(9)).toBe(2);
    expect(strideBytesFor(8)).toBe(1);
  });
});

describe('toBase64', () => {
  it('바이트를 그대로 인코딩한다', () => {
    expect(toBase64(new Uint8Array([0, 255, 16]))).toBe('AP8Q');
  });
});
