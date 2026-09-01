// .NET 호스트와 공유하는 API 계약. C# 쪽 원본은
// apps/print-host/Contracts/ApiContracts.cs 와 Contracts/ErrorCodes.cs 다.
// 한쪽만 바꾸지 않는다. 실제 경로·상태는 docs/API_CONTRACT.md에 정리돼 있다.

export const SCHEMA_VERSION = 1;

export const CLIENT_HEADER_NAME = 'X-YSFourcut-Client';
export const CLIENT_HEADER_VALUE = 'web';

// ── 오류 ──────────────────────────────────────────────────────────────────────

export const ERROR_CODES = [
  // 전송·요청 형식
  'HOST_NOT_ALLOWED',
  'ORIGIN_NOT_ALLOWED',
  'CLIENT_HEADER_REQUIRED',
  'CONTENT_TYPE_UNSUPPORTED',
  'BODY_TOO_LARGE',
  'MALFORMED_JSON',
  'SCHEMA_VERSION_UNSUPPORTED',
  'VALIDATION_FAILED',
  // 인증·세션
  'SESSION_REQUIRED',
  'SESSION_INVALID',
  'BOOTSTRAP_CODE_INVALID',
  'BOOTSTRAP_CODE_EXPIRED',
  'BOOTSTRAP_CODE_ALREADY_USED',
  // 프린터·프로필
  'PRINTER_NOT_FOUND',
  'PROFILE_CHANGED',
  'PROFILE_UNVERIFIED',
  'PROFILE_KIND_MISMATCH',
  'PLATFORM_UNSUPPORTED',
  // 비트맵·작업
  'INVALID_BITMAP',
  'PAGE_SIZE_UNSUPPORTED',
  'JOB_ID_CONFLICT',
  'PRINTER_BUSY',
  'PRINT_OUTCOME_UNKNOWN',
  // 렌더링
  'RENDER_LIMIT_EXCEEDED',
  'RENDER_TIMEOUT',
  'VIRTUAL_RENDER_FAILED',
  'JOB_RECORD_FAILED',
  'HOST_RESTARTED',
  // 가상 모드의 모의 장치 오류(실제 센서 감지가 아님)
  'VIRTUAL_OUT_OF_PAPER',
  'VIRTUAL_COVER_OPEN',
  'VIRTUAL_PRINTER_OFFLINE',
  'RESOLVE_NOT_REQUIRED',
  // 결과 이미지
  'ARTIFACT_NOT_READY',
  'ARTIFACT_EXPIRED',
  'ARTIFACT_NOT_FOUND',
  // 미구현·기타
  'NOT_IMPLEMENTED',
  'INTERNAL_ERROR',
] as const;

export type ErrorCode = (typeof ERROR_CODES)[number];

export type ApiErrorDetail = {
  code: ErrorCode | string;
  message: string;
  details?: Record<string, unknown>;
};

export type ApiErrorBody = {
  schemaVersion: number;
  error: ApiErrorDetail;
};

/** 코드별 한국어 안내. 사진·비트맵·인증값은 어떤 문구에도 넣지 않는다. */
export const ERROR_MESSAGES_KO: Record<ErrorCode, string> = {
  HOST_NOT_ALLOWED: '127.0.0.1 주소로 접속해 주세요.',
  ORIGIN_NOT_ALLOWED: '허용되지 않은 화면에서 보낸 요청이에요.',
  CLIENT_HEADER_REQUIRED: '앱에서 보낸 요청이 아니에요.',
  CONTENT_TYPE_UNSUPPORTED: '요청 형식이 올바르지 않아요.',
  BODY_TOO_LARGE: '보낸 이미지가 너무 커요. 다시 시도해 주세요.',
  MALFORMED_JSON: '요청을 읽지 못했어요. 다시 시도해 주세요.',
  SCHEMA_VERSION_UNSUPPORTED: '앱과 로컬 프로그램의 버전이 달라요. 로컬 프로그램을 업데이트해 주세요.',
  VALIDATION_FAILED: '요청 값이 올바르지 않아요.',
  SESSION_REQUIRED: '로컬 프로그램과 연결해 주세요.',
  SESSION_INVALID: '연결이 만료됐어요. 다시 연결해 주세요.',
  BOOTSTRAP_CODE_INVALID: '연결 코드가 올바르지 않아요. 다시 연결해 주세요.',
  BOOTSTRAP_CODE_EXPIRED: '연결 코드가 만료됐어요. 다시 연결해 주세요.',
  BOOTSTRAP_CODE_ALREADY_USED: '이미 사용한 연결 코드예요. 다시 연결해 주세요.',
  PRINTER_NOT_FOUND: '선택한 프린터 설정을 찾지 못했어요.',
  PROFILE_CHANGED: '프린터 설정이 바뀌었어요. 설정을 다시 불러온 뒤 진행해 주세요.',
  PROFILE_UNVERIFIED: '아직 실물 출력 검수를 마치지 않은 설정이에요.',
  PROFILE_KIND_MISMATCH: '지금 실행 모드에서 쓸 수 없는 프린터 설정이에요.',
  PLATFORM_UNSUPPORTED: '이 컴퓨터에서는 지원하지 않는 출력 모드예요.',
  INVALID_BITMAP: '출력할 이미지 데이터가 올바르지 않아요.',
  PAGE_SIZE_UNSUPPORTED: '출력할 수 있는 크기를 넘었어요.',
  JOB_ID_CONFLICT: '같은 작업 번호로 다른 내용을 보냈어요. 새로 만들어 주세요.',
  PRINTER_BUSY: '이미 진행 중인 출력이 있어요. 끝난 뒤 다시 시도해 주세요.',
  PRINT_OUTCOME_UNKNOWN: '출력 결과를 확인하지 못했어요. 종이와 프린터 큐를 확인해 주세요.',
  RENDER_LIMIT_EXCEEDED: '만들 수 있는 영수증 크기를 넘었어요. 사진 수나 길이를 줄여 주세요.',
  RENDER_TIMEOUT: '가상 영수증을 만드는 데 너무 오래 걸렸어요. 다시 시도해 주세요.',
  VIRTUAL_RENDER_FAILED: '가상 출력에 실패했어요. 다시 시도해 주세요.',
  JOB_RECORD_FAILED: '작업 기록을 저장하지 못해 출력을 시작하지 않았어요.',
  HOST_RESTARTED: '로컬 프로그램이 다시 시작되어 이전 작업이 정리됐어요.',
  VIRTUAL_OUT_OF_PAPER: '가상 용지가 없어요(모의 오류). 다시 시도해 주세요.',
  VIRTUAL_COVER_OPEN: '가상 덮개가 열려 있어요(모의 오류). 다시 시도해 주세요.',
  VIRTUAL_PRINTER_OFFLINE: '가상 프린터가 오프라인이에요(모의 오류). 다시 시도해 주세요.',
  RESOLVE_NOT_REQUIRED: '가상 출력은 종이나 프린터 큐 확인이 필요 없어요. 새 작업으로 다시 시도해 주세요.',
  ARTIFACT_NOT_READY: '아직 결과 이미지를 만드는 중이에요.',
  ARTIFACT_EXPIRED: '결과 이미지가 만료됐어요.',
  ARTIFACT_NOT_FOUND: '결과 이미지를 찾지 못했어요.',
  NOT_IMPLEMENTED: '아직 준비되지 않은 기능이에요.',
  INTERNAL_ERROR: '로컬 프로그램에서 오류가 났어요.',
};

export function describeError(error: ApiErrorDetail | null | undefined): string {
  if (!error) return '알 수 없는 오류가 발생했어요.';
  const known = (ERROR_MESSAGES_KO as Record<string, string | undefined>)[error.code];
  return known ?? error.message;
}

// ── health / bootstrap ────────────────────────────────────────────────────────

export type PrinterMode = 'virtual' | 'physical';

export type SessionStatus = { authenticated: boolean };

export type HealthResponse = {
  schemaVersion: number;
  status: string;
  ready: boolean;
  printerMode: PrinterMode;
  hostInstanceId: string;
  hostVersion: string;
  environment: string;
  session: SessionStatus;
};

export type BootstrapResponse = {
  schemaVersion: number;
  session: SessionStatus;
  sessionIdleTimeoutSeconds: number;
  hostInstanceId: string;
};

// ── 프린터·프로필 ─────────────────────────────────────────────────────────────

export type ProfileKind = 'virtual' | 'hardware';
export type CutStyle = 'straight' | 'tear' | 'none';

export type PaperGeometryView = {
  paperWidthDots: number;
  contentWidthDots: number;
  sideMarginDots: number;
  leadingFeedDots: number;
  trailingFeedDots: number;
  dotsPerMmX: number;
  dotsPerMmY: number;
  dpiX: number;
  dpiY: number;
  paperWidthMm: number;
  contentWidthMm: number;
  cutStyle: CutStyle;
};

export type ProfileLimitsView = {
  maxContentWidthDots: number;
  maxContentHeightDots: number;
  maxDecodedBytes: number;
};

export type VerificationView = {
  verified: boolean;
  verifiedRevision: string | null;
  verifiedAtUtc: string | null;
  testJobId: string | null;
};

export type PrinterProfileView = {
  profileId: string;
  revision: string;
  kind: ProfileKind;
  displayName: string;
  hardwareVerified: boolean;
  isSelected: boolean;
  paper: PaperGeometryView;
  limits: ProfileLimitsView;
  verification: VerificationView;
  notes: string;
};

export type ServiceLimitsView = {
  maxRequestBodyBytes: number;
  maxWidthDots: number;
  maxHeightDots: number;
  maxDecodedBytes: number;
};

export type PrintersResponse = {
  schemaVersion: number;
  printerMode: PrinterMode;
  osQueryPerformed: boolean;
  printers: PrinterProfileView[];
};

export type PrinterProfileResponse = {
  schemaVersion: number;
  printerMode: PrinterMode;
  profile: PrinterProfileView;
  serviceLimits: ServiceLimitsView;
};

export type UpdatePrinterProfileRequest = {
  schemaVersion: number;
  profileId: string;
  /** 마지막으로 본 **현재 활성 프로필**의 revision. 다르면 PROFILE_CHANGED. */
  expectedRevision: string;
  cutStyle?: CutStyle;
};

// ── 출력 작업 (접수는 05번에서 구현) ──────────────────────────────────────────

export const BIT_ORDER = 'msb-first' as const;
export const BLACK_BIT = 1 as const;

export type PrintJobBitmap = {
  widthDots: number;
  heightDots: number;
  /** ceil(widthDots / 8) 과 정확히 같아야 한다. */
  strideBytes: number;
  bitOrder: typeof BIT_ORDER;
  blackBit: typeof BLACK_BIT;
  /** 위에서 아래로, 왼쪽부터. 행 우측 남는 비트는 흰색 0. */
  dataBase64: string;
};

export type PrintJobRequest = {
  schemaVersion: number;
  /** 클릭 한 번에 만드는 UUID. 재전송해도 같은 값을 유지한다. */
  clientJobId: string;
  profileId: string;
  profileRevision: string;
  bitmap: PrintJobBitmap;
};

export type PrintJobState =
  | 'accepted'
  | 'rendering'
  | 'rendered'
  | 'virtual_failed'
  | 'submitting'
  | 'submitted'
  | 'failed_before_submit'
  | 'outcome_unknown';

export type ArtifactKind = 'content' | 'paper' | 'appearance';

export type ReceiptLayoutView = {
  paperWidthDots: number;
  paperHeightDots: number;
  contentXDots: number;
  contentYDots: number;
  contentWidthDots: number;
  contentHeightDots: number;
  leadingFeedDots: number;
  trailingFeedDots: number;
  dpiX: number;
  dpiY: number;
};

export type ArtifactView = {
  kind: ArtifactKind;
  path: string;
  available: boolean;
  complete: boolean;
  widthPx: number | null;
  heightPx: number | null;
  byteLength: number | null;
  expiresAtUtc: string | null;
};

export type SimulationView = {
  appearanceRevision: string;
  seed: number;
  estimatedEffects: string[];
  injectedFault: string | null;
  isSimulated: boolean;
};

export type PrintJobFailureView = {
  code: string;
  message: string;
};

export type ClearArtifactsResponse = {
  schemaVersion: number;
  clearedJobs: number;
  /** 다음 사용자 세션은 이 값이 0인 것을 확인했거나 hostInstanceId가 바뀐 뒤에만 시작한다. */
  remainingJobs: number;
  hostInstanceId: string;
};

// ── 가상 모드 실패 주입 (모의 오류) ───────────────────────────────────────────

export const VIRTUAL_FAULTS = [
  'delay',
  'out_of_paper',
  'cover_open',
  'offline',
  'fail_before_render',
  'fail_after_rows',
  'render_timeout',
] as const;

export type VirtualFaultKind = (typeof VIRTUAL_FAULTS)[number];

export const VIRTUAL_FAULT_LABELS_KO: Record<VirtualFaultKind, string> = {
  delay: '출력 지연',
  out_of_paper: '용지 없음(모의)',
  cover_open: '덮개 열림(모의)',
  offline: '오프라인(모의)',
  fail_before_render: '렌더 시작 전 실패',
  fail_after_rows: 'N행 뒤 실패',
  render_timeout: '응답 없음(시간 초과)',
};

export const VIRTUAL_FAULT_DELAY_MIN_MS = 500;
export const VIRTUAL_FAULT_DELAY_MAX_MS = 10_000;

export type VirtualFaultView = {
  kind: VirtualFaultKind;
  delayMs: number;
  failAfterRows: number | null;
  /** 항상 true. 실제 장치 상태가 아니라 모의 오류임을 뜻한다. */
  isSimulated: boolean;
};

export type VirtualFaultRequest = {
  schemaVersion: number;
  /** null이면 주입을 해제한다. */
  fault: VirtualFaultKind | null;
  delayMs?: number;
  failAfterRows?: number | null;
};

export type VirtualFaultResponse = {
  schemaVersion: number;
  printerMode: PrinterMode;
  fault: VirtualFaultView | null;
  availableFaults: VirtualFaultKind[];
};

export type PrintJobStatusResponse = {
  schemaVersion: number;
  clientJobId: string;
  printerMode: PrinterMode;
  state: PrintJobState;
  isPhysical: boolean;
  spoolJobId: string | null;
  queueBusy: boolean;
  profileId: string;
  profileRevision: string;
  sourceDigest: string;
  layout: ReceiptLayoutView | null;
  artifacts: ArtifactView[];
  simulation: SimulationView | null;
  failure: PrintJobFailureView | null;
  createdAtUtc: string;
  updatedAtUtc: string;
};

/** 계약상 stride. 서버도 같은 식으로 검사한다. */
export function strideBytesFor(widthDots: number): number {
  return Math.ceil(widthDots / 8);
}
