const active = new Map();
let themeObserver;
const variables = ["--app-accent", "--app-surface", "--app-text", "--app-muted", "--app-line"];

function position() {
    const shell = document.querySelector(".app-shell, .resident-portal");
    const theme = shell ? getComputedStyle(shell) : null;
    const gap = 12;
    let top = Math.min(88, window.innerHeight * .15);
    const limit = Math.max(72, (window.innerHeight - top - 16 - gap * (active.size - 1)) / Math.max(1, active.size));
    const entries = [...active.values()].sort((a, b) =>
        a.toast.compareDocumentPosition(b.toast) & Node.DOCUMENT_POSITION_FOLLOWING ? -1 : 1);
    for (const { toast } of entries) {
        for (const name of variables) {
            const value = theme?.getPropertyValue(name).trim();
            if (value) toast.style.setProperty(name, value);
            else toast.style.removeProperty(name);
        }
        toast.style.maxHeight = `${limit}px`;
        toast.style.setProperty("--pf-toast-top", `${top}px`);
        top += toast.getBoundingClientRect().height + gap;
    }
}

export function show(toast, id) {
    if (!toast?.isConnected || active.has(id)) return;
    const observer = new ResizeObserver(position);
    active.set(id, { toast, observer });
    toast.showPopover();
    observer.observe(toast);
    if (active.size === 1) {
        window.addEventListener("resize", position);
        const shell = document.querySelector(".app-shell, .resident-portal");
        themeObserver = new MutationObserver(position);
        themeObserver.observe(document.documentElement, { attributes: true, attributeFilter: ["class", "data-theme"] });
        if (shell) themeObserver.observe(shell, { attributes: true, attributeFilter: ["class"] });
    }
    position();
}

export function hide(id) {
    const entry = active.get(id);
    if (!entry) return;
    const { toast, observer } = entry;
    observer.disconnect();
    active.delete(id);
    if (toast.matches(":popover-open")) toast.hidePopover();
    if (active.size === 0) {
        window.removeEventListener("resize", position);
        themeObserver?.disconnect();
        themeObserver = undefined;
    } else position();
}
