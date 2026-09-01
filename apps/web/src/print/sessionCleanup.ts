// 다음 사용자 세션을 시작해도 되는지 판단하는 순수 규칙.
// SERVICE_DESIGN 10절 · VIRTUAL_PRINTER_DESIGN 9절 · docs/API_CONTRACT.md "실패·정리·재시작 규칙":
// 정리 응답의 remainingJobs가 0이거나 hostInstanceId가 바뀐(=캐시가 빈 새 호스트) 경우에만 넘어간다.
// 잠금이 풀렸다는 이유만으로 삭제 실패를 무시하지 않는다.

export type CleanupInput = {
  /** POST /api/session/artifacts/clear 응답. 실패했으면 null */
  clear: { clearedJobs: number; remainingJobs: number } | null;
  /** 정리 호출이 실패했을 때의 한국어 안내 */
  clearErrorMessage: string | null;
  /** 이번 촬영 세션을 시작할 때 본 hostInstanceId */
  sessionHostInstanceId: string | null;
  /** 정리 직후 다시 읽은 hostInstanceId. 못 읽었으면 null */
  currentHostInstanceId: string | null;
};

export type CleanupDecision = {
  /** 다음 사용자를 시작해도 되는지 */
  canStartNext: boolean;
  reason: 'cleared' | 'host-restarted' | 'jobs-remain' | 'clear-failed';
  /** 화면에 그대로 쓰는 한국어 안내 */
  message: string;
};

export function decideCleanup(input: CleanupInput): CleanupDecision {
  if (input.clear && input.clear.remainingJobs === 0) {
    return {
      canStartNext: true,
      reason: 'cleared',
      message:
        input.clear.clearedJobs > 0
          ? `서버 결과 ${input.clear.clearedJobs}건을 정리했어요.`
          : '정리할 서버 결과가 없었어요.',
    };
  }

  const restarted =
    input.currentHostInstanceId !== null &&
    input.sessionHostInstanceId !== null &&
    input.currentHostInstanceId !== input.sessionHostInstanceId;

  if (restarted) {
    return {
      canStartNext: true,
      reason: 'host-restarted',
      message: '로컬 프로그램이 다시 시작되어 이전 결과가 모두 사라졌어요.',
    };
  }

  if (input.clear) {
    return {
      canStartNext: false,
      reason: 'jobs-remain',
      message: `서버에 결과 ${input.clear.remainingJobs}건이 남아 있어요. 화면 사진은 지웠지만 정리가 끝나야 다음 사용자를 시작할 수 있어요.`,
    };
  }

  return {
    canStartNext: false,
    reason: 'clear-failed',
    message: `${input.clearErrorMessage ?? '서버 결과를 정리하지 못했어요.'} 화면 사진은 지웠지만 서버 결과가 남아 있을 수 있어요. 정리가 끝나야 다음 사용자를 시작할 수 있어요.`,
  };
}
