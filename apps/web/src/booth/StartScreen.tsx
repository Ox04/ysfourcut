import { Button } from '../ui/Button';
import { StatusBadge } from '../ui/parts';
import type { DeviceReadiness } from './types';

/**
 * 시작 화면. 큰 촬영 시작 버튼과 준비 상태만 보여 준다. 긴 소개 문구를 두지 않는다.
 */
export function StartScreen({
  device,
  onStart,
}: {
  device: DeviceReadiness;
  onStart: () => void;
}) {
  return (
    <div className="start">
      <div className="start__ticket">
        <p className="start__eyebrow">인생 네컷을 영수증으로</p>
        <h1 className="start__title">네컷 찍고,
          <br />
          영수증으로 뽑아요
        </h1>
        <p className="start__hint">4컷 · 컷마다 3초 카운트다운 · 약 1분</p>

        <Button variant="primary" size="lg" className="start__cta" onClick={onStart}>
          촬영 시작
        </Button>

        <ul className="start__devices">
          <li>
            {device.cameraReady ? (
              <StatusBadge tone="ok">카메라 준비됨</StatusBadge>
            ) : (
              <StatusBadge tone="warn">카메라 연결 안 됨</StatusBadge>
            )}
          </li>
          <li>
            {device.printReady ? (
              <StatusBadge tone={device.printerMode === 'virtual' ? 'virtual' : 'ok'}>
                {device.printerMode === 'virtual' ? '가상 영수증으로 출력돼요' : '영수증으로 출력돼요'}
              </StatusBadge>
            ) : (
              <StatusBadge tone="warn">
                {device.printBlockedReason ?? '지금은 출력할 수 없어요 (촬영·저장은 가능)'}
              </StatusBadge>
            )}
          </li>
        </ul>
      </div>
    </div>
  );
}
