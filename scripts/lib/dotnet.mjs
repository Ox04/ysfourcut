// dotnet 실행 파일 찾기. PATH에 없어도 사용자 범위 설치 위치를 쓴다.
// 셸 rc 파일이나 전역 기본값은 건드리지 않는다 (docs/ENVIRONMENT.md).

import { existsSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';

export function findOnPath(binName) {
  const pathEnv = process.env.PATH ?? '';
  for (const dir of pathEnv.split(path.delimiter)) {
    if (!dir) continue;
    const candidate = path.join(dir, binName);
    if (existsSync(candidate)) return candidate;
  }
  return null;
}

export function resolveDotnetBin() {
  if (process.env.DOTNET_BIN && existsSync(process.env.DOTNET_BIN)) {
    return process.env.DOTNET_BIN;
  }

  const onPath = findOnPath('dotnet');
  if (onPath) return onPath;

  // 공식 사용자 범위 설치 스크립트(dotnet-install.sh)의 기본 위치.
  const userScope = path.join(os.homedir(), '.dotnet', 'dotnet');
  if (existsSync(userScope)) return userScope;

  return null;
}

export const DOTNET_NOT_FOUND_MESSAGE =
  'dotnet 실행 파일을 찾지 못했습니다. PATH에 추가하거나 DOTNET_BIN 환경 변수로 지정하세요. (docs/ENVIRONMENT.md 참고)';
