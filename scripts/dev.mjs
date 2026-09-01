#!/usr/bin/env node
// WSL 개발 실행기.
// Vite(5173)와 실제 Linux .NET 가상 호스트(4317)를 함께 띄운다.
// 이 스크립트가 시작한 두 자식 프로세스만 종료 시 정리하고, 다른 서버는 건드리지 않는다.

import { spawn } from 'node:child_process';
import { existsSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { DOTNET_NOT_FOUND_MESSAGE, resolveDotnetBin } from './lib/dotnet.mjs';

const rootDir = path.dirname(path.dirname(fileURLToPath(import.meta.url)));

const dotnetBin = resolveDotnetBin();
if (!dotnetBin) {
  console.error(`[dev] ${DOTNET_NOT_FOUND_MESSAGE}`);
  process.exit(1);
}

const viteBin = path.join(rootDir, 'node_modules', '.bin', 'vite');
if (!existsSync(viteBin)) {
  console.error('[dev] vite 실행 파일을 찾지 못했습니다. 먼저 `npm ci`를 실행하세요.');
  process.exit(1);
}

const dotnetRoot = process.env.DOTNET_ROOT ?? path.dirname(dotnetBin);

const children = [];
let shuttingDown = false;

function spawnChild(name, command, args, cwd, extraEnv) {
  const child = spawn(command, args, {
    cwd,
    env: { ...process.env, ...extraEnv },
    stdio: 'inherit',
  });
  children.push({ name, child });

  child.on('exit', (code, signal) => {
    if (shuttingDown) return;
    console.error(
      `[dev] ${name} 프로세스가 종료됨 (code=${code}, signal=${signal}). 나머지 프로세스를 정리합니다.`,
    );
    shutdown(code ?? 1);
  });

  child.on('error', (error) => {
    console.error(`[dev] ${name} 실행 실패:`, error.message);
    shutdown(1);
  });

  return child;
}

function shutdown(exitCode) {
  if (shuttingDown) return;
  shuttingDown = true;
  for (const { child } of children) {
    if (child.exitCode === null && !child.killed) {
      try {
        child.kill('SIGTERM');
      } catch {
        // 이미 종료된 경우 무시한다.
      }
    }
  }
  setTimeout(() => process.exit(exitCode ?? 0), 300);
}

process.on('SIGINT', () => shutdown(0));
process.on('SIGTERM', () => shutdown(0));

console.log('[dev] .NET 가상 호스트 시작: http://127.0.0.1:4317');
spawnChild(
  'dotnet-host',
  dotnetBin,
  ['run', '--project', path.join('apps', 'print-host', 'YsFourcut.Host.csproj')],
  rootDir,
  { ASPNETCORE_ENVIRONMENT: 'Development', DOTNET_ROOT: dotnetRoot },
);

console.log('[dev] Vite 시작: http://127.0.0.1:5173');
spawnChild('vite', viteBin, [], path.join(rootDir, 'apps', 'web'), {});

// 이 주소 자체에는 코드가 없다. 호스트가 접속할 때 일회용 코드를 만들어 fragment로 전달한다.
console.log('[dev] 연결이 필요하면 화면의 "로컬 프로그램과 연결하기" 또는 http://127.0.0.1:4317/pair 를 여세요.');
