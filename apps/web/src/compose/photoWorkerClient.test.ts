import { describe, expect, it } from 'vitest';
import { PhotoWorkerClient, PhotoWorkerError, type PhotoWorkerFactory } from './photoWorkerClient';
import type { PhotoWorkerResponse } from './photoWorkerProtocol';

// Worker 실패 뒤에도 합성 큐가 멈추지 않아야 한다. 실제 Worker 대신 실패를 주입할 수 있는
// 가짜 Worker로 확인한다(DOM·번들러에 의존하지 않는 순수 시험).

class FakeWorker {
  static instances: FakeWorker[] = [];
  onmessage: ((event: MessageEvent<PhotoWorkerResponse>) => void) | null = null;
  onerror: ((event: unknown) => void) | null = null;
  posted: { revision: number }[] = [];
  terminated = false;

  constructor() {
    FakeWorker.instances.push(this);
  }

  postMessage(message: { revision: number }): void {
    this.posted.push(message);
  }

  terminate(): void {
    this.terminated = true;
  }

  /** Worker가 정상 결과를 돌려주는 상황 */
  respond(revision: number, byteLength = 4): void {
    const response: PhotoWorkerResponse = {
      revision,
      ok: true,
      bits: [new Uint8Array(byteLength).buffer],
    };
    this.onmessage?.({ data: response } as MessageEvent<PhotoWorkerResponse>);
  }

  /** 모듈 로드 실패처럼 Worker 자체가 죽는 상황 */
  fail(): void {
    this.onerror?.(new Error('worker load failed'));
  }
}

function makeRequest(revision: number) {
  return {
    revision,
    widthDots: 2,
    heightDots: 2,
    brightness: 0,
    contrast: 0,
    method: 'floyd-steinberg' as const,
    photos: [new Uint8ClampedArray(16)],
  };
}

function factory(): { create: PhotoWorkerFactory; count: () => number } {
  let calls = 0;
  return {
    create: () => {
      calls += 1;
      return new FakeWorker() as unknown as Worker;
    },
    count: () => calls,
  };
}

describe('PhotoWorkerClient 복구', () => {
  it('Worker가 죽으면 대기 요청을 오류로 끝내고 다음 요청은 새 Worker로 복구한다', async () => {
    FakeWorker.instances = [];
    const { create, count } = factory();
    const client = new PhotoWorkerClient(create);

    const first = client.dither(makeRequest(1));
    expect(count()).toBe(1);

    FakeWorker.instances[0].fail();

    await expect(first).rejects.toThrow(PhotoWorkerError);
    await first.catch((error: PhotoWorkerError) => expect(error.code).toBe('WORKER_FAILED'));
    expect(FakeWorker.instances[0].terminated).toBe(true);

    // 다음 요청은 망가진 Worker를 재사용하지 않는다(무한 대기 방지).
    const second = client.dither(makeRequest(2));
    expect(count()).toBe(2);
    expect(FakeWorker.instances[1].posted.map((message) => message.revision)).toEqual([2]);

    FakeWorker.instances[1].respond(2, 8);
    const bits = await second;
    expect(bits).toHaveLength(1);
    expect(bits[0].length).toBe(8);
  });

  it('terminate는 대기 중인 요청을 미결로 남기지 않는다', async () => {
    FakeWorker.instances = [];
    const client = new PhotoWorkerClient(factory().create);

    const pending = client.dither(makeRequest(1));
    client.terminate();

    await expect(pending).rejects.toMatchObject({ code: 'WORKER_TERMINATED' });
    expect(FakeWorker.instances[0].terminated).toBe(true);
  });

  it('Worker를 만들지 못하면 명시적 오류로 끝나고 다음 요청이 다시 시도한다', async () => {
    FakeWorker.instances = [];
    let attempts = 0;
    const client = new PhotoWorkerClient(() => {
      attempts += 1;
      if (attempts === 1) throw new Error('Worker 생성 실패');
      return new FakeWorker() as unknown as Worker;
    });

    await expect(client.dither(makeRequest(1))).rejects.toMatchObject({ code: 'WORKER_UNAVAILABLE' });

    const retry = client.dither(makeRequest(2));
    expect(attempts).toBe(2);
    FakeWorker.instances[0].respond(2);
    await expect(retry).resolves.toHaveLength(1);
  });

  it('버린 요청의 응답이 뒤늦게 와도 아무 일도 하지 않는다', async () => {
    FakeWorker.instances = [];
    const client = new PhotoWorkerClient(factory().create);

    const pending = client.dither(makeRequest(1));
    client.forget(1);
    FakeWorker.instances[0].respond(1);

    client.terminate();
    // forget으로 버린 요청은 terminate의 정리 대상에도 없다(그대로 미해결로 끝난다).
    await expect(Promise.race([pending, Promise.resolve('버려짐')])).resolves.toBe('버려짐');
  });
});
