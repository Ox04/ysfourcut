// 부스 화면의 props 경계. 카메라·편집·API 로직은 여기 타입만 채우면 된다.
// 08(카메라), 09(합성), 10(연결)이 이 경계를 구현하고, 화면 자체는 재디자인하지 않는다.

import { MAX_CAPTION_LENGTH, type DitherMethod, type FrameStyle } from '@ysfourcut/imaging';
import type { PrintJobState, ReceiptLayoutView, VirtualFaultKind } from '../api/contract';

/** 촬영 슬롯. imageUrl은 브라우저 메모리의 blob/데이터 URL이다. 서버로 보내지 않는다. */
export type CutSlot = {
  /** 1~4 */
  index: number;
  /** 없으면 아직 찍지 않은 슬롯 */
  imageUrl: string | null;
};

export type { DitherMethod, FrameStyle };

export type EditSettings = {
  frame: FrameStyle;
  /** -100 ~ +100. 최종 비트맵의 사진 픽셀에 그대로 반영한다. */
  brightness: number;
  /** -100 ~ +100 */
  contrast: number;
  /** 하단 문구, 최대 24자 */
  caption: string;
  /** 사진 디더링 방식. 기본은 Floyd–Steinberg. */
  dither: DitherMethod;
};

/** 배치·검증과 같은 값을 쓴다(packages/imaging). */
export const CAPTION_MAX_LENGTH = MAX_CAPTION_LENGTH;

/** 장치 준비 상태. 문구는 화면이 정하고 판단은 호출자가 넘긴다. */
export type DeviceReadiness = {
  cameraReady: boolean;
  /** 호스트 연결 + 가상/실물 출력 가능 여부 */
  printReady: boolean;
  printerMode: 'virtual' | 'physical';
  /** 출력이 불가능할 때 이유 한 줄 (예: 로컬 프로그램 연결 안 됨) */
  printBlockedReason?: string;
  /** 서버에 주입된 모의 오류. 실제 장치 상태가 아니며 모든 화면에 상시 표시한다. */
  injectedFault?: VirtualFaultKind | null;
};

/**
 * 결과 화면 상태. 서버 상태 계약(PrintJobState)을 그대로 쓴다.
 * **전제조건**: `state === 'rendered'`로 넘기려면 세 이미지의 blob URL을 모두 받은 뒤여야 한다.
 * 아티팩트 조회에 실패하면 rendered 대신 실패/재시도 상태로 넘긴다(10번 연결 규칙).
 */
export type ResultView = {
  state: PrintJobState;
  /** 서버가 만든 PNG의 blob URL. rendered 전에는 비어 있다. */
  images: Partial<Record<'appearance' | 'content' | 'paper', string>>;
  layout: ReceiptLayoutView | null;
  /** 실패 시 한국어 안내 (describeError 결과) */
  failureMessage?: string;
  /** 부분 이미지 여부 — 진단용 표시에만 쓴다 */
  isPartial?: boolean;
};
