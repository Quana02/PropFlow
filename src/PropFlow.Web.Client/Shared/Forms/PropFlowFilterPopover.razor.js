export function connect(root, dotnet) {
    const trigger = root.querySelector('.pf-filter-trigger');
    const popover = root.querySelector('.pf-filter-popover');
    const fields = popover.querySelector('.pf-filter-fields');
    const closeButton = popover.querySelector('.pf-filter-close');
    const directoryDrawer = root.classList.contains('pf-filter--directory-drawer');
    let listeners;
    let resizeObserver;
    let contentObserver;

    function position() {
        if (!popover.matches(':popover-open') || !fields.firstElementChild) return;
        const inset = 8;
        const gap = 6;
        const anchor = trigger.getBoundingClientRect();
        const viewport = window.visualViewport;
        const width = viewport?.width ?? window.innerWidth;
        const height = viewport?.height ?? window.innerHeight;
        const x = viewport?.offsetLeft ?? 0;
        const y = viewport?.offsetTop ?? 0;
        const below = y + height - anchor.bottom - gap - inset;
        const above = anchor.top - y - gap - inset;
        popover.style.maxWidth = `${width - 2 * inset}px`;
        const naturalHeight = popover.offsetHeight + fields.scrollHeight - fields.clientHeight;
        const placeAbove = naturalHeight > below && above > below;
        const space = Math.max(above, below);
        const panelHeight = directoryDrawer ? height - 2 * inset : Math.min(height - 2 * inset, Math.max(space, 176));
        popover.style.maxHeight = `${panelHeight}px`;
        if (directoryDrawer) popover.style.setProperty('--pf-filter-scrollbar-width', `${Math.max(0, fields.offsetWidth - fields.clientWidth)}px`);
        const rect = popover.getBoundingClientRect();
        const left = Math.max(x + inset, Math.min(anchor.right - rect.width, x + width - inset - rect.width));
        const top = placeAbove ? anchor.top - gap - rect.height : anchor.bottom + gap;
        popover.style.left = `${left}px`;
        popover.style.top = `${Math.max(y + inset, Math.min(top, y + height - inset - rect.height))}px`;
        if (!popover.hasAttribute('data-positioned')) {
            popover.setAttribute('data-positioned', '');
            closeButton.focus({ preventScroll: true });
        }
    }

    function stop() {
        listeners?.abort();
        resizeObserver?.disconnect();
        contentObserver?.disconnect();
        listeners = resizeObserver = contentObserver = undefined;
    }

    function beforeToggle() {
        popover.removeAttribute('data-positioned');
    }

    function toggle(event) {
        stop();
        const open = event.newState === 'open';
        trigger.setAttribute('aria-expanded', String(open));
        if (!open) {
            if (!document.activeElement?.closest(':popover-open')) trigger.focus({ preventScroll: true });
            void dotnet.invokeMethodAsync('Closed');
            return;
        }
        listeners = new AbortController();
        const options = { signal: listeners.signal, passive: true };
        window.addEventListener('resize', position, options);
        window.addEventListener('scroll', position, { ...options, capture: true });
        window.visualViewport?.addEventListener('resize', position, options);
        window.visualViewport?.addEventListener('scroll', position, options);
        resizeObserver = new ResizeObserver(position);
        resizeObserver.observe(popover);
        resizeObserver.observe(trigger);
        contentObserver = new MutationObserver(position);
        contentObserver.observe(fields, { childList: true });
        position();
    }

    function preventBusyOpen(event) {
        if (trigger.getAttribute('aria-disabled') === 'true' && !popover.matches(':popover-open')) event.preventDefault();
    }

    function keyboard(event) {
        if (!directoryDrawer || !popover.matches(':popover-open')) return;
        // Nested top-layer listboxes own Escape until they have closed.
        if (event.key === 'Escape') {
            if (popover.querySelector('[role="listbox"]:popover-open, .pf-date-range__calendar:popover-open')) return;
            event.preventDefault(); event.stopPropagation(); popover.hidePopover();
        }
        if (event.key !== 'Tab') return;
        const activeOverlay = popover.querySelector('[data-filter-placement="overlay"]:popover-open');
        const focusable = [...(activeOverlay ?? popover).querySelectorAll('button:not(:disabled), input:not(:disabled), [tabindex="0"]')]
            .filter(e => e.getClientRects().length && getComputedStyle(e).visibility !== 'hidden');
        if (!focusable.length) return;
        const index = focusable.indexOf(document.activeElement);
        if (event.shiftKey && index <= 0) { event.preventDefault(); focusable.at(-1).focus(); }
        else if (!event.shiftKey && (index < 0 || index === focusable.length - 1)) { event.preventDefault(); focusable[0].focus(); }
    }

    trigger.addEventListener('click', preventBusyOpen);
    popover.addEventListener('beforetoggle', beforeToggle);
    popover.addEventListener('toggle', toggle);
    popover.addEventListener('keydown', keyboard);
    return {
        close() { if (popover.matches(':popover-open')) popover.hidePopover(); },
        disconnect() {
            stop();
            trigger.removeEventListener('click', preventBusyOpen);
            popover.removeEventListener('beforetoggle', beforeToggle);
            popover.removeEventListener('toggle', toggle);
            popover.removeEventListener('keydown', keyboard);
        }
    };
}

// One lifetime per nested popup. Reserve room in short drawers, then choose a
// side that fits the whole menu or an explicitly labelled body overlay.
export function connectDirectoryFilterMenu(trigger, menu, preferredHeight, close) {
    const root = trigger.closest('.pf-filter--directory-drawer');
    if (!root) return null;
    const panel = root.querySelector('.pf-filter-popover');
    const fields = panel.querySelector('.pf-filter-fields');
    const heading = menu.querySelector('[data-filter-menu-heading]');
    const button = menu.querySelector('[data-filter-menu-close]');
    const originalMinHeight = panel.style.minHeight;
    const initialPanelHeight = panel.getBoundingClientRect().height;
    let naturalHeight, frame, stopped = false;
    const set = (element, name, value) => { if (element.style[name] !== value) element.style[name] = value; };
    function position() {
        if (stopped || !menu.matches(':popover-open')) return;
        const v = window.visualViewport;
        const x = v?.offsetLeft ?? 0, y = v?.offsetTop ?? 0;
        const width = v?.width ?? innerWidth, height = v?.height ?? innerHeight;
        set(menu, 'width', `${Math.min(trigger.getBoundingClientRect().width, width - 16)}px`);
        if (naturalHeight === undefined) {
            set(menu, 'maxHeight', 'none');
            naturalHeight = Math.min(preferredHeight, menu.scrollHeight + 2);
        }
        const overlayHeight = Math.min(preferredHeight, naturalHeight + (heading ? 40 : 0));
        const chrome = panel.querySelector(':scope > header').offsetHeight + panel.querySelector(':scope > footer').offsetHeight + 2;
        set(panel, 'minHeight', `${Math.max(0, Math.min(height - 16, Math.max(initialPanelHeight, chrome + overlayHeight + 8)))}px`);
        const body = fields.getBoundingClientRect(), anchor = trigger.getBoundingClientRect();
        const top = Math.max(y + 8, body.top + 4), bottom = Math.min(y + height - 8, body.bottom - 4);
        const above = Math.max(0, Math.min(anchor.top, bottom) - top - 6);
        const below = Math.max(0, bottom - Math.max(anchor.bottom, top) - 6);
        const up = below < naturalHeight && above > below;
        const overlay = Math.max(above, below) < naturalHeight;
        if (heading) heading.hidden = !overlay;
        const placement = overlay ? 'overlay' : up ? 'top' : 'bottom';
        if (menu.dataset.filterPlacement !== placement) menu.dataset.filterPlacement = placement;
        const available = Math.max(0, overlay ? bottom - top : up ? above : below);
        const actualHeight = Math.min(overlay ? overlayHeight : naturalHeight, available);
        // Keep this height throughout search/checkbox renders; only options scroll.
        set(menu, 'height', `${actualHeight}px`);
        set(menu, 'maxHeight', `${available}px`);
        set(menu, 'left', `${Math.max(x + 8, Math.min(anchor.left, x + width - menu.offsetWidth - 8))}px`);
        const menuTop = overlay ? top : up ? anchor.top - actualHeight - 6 : anchor.bottom + 6;
        set(menu, 'top', `${Math.max(top, Math.min(menuTop, bottom - actualHeight))}px`);
    }
    function schedule() { if (!stopped) { cancelAnimationFrame(frame); frame = requestAnimationFrame(position); } }
    const resize = new ResizeObserver(schedule);
    resize.observe(fields); resize.observe(trigger);
    const movement = new MutationObserver(schedule);
    movement.observe(panel, { attributes:true, attributeFilter:['style'] });
    button?.addEventListener('click', close);
    position(); schedule();
    return {
        position,
        disconnect() {
            if (stopped) return;
            stopped = true; cancelAnimationFrame(frame); resize.disconnect(); movement.disconnect();
            button?.removeEventListener('click', close);
            set(panel, 'minHeight', originalMinHeight);
        }
    };
}
