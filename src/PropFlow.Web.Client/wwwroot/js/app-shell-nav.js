const storageKey = "propflow-app-nav-position";
const EDGE_PADDING = 24;

export function initialize(element) {
  const handle = element.querySelector(".app-nav-drag");
  if (!handle) return { dispose() { } };

  const clamp = (value, low, high) => Math.max(low, Math.min(value, high));

  const leftEdgeLeft = () => EDGE_PADDING;
  const rightEdgeLeft = () => Math.max(0, window.innerWidth - element.offsetWidth - EDGE_PADDING);
  const halfScreenX = () => window.innerWidth / 2;

  const place = (left, top) => {
    element.style.right = "auto";
    element.style.bottom = "auto";
    element.style.transform = "none";
    element.style.left = `${clamp(left, 0, Math.max(0, innerWidth - element.offsetWidth))}px`;
    element.style.top = `${clamp(top, 0, Math.max(0, innerHeight - element.offsetHeight))}px`;
  };

  // Neo dock về đúng mép gần nhất (trái hoặc phải) tùy theo tâm dock đang ở
  // nửa nào của màn hình. Dùng khi khôi phục vị trí đã lưu hoặc khi resize.
  const enforceEdgeSnap = (left, top) => {
    const centerX = left + element.offsetWidth / 2;
    const snappedLeft = centerX < halfScreenX() ? leftEdgeLeft() : rightEdgeLeft();
    place(snappedLeft, top);
  };

  try {
    const saved = JSON.parse(sessionStorage.getItem(storageKey) ?? "null");
    if (Number.isFinite(saved?.left) && Number.isFinite(saved?.top)) {
      enforceEdgeSnap(saved.left, saved.top);
    }
  } catch { /* A blocked storage setting should not disable navigation. */ }

  let dragging = false;
  let offsetX = 0;
  let offsetY = 0;

  const persist = () => {
    try {
      sessionStorage.setItem(storageKey, JSON.stringify({
        left: parseFloat(element.style.left),
        top: parseFloat(element.style.top)
      }));
    } catch { /* Position persistence is optional. */ }
  };

  const pointerDown = event => {
    if (event.button !== 0) return;
    const bounds = element.getBoundingClientRect();
    offsetX = event.clientX - bounds.left;
    offsetY = event.clientY - bounds.top;
    dragging = true;
    element.classList.add("is-dragging");
    element.style.transition = "none";
    handle.setPointerCapture(event.pointerId);
    event.preventDefault();
  };

  const pointerMove = event => {
    if (!dragging) return;
    // Trong lúc kéo, cho di chuyển tự do khắp màn hình để có phản hồi trực quan.
    // Việc neo về mép trái/phải chỉ áp dụng khi thả tay (pointerUp).
    place(event.clientX - offsetX, event.clientY - offsetY);
  };

  const pointerUp = event => {
    if (!dragging) return;
    dragging = false;
    element.classList.remove("is-dragging");
    if (handle.hasPointerCapture(event.pointerId)) handle.releasePointerCapture(event.pointerId);

    const bounds = element.getBoundingClientRect();
    const centerX = bounds.left + element.offsetWidth / 2;

    element.style.transition =
      "left 0.28s cubic-bezier(0.16, 1, 0.3, 1), top 0.2s ease";

    if (centerX < halfScreenX()) {
      // Thả ở nửa TRÁI màn hình -> neo về sát mép trái.
      place(leftEdgeLeft(), bounds.top);
    } else {
      // Thả ở nửa PHẢI màn hình -> neo về sát mép phải.
      place(rightEdgeLeft(), bounds.top);
    }

    // Xóa transition sau khi animation chạy xong để không ảnh hưởng lần kéo tiếp theo.
    window.setTimeout(() => { element.style.transition = "none"; }, 300);

    persist();
  };

  const keyDown = event => {
    const delta = { ArrowLeft: [-16, 0], ArrowRight: [16, 0], ArrowUp: [0, -16], ArrowDown: [0, 16] }[event.key];
    if (!delta) return;
    event.preventDefault();
    const bounds = element.getBoundingClientRect();
    // Điều hướng bằng bàn phím vẫn di chuyển tự do; nhấn Enter/Space (nếu cần)
    // có thể bổ sung để chủ động neo, còn mặc định giữ đúng vị trí đang có.
    place(bounds.left + delta[0], bounds.top + delta[1]);
    persist();
  };

  const keepVisible = () => {
    if (!element.style.left) return;
    enforceEdgeSnap(parseFloat(element.style.left), parseFloat(element.style.top));
  };

  handle.addEventListener("pointerdown", pointerDown);
  handle.addEventListener("pointermove", pointerMove);
  handle.addEventListener("pointerup", pointerUp);
  handle.addEventListener("pointercancel", pointerUp);
  handle.addEventListener("keydown", keyDown);
  window.addEventListener("resize", keepVisible);
  const observer = new ResizeObserver(keepVisible);
  observer.observe(element);

  return {
    dispose() {
      handle.removeEventListener("pointerdown", pointerDown);
      handle.removeEventListener("pointermove", pointerMove);
      handle.removeEventListener("pointerup", pointerUp);
      handle.removeEventListener("pointercancel", pointerUp);
      handle.removeEventListener("keydown", keyDown);
      window.removeEventListener("resize", keepVisible);
      observer.disconnect();
    }
  };
}
