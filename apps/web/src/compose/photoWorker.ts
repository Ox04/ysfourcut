/// <reference lib="webworker" />
// 무거운 사진 처리(명암 → 그레이스케일 → 디더링)를 담당하는 별도 Worker.
// 순수 함수는 @ysfourcut/imaging에 있고 여기서는 메시지 처리만 한다.

import {
  ImagingError,
  applyBrightnessContrast,
  ditherGray,
  rgbaToGrayscale,
} from '@ysfourcut/imaging';
import type { PhotoWorkerRequest, PhotoWorkerResponse } from './photoWorkerProtocol';

const scope = self as unknown as DedicatedWorkerGlobalScope;

scope.onmessage = (event: MessageEvent<PhotoWorkerRequest>) => {
  const request = event.data;

  try {
    const pixelCount = request.widthDots * request.heightDots;
    const bits = request.photos.map((buffer) => {
      const gray = rgbaToGrayscale(new Uint8Array(buffer), pixelCount);
      const adjusted = applyBrightnessContrast(gray, request.brightness, request.contrast);
      return ditherGray(adjusted, request.widthDots, request.heightDots, request.method).buffer as ArrayBuffer;
    });

    const response: PhotoWorkerResponse = { revision: request.revision, ok: true, bits };
    scope.postMessage(response, bits);
  } catch (error) {
    const response: PhotoWorkerResponse = {
      revision: request.revision,
      ok: false,
      code: error instanceof ImagingError ? error.code : 'COMPOSE_FAILED',
      message: error instanceof Error ? error.message : '사진을 처리하지 못했어요.',
    };
    scope.postMessage(response);
  }
};
