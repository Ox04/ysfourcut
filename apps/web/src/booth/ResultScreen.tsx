import { Button } from '../ui/Button';
import { StatusBadge } from '../ui/parts';
import { ReceiptViewer } from './ReceiptViewer';
import type { ResultView } from './types';

/**
 * 결과 화면. 서버가 만든 PNG가 도착하기 전에는 어떤 성공 표현도 하지 않는다.
 * 상태 문구는 SERVICE_DESIGN 9절의 상태표를 따른다.
 */
export function ResultScreen({
  result,
  busy = false,
  notice,
  onSave,
  onReprint,
  onNextSession,
}: {
  result: ResultView;
  /** 저장·재출력·정리 요청이 진행 중일 때 */
  busy?: boolean;
  /** 중복 출력 안내·정리 결과 등 한 줄 알림 */
  notice?: string | null;
  /** 사용자가 눌렀을 때만 PNG를 파일로 저장한다 */
  onSave: () => void;
  /** 중복 안내 후 새 작업 번호로 접수하는 명시적 재출력 */
  onReprint: () => void;
  onNextSession: () => void;
}) {
  const { state } = result;
  const inProgress = state === 'accepted' || state === 'rendering';
  const rendered = state === 'rendered';
  const failed = state === 'virtual_failed';

  return (
    <div className="result">
      {inProgress && (
        <div className="result__progress" role="status">
          <div className="result__spinner" aria-hidden="true" />
          <h2 className="result__progress-title">
            {state === 'accepted' ? '출력 준비 중…' : '가상 영수증을 만드는 중…'}
          </h2>
          <p className="result__progress-hint">서버가 영수증을 다 만들면 여기에 보여 드려요.</p>
        </div>
      )}

      {rendered && (
        <>
          <div className="result__done" role="status">
            <StatusBadge tone="virtual">가상 영수증을 만들었어요</StatusBadge>
            <p className="result__done-hint">실제 종이가 아니라 서버가 만든 가상 결과예요.</p>
          </div>
          <ReceiptViewer images={result.images} layout={result.layout} animateEject />
        </>
      )}

      {failed && (
        <div className="result__failed" role="alert">
          <h2>가상 출력에 실패했어요</h2>
          <p>{result.failureMessage ?? '다시 시도해 주세요.'}</p>
          {result.isPartial && result.images.content && (
            <details className="result__partial">
              <summary>실패 지점까지의 진단 이미지 보기 (완성본 아님)</summary>
              <img src={result.images.content} alt="실패 지점까지 그려진 진단용 부분 이미지" />
            </details>
          )}
        </div>
      )}

      <div className="result__actions">
        {rendered && (
          <>
            <Button variant="secondary" disabled={busy} onClick={onSave}>
              PNG 저장
            </Button>
            <Button variant="secondary" disabled={busy} onClick={onReprint}>
              다시 출력
            </Button>
          </>
        )}
        {failed && (
          <Button variant="primary" disabled={busy} onClick={onReprint}>
            다시 시도
          </Button>
        )}
        <Button
          variant={rendered ? 'primary' : 'quiet'}
          size={rendered ? 'lg' : 'md'}
          disabled={busy || inProgress}
          onClick={onNextSession}
        >
          다음 촬영
        </Button>
      </div>
      {notice && (
        <p className="result__lock-hint" role="status">
          {notice}
        </p>
      )}
      {inProgress && (
        <p className="result__lock-hint" role="status">
          출력이 끝나기 전에는 다음 촬영을 시작할 수 없어요.
        </p>
      )}
    </div>
  );
}
