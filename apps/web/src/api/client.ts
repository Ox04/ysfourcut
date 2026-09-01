import {
  CLIENT_HEADER_NAME,
  CLIENT_HEADER_VALUE,
  SCHEMA_VERSION,
  type ApiErrorBody,
  type ApiErrorDetail,
  type ArtifactKind,
  type BootstrapResponse,
  type ClearArtifactsResponse,
  type HealthResponse,
  type PrintJobRequest,
  type PrintJobStatusResponse,
  type PrinterProfileResponse,
  type PrintersResponse,
  type UpdatePrinterProfileRequest,
  type VirtualFaultRequest,
  type VirtualFaultResponse,
} from './contract';

/** 호스트가 구조화된 오류를 돌려준 경우. 네트워크 실패와 구분한다. */
export class ApiError extends Error {
  readonly status: number;
  readonly detail: ApiErrorDetail;

  constructor(status: number, detail: ApiErrorDetail) {
    super(detail.message);
    this.name = 'ApiError';
    this.status = status;
    this.detail = detail;
  }

  get code(): string {
    return this.detail.code;
  }
}

/**
 * 상대 경로 `/api`만 사용한다. 개발에서는 Vite가 4317로 프록시하므로
 * 브라우저 입장에서는 항상 같은 출처이고 교차 출처 호출이 없다.
 */
async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    ...init,
    cache: 'no-store',
    credentials: 'same-origin',
    headers: {
      [CLIENT_HEADER_NAME]: CLIENT_HEADER_VALUE,
      ...(init?.body ? { 'Content-Type': 'application/json' } : {}),
      ...init?.headers,
    },
  });

  if (!response.ok) {
    throw new ApiError(response.status, await readErrorDetail(response));
  }

  return (await response.json()) as T;
}

async function readErrorDetail(response: Response): Promise<ApiErrorDetail> {
  try {
    const body = (await response.json()) as ApiErrorBody;
    if (body?.error?.code) {
      return body.error;
    }
  } catch {
    // 구조화된 본문이 아니면 아래 기본값을 쓴다.
  }

  return { code: 'INTERNAL_ERROR', message: `요청이 실패했습니다 (HTTP ${response.status}).` };
}

export const api = {
  health: () => request<HealthResponse>('/api/health'),

  bootstrap: (code: string) =>
    request<BootstrapResponse>('/api/bootstrap', {
      method: 'POST',
      body: JSON.stringify({ schemaVersion: SCHEMA_VERSION, code }),
    }),

  printers: () => request<PrintersResponse>('/api/printers'),

  printerProfile: () => request<PrinterProfileResponse>('/api/printer-profile'),

  savePrinterProfile: (body: UpdatePrinterProfileRequest) =>
    request<PrinterProfileResponse>('/api/printer-profile', {
      method: 'PUT',
      body: JSON.stringify(body),
    }),

  submitPrintJob: (body: PrintJobRequest) =>
    request<PrintJobStatusResponse>('/api/print-jobs', {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  printJob: (clientJobId: string) =>
    request<PrintJobStatusResponse>(`/api/print-jobs/${encodeURIComponent(clientJobId)}`),

  virtualFault: () => request<VirtualFaultResponse>('/api/virtual-printer/fault'),

  setVirtualFault: (body: VirtualFaultRequest) =>
    request<VirtualFaultResponse>('/api/virtual-printer/fault', {
      method: 'PUT',
      body: JSON.stringify(body),
    }),

  clearSessionArtifacts: () =>
    request<ClearArtifactsResponse>('/api/session/artifacts/clear', {
      method: 'POST',
      body: JSON.stringify({ schemaVersion: SCHEMA_VERSION }),
    }),
};

/**
 * 결과 PNG는 인증 fetch로 받아 Blob으로 표시한다.
 * `<img src>`로 직접 연결하지 않으며 URL은 사용이 끝나면 해제한다.
 */
export async function fetchArtifactBlobUrl(clientJobId: string, kind: ArtifactKind): Promise<string> {
  const response = await fetch(
    `/api/print-jobs/${encodeURIComponent(clientJobId)}/artifacts/${encodeURIComponent(kind)}`,
    {
      cache: 'no-store',
      credentials: 'same-origin',
      headers: { [CLIENT_HEADER_NAME]: CLIENT_HEADER_VALUE },
    },
  );

  if (!response.ok) {
    throw new ApiError(response.status, await readErrorDetail(response));
  }

  return URL.createObjectURL(await response.blob());
}

/**
 * 호스트의 페어링 화면 주소. 이 주소 자체에는 코드가 없고,
 * 호스트가 일회용 코드를 URL fragment로 붙여 앱으로 되돌려 보낸다.
 */
export function pairingUrl(isDev: boolean): string {
  return isDev ? 'http://127.0.0.1:4317/pair' : '/pair';
}

/**
 * 주소의 `#bootstrap=` fragment에서 코드를 꺼내고 즉시 주소에서 지운다.
 * 코드는 화면·로그·히스토리에 남기지 않는다.
 */
export function takeBootstrapCodeFromLocation(location: Location, history: History): string | null {
  const hash = location.hash.startsWith('#') ? location.hash.slice(1) : location.hash;
  if (!hash) return null;

  const params = new URLSearchParams(hash);
  const code = params.get('bootstrap');
  if (!code) return null;

  params.delete('bootstrap');
  const remaining = params.toString();
  history.replaceState(null, '', `${location.pathname}${location.search}${remaining ? `#${remaining}` : ''}`);

  return code;
}
