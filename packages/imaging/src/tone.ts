// 사진 픽셀의 명암 조정과 그레이스케일. 원본 배열을 바꾸지 않고 새 배열을 돌려준다.
// DOM·Canvas에 의존하지 않으므로 Worker와 시험에서 그대로 쓴다.

import { ImagingError } from './errors';

/** 사진 한 컷의 원본 픽셀 상한. 넘으면 설명 가능한 오류로 거부한다. */
export const MAX_SOURCE_PIXELS = 4_000_000;

function clamp255(value: number): number {
  return value < 0 ? 0 : value > 255 ? 255 : value;
}

/**
 * RGBA(8비트)를 그레이스케일로 바꾼다. 투명한 픽셀은 **흰 종이 위에 얹은 것**으로 계산한다.
 * 감열지에는 투명이 없으므로 알파를 흰색과 합성해 확정한다.
 */
export function rgbaToGrayscale(rgba: Uint8ClampedArray | Uint8Array, pixelCount: number): Uint8Array {
  if (pixelCount <= 0 || pixelCount > MAX_SOURCE_PIXELS) {
    throw new ImagingError('SOURCE_TOO_LARGE', { pixelCount, maximum: MAX_SOURCE_PIXELS });
  }
  if (rgba.length < pixelCount * 4) {
    throw new ImagingError('PHOTO_SIZE_MISMATCH', { expected: pixelCount * 4, received: rgba.length });
  }

  const gray = new Uint8Array(pixelCount);

  for (let index = 0; index < pixelCount; index += 1) {
    const offset = index * 4;
    const alpha = rgba[offset + 3] / 255;
    // 흰 배경 합성 후 Rec.601 휘도
    const r = rgba[offset] * alpha + 255 * (1 - alpha);
    const g = rgba[offset + 1] * alpha + 255 * (1 - alpha);
    const b = rgba[offset + 2] * alpha + 255 * (1 - alpha);
    gray[index] = clamp255(Math.round(0.299 * r + 0.587 * g + 0.114 * b));
  }

  return gray;
}

/**
 * 밝기·대비(-100 ~ +100)를 적용한다. 0/0이면 값이 그대로다.
 * 원본 사진에는 적용하지 않고 목표 dot 크기의 사본에만 적용한다.
 */
export function applyBrightnessContrast(
  gray: Uint8Array,
  brightness: number,
  contrast: number,
): Uint8Array {
  const b = Math.max(-100, Math.min(100, brightness)) * 1.27;
  const c = Math.max(-100, Math.min(100, contrast)) * 1.27;
  // 표준 대비 계수. c = 0이면 factor = 1이다.
  const factor = (259 * (c + 255)) / (255 * (259 - c));

  const out = new Uint8Array(gray.length);
  for (let index = 0; index < gray.length; index += 1) {
    out[index] = clamp255(Math.round(factor * (gray[index] - 128) + 128 + b));
  }

  return out;
}
