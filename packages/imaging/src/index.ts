// 브라우저에서 쓰는 순수 TS 이미지 함수. DOM·Canvas·네트워크에 의존하지 않는다.
// 무거운 사진 처리는 이 함수들을 Worker에서 호출한다.

import type { MonoPage } from './page';

export const IMAGING_PACKAGE_NAME = '@ysfourcut/imaging';

export * from './compose';
export * from './dither';
export * from './errors';
export * from './layout';
export * from './page';
export * from './tone';

/** 서버와 같은 계약: stride = ceil(widthDots / 8). */
export function strideBytesFor(widthDots: number): number {
  return Math.ceil(widthDots / 8);
}

export type MonoBitmap = {
  widthDots: number;
  heightDots: number;
  strideBytes: number;
  /** msb-first, 1 = 검정, 위에서 아래로. 행 우측 남는 비트는 흰색 0. */
  data: Uint8Array;
};

/**
 * 0/1 픽셀(행 우선)을 1비트 비트맵으로 묶는다.
 * x=0이 최상위 비트(0x80), x=7이 최하위 비트(0x01)다.
 */
export function packMonoBitmap(pixels: Uint8Array, widthDots: number, heightDots: number): MonoBitmap {
  if (widthDots <= 0 || heightDots <= 0) {
    throw new RangeError('widthDots와 heightDots는 양수여야 합니다.');
  }
  if (pixels.length !== widthDots * heightDots) {
    throw new RangeError(`픽셀 수가 ${widthDots * heightDots}개가 아닙니다: ${pixels.length}`);
  }

  const strideBytes = strideBytesFor(widthDots);
  const data = new Uint8Array(strideBytes * heightDots);

  for (let y = 0; y < heightDots; y += 1) {
    const rowStart = y * strideBytes;
    const sourceStart = y * widthDots;

    for (let x = 0; x < widthDots; x += 1) {
      if (pixels[sourceStart + x]) {
        data[rowStart + (x >> 3)] |= 0x80 >> (x & 7);
      }
    }
  }

  return { widthDots, heightDots, strideBytes, data };
}

/** 브라우저 표준 API만 사용하는 base64 인코딩. */
export function toBase64(data: Uint8Array): string {
  let binary = '';
  const chunkSize = 0x8000;

  for (let offset = 0; offset < data.length; offset += chunkSize) {
    binary += String.fromCharCode(...data.subarray(offset, offset + chunkSize));
  }

  return btoa(binary);
}

/** 최종 페이지를 서버 입력 형식으로 묶는다. 미리보기·PNG·서버 전달이 모두 이 결과를 쓴다. */
export function packMonoPage(page: MonoPage): MonoBitmap {
  return packMonoBitmap(page.pixels, page.widthDots, page.heightDots);
}
