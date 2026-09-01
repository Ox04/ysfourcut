import { describe, expect, it } from 'vitest';
import {
  CUT_ASPECT,
  captureTargetSize,
  coverCropRect,
  isFrameReady,
} from './frameCapture';

describe('coverCropRect', () => {
  it('넓은 영상은 좌우를 잘라 4:3을 만든다 (세로는 그대로)', () => {
    const crop = coverCropRect({ width: 1920, height: 1080 }, CUT_ASPECT);

    expect(crop.sHeight).toBe(1080);
    expect(crop.sWidth).toBe(1440); // 1080 × 4/3
    expect(crop.sx).toBe(240); // 좌우 균등
    expect(crop.sy).toBe(0);
    expect(crop.sWidth / crop.sHeight).toBeCloseTo(CUT_ASPECT, 10);
  });

  it('좁은 영상은 위아래를 잘라 4:3을 만든다 (가로는 그대로)', () => {
    const crop = coverCropRect({ width: 480, height: 640 }, CUT_ASPECT);

    expect(crop.sWidth).toBe(480);
    expect(crop.sHeight).toBe(360); // 480 ÷ 4/3
    expect(crop.sx).toBe(0);
    expect(crop.sy).toBe(140);
    expect(crop.sWidth / crop.sHeight).toBeCloseTo(CUT_ASPECT, 10);
  });

  it('이미 4:3이면 자르지 않는다', () => {
    const crop = coverCropRect({ width: 640, height: 480 }, CUT_ASPECT);

    expect(crop).toEqual({ sx: 0, sy: 0, sWidth: 640, sHeight: 480 });
  });

  it('잘못된 크기는 거부한다', () => {
    expect(() => coverCropRect({ width: 0, height: 480 }, CUT_ASPECT)).toThrow(RangeError);
    expect(() => coverCropRect({ width: 640, height: 480 }, 0)).toThrow(RangeError);
  });
});

describe('captureTargetSize', () => {
  it('상한을 넘으면 비율을 유지한 채 줄인다', () => {
    const size = captureTargetSize({ sx: 0, sy: 0, sWidth: 1440, sHeight: 1080 }, 1280);

    expect(size.width).toBe(1280);
    expect(size.height).toBe(960); // 비율 유지
    expect(size.width / size.height).toBeCloseTo(CUT_ASPECT, 10);
  });

  it('상한보다 작으면 늘리지 않는다', () => {
    const size = captureTargetSize({ sx: 0, sy: 0, sWidth: 640, sHeight: 480 }, 1280);

    expect(size).toEqual({ width: 640, height: 480 });
  });
});

describe('isFrameReady', () => {
  it('준비되지 않은 영상은 캡처하지 않는다', () => {
    expect(isFrameReady({ readyState: 0, videoWidth: 640, videoHeight: 480 })).toBe(false);
    expect(isFrameReady({ readyState: 1, videoWidth: 640, videoHeight: 480 })).toBe(false);
    expect(isFrameReady({ readyState: 4, videoWidth: 0, videoHeight: 0 })).toBe(false);
  });

  it('현재 프레임이 있으면 캡처한다', () => {
    expect(isFrameReady({ readyState: 2, videoWidth: 640, videoHeight: 480 })).toBe(true);
    expect(isFrameReady({ readyState: 4, videoWidth: 1280, videoHeight: 720 })).toBe(true);
  });
});
