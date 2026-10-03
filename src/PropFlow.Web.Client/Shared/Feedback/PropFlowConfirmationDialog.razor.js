const states = new WeakMap();

export function open(dialog) {
    close(dialog);
    const previous = document.activeElement;
    const focusableSelector = 'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';
    const keydown = event => {
        if (event.key !== 'Tab') return;
        const focusable = [...dialog.querySelectorAll(focusableSelector)];
        if (focusable.length === 0) { event.preventDefault(); dialog.focus(); return; }
        const first = focusable[0];
        const last = focusable[focusable.length - 1];
        if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
        else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
    };
    dialog.addEventListener('keydown', keydown);
    states.set(dialog, { previous, keydown });
    requestAnimationFrame(() => (dialog.querySelector('[data-autofocus]') ?? dialog).focus());
}

export function close(dialog) {
    const state = states.get(dialog);
    if (!state) return;
    dialog.removeEventListener('keydown', state.keydown);
    if (state.previous instanceof HTMLElement && document.contains(state.previous)) state.previous.focus();
    states.delete(dialog);
}
