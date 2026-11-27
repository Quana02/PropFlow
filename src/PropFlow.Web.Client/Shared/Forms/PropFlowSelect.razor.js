import { connectDirectoryFilterMenu } from './PropFlowFilterPopover.razor.js?v=20261125.5';
const states = new WeakMap();

export function open(root, dotnet) {
    close(root);
    const trigger = root.querySelector('.pf-select__trigger');
    const menu = root.querySelector('.pf-select__menu');
    const directoryDrawer = !!root.closest('.pf-filter--directory-drawer');
    if (!trigger || !menu) return;
    if (typeof menu.showPopover === 'function' && !menu.matches(':popover-open')) menu.showPopover();
    const directory = connectDirectoryFilterMenu(trigger, menu, 288, () => {
        close(root); trigger.focus({preventScroll:true}); void dotnet.invokeMethodAsync('CloseFromOutside');
    });
    const position = () => {
        if (directory) { directory.position(); return; }
        const rect = trigger.getBoundingClientRect();
        const gap = 6;
        const viewport = directoryDrawer ? window.visualViewport : null;
        const viewportTop = viewport?.offsetTop ?? 0, viewportLeft = viewport?.offsetLeft ?? 0;
        const viewportHeight = viewport?.height ?? window.innerHeight, viewportWidth = viewport?.width ?? window.innerWidth;
        const below = viewportTop + viewportHeight - rect.bottom - gap;
        const above = rect.top - viewportTop - gap;
        const placeAbove = below < 180 && above > below;
        const available = Math.max(0, (placeAbove ? above : below) - 8);
        const maxHeight = Math.min(18 * parseFloat(getComputedStyle(document.documentElement).fontSize), available);
        menu.style.maxHeight = `${maxHeight}px`;
        const height = Math.min(menu.scrollHeight, maxHeight);
        const top = placeAbove ? Math.max(viewportTop + 8, rect.top - height - gap) : rect.bottom + gap;
        const width = Math.min(rect.width, viewportWidth - 16);
        const left = Math.min(Math.max(viewportLeft + 8, rect.left), viewportLeft + viewportWidth - width - 8);
        root.style.setProperty('--pf-select-top', `${top}px`);
        root.style.setProperty('--pf-select-left', `${left}px`);
        root.style.setProperty('--pf-select-width', `${width}px`);
        root.style.setProperty('--pf-select-space', `${available}px`);
    };
    position();
    const outside = event => {
        if (root.contains(event.target)) return;
        close(root);
        setTimeout(() => dotnet.invokeMethodAsync('CloseFromOutside'), 0);
    };
    const state = { outside, directory, reposition: null, trigger, selectActive: null, stopWheel: null, protectPopover: null, frame: 0, closing: false };
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
    document.addEventListener('click', outside, true);
    trigger.addEventListener('keydown', selectActive);
    menu.addEventListener('wheel', stopWheel, { passive: true });
    menu.addEventListener('beforetoggle', protectPopover);
    window.addEventListener('resize', reposition, { passive: true });
    window.addEventListener('scroll', reposition, true);
    state.reposition = reposition;
    state.selectActive = selectActive;
    state.stopWheel = stopWheel;
    state.protectPopover = protectPopover;
    if (directoryDrawer) {
        window.visualViewport?.addEventListener('resize', reposition);
        window.visualViewport?.addEventListener('scroll', reposition);
        state.escape = event => {
            if (event.key !== 'Escape') return;
            event.preventDefault(); event.stopPropagation(); close(root);
            trigger.focus({ preventScroll: true }); void dotnet.invokeMethodAsync('CloseFromOutside');
        };
        root.addEventListener('keydown', state.escape, true);
    }
    states.set(root, state);
}

export function close(root) {
    const menu = root.querySelector('.pf-select__menu');
    const state = states.get(root);
    if (state) state.closing = true;
    state?.directory?.disconnect();
    if (menu && typeof menu.hidePopover === 'function' && menu.matches(':popover-open')) menu.hidePopover();
    if (!state) return;
    document.removeEventListener('click', state.outside, true);
    state.trigger.removeEventListener('keydown', state.selectActive);
    if (state.escape) root.removeEventListener('keydown', state.escape, true);
    if (menu) {
        menu.removeEventListener('wheel', state.stopWheel);
        menu.removeEventListener('beforetoggle', state.protectPopover);
    }
    window.removeEventListener('resize', state.reposition);
    window.removeEventListener('scroll', state.reposition, true);
    window.visualViewport?.removeEventListener('resize', state.reposition);
    window.visualViewport?.removeEventListener('scroll', state.reposition);
    cancelAnimationFrame(state.frame);
    states.delete(root);
}
