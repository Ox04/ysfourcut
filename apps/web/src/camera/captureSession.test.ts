import { describe, expect, it } from 'vitest';
import {
  CUT_COUNT,
  acceptFrame,
  cancelSession,
  createCaptureState,
  filledCutCount,
  isBusy,
  retakeAll,
  retakeCut,
  startSession,
  tick,
  toCutSlots,
  type CaptureEffect,
  type CaptureResult,
  type CaptureState,
} from './captureSession';

/** 카운트다운을 끝까지 돌려 캡처 요청을 얻는다. */
function runCountdown(state: CaptureState): { state: CaptureState; effects: CaptureEffect[] } {
  let current = state;
  let effects: CaptureEffect[] = [];
  for (let i = 0; i < 3; i += 1) {
    const result = tick(current);
    current = result.state;
    effects = result.effects;
  }
  return { state: current, effects };
}

function captureRequest(effects: CaptureEffect[]) {
  const found = effects.find((effect) => effect.type === 'capture-frame');
  if (!found || found.type !== 'capture-frame') throw new Error('캡처 요청이 없습니다.');
  return found;
}

function released(effects: CaptureEffect[]): string[] {
  return effects.filter((e) => e.type === 'release-url').map((e) => (e as { url: string }).url);
}

/** 한 컷을 카운트다운부터 프레임 수신까지 끝낸다. */
function shoot(state: CaptureState, url: string): CaptureResult {
  const counted = runCountdown(state);
  const request = captureRequest(counted.effects);
  return acceptFrame(counted.state, {
    sessionId: request.sessionId,
    captureToken: request.captureToken,
    url,
    nowMs: 1_000,
  });
}

describe('네 컷 촬영', () => {
  it('컷마다 3초 카운트다운을 거쳐 정확히 네 슬롯을 채운다', () => {
    let state = startSession(createCaptureState()).state;
    expect(state.status).toBe('countdown');
    expect(state.secondsLeft).toBe(3);
    expect(state.targetCut).toBe(1);

    for (let cut = 1; cut <= CUT_COUNT; cut += 1) {
      expect(state.targetCut).toBe(cut);
      expect(state.secondsLeft).toBe(3);

      const first = tick(state);
      expect(first.state.secondsLeft).toBe(2);
      expect(first.effects).toHaveLength(0);

      state = shoot(state, `blob:cut-${cut}`).state;
      expect(filledCutCount(state)).toBe(cut);
    }

    expect(state.status).toBe('complete');
    expect(state.targetCut).toBeNull();
    expect(toCutSlots(state).map((slot) => slot.imageUrl)).toEqual([
      'blob:cut-1',
      'blob:cut-2',
      'blob:cut-3',
      'blob:cut-4',
    ]);
  });

  it('카운트다운이 끝나기 전에는 캡처를 요청하지 않는다', () => {
    const state = startSession(createCaptureState()).state;

    expect(tick(state).effects).toHaveLength(0);
    expect(tick(tick(state).state).effects).toHaveLength(0);
    expect(captureRequest(runCountdown(state).effects).cut).toBe(1);
  });

  it('촬영 중 시작 연타는 무시된다 (세션이 겹치지 않는다)', () => {
    const started = startSession(createCaptureState()).state;
    expect(isBusy(started)).toBe(true);

    const again = startSession(started);

    expect(again.state).toBe(started);
    expect(again.effects).toHaveLength(0);
    expect(again.state.sessionId).toBe(started.sessionId);
  });

  it('같은 셔터에서 프레임이 두 번 와도 한 슬롯만 채운다', () => {
    const started = startSession(createCaptureState()).state;
    const counted = runCountdown(started);
    const request = captureRequest(counted.effects);
    const frame = { sessionId: request.sessionId, captureToken: request.captureToken, nowMs: 1 };

    const first = acceptFrame(counted.state, { ...frame, url: 'blob:a' });
    const second = acceptFrame(first.state, { ...frame, url: 'blob:b' });

    expect(filledCutCount(first.state)).toBe(1);
    expect(filledCutCount(second.state)).toBe(1);
    expect(second.state.slots[0]?.url).toBe('blob:a');
    // 두 번째 프레임은 버려지고 URL 해제를 알린다.
    expect(released(second.effects)).toEqual(['blob:b']);
  });
});

describe('재촬영과 세션 무효화', () => {
  it('한 슬롯 재촬영은 그 슬롯만 바꾸고 나머지는 보존한다', () => {
    let state = startSession(createCaptureState()).state;
    for (let cut = 1; cut <= CUT_COUNT; cut += 1) {
      state = shoot(state, `blob:cut-${cut}`).state;
    }

    const retake = retakeCut(state, 2);
    expect(retake.state.status).toBe('countdown');
    expect(retake.state.targetCut).toBe(2);
    expect(retake.state.isRetake).toBe(true);

    const done = shoot(retake.state, 'blob:cut-2-new');

    expect(toCutSlots(done.state).map((slot) => slot.imageUrl)).toEqual([
      'blob:cut-1',
      'blob:cut-2-new',
      'blob:cut-3',
      'blob:cut-4',
    ]);
    // 교체된 이전 사진만 해제한다.
    expect(released(done.effects)).toEqual(['blob:cut-2']);
    // 재촬영 뒤 다음 컷으로 넘어가지 않는다.
    expect(done.state.status).toBe('complete');
    expect(done.state.targetCut).toBeNull();
  });

  it('취소 뒤 도착한 늦은 프레임은 이전 세션을 되살리지 않는다', () => {
    const started = startSession(createCaptureState()).state;
    const counted = runCountdown(started);
    const request = captureRequest(counted.effects);

    const cancelled = cancelSession(counted.state);
    expect(cancelled.state.status).toBe('idle');
    expect(filledCutCount(cancelled.state)).toBe(0);

    const late = acceptFrame(cancelled.state, {
      sessionId: request.sessionId,
      captureToken: request.captureToken,
      url: 'blob:late',
      nowMs: 2,
    });

    expect(filledCutCount(late.state)).toBe(0);
    expect(late.state.status).toBe('idle');
    expect(released(late.effects)).toEqual(['blob:late']);
  });

  it('전체 다시 찍기 뒤 이전 세션의 프레임은 버려진다', () => {
    let state = startSession(createCaptureState()).state;
    state = shoot(state, 'blob:old-1').state;

    const counted = runCountdown(state);
    const staleRequest = captureRequest(counted.effects);

    const restarted = retakeAll(counted.state);
    expect(filledCutCount(restarted.state)).toBe(0);
    expect(released(restarted.effects)).toEqual(['blob:old-1']);

    const late = acceptFrame(restarted.state, {
      sessionId: staleRequest.sessionId,
      captureToken: staleRequest.captureToken,
      url: 'blob:stale',
      nowMs: 3,
    });

    expect(filledCutCount(late.state)).toBe(0);
    expect(released(late.effects)).toEqual(['blob:stale']);
  });

  it('취소는 찍은 사진을 모두 해제한다', () => {
    let state = startSession(createCaptureState()).state;
    state = shoot(state, 'blob:1').state;
    state = shoot(state, 'blob:2').state;

    const cancelled = cancelSession(state);

    expect(released(cancelled.effects).sort()).toEqual(['blob:1', 'blob:2']);
    expect(filledCutCount(cancelled.state)).toBe(0);
  });

  it('촬영 중에는 재촬영 요청을 받지 않는다', () => {
    const started = startSession(createCaptureState()).state;

    expect(retakeCut(started, 3).state).toBe(started);
    expect(retakeCut(started, 0).state).toBe(started);
    expect(retakeCut(started, 5).state).toBe(started);
  });

  it('대기 상태의 tick은 아무 일도 하지 않는다 (늦은 타이머)', () => {
    const idle = createCaptureState();

    expect(tick(idle).state).toBe(idle);
    expect(tick(idle).effects).toHaveLength(0);
  });
});
