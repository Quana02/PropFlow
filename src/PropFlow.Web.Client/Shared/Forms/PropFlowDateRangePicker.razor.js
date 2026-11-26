import { connectDirectoryFilterMenu } from './PropFlowFilterPopover.razor.js?v=20261125.5';
const states = new WeakMap();

export function open(root, dotnet) {
    close(root);
    const trigger = root.querySelector('.pf-date-range__trigger');
    const calendar = root.querySelector('.pf-date-range__calendar');
    if (!trigger || !calendar) return;
    if (typeof calendar.showPopover === 'function' && !calendar.matches(':popover-open')) calendar.showPopover();
    const directory = connectDirectoryFilterMenu(trigger,calendar,480,()=>{
        close(root);trigger.focus({preventScroll:true});void dotnet.invokeMethodAsync('CloseFromOutside');
    });
    const position = () => {
        if(directory){directory.position();return;}
        const rect = trigger.getBoundingClientRect();
        const gap = 6;
        const width = Math.min(288, window.innerWidth - 16);
        const below = window.innerHeight - rect.bottom - gap;
        const placeAbove = below < 330 && rect.top > below;
        const top = placeAbove ? Math.max(8, rect.top - calendar.offsetHeight - gap) : rect.bottom + gap;
        const left = Math.min(Math.max(8, rect.left), window.innerWidth - width - 8);
        root.style.setProperty('--pf-date-range-top', `${top}px`);
        root.style.setProperty('--pf-date-range-left', `${left}px`);
    };
    position();
    const state = { directory, outsideEvent:directory?'click':'pointerdown', frame: 0, closing: false };
    state.keyboard=event=>{
        if(root.classList.contains('pf-date-range--directory') && event.key==='Escape'){
            event.preventDefault();event.stopPropagation();close(root);void dotnet.invokeMethodAsync('CloseFromOutside');trigger.focus({preventScroll:true});
        }
    };
    root.addEventListener('keydown',state.keyboard);
    state.outside = event => { if (!root.contains(event.target)) dotnet.invokeMethodAsync('CloseFromOutside'); };
    state.reposition = () => { cancelAnimationFrame(state.frame); state.frame = requestAnimationFrame(position); };
    state.protectPopover = event => { if (event.newState === 'closed' && !state.closing) event.preventDefault(); };
    document.addEventListener(state.outsideEvent, state.outside, true);
    calendar.addEventListener('beforetoggle', state.protectPopover);
    window.addEventListener('resize', state.reposition, { passive: true });
    window.addEventListener('scroll', state.reposition, true);
    if(directory){window.visualViewport?.addEventListener('resize', state.reposition);window.visualViewport?.addEventListener('scroll', state.reposition);}
    states.set(root, state);
}

export function close(root) {
    const calendar = root.querySelector('.pf-date-range__calendar');
    const state = states.get(root);
    if (state) state.closing = true;
    state?.directory?.disconnect();
    if (calendar && typeof calendar.hidePopover === 'function' && calendar.matches(':popover-open')) calendar.hidePopover();
    if (!state) return;
    root.removeEventListener('keydown',state.keyboard);
    document.removeEventListener(state.outsideEvent, state.outside, true);
    if (calendar) calendar.removeEventListener('beforetoggle', state.protectPopover);
    window.removeEventListener('resize', state.reposition);
    window.removeEventListener('scroll', state.reposition, true);
    window.visualViewport?.removeEventListener('resize', state.reposition);
    window.visualViewport?.removeEventListener('scroll', state.reposition);
    cancelAnimationFrame(state.frame);
    states.delete(root);
}
