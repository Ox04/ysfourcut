// Worker 수명과 요청/응답 짝맞춤. 늦게 온 응답은 revision으로 걸러 버린다.

import type { DitherMethod } from '@ysfourcut/imaging';
import type { PhotoWorkerRequest, PhotoWorkerResponse } from './photoWorkerProtocol';

export class PhotoWorkerError extends Error {
  readonly code: string;

  constructor(code: string, message: string) {
    super(message);
    this.name = 'PhotoWorkerError';
    this.code = code;
  }
}

type Waiting = {
  resolve: (bits: Uint8Array[]) => void;
  reject: (error: Error) => void;
};

/** 실제 Worker를 만드는 기본 방법. 시험은 실패를 주입할 수 있게 다른 만들기를 넘긴다. */
export type PhotoWorkerFactory = () => Worker;

const createModuleWorker: PhotoWorkerFactory = () =>
  new Worker(new URL('./photoWorker.ts', import.meta.url), { type: 'module' });

/** 한 번에 한 요청만 보낸다(합성 큐가 순서를 보장한다). */
export class PhotoWorkerClient {
  private worker: Worker | null = null;
  private waiting = new Map<number, Waiting>();

  constructor(private readonly createWorker: PhotoWorkerFactory = createModuleWorker) {}

  /** 대기 중인 요청을 모두 끝낸다. 미결 promise를 남기면 합성 큐가 영구히 멈춘다. */
  private rejectAllWaiting(code: string, message: string): void {
    const pendings = [...this.waiting.values()];
    this.waiting.clear();
    pendings.forEach((pending) => pending.reject(new PhotoWorkerError(code, message)));
  }

  /** 망가진 Worker는 버린다. 다음 요청이 새 Worker로 다시 시작한다. */
  private discardWorker(): void {
    const worker = this.worker;
    this.worker = null;
    worker?.terminate();
  }

  private ensureWorker(): Worker {
    if (this.worker) return this.worker;

    const worker = this.createWorker();

    worker.onmessage = (event: MessageEvent<PhotoWorkerResponse>) => {
      const response = event.data;
      const pending = this.waiting.get(response.revision);
      // 이미 버린 요청의 응답이면 아무것도 하지 않는다.
      if (!pending) return;

      this.waiting.delete(response.revision);
      if (response.ok) {
        pending.resolve(response.bits.map((buffer) => new Uint8Array(buffer)));
      } else {
        pending.reject(new PhotoWorkerError(response.code, response.message));
      }
    };

    worker.onerror = () => {
      // 망가진 Worker를 그대로 두면 다음 요청의 응답이 오지 않아 편집 화면이 갇힌다.
      this.discardWorker();
      this.rejectAllWaiting('WORKER_FAILED', '사진 처리를 시작하지 못했어요.');
    };

    this.worker = worker;
    return worker;
  }

  dither(request: {
    revision: number;
    widthDots: number;
    heightDots: number;
    brightness: number;
    contrast: number;
    method: DitherMethod;
    photos: Uint8ClampedArray[];
  }): Promise<Uint8Array[]> {
    let worker: Worker;
    try {
      worker = this.ensureWorker();
    } catch {
      return Promise.reject(new PhotoWorkerError('WORKER_UNAVAILABLE', '이 브라우저에서 사진을 처리하지 못했어요.'));
    }

    // 전송으로 넘기면 메인 스레드의 사본이 사라진다(원본은 blob URL에 그대로 있다).
    const photos = request.photos.map((pixels) => {
      const copy = new Uint8Array(pixels.length);
      copy.set(pixels);
      return copy.buffer as ArrayBuffer;
    });

    const message: PhotoWorkerRequest = {
      revision: request.revision,
      widthDots: request.widthDots,
      heightDots: request.heightDots,
      brightness: request.brightness,
      contrast: request.contrast,
      method: request.method,
      photos,
    };

    return new Promise<Uint8Array[]>((resolve, reject) => {
      this.waiting.set(request.revision, { resolve, reject });
      worker.postMessage(message, photos);
    });
  }

  /** 이 요청의 결과를 더 기다리지 않는다. */
  forget(revision: number): void {
    this.waiting.delete(revision);
  }

  terminate(): void {
    this.discardWorker();
    this.rejectAllWaiting('WORKER_TERMINATED', '사진 처리를 중단했어요.');
  }
}
