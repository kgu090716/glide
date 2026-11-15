import { cp, mkdir, readFile, writeFile } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const docs = dirname(fileURLToPath(import.meta.url));
const intro = {
  '프로그램 이름': 'App name',
  '기능 설명': 'Features',
  '프로그램 소개': 'Introduction',
  '세 손가락으로': 'Three fingers.',
  'Windows 창을 움직이세요.': 'Move your windows.',
  '창 이동 · 크기 조절 · 화면 배치': 'Drag · Resize · Snap',
  '트랙패드의 세 접점이 움직이자 화면 속 창과 커서가 따라 움직임':
    'A window and cursor follow three points moving on a touchpad',
};

const guide = {
  '제스처 가이드': 'Gesture guide',
  '트랙패드': 'Touchpad',
  '화면': 'Screen',
  '메모장': 'Notepad',
  '세 손가락 준비': 'Three fingers ready',
  '창의 위치와 크기': 'Window position and size',
  '창 이동': 'Move a window',
  '세 손가락을 나란히 움직이면 창이 따라옵니다.': 'Slide three fingers together to move the window.',
  '창 확대': 'Enlarge a window',
  '세 손가락을 펼치면 창의 가로와 세로가 커집니다.': 'Spread three fingers to make the window wider and taller.',
  '창 축소': 'Shrink a window',
  '세 손가락을 오므리면 창의 가로와 세로가 작아집니다.': 'Pinch three fingers to make the window smaller.',
  '방향별 크기 조절': 'Resize from the edges',
  '세 손가락을 올려둔 뒤 밀어 테두리를 조절합니다. 대기 시간은 바꿀 수 있습니다.':
    'Hold three fingers still, then slide to resize. The hold time is adjustable.',
  '위쪽 끝에서 최대화': 'Maximize at the top edge',
  '창을 화면 위쪽 끝으로 끌고 손을 떼면 최대화됩니다.': 'Drag the window to the top edge and lift your fingers.',
  '좌우 반쪽 배치': 'Snap to either side',
  '화면 끝에 놓으면 절반에 맞고, 다시 끌면 원래 크기로 돌아옵니다.':
    'Release at a side edge to snap. Drag away to restore the original size.',
  '빠르게 밀어서 창 배치': 'Snap with a quick flick',
  '0.35초 안에 밀고 떼세요. 위는 최대화, 좌우는 반쪽 배치입니다.':
    'Flick and lift within 0.35 seconds. Flick up to maximize, or sideways to snap.',
  '손가락 뗌': 'Fingers lifted',
  '세 손가락 나란히 이동': 'Slide three fingers',
  '창 본문에 커서 두기': 'Point inside the window',
  '창과 커서가 함께 이동': 'Window and cursor move together',
  '놓은 위치에서 멈춤': 'Window stays where you release it',
  '세 손가락 사이 벌리기': 'Spread three fingers',
  '세 손가락 사이 오므리기': 'Pinch three fingers',
  '가운데 위치를 기준으로': 'Resize from the center',
  '가로와 세로가 함께 늘어남': 'Width and height increase',
  '가로와 세로가 함께 줄어듦': 'Width and height decrease',
  '가만히 올려두기 · 1초 예시': 'Hold still (1 second)',
  '오른쪽 아래로 밀기': 'Slide down and right',
  '대기 중에는 창이 그대로': 'Window stays still during the hold',
  '오른쪽·아래 테두리로 크기 조절': 'Bottom and right edges move',
  '조절한 크기로 유지': 'Window keeps the new size',
  '위쪽 끝까지 끌기': 'Drag to the top edge',
  '창을 화면 위로 이동': 'Window moves toward the top',
  '위쪽 끝에서 놓기': 'Release at the top edge',
  '손을 뗀 뒤 최대화': 'Window maximizes on release',
  '오른쪽 끝까지 끌기': 'Drag to the right edge',
  '왼쪽 끝까지 끌기': 'Drag to the left edge',
  '세 손가락으로 다시 끌기': 'Drag away with three fingers',
  '화면 가장자리에 닿기': 'Window reaches the screen edge',
  '오른쪽 절반에 배치': 'Snaps to the right half',
  '왼쪽 절반에 배치': 'Snaps to the left half',
  '배치 전의 창 크기로 복원': 'Window returns to its original size',
  '위로 밀기': 'Flick up',
  '왼쪽으로 밀기': 'Flick left',
  '오른쪽으로 밀기': 'Flick right',
  '손을 떼면 최대화': 'Maximizes on release',
  '손을 떼면 왼쪽 절반': 'Snaps left on release',
  '손을 떼면 오른쪽 절반': 'Snaps right on release',
  '밀고 떼기 · 0.28초 예시': 'Flick and lift (0.28 s)',
  '화면 가운데에서 시작': 'Start away from the screen edges',
};

function localize(source, phrases) {
  const keys = Object.keys(phrases).sort((a, b) => b.length - a.length);
  for (const phrase of keys) source = source.replaceAll(phrase, phrases[phrase]);
  return source
    .replace('<html lang="ko"', '<html lang="en"')
    .replace(/@font-face\s*{[^}]*"Glide Korean"[^}]*}\s*/g, '')
    .replace(/"Glide Korean",\s*(?:Inter,\s*)?/g, 'Inter, ');
}

async function prepare(name, phrases, sources, assets) {
  const project = join(docs, name);
  const englishName = name + '-en';
  const output = join(docs, 'glide-gestures', '.hyperframes', 'english', englishName);
  await mkdir(output, { recursive: true });
  for (const file of sources) {
    const original = await readFile(join(project, file), 'utf8');
    const translated = localize(original, phrases).replaceAll(name, englishName);
    const untranslated = translated.match(/[\p{Script=Hangul}]+/gu);
    if (untranslated) throw new Error('Untranslated text in ' + file + ': ' + untranslated.join(', '));
    await writeFile(join(output, file), translated);
  }
  for (const file of assets) await cp(join(project, file), join(output, file), { recursive: true });
  console.log(output);
}

await prepare('glide-intro', intro, ['index.html', 'meta.json'], ['hyperframes.json', 'assets']);
await prepare('glide-gestures', guide, ['index.html', 'guide.js', 'guide.css', 'meta.json'],
  ['hyperframes.json', 'index.motion.json', 'assets']);
