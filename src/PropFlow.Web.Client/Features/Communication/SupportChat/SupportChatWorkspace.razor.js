export function bottom(element) {
    if (element) element.scrollTop = element.scrollHeight;
}

export function focusComposer(workspace) {
    workspace?.querySelector(".support-compose textarea")?.focus();
}

export function focusMessagePicker(thread, id) {
    thread?.querySelector(`[data-message-id="${id}"] .support-message-picker`)?.focus({ preventScroll: true });
}

export function copyMessageText(text) {
    return navigator.clipboard.writeText(text);
}

export function visibleMessages(thread) {
    if (!thread || document.visibilityState !== "visible" || !document.hasFocus()) return [];
    const bounds = thread.getBoundingClientRect();
    const top = Math.max(0, bounds.top);
    const bottom = Math.min(innerHeight, bounds.bottom);
    return [...thread.querySelectorAll("[data-message-id]")].filter(element => {
        const row = element.getBoundingClientRect();
        return row.height > 0 && Math.min(row.bottom, bottom) - Math.max(row.top, top) >= Math.min(row.height / 2, 40);
    }).map(element => element.dataset.messageId);
}
