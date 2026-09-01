// 네컷 스트립의 정수 dot 배치. 프로필의 실제 인쇄 가능 폭만 입력으로 쓰고
// 80/58mm 같은 이름으로 dot를 추측하지 않는다. DOM·Canvas에 의존하지 않는다.

import { ImagingError } from './errors';

export type FrameStyle = 'basic' | 'receipt';

export const FRAME_STYLES: readonly FrameStyle[] = ['basic', 'receipt'];

/** 세로 1열 네 컷 고정. */
export const CUT_COUNT = 4;

/** 사진 한 컷의 비율 4:3 (가로:세로). */
export const CUT_ASPECT_W = 4;
export const CUT_ASPECT_H = 3;

/** 하단 문구 최대 길이(코드 포인트 기준). */
export const MAX_CAPTION_LENGTH = 24;

/**
 * SERVICE_DESIGN 5절의 계산 예시가 쓰는 기준 폭. 다른 폭은 이 비율을 정수로 환산한다.
 * 이 값 자체가 장치 사양은 아니다.
 */
export const REFERENCE_CONTENT_WIDTH_DOTS = 576;

/** 서비스 안전 상한(SERVICE_DESIGN 8절). 장치 사양이 아니다. */
export const SERVICE_MAX_WIDTH_DOTS = 1024;
export const SERVICE_MAX_HEIGHT_DOTS = 4096;
export const SERVICE_MAX_DECODED_BYTES = 512 * 1024;

/** 배치를 만들 수 있는 최소 인쇄 폭. 사진 폭이 8dot 아래로 내려가지 않게 한다. */
export const MIN_CONTENT_WIDTH_DOTS = 48;

export type Rect = { x: number; y: number; width: number; height: number };

export type StripLayout = {
  widthDots: number;
  heightDots: number;
  frame: FrameStyle;
  /** 사진 네 칸. 세로 1열, 모두 같은 크기이고 정확히 4:3이다. */
  cuts: readonly Rect[];
  photoWidthDots: number;
  photoHeightDots: number;
  gapDots: number;
  header: Rect;
  footer: Rect;
  /** 글자를 그릴 수 있는 영역. 구분선과 겹치지 않는다. */
  headerTextRect: Rect;
  footerTextRect: Rect;
  /** 구분선의 y 좌표(두께 ruleDots) */
  headerRuleY: number;
  footerRuleY: number;
  /** 테두리·구분선 두께 */
  ruleDots: number;
  /** 글자 크기(dot). 실제 그리기는 브라우저가 하고 배치는 여기서 정한다. */
  headerFontDots: number;
  captionFontDots: number;
  dateFontDots: number;
};

function scaled(value: number, scale: number, minimum: number): number {
  return Math.max(minimum, Math.round(value * scale));
}

/**
 * 인쇄 가능 폭에서 네컷 배치를 만든다.
 * 사진 폭은 4의 배수로 맞춰 4:3 높이가 정수가 되게 한다.
 */
export function createStripLayout(input: {
  contentWidthDots: number;
  frame: FrameStyle;
}): StripLayout {
  const { contentWidthDots, frame } = input;

  if (
    !Number.isInteger(contentWidthDots) ||
    contentWidthDots < MIN_CONTENT_WIDTH_DOTS ||
    contentWidthDots > SERVICE_MAX_WIDTH_DOTS
  ) {
    throw new ImagingError('CONTENT_WIDTH_UNSUPPORTED', {
      contentWidthDots,
      minimum: MIN_CONTENT_WIDTH_DOTS,
      maximum: SERVICE_MAX_WIDTH_DOTS,
    });
  }

  const scale = contentWidthDots / REFERENCE_CONTENT_WIDTH_DOTS;

  // 기준 배치: 좌우 여백 24dot, 사진 간격 12dot, 머리 64dot, 꼬리 104dot.
  const sideMargin = scaled(24, scale, 2);
  const rawPhotoWidth = contentWidthDots - sideMargin * 2;
  const photoWidthDots = rawPhotoWidth - (rawPhotoWidth % CUT_ASPECT_W);

  if (photoWidthDots < 8) {
    throw new ImagingError('CONTENT_WIDTH_UNSUPPORTED', {
      contentWidthDots,
      photoWidthDots,
    });
  }

  const photoHeightDots = (photoWidthDots / CUT_ASPECT_W) * CUT_ASPECT_H;
  const photoX = Math.floor((contentWidthDots - photoWidthDots) / 2);
  const gapDots = scaled(12, scale, 2);
  const headerHeight = scaled(64, scale, 10);
  const footerHeight = scaled(104, scale, 16);

  const cuts: Rect[] = [];
  for (let index = 0; index < CUT_COUNT; index += 1) {
    cuts.push({
      x: photoX,
      y: headerHeight + index * (photoHeightDots + gapDots),
      width: photoWidthDots,
      height: photoHeightDots,
    });
  }

  const lastCut = cuts[CUT_COUNT - 1];
  const footerY = lastCut.y + lastCut.height;
  const heightDots = footerY + footerHeight;

  const ruleDots = scaled(2, scale, 1);
  const ruleGap = Math.max(1, Math.floor(gapDots / 2));
  const headerRuleY = Math.max(1, headerHeight - ruleDots - ruleGap);
  const footerRuleY = footerY + ruleGap;
  const footerTextY = footerRuleY + ruleDots;

  return {
    widthDots: contentWidthDots,
    heightDots,
    frame,
    cuts,
    photoWidthDots,
    photoHeightDots,
    gapDots,
    header: { x: photoX, y: 0, width: photoWidthDots, height: headerHeight },
    footer: { x: photoX, y: footerY, width: photoWidthDots, height: footerHeight },
    headerTextRect: { x: photoX, y: 0, width: photoWidthDots, height: headerRuleY },
    footerTextRect: {
      x: photoX,
      y: footerTextY,
      width: photoWidthDots,
      height: heightDots - footerTextY,
    },
    headerRuleY,
    footerRuleY,
    ruleDots,
    headerFontDots: scaled(28, scale, 6),
    captionFontDots: scaled(30, scale, 6),
    dateFontDots: scaled(22, scale, 5),
  };
}

/** 최종 이미지가 서비스·프로필 상한 안에 있는지 확인한다. 넘으면 설명 가능한 오류다. */
export function assertLayoutWithinLimits(
  layout: StripLayout,
  limits: { maxHeightDots: number; maxDecodedBytes: number },
): void {
  const maxHeight = Math.min(limits.maxHeightDots, SERVICE_MAX_HEIGHT_DOTS);
  if (layout.heightDots > maxHeight) {
    throw new ImagingError('PAGE_TOO_TALL', { heightDots: layout.heightDots, maximum: maxHeight });
  }

  const maxBytes = Math.min(limits.maxDecodedBytes, SERVICE_MAX_DECODED_BYTES);
  const decodedBytes = Math.ceil(layout.widthDots / 8) * layout.heightDots;
  if (decodedBytes > maxBytes) {
    throw new ImagingError('DECODED_TOO_LARGE', { decodedBytes, maximum: maxBytes });
  }
}

/** 한 줄로 그릴 수 없는 문자(제어문자·줄바꿈·구분자). */
const CONTROL_CHARACTERS = /[\u0000-\u001F\u007F-\u009F\u2028\u2029]/u;

/** 문구 검사. 24자를 넘거나 한 줄로 그릴 수 없으면 거부한다. */
export function normalizeCaption(caption: string): string {
  const trimmed = caption.replace(/\s+$/u, '');
  const points = [...trimmed];

  if (points.length > MAX_CAPTION_LENGTH) {
    throw new ImagingError('CAPTION_TOO_LONG', {
      length: points.length,
      maximum: MAX_CAPTION_LENGTH,
    });
  }

  if (CONTROL_CHARACTERS.test(trimmed)) {
    throw new ImagingError('CAPTION_INVALID', { length: points.length });
  }

  return trimmed;
}
