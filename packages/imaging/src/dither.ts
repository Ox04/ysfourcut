// 사진 영역의 1비트 변환. 목표 dot 크기의 그레이스케일을 그대로 받아 처리한다.
// 결과는 픽셀당 0(흰색)/1(검정)이며 packMonoBitmap의 입력과 같은 형식이다.

import { ImagingError } from './errors';

export const DITHER_METHODS = ['floyd-steinberg', 'ordered'] as const;

export type DitherMethod = (typeof DITHER_METHODS)[number];

/** 기본은 Floyd–Steinberg. ordered는 비교용이다. */
export const DEFAULT_DITHER_METHOD: DitherMethod = 'floyd-steinberg';

export const DITHER_LABELS_KO: Record<DitherMethod, string> = {
  'floyd-steinberg': '기본 (오차 확산)',
  ordered: '패턴 (비교용)',
};

const THRESHOLD = 128;

/** 8×8 Bayer 행렬. 값 0~63. */
const BAYER_8 = [
  0, 32, 8, 40, 2, 34, 10, 42, 48, 16, 56, 24, 50, 18, 58, 26, 12, 44, 4, 36, 14, 46, 6, 38, 60, 28,
  52, 20, 62, 30, 54, 22, 3, 35, 11, 43, 1, 33, 9, 41, 51, 19, 59, 27, 49, 17, 57, 25, 15, 47, 7, 39,
  13, 45, 5, 37, 63, 31, 55, 23, 61, 29, 53, 21,
];

function assertSize(gray: Uint8Array, width: number, height: number): void {
  if (width <= 0 || height <= 0 || gray.length !== width * height) {
    throw new ImagingError('PHOTO_SIZE_MISMATCH', {
      expected: Math.max(0, width * height),
      received: gray.length,
    });
  }
}

/**
 * Floyd–Steinberg 오차 확산. 같은 입력이면 항상 같은 결과다(난수 없음).
 * 오차는 float 누산기로 유지하고 결과만 0/1로 확정한다.
 */
export function ditherFloydSteinberg(gray: Uint8Array, width: number, height: number): Uint8Array {
  assertSize(gray, width, height);

  const buffer = Float32Array.from(gray);
  const bits = new Uint8Array(width * height);

  for (let y = 0; y < height; y += 1) {
    for (let x = 0; x < width; x += 1) {
      const index = y * width + x;
      const old = buffer[index];
      const isBlack = old < THRESHOLD;
      bits[index] = isBlack ? 1 : 0;

      const error = old - (isBlack ? 0 : 255);

      if (x + 1 < width) buffer[index + 1] += (error * 7) / 16;
      if (y + 1 < height) {
        if (x > 0) buffer[index + width - 1] += (error * 3) / 16;
        buffer[index + width] += (error * 5) / 16;
        if (x + 1 < width) buffer[index + width + 1] += error / 16;
      }
    }
  }

  return bits;
}

/** 8×8 ordered(Bayer) 디더링. 비교용 옵션이다. */
export function ditherOrdered(gray: Uint8Array, width: number, height: number): Uint8Array {
  assertSize(gray, width, height);

  const bits = new Uint8Array(width * height);

  for (let y = 0; y < height; y += 1) {
    for (let x = 0; x < width; x += 1) {
      const index = y * width + x;
      // 임계값을 0~63 격자로 흔든다(중심 128).
      const threshold = ((BAYER_8[(y % 8) * 8 + (x % 8)] + 0.5) / 64) * 255;
      bits[index] = gray[index] < threshold ? 1 : 0;
    }
  }

  return bits;
}

export function ditherGray(
  gray: Uint8Array,
  width: number,
  height: number,
  method: DitherMethod,
): Uint8Array {
  return method === 'ordered'
    ? ditherOrdered(gray, width, height)
    : ditherFloydSteinberg(gray, width, height);
}
