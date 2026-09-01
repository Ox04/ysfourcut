import { describe, expect, it } from 'vitest';
import { ImagingError } from './errors';
import { applyBrightnessContrast, rgbaToGrayscale } from './tone';

function rgba(pixels: readonly (readonly [number, number, number, number])[]): Uint8Array {
  return Uint8Array.from(pixels.flat());
}

describe('rgbaToGrayscale', () => {
  it('투명한 픽셀은 흰 종이로 확정한다', () => {
    const gray = rgbaToGrayscale(
      rgba([
        [0, 0, 0, 0], // 완전 투명한 검정 → 흰색
        [0, 0, 0, 255], // 불투명 검정
        [255, 255, 255, 255],
      ]),
      3,
    );

    expect(Array.from(gray)).toEqual([255, 0, 255]);
  });

  it('반투명은 흰 배경과 섞는다', () => {
    const gray = rgbaToGrayscale(rgba([[0, 0, 0, 128]]), 1);
    expect(gray[0]).toBeGreaterThan(120);
    expect(gray[0]).toBeLessThan(135);
  });

  it('Rec.601 휘도를 쓴다(초록이 가장 밝다)', () => {
    const gray = rgbaToGrayscale(
      rgba([
        [255, 0, 0, 255],
        [0, 255, 0, 255],
        [0, 0, 255, 255],
      ]),
      3,
    );

    expect(gray[1]).toBeGreaterThan(gray[0]);
    expect(gray[0]).toBeGreaterThan(gray[2]);
  });

  it('픽셀 수가 모자라면 거부한다', () => {
    try {
      rgbaToGrayscale(new Uint8Array(4), 10);
      throw new Error('오류가 나야 한다');
    } catch (error) {
      expect((error as ImagingError).code).toBe('PHOTO_SIZE_MISMATCH');
    }
  });

  it('원본 픽셀 상한을 넘으면 거부한다', () => {
    try {
      rgbaToGrayscale(new Uint8Array(4), 5_000_000);
      throw new Error('오류가 나야 한다');
    } catch (error) {
      expect((error as ImagingError).code).toBe('SOURCE_TOO_LARGE');
    }
  });
});

describe('applyBrightnessContrast', () => {
  const gray = Uint8Array.from([0, 64, 128, 192, 255]);

  it('0/0이면 값이 그대로다', () => {
    expect(Array.from(applyBrightnessContrast(gray, 0, 0))).toEqual(Array.from(gray));
  });

  it('원본 배열을 바꾸지 않는다', () => {
    applyBrightnessContrast(gray, 50, 50);
    expect(Array.from(gray)).toEqual([0, 64, 128, 192, 255]);
  });

  it('밝기를 올리면 모든 값이 커지거나 같다', () => {
    const brighter = applyBrightnessContrast(gray, 40, 0);
    gray.forEach((value, index) => expect(brighter[index]).toBeGreaterThanOrEqual(value));
    expect(brighter[1]).toBeGreaterThan(gray[1]);
  });

  it('대비를 올리면 중간값은 유지되고 양끝이 벌어진다', () => {
    const harder = applyBrightnessContrast(gray, 0, 60);
    expect(harder[2]).toBe(128);
    expect(harder[1]).toBeLessThan(gray[1]);
    expect(harder[3]).toBeGreaterThan(gray[3]);
  });

  it('0~255 밖으로 나가지 않는다', () => {
    const extreme = applyBrightnessContrast(gray, 100, 100);
    extreme.forEach((value) => {
      expect(value).toBeGreaterThanOrEqual(0);
      expect(value).toBeLessThanOrEqual(255);
    });
  });
});
