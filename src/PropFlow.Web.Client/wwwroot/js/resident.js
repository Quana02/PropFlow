(function (global) {
    "use strict";

    const themeKey = "propflow-theme";

    let clockTimer = null;
    let canvasState = null;
    let outsideClickBound = false;

    function currentTheme() {
        return document.documentElement.classList.contains("dark") ||
            document.documentElement.dataset.theme === "dark"
            ? "dark"
            : "light";
    }

    function applyTheme(theme, persist) {
        const isDark = theme === "dark";
        const root = document.documentElement;

        root.classList.toggle("dark", isDark);
        root.classList.toggle("light", !isDark);
        root.dataset.theme = isDark ? "dark" : "light";

        const buttons = document.querySelectorAll(
            "#theme-toggle-btn, [data-action='toggle-theme'], .theme-toggle-btn"
        );

        buttons.forEach(button => {
            const label = isDark
                ? "Chuyển sang giao diện sáng"
                : "Chuyển sang giao diện tối";

            button.title = label;
            button.setAttribute("aria-label", label);

            const sun = button.querySelector(
                ".theme-icon-sun, .lucide-sun"
            );

            const moon = button.querySelector(
                ".theme-icon-moon, .lucide-moon"
            );

            if (sun && moon) {
                sun.classList.toggle("hidden", isDark);
                moon.classList.toggle("hidden", !isDark);
            }
        });

        if (persist) {
            try {
                localStorage.setItem(
                    themeKey,
                    isDark ? "dark" : "light"
                );
            } catch {
                // localStorage có thể bị chặn
            }
        }
    }

    function initializeTheme() {
        let theme;

        try {
            theme = localStorage.getItem(themeKey);
        } catch {
            // optional
        }

        if (theme !== "dark" && theme !== "light") {
            theme = window.matchMedia(
                "(prefers-color-scheme: dark)"
            ).matches
                ? "dark"
                : "light";
        }

        applyTheme(theme, false);

        document.querySelectorAll(
            "#theme-toggle-btn, [data-action='toggle-theme'], .theme-toggle-btn"
        ).forEach(btn => {

            if (btn.dataset.bound === "true") {
                return;
            }

            btn.dataset.bound = "true";

            btn.addEventListener(
                "click",
                event => api.toggleTheme(event)
            );
        });
    }

    function updateClock() {
        const time =
            document.getElementById("clockTime") ||
            document.getElementById("digital-clock-time");

        const period =
            document.getElementById("clockPeriod") ||
            document.getElementById("digital-clock-period");

        const date =
            document.getElementById("clockDate") ||
            document.getElementById("digital-clock-date");

        if (!time) {
            return;
        }

        const now = new Date();
        const hours = now.getHours();

        time.textContent =
            `${String(hours % 12 || 12).padStart(2, "0")}:` +
            `${String(now.getMinutes()).padStart(2, "0")}`;

        if (period) {
            period.textContent =
                hours >= 12 ? "PM" : "AM";
        }

        if (date) {
            date.textContent =
                new Intl.DateTimeFormat(
                    "vi-VN",
                    {
                        weekday: "long",
                        day: "numeric",
                        month: "long",
                        year: "numeric"
                    }
                )
                    .format(now)
                    .replace(
                        /^./,
                        value => value.toUpperCase()
                    );
        }
    }

    function initializeClock() {
        if (clockTimer) {
            clearInterval(clockTimer);
        }

        updateClock();

        clockTimer =
            window.setInterval(
                updateClock,
                1000
            );
    }

    function initializeProfileMenu() {
        const trigger =
            document.getElementById("profileDropdownBtn") ||
            document.getElementById("userProfileBtn");

        const popover =
            document.getElementById("profilePopover") ||
            document.getElementById("userProfilePopover");

        if (
            !trigger ||
            !popover ||
            trigger.dataset.bound === "true"
        ) {
            return;
        }

        trigger.dataset.bound = "true";

        trigger.addEventListener(
            "click",
            event => {
                event.stopPropagation();

                const isHidden =
                    popover.hidden ||
                    popover.classList.contains("hidden");

                if (isHidden) {
                    popover.classList.remove("hidden");
                    popover.hidden = false;
                } else {
                    popover.classList.add("hidden");
                    popover.hidden = true;
                }

                trigger.setAttribute(
                    "aria-expanded",
                    String(isHidden)
                );
            }
        );

        if (!outsideClickBound) {
            outsideClickBound = true;

            document.addEventListener(
                "click",
                event => {

                    const activeTrigger =
                        document.getElementById("profileDropdownBtn") ||
                        document.getElementById("userProfileBtn");

                    const activePopover =
                        document.getElementById("profilePopover") ||
                        document.getElementById("userProfilePopover");

                    if (
                        !activeTrigger ||
                        !activePopover
                    ) {
                        return;
                    }

                    const isVisible =
                        !activePopover.hidden &&
                        !activePopover.classList.contains("hidden");

                    if (
                        isVisible &&
                        !activeTrigger.contains(event.target) &&
                        !activePopover.contains(event.target)
                    ) {
                        activePopover.hidden = true;
                        activePopover.classList.add("hidden");

                        activeTrigger.setAttribute(
                            "aria-expanded",
                            "false"
                        );
                    }
                }
            );
        }
    }

    function initializeSearchShortcut() {
        if (
            document.documentElement
                .dataset
                .residentSearchBound === "true"
        ) {
            return;
        }

        document.documentElement
            .dataset
            .residentSearchBound = "true";

        document.addEventListener(
            "keydown",
            event => {

                if (
                    (event.ctrlKey || event.metaKey) &&
                    event.key.toLowerCase() === "k"
                ) {
                    const input =
                        document.getElementById("residentSearch") ||
                        document.querySelector(
                            "input[type='search'], input[placeholder*='Tìm kiếm']"
                        );

                    if (input) {
                        event.preventDefault();
                        input.focus();
                    }
                }

                if (event.key === "Escape") {
                    api.closeProfile();
                    api.closeService();
                }
            }
        );
    }

    function initializeFloatingNavigation() {
        const nav =
            document.getElementById("floating-nav") ||
            document.getElementById("floatingNav");

        const handle =
            document.getElementById("navDragHandle") ||
            (
                nav
                    ? nav.querySelector(".drag-handle")
                    : null
            );

        if (
            !nav ||
            !handle ||
            nav.dataset.dragBound === "true"
        ) {
            return;
        }

        nav.dataset.dragBound = "true";

        let dragging = false;
        let moved = false;

        let startX = 0;
        let startY = 0;

        let initialLeft = 0;
        let initialTop = 0;

        handle.addEventListener(
            "pointerdown",
            event => {

                if (window.innerWidth <= 760) {
                    return;
                }

                dragging = true;
                moved = false;

                startX = event.clientX;
                startY = event.clientY;

                const rect =
                    nav.getBoundingClientRect();

                initialLeft = rect.left;
                initialTop = rect.top;

                nav.classList.remove(
                    "is-snapping"
                );

                nav.style.right = "auto";
                nav.style.transform = "none";

                nav.style.left =
                    `${initialLeft}px`;

                nav.style.top =
                    `${initialTop}px`;

                handle.setPointerCapture?.(
                    event.pointerId
                );
            }
        );

        handle.addEventListener(
            "pointermove",
            event => {

                if (!dragging) {
                    return;
                }

                const dx =
                    event.clientX -
                    startX;

                const dy =
                    event.clientY -
                    startY;

                moved =
                    moved ||
                    Math.hypot(dx, dy) > 5;

                const left =
                    Math.max(
                        12,
                        Math.min(
                            window.innerWidth -
                            nav.offsetWidth -
                            12,

                            initialLeft +
                            dx
                        )
                    );

                const top =
                    Math.max(
                        72,
                        Math.min(
                            window.innerHeight -
                            nav.offsetHeight -
                            72,

                            initialTop +
                            dy
                        )
                    );

                nav.style.left =
                    `${left}px`;

                nav.style.top =
                    `${top}px`;
            }
        );

        function finishDrag() {
            if (!dragging) {
                return;
            }

            dragging = false;

            if (!moved) {
                return;
            }

            const rect =
                nav.getBoundingClientRect();

            const onLeft =
                rect.left +
                rect.width / 2 <
                window.innerWidth / 2;

            nav.classList.add(
                "is-snapping"
            );

            nav.style.left =
                `${
                    onLeft
                        ? 24
                        : window.innerWidth -
                          rect.width -
                          24
                }px`;

            nav.querySelectorAll(
                ".resident-nav-tooltip"
            ).forEach(
                tooltip => {

                    tooltip.style.right =
                        onLeft
                            ? "auto"
                            : "calc(100% + .75rem)";

                    tooltip.style.left =
                        onLeft
                            ? "calc(100% + .75rem)"
                            : "auto";
                }
            );
        }

        handle.addEventListener(
            "pointerup",
            finishDrag
        );

        handle.addEventListener(
            "pointercancel",
            finishDrag
        );
    }

    function initializeCanvas() {
        const canvas =
            document.getElementById("ambientCanvas") ||
            document.getElementById("particleCanvas");

        if (!canvas) {
            return;
        }

        /*
         * Tránh tạo animation loop thứ hai
         * nếu đúng canvas này đã chạy.
         */
        if (
            canvasState?.canvas ===
            canvas
        ) {
            return;
        }

        if (canvasState) {
            canvasState.dispose();
        }

        const ctx =
            canvas.getContext(
                "2d",
                {
                    alpha: true
                }
            );

        if (!ctx) {
            return;
        }

        let width = 0;
        let height = 0;

        let dpr =
            Math.min(
                window.devicePixelRatio || 1,
                2
            );

        let frame = 0;
        let disposed = false;

        const prefersReducedMotion =
            window.matchMedia(
                "(prefers-reduced-motion: reduce)"
            ).matches;

        /*
         * ============================
         * POINTER
         * ============================
         */
        const pointer = {
            x: -9999,
            y: -9999,

            targetX: -9999,
            targetY: -9999,

            radius: 140,
            active: false
        };

        function resizeCanvas() {
            dpr =
                Math.min(
                    window.devicePixelRatio || 1,
                    2
                );

            width =
                window.innerWidth;

            height =
                window.innerHeight;

            canvas.width =
                Math.floor(
                    width *
                    dpr
                );

            canvas.height =
                Math.floor(
                    height *
                    dpr
                );

            canvas.style.width =
                `${width}px`;

            canvas.style.height =
                `${height}px`;

            /*
             * Quan trọng:
             * dùng setTransform để không
             * tích lũy scale sau nhiều resize.
             */
            ctx.setTransform(
                dpr,
                0,
                0,
                dpr,
                0,
                0
            );
        }

        function updatePointerPos(
            x,
            y
        ) {
            pointer.targetX = x;
            pointer.targetY = y;

            if (!pointer.active) {
                pointer.x = x;
                pointer.y = y;
            }

            pointer.active = true;
        }

        const onMouseMove =
            event => {

                updatePointerPos(
                    event.clientX,
                    event.clientY
                );
            };

        const onTouchMove =
            event => {

                if (
                    event.touches?.[0]
                ) {
                    updatePointerPos(
                        event.touches[0].clientX,
                        event.touches[0].clientY
                    );
                }
            };

        const onTouchStart =
            event => {

                if (
                    event.touches?.[0]
                ) {
                    updatePointerPos(
                        event.touches[0].clientX,
                        event.touches[0].clientY
                    );
                }
            };

        const onPointerLeave =
            () => {
                pointer.active = false;
            };

        /*
         * ============================
         * PARTICLE
         * ============================
         */
        class FloatingParticle {
            constructor() {
                this.reset(true);
            }

            reset(init = false) {
                this.x =
                    init
                        ? Math.random() *
                          width

                        : Math.random() <
                          0.5

                            ? -15

                            : width +
                              15;

                this.y =
                    Math.random() *
                    height;

                this.baseRadius =
                    Math.random() *
                    2.2 +
                    1.2;

                this.radius =
                    this.baseRadius;

                const speed =
                    prefersReducedMotion
                        ? 0.04

                        : Math.random() *
                          0.42 +
                          0.18;

                const angle =
                    Math.random() *
                    Math.PI *
                    2;

                this.vx =
                    Math.cos(
                        angle
                    ) *
                    speed;

                this.vy =
                    Math.sin(
                        angle
                    ) *
                    speed;

                this.fx = 0;
                this.fy = 0;

                this.baseAlpha =
                    Math.random() *
                    0.32 +
                    0.18;

                this.alpha =
                    this.baseAlpha;

                this.pulseSeed =
                    Math.random() *
                    100;

                this.pulseSpeed =
                    Math.random() *
                    0.02 +
                    0.008;

                this.colorType =
                    Math.floor(
                        Math.random() *
                        3
                    );
            }

            update() {
                /*
                 * Pulse nhẹ.
                 */
                if (
                    !prefersReducedMotion
                ) {
                    this.pulseSeed +=
                        this.pulseSpeed;

                    this.alpha =
                        this.baseAlpha +
                        Math.sin(
                            this.pulseSeed
                        ) *
                        0.12;
                }

                /*
                 * ========================
                 * POINTER PHYSICS
                 * ========================
                 */
                if (
                    pointer.active
                ) {
                    const dx =
                        pointer.x -
                        this.x;

                    const dy =
                        pointer.y -
                        this.y;

                    const dist =
                        Math.hypot(
                            dx,
                            dy
                        );

                    if (
                        dist <
                            pointer.radius &&
                        dist >
                            1
                    ) {
                        const force =
                            1 -
                            dist /
                            pointer.radius;

                        const normX =
                            dx /
                            dist;

                        const normY =
                            dy /
                            dist;

                        /*
                         * Vùng rất gần:
                         * đẩy particle ra.
                         */
                        if (
                            dist <
                            45
                        ) {
                            const repel =
                                (
                                    1 -
                                    dist /
                                    45
                                ) *
                                1.8;

                            this.fx -=
                                normX *
                                repel;

                            this.fy -=
                                normY *
                                repel;
                        }

                        /*
                         * Vùng ngoài:
                         *
                         * attraction
                         * +
                         * tangential force
                         *
                         * Đây chính là phần
                         * tạo cảm giác bay
                         * quanh chuột.
                         */
                        else {
                            this.fx +=
                                normX *
                                    force *
                                    0.35
                                +
                                -normY *
                                    force *
                                    0.22;

                            this.fy +=
                                normY *
                                    force *
                                    0.35
                                +
                                normX *
                                    force *
                                    0.22;
                        }
                    }
                }

                /*
                 * Damping / quán tính.
                 */
                this.fx *=
                    0.91;

                this.fy *=
                    0.91;

                /*
                 * Base movement
                 * +
                 * pointer force.
                 */
                this.x +=
                    this.vx +
                    this.fx;

                this.y +=
                    this.vy +
                    this.fy;

                /*
                 * Wrap viewport.
                 */
                if (
                    this.x <
                    -30
                ) {
                    this.x =
                        width +
                        25;
                }

                if (
                    this.x >
                    width +
                    30
                ) {
                    this.x =
                        -25;
                }

                if (
                    this.y <
                    -30
                ) {
                    this.y =
                        height +
                        25;
                }

                if (
                    this.y >
                    height +
                    30
                ) {
                    this.y =
                        -25;
                }
            }

            draw(isDark) {
                ctx.save();

                const currentAlpha =
                    Math.max(
                        0.08,
                        Math.min(
                            0.65,
                            this.alpha
                        )
                    );

                let coreColor;
                let glowColor;

                if (isDark) {
                    if (
                        this.colorType ===
                        0
                    ) {
                        coreColor =
                            `rgba(0, 242, 255, ${currentAlpha * 1.4})`;

                        glowColor =
                            `rgba(0, 242, 255, ${currentAlpha * 0.5})`;
                    }

                    else if (
                        this.colorType ===
                        1
                    ) {
                        coreColor =
                            `rgba(56, 189, 248, ${currentAlpha * 1.2})`;

                        glowColor =
                            `rgba(14, 165, 233, ${currentAlpha * 0.4})`;
                    }

                    else {
                        coreColor =
                            `rgba(99, 102, 241, ${currentAlpha * 1.25})`;

                        glowColor =
                            `rgba(79, 70, 229, ${currentAlpha * 0.35})`;
                    }
                }

                else {
                    if (
                        this.colorType ===
                        0
                    ) {
                        coreColor =
                            `rgba(2, 132, 199, ${currentAlpha * 0.95})`;

                        glowColor =
                            `rgba(14, 165, 233, ${currentAlpha * 0.28})`;
                    }

                    else if (
                        this.colorType ===
                        1
                    ) {
                        coreColor =
                            `rgba(79, 70, 229, ${currentAlpha * 0.85})`;

                        glowColor =
                            `rgba(99, 102, 241, ${currentAlpha * 0.22})`;
                    }

                    else {
                        coreColor =
                            `rgba(13, 148, 136, ${currentAlpha * 0.85})`;

                        glowColor =
                            `rgba(20, 184, 166, ${currentAlpha * 0.22})`;
                    }
                }

                const glowRad =
                    this.radius *
                    3.2;

                const gradient =
                    ctx.createRadialGradient(
                        this.x,
                        this.y,
                        this.radius *
                        0.3,

                        this.x,
                        this.y,
                        glowRad
                    );

                gradient.addColorStop(
                    0,
                    coreColor
                );

                gradient.addColorStop(
                    0.5,
                    glowColor
                );

                gradient.addColorStop(
                    1,
                    "rgba(255,255,255,0)"
                );

                ctx.fillStyle =
                    gradient;

                ctx.beginPath();

                ctx.arc(
                    this.x,
                    this.y,
                    glowRad,
                    0,
                    Math.PI *
                    2
                );

                ctx.fill();

                ctx.fillStyle =
                    coreColor;

                ctx.beginPath();

                ctx.arc(
                    this.x,
                    this.y,
                    this.radius *
                    0.8,
                    0,
                    Math.PI *
                    2
                );

                ctx.fill();

                ctx.restore();
            }
        }

        resizeCanvas();

        /*
         * ============================
         * PARTICLE COUNT
         * ============================
         */
        const isMobile =
            window.innerWidth <
            768;

        const particleCount =
            isMobile

                ? Math.min(
                    Math.max(
                        Math.floor(
                            (
                                window.innerWidth *
                                window.innerHeight
                            ) /
                            14000
                        ) +
                        35,

                        52
                    ),

                    65
                )

                : Math.min(
                    Math.max(
                        Math.floor(
                            (
                                window.innerWidth *
                                window.innerHeight
                            ) /
                            9500
                        ) +
                        40,

                        105
                    ),

                    120
                );

        const particles =
            [];

        for (
            let i = 0;
            i < particleCount;
            i++
        ) {
            particles.push(
                new FloatingParticle()
            );
        }

        const CONNECTION_DIST =
            145;

        /*
         * ============================
         * MAIN LOOP
         * ============================
         */
        function renderScene() {
            if (disposed) {
                return;
            }

            ctx.clearRect(
                0,
                0,
                width,
                height
            );

            const isDark =
                currentTheme() ===
                "dark";

            /*
             * Smooth mouse tracking.
             */
            if (
                pointer.active
            ) {
                pointer.x +=
                    (
                        pointer.targetX -
                        pointer.x
                    ) *
                    0.18;

                pointer.y +=
                    (
                        pointer.targetY -
                        pointer.y
                    ) *
                    0.18;
            }

            const pLen =
                particles.length;

            /*
             * ========================
             * CONNECTION LINES
             * ========================
             */
            ctx.lineWidth =
                0.8;

            for (
                let i = 0;
                i < pLen;
                i++
            ) {
                const p1 =
                    particles[i];

                for (
                    let j =
                        i + 1;

                    j <
                    pLen;

                    j++
                ) {
                    const p2 =
                        particles[j];

                    const dx =
                        p1.x -
                        p2.x;

                    const dy =
                        p1.y -
                        p2.y;

                    const distSq =
                        dx *
                        dx +
                        dy *
                        dy;

                    if (
                        distSq <
                        CONNECTION_DIST *
                        CONNECTION_DIST
                    ) {
                        const dist =
                            Math.sqrt(
                                distSq
                            );

                        const lineAlpha =
                            (
                                1 -
                                dist /
                                CONNECTION_DIST
                            ) *
                            (
                                isDark
                                    ? 0.22
                                    : 0.09
                            );

                        ctx.strokeStyle =
                            isDark

                                ? `rgba(0, 242, 255, ${lineAlpha})`

                                : `rgba(2, 132, 199, ${lineAlpha})`;

                        ctx.beginPath();

                        ctx.moveTo(
                            p1.x,
                            p1.y
                        );

                        ctx.lineTo(
                            p2.x,
                            p2.y
                        );

                        ctx.stroke();
                    }
                }
            }

            /*
             * Update + draw.
             */
            for (
                let i = 0;
                i < pLen;
                i++
            ) {
                particles[i]
                    .update();

                particles[i]
                    .draw(
                        isDark
                    );
            }

            frame =
                requestAnimationFrame(
                    renderScene
                );
        }

        /*
         * ============================
         * EVENTS
         * ============================
         */
        window.addEventListener(
            "resize",
            resizeCanvas,
            {
                passive: true
            }
        );

        window.addEventListener(
            "mousemove",
            onMouseMove,
            {
                passive: true
            }
        );

        window.addEventListener(
            "touchmove",
            onTouchMove,
            {
                passive: true
            }
        );

        window.addEventListener(
            "touchstart",
            onTouchStart,
            {
                passive: true
            }
        );

        window.addEventListener(
            "touchend",
            onPointerLeave
        );

        document.addEventListener(
            "mouseleave",
            onPointerLeave
        );

        frame =
            requestAnimationFrame(
                renderScene
            );

        /*
         * Cleanup.
         */
        canvasState = {
            canvas,

            dispose() {
                disposed = true;

                cancelAnimationFrame(
                    frame
                );

                window.removeEventListener(
                    "resize",
                    resizeCanvas
                );

                window.removeEventListener(
                    "mousemove",
                    onMouseMove
                );

                window.removeEventListener(
                    "touchmove",
                    onTouchMove
                );

                window.removeEventListener(
                    "touchstart",
                    onTouchStart
                );

                window.removeEventListener(
                    "touchend",
                    onPointerLeave
                );

                document.removeEventListener(
                    "mouseleave",
                    onPointerLeave
                );
            }
        };
    }

    const api = {
        init() {
            initializeTheme();
            initializeClock();
            initializeProfileMenu();
            initializeSearchShortcut();
            initializeFloatingNavigation();
            initializeCanvas();

            const requestedTab =
                new URLSearchParams(
                    global.location.search
                ).get("tab");

            if (
                document.getElementById(
                    "tabHomeContent"
                ) &&
                [
                    "home",
                    "processing",
                    "notifications"
                ].includes(requestedTab)
            ) {
                api.switchTab(
                    requestedTab,
                    false
                );
            } else if (
                global.location.pathname.startsWith(
                    "/resident/service-requests"
                )
            ) {
                document.getElementById(
                    "navHome"
                )?.classList.remove(
                    "active"
                );

                document.getElementById(
                    "navProcessing"
                )?.classList.add(
                    "active"
                );
            }

            return true;
        },

        toggleTheme(event) {
            event?.stopPropagation();

            applyTheme(
                currentTheme() === "dark"
                    ? "light"
                    : "dark",
                true
            );
        },

        switchTab(tab, scroll = true) {
            const homePanel =
                document.getElementById(
                    "tabHomeContent"
                );

            if (!homePanel) {
                const target =
                    tab === "home"
                        ? "/resident"
                        : tab === "processing"
                            ? "/resident?tab=processing"
                            : "/resident?tab=notifications";

                if (global.Blazor?.navigateTo) {
                    global.Blazor.navigateTo(target);
                } else {
                    global.location.assign(target);
                }

                return;
            }

            const tabs = {
                home: "Home",
                processing: "Processing",
                notifications: "Notifications"
            };

            if (!tabs[tab]) {
                return;
            }

            Object.entries(
                tabs
            ).forEach(([
                key,
                suffix
            ]) => {
                const selected =
                    key === tab;

                const panel =
                    document.getElementById(
                        `tab${suffix}Content`
                    );

                const button =
                    document.getElementById(
                        `nav${suffix}`
                    );

                if (panel) {
                    panel.hidden =
                        !selected;

                    panel.classList.toggle(
                        "active",
                        selected
                    );
                }

                if (button) {
                    button.classList.toggle(
                        "active",
                        selected
                    );

                    button.setAttribute(
                        "aria-pressed",
                        String(selected)
                    );
                }
            });

            if (scroll) {
                document.querySelector(
                    ".resident-tabs"
                )?.scrollIntoView({
                    behavior: "smooth",
                    block: "start"
                });
            }
        },

        openProfile() {
            const modal =
                document.getElementById("profileModal") ||
                document.getElementById("userProfileModal");

            const popover =
                document.getElementById("profilePopover") ||
                document.getElementById("userProfilePopover");

            if (popover) {
                popover.hidden = true;
                popover.classList.add("hidden");
            }

            if (modal) {
                modal.hidden = false;
                modal.classList.remove("hidden");
            }

            document.body.style.overflow =
                "hidden";
        },

        closeProfile() {
            const modal =
                document.getElementById("profileModal") ||
                document.getElementById("userProfileModal");

            if (modal) {
                modal.hidden = true;
                modal.classList.add("hidden");
            }

            if (
                !document.querySelector(
                    ".resident-portal-modal:not([hidden]):not(.hidden)"
                )
            ) {
                document.body.style.overflow =
                    "";
            }
        },

        openService(title) {
            const modal =
                document.getElementById("serviceModal") ||
                document.getElementById("service-modal");

            const heading =
                document.getElementById("serviceModalTitle") ||
                document.getElementById("service-modal-title");

            if (
                heading &&
                title
            ) {
                heading.textContent =
                    title;
            }

            if (modal) {
                modal.hidden = false;

                modal.classList.remove(
                    "hidden"
                );

                modal.classList.add(
                    "flex"
                );

                modal.classList.remove(
                    "opacity-0",
                    "pointer-events-none"
                );
            }

            document.body.style.overflow =
                "hidden";
        },

        closeService() {
            const modal =
                document.getElementById("serviceModal") ||
                document.getElementById("service-modal");

            if (modal) {
                modal.hidden = true;

                modal.classList.add(
                    "hidden"
                );

                modal.classList.remove(
                    "flex"
                );

                modal.classList.add(
                    "opacity-0",
                    "pointer-events-none"
                );
            }

            if (
                !document.querySelector(
                    ".resident-portal-modal:not([hidden]):not(.hidden)"
                )
            ) {
                document.body.style.overflow =
                    "";
            }
        },

        toggleAssistant() {
            const chat =
                document.getElementById("aiChatWindow") ||
                document.getElementById("ai-copilot-window");

            if (!chat) {
                return;
            }

            const willOpen =
                chat.hidden ||
                chat.classList.contains(
                    "hidden"
                );

            chat.hidden =
                !willOpen;

            chat.classList.toggle(
                "hidden",
                !willOpen
            );

            document.querySelectorAll(
                '[aria-controls="aiChatWindow"]'
            ).forEach(button =>
                button.setAttribute(
                    "aria-expanded",
                    String(willOpen)
                )
            );
        },

        destroy() {
            if (clockTimer) {
                clearInterval(
                    clockTimer
                );

                clockTimer = null;
            }

            if (canvasState) {
                canvasState.dispose();
                canvasState = null;
            }
        }
    };

    /*
     * =====================================
     * QUAN TRỌNG CHO BLAZOR
     * =====================================
     *
     * Expose object ngay khi file JS chạy.
     */
    global.propFlowResident =
        api;

    /*
     * KHÔNG auto-init tại đây.
     *
     * Để Blazor gọi:
     *
     * propFlowResident.init()
     *
     * sau khi component đã render.
     */

})(window);
