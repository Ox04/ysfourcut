import { useEffect, type RefObject } from 'react';
import { Button } from '../ui/Button';
import type { CameraErrorInfo } from './errors';

// 미리보기 <video>. 좌우 반전과 cover 크롭을 캡처와 같은 규칙으로 적용한다
// (반전은 CSS transform, 크롭은 object-fit: cover — useCaptureSession이 같은 계산을 쓴다).

export function CameraPreview({
  videoRef,
  stream,
  status,
  error,
  mirrored,
  onRetry,
}: {
  videoRef: RefObject<HTMLVideoElement | null>;
  stream: MediaStream | null;
  status: 'idle' | 'starting' | 'ready' | 'error';
  error: CameraErrorInfo | null;
  mirrored: boolean;
  onRetry: () => void;
}) {
  useEffect(() => {
    const video = videoRef.current;
    if (!video) return;

    video.srcObject = stream;
    if (stream) {
      // 자동재생 정책상 실패할 수 있으므로 조용히 무시한다(사용자 동작 뒤 다시 시도된다).
      void video.play().catch(() => undefined);
    }

    return () => {
      video.srcObject = null;
    };
  }, [stream, videoRef]);

  if (status === 'error' && error) {
    return (
      <div className="camera-error" role="alert">
        <p className="camera-error__message">{error.message}</p>
        <p className="camera-error__recovery">{error.recovery}</p>
        {error.canRetry && (
          <Button variant="secondary" onClick={onRetry}>
            다시 시도
          </Button>
        )}
      </div>
    );
  }

  return (
    <>
      <video
        ref={videoRef}
        className={`camera-video${mirrored ? ' camera-video--mirrored' : ''}`}
        playsInline
        muted
        aria-label="카메라 미리보기 영상"
      />
      {status !== 'ready' && (
        <p className="camera-video__waiting" role="status">
          {status === 'starting' ? '카메라를 준비하는 중…' : '카메라 준비 전이에요.'}
        </p>
      )}
    </>
  );
}
