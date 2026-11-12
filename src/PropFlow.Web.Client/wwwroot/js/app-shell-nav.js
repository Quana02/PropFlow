const storageKey = "propflow-app-nav-position";
const EDGE_PADDING = 24;

export function initialize(element) {
  const scrolling = initializeScrolling(element);
  let transitionTimer;
  const handle = element.querySelector(".app-nav-drag");
  if (!handle) return scrolling;

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
    const dockOnLeft = centerX < halfScreenX();
    element.dataset.dockSide = dockOnLeft ? "left" : "right";
    place(dockOnLeft ? leftEdgeLeft() : rightEdgeLeft(), top);
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
    element.style.transition =
      "left 0.28s cubic-bezier(0.16, 1, 0.3, 1), top 0.2s ease";

    enforceEdgeSnap(bounds.left, bounds.top);

    // Xóa transition sau khi animation chạy xong để không ảnh hưởng lần kéo tiếp theo.
    window.clearTimeout(transitionTimer);
    transitionTimer = window.setTimeout(() => { element.style.transition = "none"; }, 300);

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
      scrolling.dispose();
      window.clearTimeout(transitionTimer);
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

// Only change this scroll viewport, never scrollIntoView (which can move the page).
export function initializeScrolling(element) {
  const viewport = element.querySelector(".app-nav-scroll");
  if (!viewport) return { dispose() {} };
  const content = viewport.querySelector(".app-nav-scroll-content");
  const up = element.querySelector(".app-nav-scroll-up");
  const down = element.querySelector(".app-nav-scroll-down");
  const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)");
  let disposed = false;
  let queued = false;
  let forceNext = false;
  let lastActive = null;
  const scrollRange = () => viewport.scrollHeight - viewport.clientHeight;
  const updateEdge = (button, overflow, available) => {
    if (!available && button === button.ownerDocument.activeElement) {
      viewport.focus({ preventScroll: true });
    }
    button.disabled = !available;
    button.hidden = !overflow;
  };
  const updateEdges = () => {
    const range = scrollRange();
    // Allow for rounding in scrollHeight/clientHeight and fractional scrollTop.
    const overflow = viewport.clientHeight > 0 && range > 1;
    updateEdge(up, overflow, overflow && viewport.scrollTop > 0.5);
    updateEdge(down, overflow, overflow && viewport.scrollTop < range - 0.5);
  };
  const scrollByItems = direction => {
    const item = content.querySelector("a[title]");
    if (!item) return;
    viewport.scrollBy({
      top: direction * item.offsetHeight * 2.75,
      behavior: reducedMotion.matches ? "instant" : "smooth"
    });
  };
  const scrollUp = () => scrollByItems(-1);
  const scrollDown = () => scrollByItems(1);
  const wheelOnRail = event => {
    if (event.ctrlKey || !event.deltaY) return;
    // The viewport handles wheel input natively when it has room to scroll.
    const canScroll = scrollRange() > 1;
    if (canScroll && viewport.contains(event.target)) return;
    event.preventDefault();
    if (!canScroll) return;
    const unit = event.deltaMode === 1 ? 16 : event.deltaMode === 2 ? viewport.clientHeight : 1;
    viewport.scrollTop += event.deltaY * unit;
  };
  const reveal = item => {
    if (!item || viewport.clientHeight === 0) return;
    const bounds = viewport.getBoundingClientRect();
    const target = item.getBoundingClientRect();
    const padding = window.getComputedStyle(viewport);
    const top = bounds.top + (parseFloat(padding.scrollPaddingTop) || 0);
    const bottom = bounds.bottom - (parseFloat(padding.scrollPaddingBottom) || 0);
    if (target.top < top) viewport.scrollTop -= top - target.top;
    else if (target.bottom > bottom) viewport.scrollTop += target.bottom - bottom;
  };
  const schedule = (force = false) => {
    forceNext ||= force;
    if (queued || disposed) return;
    queued = true;
    queueMicrotask(() => {
      queued = false;
      if (disposed) return;
      const active = content.querySelector('a[aria-current="page"]') ?? content.querySelector("a.active, a.is-selected");
      if (forceNext || active !== lastActive) reveal(active);
      forceNext = false;
      lastActive = active;
      updateEdges();
    });
  };
  const focused = event => reveal(event.target.closest("a"));
  viewport.addEventListener("focusin", focused);
  viewport.addEventListener("scroll", updateEdges, { passive: true });
  up.addEventListener("click", scrollUp);
  down.addEventListener("click", scrollDown);
  element.addEventListener("wheel", wheelOnRail, { passive: false });
  // Handles delayed authorization rendering, normal NavLink changes and admin query tabs.
  const changes = new MutationObserver(records => schedule(records.some(record => record.type === "childList")));
  changes.observe(content, { subtree: true, childList: true, attributes: true, attributeFilter: ["class", "aria-current"] });
  const size = new ResizeObserver(() => schedule(true));
  size.observe(viewport);
  size.observe(content);
  schedule(true);
  return {
    dispose() {
      disposed = true;
      changes.disconnect();
      size.disconnect();
      viewport.removeEventListener("focusin", focused);
      viewport.removeEventListener("scroll", updateEdges);
      up.removeEventListener("click", scrollUp);
      down.removeEventListener("click", scrollDown);
      element.removeEventListener("wheel", wheelOnRail);
    }
  };
}
