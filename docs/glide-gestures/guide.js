(() => {
  const base = { x: 211, y: 84, w: 488, h: 266 };
  const desktop = { w: 924, h: 415 };
  const hand = [[-61, 24], [0, -35], [61, 24]];
  const clamp = (x) => Math.min(1, Math.max(0, x));
  const range = (t, a, b) => clamp((t - a) / (b - a));
  const smooth = (p) => p * p * (3 - 2 * p);
  const move = (t, a, b) => smooth(range(t, a, b));
  const mix = (a, b, p) => a + (b - a) * p;
  const shift = (r, x, y) => ({ ...r, x: r.x + x, y: r.y + y });
  const blend = (a, b, p) => ({ x: mix(a.x, b.x, p), y: mix(a.y, b.y, p), w: mix(a.w, b.w, p), h: mix(a.h, b.h, p) });
  const centerScale = (r, s) => ({
    x: r.x + (r.w - r.w * s) * .5, y: r.y + (r.h - r.h * s) * .5,
    w: r.w * s, h: r.h * s
  });
  const area = (side) => ({
    x: side === "right" ? desktop.w / 2 : 0, y: 0,
    w: side === "top" ? desktop.w : desktop.w / 2, h: desktop.h
  });
  const touch = (t, down, up) => t < down ? .32 : 1 - move(t, up, up + .18);
  const isTouch = (t, down, up) => t >= down && t < up;
  const make = (left, right, bounds = base) => ({
    bounds, left, right, dx: 0, dy: 0, span: 1, contact: .32,
    active: false, edge: false, zone: "", zoneOpacity: 0, progress: 0, pointer: null
  });
  const pointerAt = (bounds) => [bounds.x + bounds.w * .5, bounds.y + bounds.h * .5];
  const touchText = (t, down, up, motion) => t < down ? "세 손가락 준비" : t < up ? motion : "손가락 뗌";

  function drag(t) {
    const p = move(t, 1.02, 2.92);
    const state = make(touchText(t, .52, 3.15, "세 손가락 나란히 이동"),
      t < 1.02 ? "창 본문에 커서 두기" : t < 3.15 ? "창과 커서가 함께 이동" : "놓은 위치에서 멈춤",
      shift(base, 157 * p, 54 * p));
    return { ...state, dx: 133 * p, dy: 52 * p, contact: touch(t, .52, 3.15), active: isTouch(t, .52, 3.15) };
  }

  function pinch(t, expand) {
    const p = move(t, .91, 2.78);
    const sign = expand ? 1 : -1;
    const startSpan = expand ? .86 : 1.57;
    const original = expand ? base : centerScale(base, 1.23);
    const state = make(touchText(t, .44, 3.02, expand ? "세 손가락 사이 벌리기" : "세 손가락 사이 오므리기"),
      t < .91 ? "가운데 위치를 기준으로" : expand ? "가로와 세로가 함께 늘어남" : "가로와 세로가 함께 줄어듦",
      centerScale(original, 1 + sign * .25 * p));
    return { ...state, span: startSpan + sign * .70 * p, contact: touch(t, .44, 3.02), active: isTouch(t, .44, 3.02) };
  }

  function hold(t) {
    const down = .62;
    const ready = down + 1;
    const p = move(t, ready + .23, 3.65);
    const state = make(
      touchText(t, down, 3.90, t < ready ? "가만히 올려두기 · 1초 예시" : "오른쪽 아래로 밀기"),
      t < ready ? "대기 중에는 창이 그대로" : t < 3.90 ? "오른쪽·아래 테두리로 크기 조절" : "조절한 크기로 유지",
      { ...base, w: base.w + 142 * p, h: base.h + 60 * p });
    return { ...state, dx: 114 * p, dy: 55 * p, contact: touch(t, down, 3.90),
      active: isTouch(t, down, 3.90), edge: t >= ready && t < 3.90, progress: range(t, down, ready) };
  }

  function maximize(t) {
    const p = move(t, .92, 2.62);
    const up = 2.94;
    const carried = shift(base, 0, -192 * p);
    const state = make(touchText(t, .49, up, "위쪽 끝까지 끌기"),
      t < 2.49 ? "창을 화면 위로 이동" : t < up ? "위쪽 끝에서 놓기" : "손을 뗀 뒤 최대화",
      blend(carried, area("top"), move(t, up, up + .40)));
    return { ...state, dy: -132 * p, contact: touch(t, .49, up), active: isTouch(t, .49, up),
      pointer: [base.x + base.w / 2, Math.max(3, base.y + base.h / 2 - 220 * p)],
      zone: "top", zoneOpacity: move(t, 2.40, 2.62) * (1 - move(t, up, up + .38)) };
  }

  function sideSnap(t) {
    const right = t >= 4.55;
    const local = right ? t - 4.55 : t;
    const side = right ? "right" : "left";
    const p = move(local, right ? .35 : .82, right ? 1.48 : 1.98);
    const endDrag = right ? 1.48 : 1.98;
    const down = right ? .15 : .40;
    const up = endDrag + .21;
    const restoreDown = right ? 2.92 : 3.12;
    const restoreEnd = right ? 4.12 : 4.31;
    const restore = move(local, restoreDown + .20, restoreEnd);
    const liftRestore = restoreEnd + .10;
    const travel = (right ? desktop.w - 20 : 7) - (base.x + base.w * .5);
    const carried = shift(base, travel * p, -15 * p);
    const snapped = area(side);
    const state = make(
      local < restoreDown ? touchText(local, down, up, right ? "오른쪽 끝까지 끌기" : "왼쪽 끝까지 끌기")
        : local < liftRestore ? "세 손가락으로 다시 끌기" : "손가락 뗌",
      local < up ? "화면 가장자리에 닿기" : local < restoreDown ? (right ? "오른쪽 절반에 배치" : "왼쪽 절반에 배치") : "배치 전의 창 크기로 복원",
      local < restoreDown ? blend(carried, snapped, move(local, up, up + .33)) : blend(snapped, base, restore));
    const sign = right ? 1 : -1;
    const edgePointer = pointerAt(carried);
    const snappedPointer = pointerAt(snapped);
    const originalPointer = pointerAt(base);
    const approach = move(local, up + .42, restoreDown);
    const pointer = local < restoreDown
      ? edgePointer.map((v, i) => mix(v, snappedPointer[i], approach))
      : snappedPointer.map((v, i) => mix(v, originalPointer[i], restore));
    return { ...state, dx: local < restoreDown ? 122 * sign * p : -110 * sign * restore,
      contact: local < restoreDown ? touch(local, down, up) : touch(local, restoreDown, liftRestore),
      active: isTouch(local, down, up) || isTouch(local, restoreDown, liftRestore),
      pointer, zone: side, zoneOpacity: move(local, endDrag - .2, endDrag) * (1 - move(local, up, up + .33)) };
  }

  function flick(t) {
    const cut = Math.min(2, Math.floor(t / 2.9));
    const local = t - cut * 2.9;
    const direction = ["top", "left", "right"][cut];
    const [x, y] = [[0, -115], [-123, 0], [123, 0]][cut];
    const down = .71;
    const up = .99; // Contact to release is 0.28 s, within Glide's 0.35 s limit.
    const p = move(local, .76, up);
    const carried = shift(base, x * .37 * p, y * .37 * p);
    const cue = ["↑ 위로 밀기", "← 왼쪽으로 밀기", "→ 오른쪽으로 밀기"][cut];
    const result = ["손을 떼면 최대화", "손을 떼면 왼쪽 절반", "손을 떼면 오른쪽 절반"][cut];
    const state = make(local < up ? cue : "밀고 떼기 · 0.28초 예시",
      local < up ? "화면 가운데에서 시작" : result,
      blend(carried, area(direction), move(local, up, up + .40)));
    return { ...state, dx: x * p, dy: y * p, pointer: pointerAt(carried),
      contact: touch(local, down, up), active: isTouch(local, down, up) };
  }

  const gestures = {
    drag, spread: (t) => pinch(t, true), pinch: (t) => pinch(t, false),
    hold, maximize, "side-snap": sideSnap, flick
  };
  const root = document.querySelector("#root");
  const duration = Number(root.dataset.duration);
  const nodes = [...root.querySelectorAll(".scene")].map((scene) => ({
    scene, start: Number(scene.dataset.start), duration: Number(scene.dataset.duration),
    evaluate: gestures[scene.dataset.gesture],
    contacts: [...scene.querySelectorAll(".finger")],
    window: scene.querySelector(".model-window"),
    pointer: scene.querySelector(".pointer"), zone: scene.querySelector(".zone"),
    connector: scene.querySelector(".connector"), progress: scene.querySelector(".progress i"),
    left: scene.querySelector(".left-phase"), right: scene.querySelector(".right-phase")
  }));
  const place = (element, x, y) => { element.style.transform = `translate3d(${x}px,${y}px,0)`; };
  function writePhase(el, label, active) {
    if (el.textContent !== label) el.textContent = label;
    el.classList.toggle("active", active);
  }
  function paint(node, t) {
    const s = node.evaluate(t);
    node.contacts.forEach((circle, i) => {
      const [x, y] = hand[i];
      place(circle, 230 + x * s.span + s.dx, 201 + y * s.span + s.dy);
      circle.style.opacity = s.contact;
    });
    place(node.window, s.bounds.x, s.bounds.y);
    node.window.style.width = `${s.bounds.w}px`;
    node.window.style.height = `${s.bounds.h}px`;
    node.window.classList.toggle("edge", s.edge);
    place(node.pointer, ...(s.pointer ?? pointerAt(s.bounds)));
    node.zone.dataset.side = s.zone;
    node.zone.style.opacity = s.zoneOpacity;
    node.connector.classList.toggle("active", s.active);
    node.progress.style.transform = `scaleX(${s.progress})`;
    writePhase(node.left, s.left, s.active);
    writePhase(node.right, s.right, s.active || s.contact === 0);
  }
  const render = (time) => {
    // The same time samples the three contacts and the window. Rendering does not depend on previous frames.
    const active = nodes.findLast(({ start }) => time >= start - .00002);
    if (!active) return;
    paint(active, Math.max(0, Math.min(active.duration - .00001, time - active.start)));
  };
  nodes.forEach((node) => paint(node, 0));
  let frame = 0;
  // GSAP applies property setters even when the exporter seeks with callbacks suppressed.
  const clock = { get frame() { return frame; }, set frame(time) { frame = time; render(time); } };
  const tl = gsap.timeline({ paused: true });
  tl.to(clock, { frame: duration, duration, ease: "none" }, 0);
  window.__timelines = window.__timelines || {};
  window.__timelines[root.dataset.compositionId] = tl;
})();
