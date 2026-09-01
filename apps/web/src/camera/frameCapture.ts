// 미리보기와 저장되는 사진의 크롭·반전을 일치시키기 위한 순수 계산.
// 비율을 강제로 늘리지 않는다(항상 잘라내기만 한다).

export type Size = { width: number; height: number };

export type CropRect = { sx: number; sy: number; sWidth: number; sHeight: number };

/** 사진 한 컷의 기본 비율 4:3 (IMPLEMENTATION_OUTLINE 2절). */
export const CUT_ASPECT = 4 / 3;

/** 저장 원본의 최대 가로 픽셀. 09번 합성이 축소해 쓰기 충분한 크기. */
export const MAX_CAPTURE_WIDTH = 1280;

/**
 * CSS `object-fit: cover`와 같은 크롭 영역. 화면 미리보기가 잘라내는 영역과 같아야
 * 저장된 사진이 미리보기와 일치한다.
 */
export function coverCropRect(source: Size, targetAspect: number): CropRect {
  if (source.width <= 0 || source.height <= 0 || targetAspect <= 0) {
    throw new RangeError('영상 크기와 목표 비율은 0보다 커야 합니다.');
  }

  const sourceAspect = source.width / source.height;

  if (sourceAspect > targetAspect) {
    // 영상이 더 넓다 → 좌우를 자른다
    const sWidth = source.height * targetAspect;
    return { sx: (source.width - sWidth) / 2, sy: 0, sWidth, sHeight: source.height };
  }

  // 영상이 더 좁거나 같다 → 위아래를 자른다
  const sHeight = source.width / targetAspect;
  return { sx: 0, sy: (source.height - sHeight) / 2, sWidth: source.width, sHeight };
}

/** 크롭 결과를 담을 캔버스 크기. 비율은 유지하고 상한만 적용한다. */
export function captureTargetSize(crop: CropRect, maxWidth = MAX_CAPTURE_WIDTH): Size {
  const scale = Math.min(1, maxWidth / crop.sWidth);
  return {
    width: Math.max(1, Math.round(crop.sWidth * scale)),
    height: Math.max(1, Math.round(crop.sHeight * scale)),
  };
}

/** 영상 프레임이 실제로 쓸 수 있는 상태인지. 준비 전 검은 프레임을 확정하지 않기 위해 쓴다. */
export function isFrameReady(video: {
  readyState: number;
  videoWidth: number;
  videoHeight: number;
}): boolean {
  // HAVE_CURRENT_DATA(2) 이상이면 현재 프레임이 있다.
  return video.readyState >= 2 && video.videoWidth > 0 && video.videoHeight > 0;
}
