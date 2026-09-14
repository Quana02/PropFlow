// ============================================================
// PropFlow Resident Portal - Interactive Logic
// ============================================================

// Module-level state (shared between drag handler and tab nav)
let hasDragged = false;

// ============================================================
// THEME MANAGEMENT
// ============================================================
window.applyResidentTheme = function(isDark) {
    const sunWrapper = document.getElementById('sunWrapper');
    const moonWrapper = document.getElementById('moonWrapper');
    const toggleBtn = document.getElementById('theme-toggle-btn');

    if (isDark) {
        document.documentElement.classList.add('dark');
        document.body.classList.add('dark');
        document.documentElement.setAttribute('data-theme', 'dark');

        if (sunWrapper) {
            sunWrapper.classList.add('opacity-0', 'scale-50', '-rotate-90');
            sunWrapper.classList.remove('opacity-100', 'scale-100', 'rotate-0');
        }
        if (moonWrapper) {
            moonWrapper.classList.add('opacity-100', 'scale-100', 'rotate-0');
            moonWrapper.classList.remove('opacity-0', 'scale-50', 'rotate-90');
        }
        if (toggleBtn) {
            toggleBtn.setAttribute('title', 'Chuyển sang giao diện Sáng');
        }
        localStorage.setItem('propflow_theme', 'dark');
    } else {
        document.documentElement.classList.remove('dark');
        document.body.classList.remove('dark');
        document.documentElement.setAttribute('data-theme', 'light');

        if (sunWrapper) {
            sunWrapper.classList.remove('opacity-0', 'scale-50', '-rotate-90');
            sunWrapper.classList.add('opacity-100', 'scale-100', 'rotate-0');
        }
        if (moonWrapper) {
            moonWrapper.classList.remove('opacity-100', 'scale-100', 'rotate-0');
            moonWrapper.classList.add('opacity-0', 'scale-50', 'rotate-90');
        }
        if (toggleBtn) {
            toggleBtn.setAttribute('title', 'Chuyển sang giao diện Tối');
        }
        localStorage.setItem('propflow_theme', 'light');
    }
};

window.toggleTheme = function(e) {
    if (e && e.stopPropagation) e.stopPropagation();
    const isCurrentlyDark = document.documentElement.classList.contains('dark')
        || document.documentElement.getAttribute('data-theme') === 'dark';
    window.applyResidentTheme(!isCurrentlyDark);
};

// ============================================================
// REAL-TIME CLOCK
// ============================================================
function startClock() {
    function updateClock() {
        const now = new Date();
        let hours = now.getHours();
        const minutes = String(now.getMinutes()).padStart(2, '0');
        const period = hours >= 12 ? 'PM' : 'AM';
        hours = hours % 12 || 12;
        const formattedHours = String(hours).padStart(2, '0');

        const clockTimeEl = document.getElementById('clockTime');
        const clockPeriodEl = document.getElementById('clockPeriod');
        const clockDateEl = document.getElementById('clockDate');

        if (clockTimeEl) clockTimeEl.textContent = `${formattedHours}:${minutes}`;
        if (clockPeriodEl) clockPeriodEl.textContent = period;

        if (clockDateEl) {
            const days = ['Chủ Nhật', 'Thứ Hai', 'Thứ Ba', 'Thứ Tư', 'Thứ Năm', 'Thứ Sáu', 'Thứ Bảy'];
            const d = now;
            clockDateEl.textContent = `${days[d.getDay()]}, ${d.getDate()} Tháng ${d.getMonth() + 1}, ${d.getFullYear()}`;
        }
    }
    updateClock();
    setInterval(updateClock, 1000);
}

// ============================================================
// TAB NAVIGATION
// ============================================================
window.switchTab = function(tabId) {
    const tabs = ['home', 'processing', 'notifications'];

    tabs.forEach(tab => {
        const contentEl = document.getElementById(`tab${capitalize(tab)}Content`);
        const navBtnEl = document.getElementById(`nav${capitalize(tab)}`);
        if (!contentEl || !navBtnEl) return;

        if (tab === tabId) {
            contentEl.classList.remove('hidden');
            navBtnEl.className = 'nav-tab-btn relative group p-3 rounded-2xl text-white bg-sky-600 dark:bg-cyan-500 dark:text-slate-950 shadow-lg shadow-sky-500/40 dark:shadow-cyan-500/40 transition-all duration-300 scale-105';
        } else {
            contentEl.classList.add('hidden');
            navBtnEl.className = 'nav-tab-btn relative group p-3 rounded-2xl text-slate-500 hover:text-slate-800 dark:text-slate-400 dark:hover:text-white hover:bg-white/60 dark:hover:bg-slate-800 transition-all duration-300';
        }
    });
};

window.handleNavTabClick = function(tabId) {
    if (hasDragged) return;
    switchTab(tabId);
};

function capitalize(str) {
    return str.charAt(0).toUpperCase() + str.slice(1);
}

// ============================================================
// DROPDOWN & MODAL
// ============================================================
window.openProfileModal = function() {
    const profilePopover = document.getElementById('profilePopover');
    const dropdownArrow = document.getElementById('dropdownArrow');
    const profileModal = document.getElementById('profileModal');
    if (profilePopover) profilePopover.classList.add('hidden');
    if (dropdownArrow) dropdownArrow.classList.remove('rotate-180');
    if (profileModal) profileModal.classList.remove('hidden');
    document.body.classList.add('overflow-hidden');
};

window.closeProfileModal = function() {
    const profileModal = document.getElementById('profileModal');
    if (profileModal) profileModal.classList.add('hidden');
    document.body.classList.remove('overflow-hidden');
};

window.saveProfileModal = function() {
    alert('Đã cập nhật thông tin cư dân thành công!');
    closeProfileModal();
};

function initDropdownAndModal() {
    const profileDropdownBtn = document.getElementById('profileDropdownBtn');
    const profilePopover = document.getElementById('profilePopover');
    const dropdownArrow = document.getElementById('dropdownArrow');
    const openProfileModalBtn = document.getElementById('openProfileModalBtn');
    const closeProfileModalBtn = document.getElementById('closeProfileModalBtn');
    const modalBackdrop = document.getElementById('modalBackdrop');

    if (profileDropdownBtn && profilePopover) {
        profileDropdownBtn.addEventListener('click', (e) => {
            e.stopPropagation();
            const isHidden = profilePopover.classList.contains('hidden');
            if (isHidden) {
                profilePopover.classList.remove('hidden');
                if (dropdownArrow) dropdownArrow.classList.add('rotate-180');
            } else {
                profilePopover.classList.add('hidden');
                if (dropdownArrow) dropdownArrow.classList.remove('rotate-180');
            }
        });

        document.addEventListener('click', (e) => {
            if (!profilePopover.contains(e.target) && !profileDropdownBtn.contains(e.target)) {
                profilePopover.classList.add('hidden');
                if (dropdownArrow) dropdownArrow.classList.remove('rotate-180');
            }
        });
    }

    if (openProfileModalBtn) openProfileModalBtn.addEventListener('click', openProfileModal);
    if (closeProfileModalBtn) closeProfileModalBtn.addEventListener('click', closeProfileModal);
    if (modalBackdrop) modalBackdrop.addEventListener('click', closeProfileModal);
}

// ============================================================
// AMBIENT PARTICLE CANVAS
// ============================================================
function initParticleCanvas() {
    const canvas = document.getElementById('ambientCanvas');
    if (!canvas) return;

    // Clear any existing canvas context to avoid duplicate animations on re-init
    if (canvas._residentAnimating) return;
    canvas._residentAnimating = true;

    const ctx = canvas.getContext('2d', { alpha: true });

    let width = 0, height = 0;
    let dpr = Math.min(window.devicePixelRatio || 1, 2);

    function resizeCanvas() {
        dpr = Math.min(window.devicePixelRatio || 1, 2);
        width = window.innerWidth;
        height = window.innerHeight;
        canvas.width = Math.floor(width * dpr);
        canvas.height = Math.floor(height * dpr);
        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    }

    window.addEventListener('resize', resizeCanvas, { passive: true });
    resizeCanvas();

    const prefersReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    const pointer = { x: -9999, y: -9999, targetX: -9999, targetY: -9999, radius: 140, active: false };

    function updatePointerPos(x, y) {
        pointer.targetX = x;
        pointer.targetY = y;
        if (!pointer.active) { pointer.x = x; pointer.y = y; }
        pointer.active = true;
    }

    window.addEventListener('mousemove', (e) => updatePointerPos(e.clientX, e.clientY), { passive: true });
    window.addEventListener('touchmove', (e) => {
        if (e.touches && e.touches[0]) updatePointerPos(e.touches[0].clientX, e.touches[0].clientY);
    }, { passive: true });
    window.addEventListener('touchend', () => { pointer.active = false; });
    document.addEventListener('mouseleave', () => { pointer.active = false; });

    class FloatingParticle {
        constructor() { this.reset(true); }

        reset(init = false) {
            this.x = init ? Math.random() * width : (Math.random() < 0.5 ? -15 : width + 15);
            this.y = Math.random() * height;
            this.baseRadius = Math.random() * 2.2 + 1.2;
            this.radius = this.baseRadius;
            const speed = prefersReducedMotion ? 0.04 : (Math.random() * 0.42 + 0.18);
            const angle = Math.random() * Math.PI * 2;
            this.vx = Math.cos(angle) * speed;
            this.vy = Math.sin(angle) * speed;
            this.fx = 0; this.fy = 0;
            this.baseAlpha = Math.random() * 0.32 + 0.18;
            this.alpha = this.baseAlpha;
            this.pulseSeed = Math.random() * 100;
            this.pulseSpeed = Math.random() * 0.02 + 0.008;
            this.colorType = Math.floor(Math.random() * 3);
        }

        update() {
            if (!prefersReducedMotion) {
                this.pulseSeed += this.pulseSpeed;
                this.alpha = this.baseAlpha + Math.sin(this.pulseSeed) * 0.12;
            }
            if (pointer.active) {
                const dx = pointer.x - this.x;
                const dy = pointer.y - this.y;
                const dist = Math.hypot(dx, dy);
                if (dist < pointer.radius && dist > 1) {
                    const force = (1 - dist / pointer.radius);
                    const normX = dx / dist;
                    const normY = dy / dist;
                    if (dist < 45) {
                        const repel = (1 - dist / 45) * 1.8;
                        this.fx -= normX * repel;
                        this.fy -= normY * repel;
                    } else {
                        this.fx += normX * force * 0.35 + -normY * force * 0.22;
                        this.fy += normY * force * 0.35 + normX * force * 0.22;
                    }
                }
            }
            this.fx *= 0.91; this.fy *= 0.91;
            this.x += this.vx + this.fx;
            this.y += this.vy + this.fy;
            if (this.x < -30) this.x = width + 25;
            if (this.x > width + 30) this.x = -25;
            if (this.y < -30) this.y = height + 25;
            if (this.y > height + 30) this.y = -25;
        }

        draw(isDark) {
            ctx.save();
            const a = Math.max(0.08, Math.min(0.65, this.alpha));
            let coreColor, glowColor;
            if (isDark) {
                if (this.colorType === 0) { coreColor = `rgba(0,242,255,${a * 1.4})`; glowColor = `rgba(0,242,255,${a * 0.5})`; }
                else if (this.colorType === 1) { coreColor = `rgba(56,189,248,${a * 1.2})`; glowColor = `rgba(14,165,233,${a * 0.4})`; }
                else { coreColor = `rgba(99,102,241,${a * 1.25})`; glowColor = `rgba(79,70,229,${a * 0.35})`; }
            } else {
                if (this.colorType === 0) { coreColor = `rgba(2,132,199,${a * 0.95})`; glowColor = `rgba(14,165,233,${a * 0.28})`; }
                else if (this.colorType === 1) { coreColor = `rgba(79,70,229,${a * 0.85})`; glowColor = `rgba(99,102,241,${a * 0.22})`; }
                else { coreColor = `rgba(13,148,136,${a * 0.85})`; glowColor = `rgba(20,184,166,${a * 0.22})`; }
            }
            const glowRad = this.radius * 3.2;
            const gradient = ctx.createRadialGradient(this.x, this.y, this.radius * 0.3, this.x, this.y, glowRad);
            gradient.addColorStop(0, coreColor);
            gradient.addColorStop(0.5, glowColor);
            gradient.addColorStop(1, 'rgba(255,255,255,0)');
            ctx.fillStyle = gradient;
            ctx.beginPath();
            ctx.arc(this.x, this.y, glowRad, 0, Math.PI * 2);
            ctx.fill();
            ctx.fillStyle = coreColor;
            ctx.beginPath();
            ctx.arc(this.x, this.y, this.radius * 0.8, 0, Math.PI * 2);
            ctx.fill();
            ctx.restore();
        }
    }

    const isMobile = window.innerWidth < 768;
    const particleCount = isMobile
        ? Math.min(Math.max(Math.floor((window.innerWidth * window.innerHeight) / 14000) + 35, 52), 65)
        : Math.min(Math.max(Math.floor((window.innerWidth * window.innerHeight) / 9500) + 40, 105), 120);
    const particles = [];
    for (let i = 0; i < particleCount; i++) particles.push(new FloatingParticle());

    const CONNECTION_DIST = 145;

    function renderScene() {
        ctx.clearRect(0, 0, width, height);
        const isDark = document.documentElement.classList.contains('dark') || document.documentElement.getAttribute('data-theme') === 'dark';

        if (pointer.active) {
            pointer.x += (pointer.targetX - pointer.x) * 0.18;
            pointer.y += (pointer.targetY - pointer.y) * 0.18;
        }

        ctx.lineWidth = 0.8;
        const pLen = particles.length;
        for (let i = 0; i < pLen; i++) {
            for (let j = i + 1; j < pLen; j++) {
                const dx = particles[i].x - particles[j].x;
                const dy = particles[i].y - particles[j].y;
                const distSq = dx * dx + dy * dy;
                if (distSq < CONNECTION_DIST * CONNECTION_DIST) {
                    const lineAlpha = (1 - Math.sqrt(distSq) / CONNECTION_DIST) * (isDark ? 0.22 : 0.09);
                    ctx.strokeStyle = isDark ? `rgba(0,242,255,${lineAlpha})` : `rgba(2,132,199,${lineAlpha})`;
                    ctx.beginPath();
                    ctx.moveTo(particles[i].x, particles[i].y);
                    ctx.lineTo(particles[j].x, particles[j].y);
                    ctx.stroke();
                }
            }
        }

        for (let i = 0; i < pLen; i++) {
            particles[i].update();
            particles[i].draw(isDark);
        }
        requestAnimationFrame(renderScene);
    }
    requestAnimationFrame(renderScene);
}

// ============================================================
// DRAGGABLE & SNAPPABLE FLOATING NAV
// ============================================================
function initDraggableNav() {
    const nav = document.getElementById('floating-nav');
    const container = document.getElementById('navContainer');
    const dragHandle = document.getElementById('navDragHandle');
    if (!nav || !container) return;

    let isDragging = false;
    let startPointerX = 0, startPointerY = 0;
    let initialNavLeft = 0, initialNavTop = 0;
    let totalMovedDistance = 0;
    let currentSide = 'right';

    function updateTooltips(side) {
        nav.querySelectorAll('.nav-tooltip').forEach(tp => {
            if (side === 'left') {
                tp.classList.remove('right-full', 'mr-3');
                tp.classList.add('left-full', 'ml-3');
            } else {
                tp.classList.remove('left-full', 'ml-3');
                tp.classList.add('right-full', 'mr-3');
            }
        });
    }

    function onPointerDown(e) {
        const clientX = e.touches ? e.touches[0].clientX : e.clientX;
        const clientY = e.touches ? e.touches[0].clientY : e.clientY;
        isDragging = true;
        hasDragged = false;
        totalMovedDistance = 0;
        startPointerX = clientX;
        startPointerY = clientY;
        nav.classList.remove('is-snapping');
        const rect = nav.getBoundingClientRect();
        initialNavLeft = rect.left;
        initialNavTop = rect.top;
        nav.style.transform = 'none';
        nav.style.right = 'auto';
        nav.style.left = `${initialNavLeft}px`;
        nav.style.top = `${initialNavTop}px`;
        document.addEventListener('mousemove', onPointerMove, { passive: false });
        document.addEventListener('mouseup', onPointerUp);
        document.addEventListener('touchmove', onPointerMove, { passive: false });
        document.addEventListener('touchend', onPointerUp);
    }

    function onPointerMove(e) {
        if (!isDragging) return;
        const clientX = e.touches ? e.touches[0].clientX : e.clientX;
        const clientY = e.touches ? e.touches[0].clientY : e.clientY;
        const deltaX = clientX - startPointerX;
        const deltaY = clientY - startPointerY;
        totalMovedDistance = Math.hypot(deltaX, deltaY);
        if (totalMovedDistance > 5) {
            hasDragged = true;
            if (e.cancelable) e.preventDefault();
            container.classList.add('scale-105', 'ring-2', 'ring-sky-400/40');
            nav.classList.add('cursor-grabbing');
            if (dragHandle) { dragHandle.classList.remove('cursor-grab'); dragHandle.classList.add('cursor-grabbing'); }
        }
        const newLeft = Math.max(12, Math.min(window.innerWidth - nav.offsetWidth - 12, initialNavLeft + deltaX));
        const newTop = Math.max(72, Math.min(window.innerHeight - nav.offsetHeight - 72, initialNavTop + deltaY));
        nav.style.left = `${newLeft}px`;
        nav.style.top = `${newTop}px`;
    }

    function onPointerUp() {
        if (!isDragging) return;
        isDragging = false;
        document.removeEventListener('mousemove', onPointerMove);
        document.removeEventListener('mouseup', onPointerUp);
        document.removeEventListener('touchmove', onPointerMove);
        document.removeEventListener('touchend', onPointerUp);
        container.classList.remove('scale-105', 'ring-2', 'ring-sky-400/40');
        nav.classList.remove('cursor-grabbing');
        if (dragHandle) { dragHandle.classList.remove('cursor-grabbing'); dragHandle.classList.add('cursor-grab'); }
        if (!hasDragged) return;
        const navRect = nav.getBoundingClientRect();
        const margin = 24;
        const targetLeft = navRect.left + navRect.width / 2 < window.innerWidth / 2
            ? (currentSide = 'left', margin)
            : (currentSide = 'right', window.innerWidth - navRect.width - margin);
        const targetTop = Math.max(80, Math.min(window.innerHeight - navRect.height - 80, navRect.top));
        nav.classList.add('is-snapping');
        nav.style.left = `${targetLeft}px`;
        nav.style.top = `${targetTop}px`;
        updateTooltips(currentSide);
        setTimeout(() => { hasDragged = false; }, 80);
    }

    nav.addEventListener('mousedown', onPointerDown);
    nav.addEventListener('touchstart', onPointerDown, { passive: true });
    updateTooltips('right');
}

// ============================================================
// MAIN INIT (called from Blazor OnAfterRenderAsync)
// ============================================================
window.initResidentPortal = function() {
    // Restore theme from localStorage
    const savedTheme = localStorage.getItem('propflow_theme')
        || (window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light');
    window.applyResidentTheme(savedTheme === 'dark');

    startClock();
    initDropdownAndModal();
    initParticleCanvas();
    initDraggableNav();
};
