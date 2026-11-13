export function attach(stage) {
    const portal = stage.closest('.resident-portal');
    const header = portal.querySelector('.resident-header');
    const compact = header.querySelector('.resident-clock-compact');
    let frame = 0, disposed = false;
    function updateTime() {
        if (disposed) return;
        const zone = header.querySelector('[data-building-time-zone]')?.dataset.buildingTimeZone || 'Asia/Ho_Chi_Minh';
        let format;
        try { format = new Intl.DateTimeFormat('en-US', { timeZone: zone, hour: '2-digit', minute: '2-digit', hour12: true }); }
        catch { format = new Intl.DateTimeFormat('en-US', { timeZone: 'Asia/Ho_Chi_Minh', hour: '2-digit', minute: '2-digit', hour12: true }); }
        const now = new Date(), parts = format.formatToParts(now);
        const part = type => parts.find(x => x.type === type)?.value ?? '';
        const time = `${part('hour')}:${part('minute')}`, period = part('dayPeriod');
        const timeZone = format.resolvedOptions().timeZone;
        const date = new Intl.DateTimeFormat('vi-VN', { timeZone, weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' }).format(now);
        stage.querySelector('#clockTime').textContent = time;
        stage.querySelector('#clockPeriod').textContent = period;
        stage.querySelector('#clockDate').textContent = date[0].toUpperCase() + date.slice(1);
        compact.querySelector('[data-clock-time]').textContent = time;
        compact.querySelector('[data-clock-period]').textContent = period;
        compact.querySelector('[data-clock-date]').textContent = new Intl.DateTimeFormat('vi-VN', { timeZone, day: '2-digit', month: '2-digit' }).format(now);
    }
    function updateScroll() {
        frame = 0;
        if (disposed) return;
        const docked = window.scrollY > 32;
        portal.classList.toggle('resident-clock-docked', docked);
        compact.setAttribute('aria-hidden', String(!docked));
        stage.setAttribute('aria-hidden', String(docked));
    }
    const schedule = () => { if (!frame) frame = requestAnimationFrame(updateScroll); };
    window.addEventListener('scroll', schedule, { passive: true });
    window.addEventListener('resize', schedule);
    updateTime(); updateScroll();
    const timer = setInterval(updateTime, 1000);
    return { dispose() {
        disposed = true; clearInterval(timer); cancelAnimationFrame(frame);
        window.removeEventListener('scroll', schedule); window.removeEventListener('resize', schedule);
        portal.classList.remove('resident-clock-docked');
        compact.setAttribute('aria-hidden', 'true'); stage.removeAttribute('aria-hidden');
    } };
}
