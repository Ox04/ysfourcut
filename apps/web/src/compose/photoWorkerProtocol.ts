// 합성 Worker와 주고받는 메시지. 사진 픽셀은 목표 dot 크기로 줄인 뒤에만 넘긴다.
// 원본 blob URL·촬영 정보는 Worker로 보내지 않는다.

import type { DitherMethod } from '@ysfourcut/imaging';

export type PhotoWorkerRequest = {
  revision: number;
  /** 사진 한 컷의 목표 dot 크기 */
  widthDots: number;
  heightDots: number;
  brightness: number;
  contrast: number;
  method: DitherMethod;
  /** 네 컷의 RGBA 버퍼(길이 = width × height × 4). 전송으로 넘긴다. */
  photos: ArrayBuffer[];
};

export type PhotoWorkerResponse =
  | { revision: number; ok: true; bits: ArrayBuffer[] }
  | { revision: number; ok: false; code: string; message: string };
