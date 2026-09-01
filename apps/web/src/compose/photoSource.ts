// 원본 사진(blob URL)을 목표 dot 크기의 RGBA로 줄인다. 원본은 그대로 두고 사본만 만든다.
// 크롭 규칙은 촬영 미리보기와 같은 cover 방식을 그대로 쓴다.

import { coverCropRect } from '../camera/frameCapture';

export class PhotoSourceError extends Error {
  readonly code = 'PHOTO_LOAD_FAILED';

  constructor(message = '사진을 불러오지 못했어요. 다시 촬영해 주세요.') {
    super(message);
    this.name = 'PhotoSourceError';
  }
}

/**
 * 목표 dot 크기의 픽셀을 만든다. 원본 픽셀에는 어떤 보정도 하지 않는다.
 * 프로필이 바뀌면 이 함수를 원본 URL로 다시 호출해 재합성한다.
 */
export async function loadPhotoPixels(
  imageUrl: string,
  widthDots: number,
  heightDots: number,
): Promise<Uint8ClampedArray> {
  const source = await decode(imageUrl);

  const canvas = document.createElement('canvas');
  canvas.width = widthDots;
  canvas.height = heightDots;

  const context = canvas.getContext('2d', { willReadFrequently: true });
  if (!context) throw new PhotoSourceError('이 브라우저에서 사진을 처리하지 못했어요.');

  // 감열지에는 투명이 없다. 흰 종이 위에 얹는다.
  context.fillStyle = '#fff';
  context.fillRect(0, 0, widthDots, heightDots);

  const crop = coverCropRect(
    { width: source.naturalWidth, height: source.naturalHeight },
    widthDots / heightDots,
  );
  context.drawImage(source, crop.sx, crop.sy, crop.sWidth, crop.sHeight, 0, 0, widthDots, heightDots);

  return context.getImageData(0, 0, widthDots, heightDots).data;
}

/**
 * blob·data URL을 이미지로 읽는다. `<img>`를 쓰는 이유는 SVG 샘플까지 같은 경로로 다루기 위해서다
 * (createImageBitmap은 SVG blob을 받지 않는다). 같은 출처 URL만 쓰므로 캔버스가 오염되지 않는다.
 */
async function decode(imageUrl: string): Promise<HTMLImageElement> {
  const image = new Image();
  image.decoding = 'async';
  image.src = imageUrl;

  try {
    await image.decode();
  } catch {
    throw new PhotoSourceError();
  }

  if (image.naturalWidth <= 0 || image.naturalHeight <= 0) {
    throw new PhotoSourceError();
  }

  return image;
}
