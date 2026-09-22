const states = new WeakMap();

export function open(root, dotnet) {
    close(root);
    const trigger = root.querySelector('.pf-select__trigger');
    const menu = root.querySelector('.pf-select__menu');
    if (!trigger || !menu) return;
    if (typeof menu.showPopover === 'function' && !menu.matches(':popover-open')) menu.showPopover();
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
    const outside = event => { if (!root.contains(event.target)) dotnet.invokeMethodAsync('CloseFromOutside'); };
    const dismiss = () => dotnet.invokeMethodAsync('CloseFromOutside');
    const selectActive = event => {
        if (event.key !== 'Enter' && event.key !== ' ') return;
        event.preventDefault();
        event.stopPropagation();
        dotnet.invokeMethodAsync('SelectActiveFromKeyboard');
    };
    document.addEventListener('pointerdown', outside, true);
    trigger.addEventListener('keydown', selectActive);
    window.addEventListener('resize', dismiss, { passive: true });
    window.addEventListener('scroll', dismiss, true);
    states.set(root, { outside, dismiss, trigger, selectActive });
}

export function close(root) {
    const menu = root.querySelector('.pf-select__menu');
    if (menu && typeof menu.hidePopover === 'function' && menu.matches(':popover-open')) menu.hidePopover();
    const state = states.get(root);
    if (!state) return;
    document.removeEventListener('pointerdown', state.outside, true);
    state.trigger.removeEventListener('keydown', state.selectActive);
    window.removeEventListener('resize', state.dismiss);
    window.removeEventListener('scroll', state.dismiss, true);
    states.delete(root);
}
