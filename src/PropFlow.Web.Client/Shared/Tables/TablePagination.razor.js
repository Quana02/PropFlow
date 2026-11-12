export function connect(navigation, popover) {
    if (!navigation?.isConnected || !popover?.isConnected) {
        return { close() {}, disconnect() {} };
    }
    const trigger = navigation.querySelector('.page-number');
    let listeners;
    let observer;

    function position() {
        const anchor = trigger.getBoundingClientRect();
        const gap = 6;
        const inset = 8;
        const { offsetWidth: width, offsetHeight: height } = popover;
        const above = anchor.top - inset - gap;
        const below = window.innerHeight - anchor.bottom - inset - gap;
        const top = below < height && above > below ? anchor.top - gap - height : anchor.bottom + gap;
        popover.style.left = Math.max(inset, Math.min(anchor.right - width, window.innerWidth - inset - width)) + 'px';
        popover.style.top = Math.max(inset, Math.min(top, window.innerHeight - inset - height)) + 'px';
    }

    function stopPositioning() {
        listeners?.abort();
        observer?.disconnect();
        listeners = observer = undefined;
    }

    function onToggle() {
        stopPositioning();
        if (!popover.matches(':popover-open')) return;
        listeners = new AbortController();
        const options = { signal: listeners.signal, passive: true };
        window.addEventListener('resize', position, options);
        window.addEventListener('scroll', position, { ...options, capture: true });
        observer = new ResizeObserver(position);
        observer.observe(popover);
        position();
    }

    popover.addEventListener('toggle', onToggle);
    onToggle();
    return {
        close() {
            if (popover.matches(':popover-open')) popover.hidePopover();
        },
        disconnect() {
            popover.removeEventListener('toggle', onToggle);
            stopPositioning();
        }
    };
}
