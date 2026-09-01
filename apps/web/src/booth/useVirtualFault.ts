// 주입된 모의 오류를 실제 API에서 읽어 부스 화면에 상시 표시한다.
// 실제 장치 상태가 아니라 가상 모드의 모의 오류이며, 값은 서버가 유일한 출처다.

import { useCallback, useEffect, useMemo, useState } from 'react';
import { api } from '../api/client';
import type { VirtualFaultView } from '../api/contract';

export function useVirtualFault(reloadKey: number) {
  const [fault, setFault] = useState<VirtualFaultView | null>(null);
  const [tick, setTick] = useState(0);

  /** 작업이 끝나면 주입이 자동 해제되므로 다시 읽는다. */
  const refresh = useCallback(() => setTick((value) => value + 1), []);

  useEffect(() => {
    let active = true;

    void (async () => {
      try {
        const response = await api.virtualFault();
        if (active) setFault(response.fault);
      } catch {
        // 실물 모드(409)나 미연결이면 표시할 주입이 없다.
        if (active) setFault(null);
      }
    })();

    return () => {
      active = false;
    };
  }, [reloadKey, tick]);

  return useMemo(() => ({ fault, refresh }), [fault, refresh]);
}
