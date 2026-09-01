import { describe, expect, it } from 'vitest';
import { ImagingError } from './errors';
import { DEFAULT_DITHER_METHOD, ditherFloydSteinberg, ditherGray, ditherOrdered } from './dither';

function gradient(width: number, height: number): Uint8Array {
  const gray = new Uint8Array(width * height);
  for (let y = 0; y < height; y += 1) {
    for (let x = 0; x < width; x += 1) {
      gray[y * width + x] = Math.round((x / (width - 1)) * 255);
    }
  }
  return gray;
}

function blackRatio(bits: Uint8Array): number {
  return bits.reduce((sum, bit) => sum + bit, 0) / bits.length;
}

describe('기본값', () => {
  it('Floyd–Steinberg가 기본이다', () => {
    expect(DEFAULT_DITHER_METHOD).toBe('floyd-steinberg');
    const gray = gradient(32, 8);
    expect(Array.from(ditherGray(gray, 32, 8, DEFAULT_DITHER_METHOD))).toEqual(
      Array.from(ditherFloydSteinberg(gray, 32, 8)),
    );
    expect(Array.from(ditherGray(gray, 32, 8, 'ordered'))).toEqual(Array.from(ditherOrdered(gray, 32, 8)));
  });
});

describe('ditherFloydSteinberg', () => {
  it('완전한 검정과 흰색은 그대로 확정한다', () => {
    expect(blackRatio(ditherFloydSteinberg(new Uint8Array(64).fill(0), 8, 8))).toBe(1);
    expect(blackRatio(ditherFloydSteinberg(new Uint8Array(64).fill(255), 8, 8))).toBe(0);
  });

  it('중간 회색은 절반 정도가 검정이 된다', () => {
    const ratio = blackRatio(ditherFloydSteinberg(new Uint8Array(64 * 64).fill(128), 64, 64));
    expect(ratio).toBeGreaterThan(0.4);
    expect(ratio).toBeLessThan(0.6);
  });

  it('같은 입력이면 항상 같은 결과다(난수 없음)', () => {
    const gray = gradient(40, 12);
    expect(Array.from(ditherFloydSteinberg(gray, 40, 12))).toEqual(
      Array.from(ditherFloydSteinberg(gray, 40, 12)),
    );
  });

  it('입력 배열을 바꾸지 않는다', () => {
    const gray = gradient(16, 4);
    const before = Array.from(gray);
    ditherFloydSteinberg(gray, 16, 4);
    expect(Array.from(gray)).toEqual(before);
  });

  it('밝을수록 검정이 적다', () => {
    const dark = blackRatio(ditherFloydSteinberg(new Uint8Array(1024).fill(64), 32, 32));
    const light = blackRatio(ditherFloydSteinberg(new Uint8Array(1024).fill(192), 32, 32));
    expect(dark).toBeGreaterThan(light);
  });
});

describe('ditherOrdered', () => {
  it('8×8 패턴으로 중간 회색을 절반씩 나눈다', () => {
    const ratio = blackRatio(ditherOrdered(new Uint8Array(64 * 64).fill(128), 64, 64));
    expect(ratio).toBeGreaterThan(0.4);
    expect(ratio).toBeLessThan(0.6);
  });

  it('Floyd–Steinberg와 다른 결과를 만든다(비교 옵션)', () => {
    const gray = gradient(64, 16);
    expect(Array.from(ditherOrdered(gray, 64, 16))).not.toEqual(Array.from(ditherFloydSteinberg(gray, 64, 16)));
  });

  it('완전한 검정·흰색은 유지한다', () => {
    expect(blackRatio(ditherOrdered(new Uint8Array(64).fill(0), 8, 8))).toBe(1);
    expect(blackRatio(ditherOrdered(new Uint8Array(64).fill(255), 8, 8))).toBe(0);
  });
});

describe('크기 검사', () => {
  it('픽셀 수가 맞지 않으면 설명 가능한 오류다', () => {
    const run = () => ditherFloydSteinberg(new Uint8Array(10), 4, 4);
    expect(run).toThrow(ImagingError);
    try {
      run();
    } catch (error) {
      expect((error as ImagingError).code).toBe('PHOTO_SIZE_MISMATCH');
    }
  });
});
