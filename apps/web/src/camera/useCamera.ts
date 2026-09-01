import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { cameraErrorInfo, describeCameraError, type CameraErrorInfo } from './errors';

// 카메라 스트림 수명 관리. Windows 브라우저의 getUserMedia를 그대로 쓰며
// WSL로 USB를 넘기지 않는다. 화면을 떠나거나 장치를 바꾸면 track을 반드시 멈춘다.

export type CameraDevice = { deviceId: string; label: string };

export type CameraStatus = 'idle' | 'starting' | 'ready' | 'error';

export type CameraState = {
  status: CameraStatus;
  stream: MediaStream | null;
  error: CameraErrorInfo | null;
  devices: CameraDevice[];
  activeDeviceId: string | null;
};

const IDEAL_CONSTRAINTS: MediaTrackConstraints = {
  width: { ideal: 1280 },
  height: { ideal: 960 },
};

function supportsCamera(): boolean {
  return typeof navigator !== 'undefined' && !!navigator.mediaDevices?.getUserMedia;
}

export function useCamera() {
  const [state, setState] = useState<CameraState>({
    status: 'idle',
    stream: null,
    error: null,
    devices: [],
    activeDeviceId: null,
  });

  // 최신 스트림을 정리 시점에 알기 위해 ref로도 들고 있는다.
  const streamRef = useRef<MediaStream | null>(null);
  const startTokenRef = useRef(0);

  const stopStream = useCallback(() => {
    streamRef.current?.getTracks().forEach((track) => track.stop());
    streamRef.current = null;
  }, []);

  const refreshDevices = useCallback(async () => {
    if (!supportsCamera()) return;
    try {
      const list = await navigator.mediaDevices.enumerateDevices();
      const devices = list
        .filter((device) => device.kind === 'videoinput')
        .map((device, index) => ({
          deviceId: device.deviceId,
          // 권한 전에는 label이 비어 있다.
          label: device.label || `카메라 ${index + 1}`,
        }));
      setState((current) => ({ ...current, devices }));
    } catch {
      // 목록 조회 실패는 촬영을 막지 않는다.
    }
  }, []);

  const start = useCallback(
    async (deviceId?: string) => {
      if (!supportsCamera()) {
        setState((current) => ({
          ...current,
          status: 'error',
          error: cameraErrorInfo(
            typeof window !== 'undefined' && !window.isSecureContext
              ? 'insecure-context'
              : 'unsupported',
          ),
        }));
        return;
      }

      const token = startTokenRef.current + 1;
      startTokenRef.current = token;

      // 새로 시작하기 전에 이전 track을 반드시 멈춘다.
      stopStream();
      setState((current) => ({ ...current, status: 'starting', error: null, stream: null }));

      try {
        const stream = await navigator.mediaDevices.getUserMedia({
          video: deviceId ? { ...IDEAL_CONSTRAINTS, deviceId: { exact: deviceId } } : IDEAL_CONSTRAINTS,
          audio: false,
        });

        // 그 사이 다른 시작·정리가 있었다면 이 스트림은 쓰지 않는다.
        if (startTokenRef.current !== token) {
          stream.getTracks().forEach((track) => track.stop());
          return;
        }

        streamRef.current = stream;
        const activeDeviceId = stream.getVideoTracks()[0]?.getSettings().deviceId ?? deviceId ?? null;
        setState((current) => ({ ...current, status: 'ready', stream, error: null, activeDeviceId }));
        void refreshDevices();
      } catch (error) {
        if (startTokenRef.current !== token) return;
        setState((current) => ({
          ...current,
          status: 'error',
          stream: null,
          error: describeCameraError(error),
        }));
      }
    },
    [refreshDevices, stopStream],
  );

  const stop = useCallback(() => {
    startTokenRef.current += 1;
    stopStream();
    setState((current) => ({ ...current, status: 'idle', stream: null }));
  }, [stopStream]);

  // 장치가 뽑히면 track이 끝난다. 사용자에게 알리고 스트림을 정리한다.
  useEffect(() => {
    const stream = state.stream;
    if (!stream) return;

    const track = stream.getVideoTracks()[0];
    if (!track) return;

    const onEnded = () => {
      stopStream();
      setState((current) => ({
        ...current,
        status: 'error',
        stream: null,
        error: cameraErrorInfo('no-device'),
      }));
    };

    track.addEventListener('ended', onEnded);
    return () => track.removeEventListener('ended', onEnded);
  }, [state.stream, stopStream]);

  // 장치 목록 변경 감시.
  useEffect(() => {
    if (!supportsCamera()) return;
    const onChange = () => void refreshDevices();
    navigator.mediaDevices.addEventListener('devicechange', onChange);
    return () => navigator.mediaDevices.removeEventListener('devicechange', onChange);
  }, [refreshDevices]);

  // 화면을 떠날 때 track 정리. 아직 대기 중인 getUserMedia 결과도 토큰으로 무효화한다.
  useEffect(
    () => () => {
      startTokenRef.current += 1;
      stopStream();
    },
    [stopStream],
  );

  return useMemo(
    () => ({ ...state, start, stop, refreshDevices }),
    [state, start, stop, refreshDevices],
  );
}
