export function bind(root) {
    if (!root) return { dispose() {} };
    const tooltip = root.querySelector('.action-tooltip');
    const events = new AbortController();
    const { signal } = events;
    let trigger;

    function hide() {
        conceal();
        trigger = null;
    }

    function conceal() {
        if (tooltip.matches(':popover-open')) tooltip.hidePopover();
    }

    function position() {
        if (!trigger) return;
        const anchor = trigger.getBoundingClientRect();
        const table = root.getBoundingClientRect();
        if (!anchor.width || anchor.right <= Math.max(0, table.left) ||
            anchor.left >= Math.min(innerWidth, table.right) || anchor.bottom <= 0 || anchor.top >= innerHeight) {
            conceal();
            return;
        }
        if (!tooltip.matches(':popover-open')) tooltip.showPopover();
        const { width, height } = tooltip.getBoundingClientRect();
        const x = Math.max(8, Math.min(innerWidth - width - 8, anchor.left + (anchor.width - width) / 2));
        const y = anchor.top - height - 6;
        tooltip.style.left = `${x}px`;
        tooltip.style.top = `${y < 8 ? anchor.bottom + 6 : y}px`;
    }

    function show(event) {
        const button = event.target?.closest('[data-action-tooltip]');
        if (!button || !root.contains(button) || button.disabled) return;
        trigger = button;
        tooltip.textContent = button.dataset.actionTooltip;
        position();
    }

    function leave(event) {
        if (trigger?.contains(event.target) && !trigger.contains(event.relatedTarget)) hide();
    }

    root.addEventListener('pointerover', show, { signal });
    root.addEventListener('pointerout', leave, { signal });
    root.addEventListener('focusin', show, { signal });
    root.addEventListener('focusout', leave, { signal });
    root.addEventListener('click', event => {
        hide();
        const menu = event.target.closest('.touch-actions button')?.closest('details');
        if (menu) {
            menu.open = false;
            menu.querySelector('summary').focus();
        }
    }, { signal });
    root.addEventListener('keydown', event => { if (event.key === 'Escape') hide(); }, { signal });
    window.addEventListener('scroll', position, { signal, capture: true, passive: true });
    window.addEventListener('resize', position, { signal, passive: true });
    show({ target: document.activeElement });
    return { dispose() { hide(); events.abort(); } };
}
