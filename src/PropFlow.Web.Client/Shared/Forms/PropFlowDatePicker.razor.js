const states = new WeakMap();

export function open(root, dotnet) {
    close(root);
    const trigger = root.querySelector('.pf-date__trigger');
    const calendar = root.querySelector('.pf-date__calendar');
    if (!trigger || !calendar) return;
    if (typeof calendar.showPopover === 'function' && !calendar.matches(':popover-open')) calendar.showPopover();
    const position = () => {
        const rect = trigger.getBoundingClientRect();
        const gap = 6;
        const width = Math.min(288, window.innerWidth - 16);
        const below = window.innerHeight - rect.bottom - gap;
        const placeAbove = below < 330 && rect.top > below;
        const top = placeAbove ? Math.max(8, rect.top - calendar.offsetHeight - gap) : rect.bottom + gap;
        const left = Math.min(Math.max(8, rect.left), window.innerWidth - width - 8);
        root.style.setProperty('--pf-date-top', `${top}px`);
        root.style.setProperty('--pf-date-left', `${left}px`);
    };
    position();
    const state = { outside: null, reposition: null, stopWheel: null, protectPopover: null, frame: 0, closing: false };
    state.outside = event => { if (!root.contains(event.target)) dotnet.invokeMethodAsync('CloseFromOutside'); };
    state.reposition = event => {
        if (event?.target && calendar.contains(event.target)) return;
        cancelAnimationFrame(state.frame);
        state.frame = requestAnimationFrame(position);
    };
    state.stopWheel = event => event.stopPropagation();
    state.protectPopover = event => {
        if (event.newState === 'closed' && !state.closing) event.preventDefault();
    };
    document.addEventListener('pointerdown', state.outside, true);
    calendar.addEventListener('wheel', state.stopWheel, { passive: true });
    calendar.addEventListener('beforetoggle', state.protectPopover);
    window.addEventListener('resize', state.reposition, { passive: true });
    window.addEventListener('scroll', state.reposition, true);
    states.set(root, state);
}

export function close(root) {
    const calendar = root.querySelector('.pf-date__calendar');
    const state = states.get(root);
    if (state) state.closing = true;
    if (calendar && typeof calendar.hidePopover === 'function' && calendar.matches(':popover-open')) calendar.hidePopover();
    if (!state) return;
    document.removeEventListener('pointerdown', state.outside, true);
    if (calendar) {
        calendar.removeEventListener('wheel', state.stopWheel);
        calendar.removeEventListener('beforetoggle', state.protectPopover);
    }
    window.removeEventListener('resize', state.reposition);
    window.removeEventListener('scroll', state.reposition, true);
    cancelAnimationFrame(state.frame);
    states.delete(root);
}
