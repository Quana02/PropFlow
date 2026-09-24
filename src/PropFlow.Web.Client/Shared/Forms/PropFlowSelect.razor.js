const states = new WeakMap();

export function open(root, dotnet) {
    close(root);
    const trigger = root.querySelector('.pf-select__trigger');
    const menu = root.querySelector('.pf-select__menu');
    if (!trigger || !menu) return;
    if (typeof menu.showPopover === 'function' && !menu.matches(':popover-open')) menu.showPopover();
    const position = () => {
        const rect = trigger.getBoundingClientRect();
        const gap = 6;
        const below = window.innerHeight - rect.bottom - gap;
        const above = rect.top - gap;
        const placeAbove = below < 180 && above > below;
        const available = Math.max(96, placeAbove ? above : below);
        const top = placeAbove ? Math.max(8, rect.top - Math.min(menu.scrollHeight, available) - gap) : rect.bottom + gap;
        const width = Math.min(rect.width, window.innerWidth - 16);
        const left = Math.min(Math.max(8, rect.left), window.innerWidth - width - 8);
        root.style.setProperty('--pf-select-top', `${top}px`);
        root.style.setProperty('--pf-select-left', `${left}px`);
        root.style.setProperty('--pf-select-width', `${width}px`);
        root.style.setProperty('--pf-select-space', `${available}px`);
    };
    position();
    const outside = event => { if (!root.contains(event.target)) dotnet.invokeMethodAsync('CloseFromOutside'); };
    const state = { outside, reposition: null, trigger, selectActive: null, stopWheel: null, protectPopover: null, frame: 0, closing: false };
    const reposition = event => {
        if (event?.target && menu.contains(event.target)) return;
        cancelAnimationFrame(state.frame);
        state.frame = requestAnimationFrame(position);
    };
    const selectActive = event => {
        if (event.key !== 'Enter' && event.key !== ' ') return;
        event.preventDefault();
        event.stopPropagation();
        dotnet.invokeMethodAsync('SelectActiveFromKeyboard');
    };
    const stopWheel = event => event.stopPropagation();
    const protectPopover = event => {
        if (event.newState === 'closed' && !state.closing) event.preventDefault();
    };
    document.addEventListener('pointerdown', outside, true);
    trigger.addEventListener('keydown', selectActive);
    menu.addEventListener('wheel', stopWheel, { passive: true });
    menu.addEventListener('beforetoggle', protectPopover);
    window.addEventListener('resize', reposition, { passive: true });
    window.addEventListener('scroll', reposition, true);
    state.reposition = reposition;
    state.selectActive = selectActive;
    state.stopWheel = stopWheel;
    state.protectPopover = protectPopover;
    states.set(root, state);
}

export function close(root) {
    const menu = root.querySelector('.pf-select__menu');
    const state = states.get(root);
    if (state) state.closing = true;
    if (menu && typeof menu.hidePopover === 'function' && menu.matches(':popover-open')) menu.hidePopover();
    if (!state) return;
    document.removeEventListener('pointerdown', state.outside, true);
    state.trigger.removeEventListener('keydown', state.selectActive);
    if (menu) {
        menu.removeEventListener('wheel', state.stopWheel);
        menu.removeEventListener('beforetoggle', state.protectPopover);
    }
    window.removeEventListener('resize', state.reposition);
    window.removeEventListener('scroll', state.reposition, true);
    cancelAnimationFrame(state.frame);
    states.delete(root);
}
