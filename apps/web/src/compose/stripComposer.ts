// 최종 1비트 네컷을 만드는 한 곳. 미리보기·PNG 저장·서버 전달이 모두 이 결과를 쓴다.
// 순서: 원본 → (촬영 때 끝난 반전) → 크롭·목표 dot 크기 → 명암/그레이스케일 → 사진 디더링
//      → 흰 프레임 → 한글 → 최종 1비트.

import {
  ImagingError,
  assertLayoutWithinLimits,
  composeStripPage,
  createStripLayout,
  normalizeCaption,
  packMonoPage,
  type DitherMethod,
  type FrameStyle,
  type MonoBitmap,
  type MonoPage,
  type StripLayout,
} from '@ysfourcut/imaging';
import { ensurePrintFont, type PrintFontState } from './fonts';
import { loadPhotoPixels } from './photoSource';
import { PhotoWorkerClient } from './photoWorkerClient';
import { renderStripTextMasks } from './textLayer';

/** 머리말은 고정 문구다. 사용자 문구는 하단에만 들어간다. */
export const STRIP_TITLE = 'YS FOURCUT';

export type StripComposeRequest = {
  revision: number;
  /** 원본 사진 URL 네 개(브라우저 메모리). 이 함수는 원본을 바꾸지 않는다. */
  photoUrls: readonly (string | null)[];
  frame: FrameStyle;
  brightness: number;
  contrast: number;
  caption: string;
  dither: DitherMethod;
  /** 촬영 세션 시작 시 고정한 날짜 */
  sessionDate: string;
  /** 프로필이 알려 준 실제 인쇄 가능 폭. 이름으로 추측하지 않는다. */
  contentWidthDots: number;
  limits: { maxHeightDots: number; maxDecodedBytes: number };
};

export type ComposedStrip = {
  revision: number;
  layout: StripLayout;
  page: MonoPage;
  /** 서버로 보낼 형식. 미리보기와 같은 픽셀에서 나온다. */
  bitmap: MonoBitmap;
  /** 화면 표시·PNG 저장용 blob URL(같은 최종 비트맵을 PNG로 인코딩한 것) */
  previewUrl: string;
  fontState: PrintFontState;
};

/** 최종 페이지를 흑백 PNG로 인코딩한다. 화면과 저장이 같은 픽셀을 쓴다. */
export async function monoPageToPngBlob(page: MonoPage): Promise<Blob> {
  const canvas = document.createElement('canvas');
  canvas.width = page.widthDots;
  canvas.height = page.heightDots;

  const context = canvas.getContext('2d');
  if (!context) throw new ImagingError('PHOTO_SIZE_MISMATCH', { expected: page.pixels.length, received: 0 });

  const image = context.createImageData(page.widthDots, page.heightDots);
  for (let index = 0; index < page.pixels.length; index += 1) {
    const value = page.pixels[index] ? 0 : 255;
    const offset = index * 4;
    image.data[offset] = value;
    image.data[offset + 1] = value;
    image.data[offset + 2] = value;
    image.data[offset + 3] = 255;
  }
  context.putImageData(image, 0, 0);

  return new Promise<Blob>((resolve, reject) => {
    canvas.toBlob((blob) => {
      if (blob) {
        resolve(blob);
        return;
      }
      reject(new Error('미리보기 이미지를 만들지 못했어요.'));
    }, 'image/png');
  });
}

/**
 * 한 번의 합성. 사진 처리는 Worker가 하고, 폰트가 필요한 글자는 메인 스레드에서 그린다.
 * 폰트 로딩이 끝나기 전에는 결과를 확정하지 않는다.
 */
export async function composeStrip(
  request: StripComposeRequest,
  worker: PhotoWorkerClient,
): Promise<ComposedStrip> {
  const caption = normalizeCaption(request.caption);
  const layout = createStripLayout({
    contentWidthDots: request.contentWidthDots,
    frame: request.frame,
  });
  assertLayoutWithinLimits(layout, request.limits);

  const urls = request.photoUrls;
  if (urls.length !== layout.cuts.length || urls.some((url) => !url)) {
    throw new ImagingError('CUTS_INCOMPLETE', { received: urls.filter(Boolean).length });
  }

  // 글자를 그리기 전에 로컬 폰트 로딩을 끝낸다.
  const fontState = await ensurePrintFont();

  const photos = await Promise.all(
    urls.map((url) => loadPhotoPixels(url as string, layout.photoWidthDots, layout.photoHeightDots)),
  );

  const photoBits = await worker.dither({
    revision: request.revision,
    widthDots: layout.photoWidthDots,
    heightDots: layout.photoHeightDots,
    brightness: request.brightness,
    contrast: request.contrast,
    method: request.dither,
    photos,
  });

  // 글자는 사진 디더링 이후에 얹는다.
  const textMasks = renderStripTextMasks(layout, {
    title: STRIP_TITLE,
    caption,
    dateText: request.sessionDate,
  });

  const page = composeStripPage({ layout, photoBits, textMasks });
  const bitmap = packMonoPage(page);
  const previewUrl = URL.createObjectURL(await monoPageToPngBlob(page));

  return { revision: request.revision, layout, page, bitmap, previewUrl, fontState };
}
