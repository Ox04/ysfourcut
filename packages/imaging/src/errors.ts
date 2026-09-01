// 합성 단계에서 사용자에게 설명할 수 있는 오류. 원본 사진·비트맵 내용은 담지 않는다.

export const IMAGING_ERROR_CODES = [
  /** 문구가 24자를 넘음 */
  'CAPTION_TOO_LONG',
  /** 줄바꿈·제어문자처럼 한 줄에 그릴 수 없는 문구 */
  'CAPTION_INVALID',
  /** 프로필의 인쇄 가능 폭이 네컷을 배치할 수 없는 값 */
  'CONTENT_WIDTH_UNSUPPORTED',
  /** 최종 이미지 높이가 상한을 넘음 */
  'PAGE_TOO_TALL',
  /** 1비트 데이터가 상한을 넘음 */
  'DECODED_TOO_LARGE',
  /** 원본 사진 픽셀 수가 상한을 넘음 */
  'SOURCE_TOO_LARGE',
  /** 합성에 넘어온 사진 크기가 배치와 다름 */
  'PHOTO_SIZE_MISMATCH',
  /** 네 컷이 모두 있지 않음 */
  'CUTS_INCOMPLETE',
] as const;

export type ImagingErrorCode = (typeof IMAGING_ERROR_CODES)[number];

/** 코드별 한국어 안내. 화면에 그대로 쓸 수 있는 문장이다. */
export const IMAGING_ERROR_MESSAGES_KO: Record<ImagingErrorCode, string> = {
  CAPTION_TOO_LONG: '문구는 24자까지 넣을 수 있어요.',
  CAPTION_INVALID: '문구에 줄바꿈이나 특수 제어문자는 넣을 수 없어요.',
  CONTENT_WIDTH_UNSUPPORTED: '지금 프린터 설정의 인쇄 폭으로는 네컷을 배치할 수 없어요.',
  PAGE_TOO_TALL: '만들 수 있는 세로 길이를 넘었어요. 프린터 설정을 확인해 주세요.',
  DECODED_TOO_LARGE: '만들 수 있는 이미지 크기를 넘었어요. 프린터 설정을 확인해 주세요.',
  SOURCE_TOO_LARGE: '사진이 너무 커요. 다시 촬영해 주세요.',
  PHOTO_SIZE_MISMATCH: '사진 크기가 배치와 달라요. 다시 합성해 주세요.',
  CUTS_INCOMPLETE: '네 컷을 모두 찍은 뒤에 만들 수 있어요.',
};

export class ImagingError extends Error {
  readonly code: ImagingErrorCode;
  /** 숫자 근거만 담는다(입력 값·상한). 사진 픽셀은 담지 않는다. */
  readonly details: Readonly<Record<string, number | string>>;

  constructor(code: ImagingErrorCode, details: Record<string, number | string> = {}) {
    super(IMAGING_ERROR_MESSAGES_KO[code]);
    this.name = 'ImagingError';
    this.code = code;
    this.details = details;
  }
}
