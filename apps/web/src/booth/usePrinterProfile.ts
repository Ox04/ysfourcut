// 합성에 필요한 **실제** 인쇄 가능 폭과 상한을 서버 프로필에서 읽는다.
// 80/58mm 같은 이름으로 dot를 추측하지 않는다.
// reloadKey가 바뀌면(장치 패널에서 80/58mm를 바꾸면) 서버에서 다시 읽어 합성·접수에 반영한다.

import { useEffect, useMemo, useState } from 'react';
import { ApiError, api } from '../api/client';
import { describeError, type PrinterMode } from '../api/contract';

export type PrinterPaper = {
  profileId: string;
  revision: string;
  printerMode: PrinterMode;
  displayName: string;
  /** 프로필이 알려 준 인쇄 가능 폭(dot) */
  contentWidthDots: number;
  limits: { maxHeightDots: number; maxDecodedBytes: number };
};

export function usePrinterProfile(reloadKey = 0) {
  const [paper, setPaper] = useState<PrinterPaper | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setLoading] = useState(true);

  useEffect(() => {
    let active = true;

    void (async () => {
      try {
        const response = await api.printerProfile();
        if (!active) return;

        setPaper({
          profileId: response.profile.profileId,
          revision: response.profile.revision,
          printerMode: response.printerMode,
          displayName: response.profile.displayName,
          contentWidthDots: response.profile.paper.contentWidthDots,
          limits: {
            maxHeightDots: Math.min(
              response.profile.limits.maxContentHeightDots,
              response.serviceLimits.maxHeightDots,
            ),
            maxDecodedBytes: Math.min(
              response.profile.limits.maxDecodedBytes,
              response.serviceLimits.maxDecodedBytes,
            ),
          },
        });
        setError(null);
      } catch (cause) {
        if (!active) return;
        setPaper(null);
        setError(
          cause instanceof ApiError
            ? describeError(cause.detail)
            : '로컬 프로그램에 연결하지 못했어요.',
        );
      } finally {
        if (active) setLoading(false);
      }
    })();

    return () => {
      active = false;
    };
  }, [reloadKey]);

  return useMemo(() => ({ paper, error, isLoading }), [paper, error, isLoading]);
}
