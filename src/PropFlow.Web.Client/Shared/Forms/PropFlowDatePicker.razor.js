const states = new WeakMap();
let activeRoot = null;
let activeDotnet = null;

export function open(root, dotnet) {
    if (activeRoot && activeRoot !== root) {
        const previousRoot = activeRoot;
        const previousDotnet = activeDotnet;
        close(previousRoot);
        previousDotnet?.invokeMethodAsync('CloseFromOutside');
    }

    close(root);
    const anchor = root.querySelector('.pf-date__control');
    const trigger = root.querySelector('.pf-date__calendar-trigger');
    const calendar = root.querySelector('.pf-date__calendar');
    if (!anchor || !trigger || !calendar) return;
    const position = () => {
        const rect = anchor.getBoundingClientRect();
        const gap = 6;
        const edge = 8;
        const width = Math.min(rect.width, window.innerWidth - edge * 2);
        let left = Math.min(Math.max(edge, rect.left), window.innerWidth - width - edge);
        root.style.setProperty('--pf-date-left', `${left}px`);
        root.style.setProperty('--pf-date-width', `${width}px`);
        const calendarHeight = calendar.offsetHeight || 320;
        const modalFooter = root.closest('.resident-modal')?.querySelector('.resident-modal-footer');
        const lowerBoundary = Math.min(window.innerHeight - edge, modalFooter?.getBoundingClientRect().top ?? window.innerHeight - edge);
        const below = lowerBoundary - rect.bottom - gap;
        const above = rect.top - gap - edge;
        const placeAbove = below < calendarHeight && above > below;
        const top = placeAbove
            ? Math.max(edge, rect.top - calendarHeight - gap)
            : Math.min(rect.bottom + gap, window.innerHeight - calendarHeight - edge);
        const closeButton = root.closest('[role="dialog"]')?.querySelector('button[aria-label="Đóng"]');
        const closeRect = closeButton?.getBoundingClientRect();
        if (closeRect
            && top < closeRect.bottom + gap
            && top + calendarHeight > closeRect.top - gap
            && left < closeRect.right + gap
            && left + width > closeRect.left - gap) {
            left = Math.max(edge, closeRect.left - width - gap);
        }
        root.style.setProperty('--pf-date-top', `${Math.max(edge, top)}px`);
        root.style.setProperty('--pf-date-left', `${left}px`);
    };

    position();
    if (typeof calendar.showPopover === 'function' && !calendar.matches(':popover-open')) calendar.showPopover();
    position();
    const state = { outside: null, keydown: null, reposition: null, protectPopover: null, frame: 0, closing: false, trigger, calendar };
    state.outside = event => {
        if (root.contains(event.target)) return;
        close(root);
        setTimeout(() => dotnet.invokeMethodAsync('CloseFromOutside'), 0);
    };
    state.keydown = event => {
        if (event.key !== 'Escape') return;
        event.stopPropagation();
        close(root, true);
        dotnet.invokeMethodAsync('CloseFromOutside');
    };
    state.reposition = () => {
        cancelAnimationFrame(state.frame);
        state.frame = requestAnimationFrame(position);
    };
    state.protectPopover = event => {
        if (event.newState === 'closed' && !state.closing) {
            close(root);
            dotnet.invokeMethodAsync('CloseFromOutside');
        }
    };

    document.addEventListener('click', state.outside, true);
    document.addEventListener('keydown', state.keydown, true);
    calendar.addEventListener('beforetoggle', state.protectPopover);
    window.addEventListener('resize', state.reposition, { passive: true });
    window.addEventListener('scroll', state.reposition, true);
    states.set(root, state);
    activeRoot = root;
    activeDotnet = dotnet;
}

export function close(root, returnFocus = false) {
    const state = states.get(root);
    const calendar = state?.calendar ?? root.querySelector('.pf-date__calendar');
    if (state) state.closing = true;
    if (calendar && typeof calendar.hidePopover === 'function' && calendar.matches(':popover-open')) calendar.hidePopover();

    if (state) {
        document.removeEventListener('click', state.outside, true);
        document.removeEventListener('keydown', state.keydown, true);
        calendar?.removeEventListener('beforetoggle', state.protectPopover);
        window.removeEventListener('resize', state.reposition);
        window.removeEventListener('scroll', state.reposition, true);
        cancelAnimationFrame(state.frame);
        states.delete(root);
        if (returnFocus) state.trigger?.focus({ preventScroll: true });
    }

    if (activeRoot === root) {
        activeRoot = null;
        activeDotnet = null;
    }
}

export function scrollSelectedYear(root) {
    const panel = root.querySelector('.pf-date__years');
    const selected = panel?.querySelector('.is-selected');
    if (!panel || !selected) return;
    panel.scrollTop = selected.offsetTop - panel.clientHeight / 2 + selected.offsetHeight / 2;
}
