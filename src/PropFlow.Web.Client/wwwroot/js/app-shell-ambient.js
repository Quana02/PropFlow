let canvasState = null;

export function initialize(canvas, toggle) {
    if (canvasState?.canvas === canvas) return canvasState;
    canvasState?.dispose();

    const context = canvas.getContext("2d", { alpha: true });
    if (!context) return { dispose() {} };

    const shell = canvas.closest(".app-shell");
    const preferenceKey = "propflow-app-ambient-motion";
    const prefersReducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

    let savedPreference = null;
    try {
        savedPreference = window.localStorage.getItem(preferenceKey);
    } catch {
        // Storage is optional.
    }

    let animate = savedPreference !== "off";
    let width = 0;
    let height = 0;
    let dpr = Math.min(window.devicePixelRatio || 1, 2);
    let frame = 0;
    let disposed = false;

    const pointer = {
        x: -9999,
        y: -9999,
        targetX: -9999,
        targetY: -9999,
        radius: 140,
        active: false
    };

    function currentThemeIsDark() {
        return shell?.classList.contains("app-shell-dark") ?? false;
    }

    function updatePointerPosition(x, y) {
        pointer.targetX = x;
        pointer.targetY = y;

        if (!pointer.active) {
            pointer.x = x;
            pointer.y = y;
        }

        pointer.active = true;
    }

    const onMouseMove = event => {
        updatePointerPosition(event.clientX, event.clientY);
    };

    const onTouchMove = event => {
        if (event.touches?.[0]) {
            updatePointerPosition(event.touches[0].clientX, event.touches[0].clientY);
        }
    };

    const onTouchStart = event => {
        if (event.touches?.[0]) {
            updatePointerPosition(event.touches[0].clientX, event.touches[0].clientY);
        }
    };

    const onPointerLeave = () => {
        pointer.active = false;
    };

    class FloatingParticle {
        constructor() {
            this.reset(true);
        }

        reset(initial = false) {
            this.x = initial
                ? Math.random() * width
                : Math.random() < 0.5
                    ? -15
                    : width + 15;

            this.y = Math.random() * height;
            this.baseRadius = Math.random() * 2.2 + 1.2;
            this.radius = this.baseRadius;

            const speed = prefersReducedMotion
                ? 0.04
                : Math.random() * 0.8 + 0.2;
            const angle = Math.random() * Math.PI * 2;

            this.vx = Math.cos(angle) * speed;
            this.vy = Math.sin(angle) * speed;
            this.fx = 0;
            this.fy = 0;
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
                const distance = Math.hypot(dx, dy);

                if (distance < pointer.radius && distance > 1) {
                    const force = 1 - distance / pointer.radius;
                    const normalX = dx / distance;
                    const normalY = dy / distance;

                    if (distance < 45) {
                        const repel = (1 - distance / 45) * 1.8;
                        this.fx -= normalX * repel;
                        this.fy -= normalY * repel;
                    } else {
                        this.fx += normalX * force * 0.35 + -normalY * force * 0.22;
                        this.fy += normalY * force * 0.35 + normalX * force * 0.22;
                    }
                }
            }

            this.fx *= 0.91;
            this.fy *= 0.91;
            this.x += this.vx + this.fx;
            this.y += this.vy + this.fy;

            if (this.x < -30) this.x = width + 25;
            if (this.x > width + 30) this.x = -25;
            if (this.y < -30) this.y = height + 25;
            if (this.y > height + 30) this.y = -25;
        }

        draw(isDark) {
            context.save();

            const currentAlpha = Math.max(0.08, Math.min(0.65, this.alpha));
            let coreColor;
            let glowColor;

            if (isDark) {
                if (this.colorType === 0) {
                    coreColor = "rgba(0, 242, 255, " + currentAlpha * 1.4 + ")";
                    glowColor = "rgba(0, 242, 255, " + currentAlpha * 0.5 + ")";
                } else if (this.colorType === 1) {
                    coreColor = "rgba(56, 189, 248, " + currentAlpha * 1.2 + ")";
                    glowColor = "rgba(14, 165, 233, " + currentAlpha * 0.4 + ")";
                } else {
                    coreColor = "rgba(99, 102, 241, " + currentAlpha * 1.25 + ")";
                    glowColor = "rgba(79, 70, 229, " + currentAlpha * 0.35 + ")";
                }
            } else if (this.colorType === 0) {
                coreColor = "rgba(2, 132, 199, " + currentAlpha * 0.95 + ")";
                glowColor = "rgba(14, 165, 233, " + currentAlpha * 0.28 + ")";
            } else if (this.colorType === 1) {
                coreColor = "rgba(79, 70, 229, " + currentAlpha * 0.85 + ")";
                glowColor = "rgba(99, 102, 241, " + currentAlpha * 0.22 + ")";
            } else {
                coreColor = "rgba(13, 148, 136, " + currentAlpha * 0.85 + ")";
                glowColor = "rgba(20, 184, 166, " + currentAlpha * 0.22 + ")";
            }

            const glowRadius = this.radius * 3.2;
            const gradient = context.createRadialGradient(
                this.x,
                this.y,
                this.radius * 0.3,
                this.x,
                this.y,
                glowRadius);

            gradient.addColorStop(0, coreColor);
            gradient.addColorStop(0.5, glowColor);
            gradient.addColorStop(1, "rgba(255,255,255,0)");

            context.fillStyle = gradient;
            context.beginPath();
            context.arc(this.x, this.y, glowRadius, 0, Math.PI * 2);
            context.fill();

            context.fillStyle = coreColor;
            context.beginPath();
            context.arc(this.x, this.y, this.radius * 0.8, 0, Math.PI * 2);
            context.fill();
            context.restore();
        }
    }

    const particles = [];
    const connectionDistance = 145;

    function desiredParticleCount() {
        return window.innerWidth < 768
            ? Math.min(
                Math.max(Math.floor((window.innerWidth * window.innerHeight) / 14000) + 35, 52),
                65)
            : Math.min(
                Math.max(Math.floor((window.innerWidth * window.innerHeight) / 9500) + 40, 105),
                120);
    }

    function syncParticleCount() {
        const count = desiredParticleCount();

        while (particles.length < count) {
            particles.push(new FloatingParticle());
        }

        particles.length = count;
    }

    function resizeCanvas() {
        dpr = Math.min(window.devicePixelRatio || 1, 2);
        width = window.innerWidth;
        height = window.innerHeight;

        canvas.width = Math.floor(width * dpr);
        canvas.height = Math.floor(height * dpr);
        canvas.style.width = width + "px";
        canvas.style.height = height + "px";

        context.setTransform(dpr, 0, 0, dpr, 0, 0);
        syncParticleCount();
        drawScene(false);
    }

    function drawScene(updateParticles) {
        context.clearRect(0, 0, width, height);
        const isDark = currentThemeIsDark();

        if (updateParticles && pointer.active) {
            pointer.x += (pointer.targetX - pointer.x) * 0.18;
            pointer.y += (pointer.targetY - pointer.y) * 0.18;
        }

        context.lineWidth = 0.8;
        const connectionDistanceSquared = connectionDistance * connectionDistance;

        for (let index = 0; index < particles.length; index++) {
            const first = particles[index];

            for (let otherIndex = index + 1; otherIndex < particles.length; otherIndex++) {
                const second = particles[otherIndex];
                const dx = first.x - second.x;
                const dy = first.y - second.y;
                const distanceSquared = dx * dx + dy * dy;

                if (distanceSquared >= connectionDistanceSquared) continue;

                const distance = Math.sqrt(distanceSquared);
                const alpha = (1 - distance / connectionDistance) * (isDark ? 0.22 : 0.09);
                context.strokeStyle = isDark
                    ? "rgba(0, 242, 255, " + alpha + ")"
                    : "rgba(2, 132, 199, " + alpha + ")";

                context.beginPath();
                context.moveTo(first.x, first.y);
                context.lineTo(second.x, second.y);
                context.stroke();
            }
        }

        for (const particle of particles) {
            if (updateParticles) particle.update();
            particle.draw(isDark);
        }
    }

    function renderScene() {
        if (disposed || !animate || document.hidden) return;

        drawScene(true);
        frame = requestAnimationFrame(renderScene);
    }

    function updateToggle() {
        if (!toggle) return;

        toggle.setAttribute("aria-pressed", String(animate));
        const label = animate ? "Tạm dừng chuyển động nền" : "Bật chuyển động nền";
        toggle.setAttribute("aria-label", label);
        toggle.title = label;
    }

    function syncAnimation() {
        cancelAnimationFrame(frame);
        frame = 0;
        drawScene(false);
        updateToggle();

        if (animate && !document.hidden && !disposed) {
            frame = requestAnimationFrame(renderScene);
        }
    }

    function toggleMotion() {
        animate = !animate;

        try {
            window.localStorage.setItem(preferenceKey, animate ? "on" : "off");
        } catch {
            // Storage is optional.
        }

        syncAnimation();
    }

    const themeObserver = new MutationObserver(() => drawScene(false));
    if (shell) {
        themeObserver.observe(shell, {
            attributes: true,
            attributeFilter: ["class"]
        });
    }

    window.addEventListener("resize", resizeCanvas, { passive: true });
    window.addEventListener("mousemove", onMouseMove, { passive: true });
    window.addEventListener("touchmove", onTouchMove, { passive: true });
    window.addEventListener("touchstart", onTouchStart, { passive: true });
    window.addEventListener("touchend", onPointerLeave);
    document.addEventListener("mouseleave", onPointerLeave);
    document.addEventListener("visibilitychange", syncAnimation);
    toggle?.addEventListener("click", toggleMotion);

    resizeCanvas();
    syncAnimation();

    const handle = {
        canvas,
        dispose() {
            if (disposed) return;
            disposed = true;
            cancelAnimationFrame(frame);
            themeObserver.disconnect();
            window.removeEventListener("resize", resizeCanvas);
            window.removeEventListener("mousemove", onMouseMove);
            window.removeEventListener("touchmove", onTouchMove);
            window.removeEventListener("touchstart", onTouchStart);
            window.removeEventListener("touchend", onPointerLeave);
            document.removeEventListener("mouseleave", onPointerLeave);
            document.removeEventListener("visibilitychange", syncAnimation);
            toggle?.removeEventListener("click", toggleMotion);
            if (canvasState === handle) canvasState = null;
        }
    };

    canvasState = handle;
    return handle;
}
