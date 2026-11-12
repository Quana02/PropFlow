export function connect(root, dotnet) {
    const trigger = root.querySelector('.pf-filter-trigger');
    const popover = root.querySelector('.pf-filter-popover');
    const fields = popover.querySelector('.pf-filter-fields');
    const closeButton = popover.querySelector('.pf-filter-close');
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
        const panelHeight = Math.min(height - 2 * inset, Math.max(space, 176));
        popover.style.maxHeight = `${panelHeight}px`;
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

    trigger.addEventListener('click', preventBusyOpen);
    popover.addEventListener('beforetoggle', beforeToggle);
    popover.addEventListener('toggle', toggle);
    return {
        close() { if (popover.matches(':popover-open')) popover.hidePopover(); },
        disconnect() {
            stop();
            trigger.removeEventListener('click', preventBusyOpen);
            popover.removeEventListener('beforetoggle', beforeToggle);
            popover.removeEventListener('toggle', toggle);
        }
    };
}
