// 최종 비트맵을 서버 계약 형식으로 바꾸는 한 지점.
// 미리보기·PNG 저장과 **같은** MonoBitmap을 쓴다. 다른 경로로 다시 만들지 않는다.

import { toBase64, type MonoBitmap } from '@ysfourcut/imaging';
import { BIT_ORDER, BLACK_BIT, type PrintJobBitmap } from '../api/contract';

export function toPrintJobBitmap(bitmap: MonoBitmap): PrintJobBitmap {
  return {
    widthDots: bitmap.widthDots,
    heightDots: bitmap.heightDots,
    strideBytes: bitmap.strideBytes,
    bitOrder: BIT_ORDER,
    blackBit: BLACK_BIT,
    dataBase64: toBase64(bitmap.data),
  };
}
