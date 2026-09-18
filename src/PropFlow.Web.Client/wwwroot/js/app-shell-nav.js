const storageKey = "propflow-app-nav-position";

export function initialize(element) {
    const handle = element.querySelector(".app-nav-drag");
    if (!handle) return { dispose() {} };

    const clamp = (value, low, high) => Math.max(low, Math.min(value, high));
    const place = (left, top) => {
        element.style.right = "auto";
        element.style.bottom = "auto";
        element.style.transform = "none";
        element.style.left = `${clamp(left, 0, Math.max(0, innerWidth - element.offsetWidth))}px`;
        element.style.top = `${clamp(top, 0, Math.max(0, innerHeight - element.offsetHeight))}px`;
    };

    try {
        const saved = JSON.parse(sessionStorage.getItem(storageKey) ?? "null");
        if (Number.isFinite(saved?.left) && Number.isFinite(saved?.top)) {
            place(saved.left, saved.top);
        }
    } catch { /* A blocked storage setting should not disable navigation. */ }

    let dragging = false;
    let offsetX = 0;
    let offsetY = 0;

    const pointerDown = event => {
        if (event.button !== 0) return;
        const bounds = element.getBoundingClientRect();
        offsetX = event.clientX - bounds.left;
        offsetY = event.clientY - bounds.top;
        dragging = true;
        handle.setPointerCapture(event.pointerId);
        event.preventDefault();
    };
    const pointerMove = event => {
        if (!dragging) return;
        place(event.clientX - offsetX, event.clientY - offsetY);
    };
    const pointerUp = event => {
        if (!dragging) return;
        dragging = false;
        if (handle.hasPointerCapture(event.pointerId)) handle.releasePointerCapture(event.pointerId);
        try {
            sessionStorage.setItem(storageKey, JSON.stringify({
                left: parseFloat(element.style.left),
                top: parseFloat(element.style.top)
            }));
        } catch { /* Position persistence is optional. */ }
    };
    const keyDown = event => {
        const delta = { ArrowLeft: [-16, 0], ArrowRight: [16, 0], ArrowUp: [0, -16], ArrowDown: [0, 16] }[event.key];
        if (!delta) return;
        event.preventDefault();
        const bounds = element.getBoundingClientRect();
        place(bounds.left + delta[0], bounds.top + delta[1]);
        try {
            sessionStorage.setItem(storageKey, JSON.stringify({
                left: parseFloat(element.style.left),
                top: parseFloat(element.style.top)
            }));
        } catch { /* Position persistence is optional. */ }
    };
    const keepVisible = () => {
        if (!element.style.left) return;
        place(parseFloat(element.style.left), parseFloat(element.style.top));
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
