# 인쇄 원본용 한글 폰트

영수증 이미지에 들어가는 한글·날짜·문구는 이 로컬 폰트로 그린다. 프린터의 한글 코드페이지를
전제로 삼지 않고, 폰트 로딩이 끝난 뒤에만 최종 비트맵을 확정한다(`src/compose/fonts.ts`).

| 항목 | 값 |
| --- | --- |
| 파일 | `NotoSansCJKkr-Bold-hangul-subset.otf` (1.4MB) |
| 원본 | Noto Sans CJK KR Bold (`NotoSansCJK-Bold.ttc`의 face 1) |
| 라이선스 | SIL Open Font License 1.1 — `LICENSE-OFL-1.1.txt` (Reserved Font Name 없음) |
| 포함 문자 | 한글 음절 11,172자 + 호환 자모 + ASCII + 자주 쓰는 기호 |

굵은 한 가지 두께만 담는다. 1비트 감열 출력에서 얇은 획이 디더링 없이 끊기지 않게 하기 위해서다.

## 다시 만드는 방법

원본 폰트가 설치된 Linux에서 fontTools로 부분집합을 만든다(배포판·전역 도구를 바꾸지 않는다).

```bash
python3 - <<'PY'
syll = ''.join(chr(c) for c in range(0xAC00, 0xD7A4))
jamo = ''.join(chr(c) for c in range(0x3131, 0x3164))
ascii_ = ''.join(chr(c) for c in range(0x20, 0x7F))
extra = '·—…‘’“”₩°※→←↑↓♥★☆'
open('subsetchars.txt', 'w', encoding='utf-8').write(syll + jamo + ascii_ + extra)
PY

python3 -m fontTools.subset /usr/share/fonts/google-noto-sans-cjk-fonts/NotoSansCJK-Bold.ttc \
  --font-number=1 --text-file=subsetchars.txt \
  --output-file=NotoSansCJKkr-Bold-hangul-subset.otf \
  --layout-features='' --no-hinting --desubroutinize \
  --name-IDs='0,1,2,3,4,5,6,13,14'
```

부분집합에 없는 문자는 브라우저 대체 폰트로 그려진다. 그 경우에도 합성은 폰트 로딩이 끝난 뒤에
한 번만 확정하며, 미리보기와 인쇄 원본은 같은 비트맵을 쓴다.
