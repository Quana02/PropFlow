const edge = 12;
export const clamp = (value, min, max) => Math.max(min, Math.min(value, Math.max(min, max)));

export function attach(widget) {
    const button = widget.querySelector('.rag-launch');
    let start = null, dragging = false, suppressClick = false, timer = 0, frame = 0;
    const viewport = () => ({ width: window.innerWidth, height: window.innerHeight });
    function place(left, top) {
        const { width, height } = viewport();
        widget.style.right = `${width - clamp(left, edge, width - button.offsetWidth - edge) - button.offsetWidth}px`;
        widget.style.bottom = `${height - clamp(top, edge, height - button.offsetHeight - edge) - button.offsetHeight}px`;
    }
    function layout() {
        frame = 0;
        const { width, height } = viewport();
        let bounds = button.getBoundingClientRect();
        place(bounds.left, bounds.top);
        bounds = button.getBoundingClientRect();
        const label = button.querySelector('.rag-launch-label');
        const extension = Math.max(0, (label?.offsetWidth ?? 56) - 56);
        widget.classList.toggle('label-right', bounds.left < extension + edge);
        const panel = widget.querySelector('.rag-window');
        if (!panel) return;
        const panelWidth = Math.min(420, width - 2 * edge);
        const panelHeight = Math.min(720, height - 2 * edge);
        const above = bounds.top - edge;
        const below = height - bounds.bottom - edge;
        // Prefer the usual space above the launcher, then use the available viewport.
        const panelTop = above >= 300 ? Math.max(edge, bounds.top - panelHeight - edge)
            : below >= 300 ? bounds.bottom + edge : edge;
        const availableHeight = above >= 300 ? above - edge
            : below >= 300 ? below - edge : height - 2 * edge;
        Object.assign(panel.style, {
            position: 'fixed', width: `${panelWidth}px`,
            height: `${Math.min(panelHeight, availableHeight)}px`,
            left: `${clamp(bounds.right - panelWidth, edge, width - panelWidth - edge)}px`,
            top: `${panelTop}px`, margin: '0'
        });
    }
    const schedule = () => { if (!frame) frame = requestAnimationFrame(layout); };
    function down(event) {
        if (!event.isPrimary || event.button !== 0) return;
        const bounds = button.getBoundingClientRect();
        start = { id: event.pointerId, x: event.clientX, y: event.clientY, left: bounds.left, top: bounds.top };
        dragging = false;
        button.setPointerCapture(event.pointerId);
    }
    function move(event) {
        if (!start || event.pointerId !== start.id) return;
        const dx = event.clientX - start.x, dy = event.clientY - start.y;
        if (!dragging && Math.hypot(dx, dy) < 6) return;
        if (!dragging) {
            dragging = true; widget.classList.add('is-dragging');
        }
        event.preventDefault();
        place(start.left + dx, start.top + dy); schedule();
    }
    function end(event) {
        if (!start || event.pointerId !== start.id) return;
        if (dragging) {
            suppressClick = true; clearTimeout(timer);
            timer = setTimeout(() => { suppressClick = false; }, 350);
        }
        start = null; dragging = false; widget.classList.remove('is-dragging');
        if (button.hasPointerCapture(event.pointerId)) button.releasePointerCapture(event.pointerId);
        schedule();
    }
    function click(event) {
        if (suppressClick) { event.preventDefault(); event.stopImmediatePropagation(); suppressClick = false; }
    }
    button.addEventListener('pointerdown', down);
    button.addEventListener('pointermove', move);
    button.addEventListener('pointerup', end);
    button.addEventListener('pointercancel', end);
    button.addEventListener('lostpointercapture', end);
    button.addEventListener('click', click, true);
    window.addEventListener('resize', schedule);
    const observer = new MutationObserver(schedule);
    observer.observe(widget, { childList: true });
    const sizeObserver = new ResizeObserver(schedule);
    sizeObserver.observe(button);
    schedule();
    return { dispose() {
        clearTimeout(timer); cancelAnimationFrame(frame); observer.disconnect(); sizeObserver.disconnect();
        window.removeEventListener('resize', schedule);
        button.removeEventListener('pointerdown', down); button.removeEventListener('pointermove', move);
        button.removeEventListener('pointerup', end); button.removeEventListener('pointercancel', end);
        button.removeEventListener('lostpointercapture', end); button.removeEventListener('click', click, true);
        if (start && button.hasPointerCapture(start.id)) button.releasePointerCapture(start.id);
    } };
}
