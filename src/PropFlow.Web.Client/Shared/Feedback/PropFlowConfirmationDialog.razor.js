const states = new WeakMap();

export function open(dialog) {
    close(dialog);
    const previous = document.activeElement;
    const overlay = dialog.closest('[popover]');
    overlay?.showPopover();
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
    states.set(dialog, { previous, keydown, overlay });
    requestAnimationFrame(() => (dialog.querySelector('[data-autofocus]') ?? dialog).focus());
}

const modalStack = new Set();

export function openDialog(dialog) {
    if (!dialog?.isConnected) return DotNet.createJSObjectReference({ close() {} });

    const previous = document.activeElement;
    const closeButton = dialog.querySelector('[data-dialog-close]');
    const fallback = dialog.querySelector('h2');
    const controller = new AbortController();
    const options = { signal: controller.signal };
    let pointerStartedOutside = false;

    dialog.addEventListener('cancel', event => {
        event.preventDefault();
        closeButton.click();
    }, options);

    dialog.addEventListener('pointerdown', event => {
        pointerStartedOutside = event.target === dialog;
    }, options);

    dialog.addEventListener('click', event => {
        if (pointerStartedOutside && event.target === dialog && dialog.dataset.dismissOnBackdrop === 'true') {
            closeButton.click();
        }
        pointerStartedOutside = false;
    }, options);

    document.addEventListener('keydown', event => {
        if (event.key !== 'Escape' || Array.from(modalStack).at(-1) !== dialog) return;
        if (!dialog.contains(event.target) && event.target !== document.body) return;

        // Handle Escape before the native close request, including repeated presses.
        event.preventDefault();
        if (dialog.querySelector('[popover]:popover-open')) return;
        event.stopPropagation();
        if (!event.repeat) closeButton.click();
    }, { ...options, capture: true });

    dialog.addEventListener('keydown', event => {
        if (event.key !== 'Tab' || event.defaultPrevented) return;
        const stops = Array.from(dialog.querySelectorAll('a[href], button, input, select, textarea, summary, [tabindex]'))
            .filter(element => element.tabIndex >= 0 && !element.matches(':disabled') &&
                !element.closest('[inert]') && element.getClientRects().length > 0 &&
                getComputedStyle(element).visibility === 'visible');
        const edge = event.shiftKey ? stops[0] : stops.at(-1);
        if (document.activeElement !== edge && stops.includes(document.activeElement)) return;

        event.preventDefault();
        (event.shiftKey ? stops.at(-1) ?? dialog : stops[0] ?? fallback).focus();
    }, options);

    dialog.showModal();
    modalStack.add(dialog);

    return DotNet.createJSObjectReference({
        close() {
            controller.abort();
            modalStack.delete(dialog);
            // Removing a modal can reset focus to the document before Blazor disposes it.
            const restore = dialog.contains(document.activeElement) || document.activeElement === document.body;
            if (dialog.open) dialog.close();
            if (restore && previous instanceof HTMLElement && previous.isConnected) {
                previous.focus({ preventScroll: true });
            }
        }
    });
}

export function close(dialog) {
    const state = states.get(dialog);
    if (!state) return;
    dialog.removeEventListener('keydown', state.keydown);
    state.overlay?.hidePopover();
    if (state.previous instanceof HTMLElement && document.contains(state.previous)) state.previous.focus();
    states.delete(dialog);
}
