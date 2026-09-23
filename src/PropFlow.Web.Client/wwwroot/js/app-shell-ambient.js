export function initialize(canvas, toggle) {
    const context = canvas.getContext("2d", { alpha: true });
    if (!context) return { dispose() {} };

    const shell = canvas.closest(".app-shell");
    const preferenceKey = "propflow-app-ambient-motion";
    let savedPreference = null;
    try { savedPreference = window.localStorage.getItem(preferenceKey); } catch { /* Storage is optional. */ }
    let animate = savedPreference !== "off";
    const particles = [];
    let width = 0;
    let height = 0;
    let frame = 0;
    let lastFrame = 0;
    let disposed = false;
    const pointer = { x: -1000, y: -1000, targetX: -1000, targetY: -1000, active: false };

    function movePointer(event) {
        pointer.targetX = event.clientX;
        pointer.targetY = event.clientY;
        if (!pointer.active) {
            pointer.x = pointer.targetX;
            pointer.y = pointer.targetY;
        }
        pointer.active = true;
    }

    function leavePointer() { pointer.active = false; }

    function resize() {
        width = window.innerWidth;
        height = window.innerHeight;
        const ratio = Math.min(window.devicePixelRatio || 1, 2);
        canvas.width = Math.round(width * ratio);
        canvas.height = Math.round(height * ratio);
        context.setTransform(ratio, 0, 0, ratio, 0, 0);

        const count = width < 768
            ? Math.min(Math.max(Math.floor((width * height) / 14000) + 35, 52), 65)
            : Math.min(Math.max(Math.floor((width * height) / 9500) + 40, 105), 120);
        while (particles.length < count) {
            const angle = Math.random() * Math.PI * 2;
            const speed = 0.38 + Math.random() * 0.55;
            particles.push({
                x: Math.random() * width,
                y: Math.random() * height,
                vx: Math.cos(angle) * speed,
                vy: Math.sin(angle) * speed,
                fx: 0,
                fy: 0,
                size: 1.2 + Math.random() * 1.4,
                tint: Math.random() < 0.22,
                phase: Math.random() * Math.PI * 2
            });
        }
        particles.length = count;
        render(false);
    }

    function render(step) {
        const dark = shell?.classList.contains("app-shell-dark");
        context.clearRect(0, 0, width, height);

        if (step && pointer.active) {
            const easing = 1 - Math.pow(0.8, step);
            pointer.x += (pointer.targetX - pointer.x) * easing;
            pointer.y += (pointer.targetY - pointer.y) * easing;
        }
        for (const particle of particles) {
            if (!step) continue;
            if (pointer.active) {
                const dx = pointer.x - particle.x;
                const dy = pointer.y - particle.y;
                const distance = Math.hypot(dx, dy);
                if (distance > 28 && distance < 240) {
                    const proximity = 1 - distance / 240;
                    const strength = 0.24 * proximity * proximity;
                    particle.fx += dx / distance * strength * step;
                    particle.fy += dy / distance * strength * step;
                } else if (distance > 1 && distance <= 28) {
                    const strength = 0.12 * (1 - distance / 28);
                    particle.fx -= dx / distance * strength * step;
                    particle.fy -= dy / distance * strength * step;
                }
            }
            particle.fx *= Math.pow(0.92, step);
            particle.fy *= Math.pow(0.92, step);
            particle.x += (particle.vx + particle.fx) * step;
            particle.y += (particle.vy + particle.fy) * step;
            if (particle.x < -20) particle.x = width + 20;
            if (particle.x > width + 20) particle.x = -20;
            if (particle.y < -20) particle.y = height + 20;
            if (particle.y > height + 20) particle.y = -20;
        }

        const connectionDistance = width < 768 ? 150 : 172;
        const connectionDistanceSquared = connectionDistance * connectionDistance;
        for (let i = 0; i < particles.length; i++) {
            const a = particles[i];
            for (let j = i + 1; j < particles.length; j++) {
                const b = particles[j];
                const dx = a.x - b.x;
                const dy = a.y - b.y;
                const distanceSquared = dx * dx + dy * dy;
                if (distanceSquared > connectionDistanceSquared) continue;
                const distance = Math.sqrt(distanceSquared);
                const alpha = (1 - distance / connectionDistance) * (dark ? 0.34 : 0.2);
                context.strokeStyle = dark ? `rgba(56, 189, 248, ${alpha})` : `rgba(2, 132, 199, ${alpha})`;
                context.lineWidth = 0.9;
                context.beginPath();
                context.moveTo(a.x, a.y);
                context.lineTo(b.x, b.y);
                context.stroke();
            }
        }

        for (const particle of particles) {
            const color = particle.tint ? (dark ? "129, 140, 248" : "79, 70, 229") : (dark ? "103, 232, 249" : "2, 132, 199");
            const pulse = 0.78 + 0.22 * Math.sin(particle.phase);
            if (step) particle.phase += 0.025 * step;
            context.shadowColor = `rgba(${color}, ${dark ? 0.85 : 0.65})`;
            context.shadowBlur = dark ? 14 : 10;
            context.fillStyle = `rgba(${color}, ${(dark ? 0.95 : 0.8) * pulse})`;
            context.beginPath();
            context.arc(particle.x, particle.y, particle.size, 0, Math.PI * 2);
            context.fill();
        }
        context.shadowBlur = 0;
    }

    function tick(now) {
        if (disposed || !animate || document.hidden) return;
        const step = lastFrame ? Math.min((now - lastFrame) / 16.67, 2) : 1;
        lastFrame = now;
        render(step);
        frame = requestAnimationFrame(tick);
    }

    function updateToggle() {
        toggle.setAttribute("aria-pressed", String(animate));
        const label = animate ? "Tạm dừng chuyển động nền" : "Bật chuyển động nền";
        toggle.setAttribute("aria-label", label);
        toggle.title = label;
    }

    function sync() {
        cancelAnimationFrame(frame);
        frame = 0;
        lastFrame = 0;
        render(0);
        updateToggle();
        if (animate && !document.hidden) frame = requestAnimationFrame(tick);
    }

    function toggleMotion() {
        animate = !animate;
        savedPreference = animate ? "on" : "off";
        try { window.localStorage.setItem(preferenceKey, savedPreference); } catch { /* Storage is optional. */ }
        sync();
    }

    const themeObserver = new MutationObserver(() => render(false));
    if (shell) themeObserver.observe(shell, { attributes: true, attributeFilter: ["class"] });
    window.addEventListener("resize", resize, { passive: true });
    window.addEventListener("pointermove", movePointer, { passive: true });
    document.addEventListener("pointerleave", leavePointer);
    toggle.addEventListener("click", toggleMotion);
    document.addEventListener("visibilitychange", sync);
    resize();
    sync();

    return {
        dispose() {
            disposed = true;
            cancelAnimationFrame(frame);
            themeObserver.disconnect();
            window.removeEventListener("resize", resize);
            window.removeEventListener("pointermove", movePointer);
            document.removeEventListener("pointerleave", leavePointer);
            toggle.removeEventListener("click", toggleMotion);
            document.removeEventListener("visibilitychange", sync);
        }
    };
}
