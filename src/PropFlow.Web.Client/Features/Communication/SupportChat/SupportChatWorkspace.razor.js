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

export function openMessageDetails(dialog) {
    if (!dialog?.isConnected || dialog.open) return;
    dialog.oncancel = event => {
        event.preventDefault();
        dialog.querySelector("button[autofocus]").click();
    };
    dialog.showModal();
}

export function closeMessageDetails(dialog) {
    dialog?.close();
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

export function bindComposer(workspace) {
    // Scope the listener to this workspace. Removing the workspace also removes the listener.
    workspace?.addEventListener("keydown", event => {
        const input = event.target;
        if (!input.matches(".support-compose textarea") || event.key !== "Enter" ||
            event.shiftKey || event.ctrlKey || event.altKey || event.metaKey ||
            event.isComposing || event.keyCode === 229) return;

        event.preventDefault();
        if (!event.repeat && !input.readOnly && input.value.trim()) input.form.requestSubmit();
    });
}
