// 카메라 오류를 한국어 안내와 복구 동작으로 옮긴다. 순수 함수라 그대로 시험한다.
// 사진·장치 식별자를 문구에 넣지 않는다.

export type CameraErrorCode =
  | 'permission-denied'
  | 'no-device'
  | 'device-busy'
  | 'constraints'
  | 'insecure-context'
  | 'unsupported'
  | 'unknown';

export type CameraErrorInfo = {
  code: CameraErrorCode;
  /** 무엇이 일어났는지 */
  message: string;
  /** 사용자가 할 수 있는 일 */
  recovery: string;
  /** 다시 시도 버튼을 보여 줄지 */
  canRetry: boolean;
};

const INFO: Record<CameraErrorCode, Omit<CameraErrorInfo, 'code'>> = {
  'permission-denied': {
    message: '카메라 사용이 허용되지 않았어요.',
    recovery: '브라우저 주소창의 카메라 아이콘에서 허용으로 바꾼 뒤 다시 시도해 주세요.',
    canRetry: true,
  },
  'no-device': {
    message: '연결된 카메라를 찾지 못했어요.',
    recovery: '웹캠을 연결한 뒤 다시 시도해 주세요.',
    canRetry: true,
  },
  'device-busy': {
    message: '다른 프로그램이 카메라를 쓰고 있어요.',
    recovery: '화상회의 등 카메라를 쓰는 프로그램을 닫고 다시 시도해 주세요.',
    canRetry: true,
  },
  constraints: {
    message: '이 카메라가 요청한 해상도를 지원하지 않아요.',
    recovery: '다른 카메라를 선택하거나 다시 시도해 주세요.',
    canRetry: true,
  },
  'insecure-context': {
    message: '안전하지 않은 주소에서는 카메라를 쓸 수 없어요.',
    recovery: '127.0.0.1 주소로 접속해 주세요.',
    canRetry: false,
  },
  unsupported: {
    message: '이 브라우저에서는 카메라를 쓸 수 없어요.',
    recovery: '최신 Chrome이나 Edge에서 열어 주세요.',
    canRetry: false,
  },
  unknown: {
    message: '카메라를 시작하지 못했어요.',
    recovery: '잠시 뒤 다시 시도해 주세요.',
    canRetry: true,
  },
};

export function cameraErrorInfo(code: CameraErrorCode): CameraErrorInfo {
  return { code, ...INFO[code] };
}

/** getUserMedia가 던진 오류를 안내로 옮긴다. 원본 메시지는 화면에 그대로 쓰지 않는다. */
export function describeCameraError(error: unknown): CameraErrorInfo {
  const name =
    typeof error === 'object' && error !== null && 'name' in error
      ? String((error as { name: unknown }).name)
      : '';

  switch (name) {
    case 'NotAllowedError':
    case 'PermissionDeniedError':
    case 'SecurityError':
      return cameraErrorInfo('permission-denied');
    case 'NotFoundError':
    case 'DevicesNotFoundError':
      return cameraErrorInfo('no-device');
    case 'NotReadableError':
    case 'TrackStartError':
      return cameraErrorInfo('device-busy');
    case 'OverconstrainedError':
    case 'ConstraintNotSatisfiedError':
      return cameraErrorInfo('constraints');
    default:
      return cameraErrorInfo('unknown');
  }
}
