#!/usr/bin/env node
// Windows 배포 폴더 생성 (13번, WSL_DEVELOPMENT.md 6절 계약).
// 1) 웹을 빌드해 apps/print-host/wwwroot에 넣는다.
// 2) .NET 호스트를 win-x64 self-contained로 dist/win-x64에 publish한다.
// Native AOT/ReadyToRun은 추가하지 않는다. 이 스크립트는 WSL에서 패키지를 "만드는" 것까지만
// 하고, Windows에서의 실행은 검증하지 않는다.

import { spawnSync } from 'node:child_process';
import { cpSync, existsSync, mkdirSync, rmSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { DOTNET_NOT_FOUND_MESSAGE, resolveDotnetBin } from './lib/dotnet.mjs';

const rootDir = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const webDir = path.join(rootDir, 'apps', 'web');
const webDistDir = path.join(webDir, 'dist');
const hostProject = path.join(rootDir, 'apps', 'print-host', 'YsFourcut.Host.csproj');
const hostWwwroot = path.join(rootDir, 'apps', 'print-host', 'wwwroot');
const publishOutDir = path.join(rootDir, 'dist', 'win-x64');

const dotnetBin = resolveDotnetBin();
if (!dotnetBin) {
  console.error(`[package:win] ${DOTNET_NOT_FOUND_MESSAGE}`);
  process.exit(1);
}

function run(name, command, args, cwd) {
  console.log(`[package:win] ${name}: ${command} ${args.join(' ')}`);
  const result = spawnSync(command, args, {
    cwd,
    env: { ...process.env, DOTNET_ROOT: process.env.DOTNET_ROOT ?? path.dirname(dotnetBin) },
    stdio: 'inherit',
  });
  if (result.error) {
    console.error(`[package:win] ${name} 실행 실패: ${result.error.message}`);
    process.exit(1);
  }
  if (result.status !== 0) {
    console.error(`[package:win] ${name} 종료 코드 ${result.status}`);
    process.exit(result.status ?? 1);
  }
}

// 1) 웹 빌드 (typecheck 포함, apps/web/package.json의 build 스크립트).
const viteBin = path.join(rootDir, 'node_modules', '.bin', 'vite');
if (!existsSync(viteBin)) {
  console.error('[package:win] vite 실행 파일을 찾지 못했습니다. 먼저 `npm ci`를 실행하세요.');
  process.exit(1);
}
const tscBin = path.join(rootDir, 'node_modules', '.bin', 'tsc');
run('web typecheck', tscBin, ['--noEmit'], webDir);
run('web build', viteBin, ['build'], webDir);

if (!existsSync(webDistDir)) {
  console.error(`[package:win] 웹 빌드 결과를 찾지 못했습니다: ${webDistDir}`);
  process.exit(1);
}

// 2) 웹 빌드 결과를 print-host의 wwwroot로 복사한다 (Web SDK가 publish 시 자동 포함).
rmSync(hostWwwroot, { recursive: true, force: true });
mkdirSync(hostWwwroot, { recursive: true });
cpSync(webDistDir, hostWwwroot, { recursive: true });
console.log(`[package:win] 웹 빌드 결과 복사: ${webDistDir} -> ${hostWwwroot}`);

// 3) win-x64 self-contained publish. 출력은 dist/win-x64로만 제한한다.
rmSync(publishOutDir, { recursive: true, force: true });
run(
  '.NET win-x64 publish',
  dotnetBin,
  [
    'publish',
    hostProject,
    '-c',
    'Release',
    '-r',
    'win-x64',
    '--self-contained',
    'true',
    '-p:PublishAot=false',
    '-p:PublishReadyToRun=false',
    '-o',
    publishOutDir,
  ],
  rootDir,
);

console.log(`[package:win] 완료: ${publishOutDir}`);
