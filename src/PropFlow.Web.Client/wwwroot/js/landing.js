window.currentTheme = "light";
window.currentScreen = "login"; // "login" | "register" | "forgot"

// UI Panel Navigation Functions
window.switchToLogin = function() {
    window.currentScreen = "login";
    
    const leftPanel = document.getElementById("left-panel");
    const rightPanel = document.getElementById("right-panel");
    const viewLeftWelcome = document.getElementById("view-left-welcome");
    const viewLeftRegister = document.getElementById("view-left-register");
    const viewRightLogin = document.getElementById("view-right-login");
    const viewRightForgot = document.getElementById("view-right-forgot");
    const viewRightWelcome = document.getElementById("view-right-welcome");
    const navTabLogin = document.getElementById("nav-tab-login");
    const navTabRegister = document.getElementById("nav-tab-register");

    // Tab UI updates
    if (navTabLogin && navTabRegister) {
        navTabLogin.className = "px-4 py-1 rounded-full text-xs font-semibold font-label-caps uppercase transition-all bg-sky-600 dark:bg-[#00f2ff] text-white dark:text-[#041e2a] shadow-sm";
        navTabRegister.className = "px-4 py-1 rounded-full text-xs font-semibold font-label-caps uppercase transition-all text-slate-600 dark:text-slate-400 hover:text-sky-600 dark:hover:text-white";
    }

    if (leftPanel && rightPanel) {
        // Panel translation
        leftPanel.style.transform = "translateX(0%)";
        rightPanel.style.transform = "translateX(0%)";

        // Left Panel content
        viewLeftWelcome.classList.remove("opacity-0", "-translate-x-8", "pointer-events-none", "z-10");
        viewLeftWelcome.classList.add("opacity-100", "translate-x-0", "pointer-events-auto", "z-20");

        viewLeftRegister.classList.remove("opacity-100", "translate-x-0", "pointer-events-auto", "z-20");
        viewLeftRegister.classList.add("opacity-0", "-translate-x-8", "pointer-events-none", "z-10");

        // Right Panel content
        viewRightLogin.classList.remove("opacity-0", "-translate-x-8", "translate-x-8", "pointer-events-none", "z-10");
        viewRightLogin.classList.add("opacity-100", "translate-x-0", "pointer-events-auto", "z-20");

        viewRightForgot.classList.remove("opacity-100", "translate-x-0", "pointer-events-auto", "z-20");
        viewRightForgot.classList.add("opacity-0", "translate-x-8", "pointer-events-none", "z-10");

        viewRightWelcome.classList.remove("opacity-100", "translate-x-0", "pointer-events-auto", "z-20");
        viewRightWelcome.classList.add("opacity-0", "translate-x-8", "pointer-events-none", "z-10");
    }
};

window.switchToRegister = function() {
    window.currentScreen = "register";

    const leftPanel = document.getElementById("left-panel");
    const rightPanel = document.getElementById("right-panel");
    const viewLeftWelcome = document.getElementById("view-left-welcome");
    const viewLeftRegister = document.getElementById("view-left-register");
    const viewRightLogin = document.getElementById("view-right-login");
    const viewRightForgot = document.getElementById("view-right-forgot");
    const viewRightWelcome = document.getElementById("view-right-welcome");
    const navTabLogin = document.getElementById("nav-tab-login");
    const navTabRegister = document.getElementById("nav-tab-register");

    // Tab UI updates
    if (navTabLogin && navTabRegister) {
        navTabRegister.className = "px-4 py-1 rounded-full text-xs font-semibold font-label-caps uppercase transition-all bg-sky-600 dark:bg-[#00f2ff] text-white dark:text-[#041e2a] shadow-sm";
        navTabLogin.className = "px-4 py-1 rounded-full text-xs font-semibold font-label-caps uppercase transition-all text-slate-600 dark:text-slate-400 hover:text-sky-600 dark:hover:text-white";
    }

    if (leftPanel && rightPanel) {
        // Smooth sliding switch: Panels cross over
        leftPanel.style.transform = "translateX(100%)";
        rightPanel.style.transform = "translateX(-100%)";

        // Left Panel content becomes Register Form
        viewLeftWelcome.classList.remove("opacity-100", "translate-x-0", "pointer-events-auto", "z-20");
        viewLeftWelcome.classList.add("opacity-0", "-translate-x-8", "pointer-events-none", "z-10");

        viewLeftRegister.classList.remove("opacity-0", "-translate-x-8", "pointer-events-none", "z-10");
        viewLeftRegister.classList.add("opacity-100", "translate-x-0", "pointer-events-auto", "z-20");

        // Right Panel content becomes Welcome Right
        viewRightLogin.classList.remove("opacity-100", "translate-x-0", "pointer-events-auto", "z-20");
        viewRightLogin.classList.add("opacity-0", "translate-x-8", "pointer-events-none", "z-10");

        viewRightForgot.classList.remove("opacity-100", "translate-x-0", "pointer-events-auto", "z-20");
        viewRightForgot.classList.add("opacity-0", "translate-x-8", "pointer-events-none", "z-10");

        viewRightWelcome.classList.remove("opacity-0", "translate-x-8", "pointer-events-none", "z-10");
        viewRightWelcome.classList.add("opacity-100", "translate-x-0", "pointer-events-auto", "z-20");
    }
};

window.switchToForgotPassword = function() {
    window.currentScreen = "forgot";
    
    const viewRightLogin = document.getElementById("view-right-login");
    const viewRightForgot = document.getElementById("view-right-forgot");

    if (viewRightLogin && viewRightForgot) {
        // Animate Login out and Forgot in on right panel
        viewRightLogin.classList.remove("opacity-100", "translate-x-0", "pointer-events-auto", "z-20");
        viewRightLogin.classList.add("opacity-0", "-translate-x-8", "pointer-events-none", "z-10");

        viewRightForgot.classList.remove("opacity-0", "translate-x-8", "pointer-events-none", "z-10");
        viewRightForgot.classList.add("opacity-100", "translate-x-0", "pointer-events-auto", "z-20");
    }
};

// --- THREE.JS GHOST SKYSCRAPERS & PARTICLES SYSTEM ---
let threeScene, threeCamera, threeRenderer, threeBuildingGroup, threeParticlesMesh, gridHelperRef;

const themePalettes = {
    light: {
        fog: 0xf8f9ff,
        grid1: 0x0284c7,
        grid2: 0x94a3b8,
        gridOpacity: 0.22,
        buildingSky: 0x0284c7,
        buildingNavy: 0x0369a1,
        buildingOpacity1: 0.45,
        buildingOpacity2: 0.35,
        particleColor: 0x0284c7,
        particleOpacity: 0.65,
        bodyBg: "#f8f9ff"
    },
    dark: {
        fog: 0x06090e,
        grid1: 0x00f2ff,
        grid2: 0x00696f,
        gridOpacity: 0.35,
        buildingSky: 0x00f2ff,
        buildingNavy: 0x0284c7,
        buildingOpacity1: 0.75,
        buildingOpacity2: 0.5,
        particleColor: 0x00f2ff,
        particleOpacity: 0.9,
        bodyBg: "#06090e"
    }
};

window.initLandingPage = function() {
    const container = document.getElementById("threejs-bg");
    if (!container) return;

    // Check if already initialized to prevent multiple canvases when navigating back and forth in Blazor
    if (container.children.length > 0) return;

    threeScene = new THREE.Scene();
    threeScene.fog = new THREE.FogExp2(themePalettes.light.fog, 0.015);

    threeCamera = new THREE.PerspectiveCamera(65, window.innerWidth / window.innerHeight, 0.1, 1000);
    threeCamera.position.set(0, 14, 38);
    threeCamera.lookAt(0, 4, 0);

    threeRenderer = new THREE.WebGLRenderer({ alpha: true, antialias: true });
    threeRenderer.setSize(window.innerWidth, window.innerHeight);
    threeRenderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
    container.appendChild(threeRenderer.domElement);

    threeBuildingGroup = new THREE.Group();

    // Architectural Ground Grid
    gridHelperRef = new THREE.GridHelper(120, 40, themePalettes.light.grid1, themePalettes.light.grid2);
    gridHelperRef.material.opacity = themePalettes.light.gridOpacity;
    gridHelperRef.material.transparent = true;
    gridHelperRef.position.y = -2;
    threeBuildingGroup.add(gridHelperRef);

    // Skyscraper wireframe towers (3 prominent central towers + urban perimeter)
    const buildingCount = 28;
    for (let i = 0; i < buildingCount; i++) {
        const w = Math.random() * 4 + 3;
        const h = Math.random() * 26 + 12;
        const d = Math.random() * 4 + 3;

        const geometry = new THREE.BoxGeometry(w, h, d, 2, Math.floor(h / 3), 2);
        const edges = new THREE.EdgesGeometry(geometry);
        
        const isSky = i % 2 === 0;
        const lineMat = new THREE.LineBasicMaterial({
            color: isSky ? themePalettes.light.buildingSky : themePalettes.light.buildingNavy,
            transparent: true,
            opacity: isSky ? themePalettes.light.buildingOpacity1 : themePalettes.light.buildingOpacity2,
            linewidth: 1.5
        });

        const building = new THREE.LineSegments(edges, lineMat);
        const angle = (i / buildingCount) * Math.PI * 2;
        const radius = Math.random() * 35 + 16;
        building.position.x = Math.cos(angle) * radius;
        building.position.z = Math.sin(angle) * radius;
        building.position.y = (h / 2) - 2;

        building.userData = { isSky, baseHeight: h };
        threeBuildingGroup.add(building);
    }

    threeScene.add(threeBuildingGroup);

    // Ambient Floating IoT & Data Particles (500 particles)
    const particleCount = 500;
    const particlesGeometry = new THREE.BufferGeometry();
    const posArray = new Float32Array(particleCount * 3);

    for (let i = 0; i < particleCount; i++) {
        const radius = Math.random() * 45 + 5;
        const angle = Math.random() * Math.PI * 2;

        posArray[i * 3] = Math.cos(angle) * radius;
        posArray[i * 3 + 1] = Math.random() * 36 - 2;
        posArray[i * 3 + 2] = Math.sin(angle) * radius;
    }

    particlesGeometry.setAttribute("position", new THREE.BufferAttribute(posArray, 3));

    const particlesMaterial = new THREE.PointsMaterial({
        size: 0.35,
        color: themePalettes.light.particleColor,
        transparent: true,
        opacity: themePalettes.light.particleOpacity
    });

    threeParticlesMesh = new THREE.Points(particlesGeometry, particlesMaterial);
    threeScene.add(threeParticlesMesh);

    function animate() {
        if (!document.getElementById("threejs-bg")) return; // Stop animation if DOM node is gone
        requestAnimationFrame(animate);
        threeBuildingGroup.rotation.y += 0.0008;
        threeParticlesMesh.rotation.y -= 0.0012;
        threeRenderer.render(threeScene, threeCamera);
    }
    animate();

    // Use a named function so we can remove it if needed, but for now anonymous is okay
    window.addEventListener("resize", () => {
        if (threeCamera && threeRenderer) {
            threeCamera.aspect = window.innerWidth / window.innerHeight;
            threeCamera.updateProjectionMatrix();
            threeRenderer.setSize(window.innerWidth, window.innerHeight);
        }
    });

    // If initial theme is already dark due to previous navigation
    if (window.currentTheme === "dark") {
        window.updateThreeJsTheme("dark");
    }
};

window.updateThreeJsTheme = function(theme) {
    if (!threeScene || !threeBuildingGroup) return;
    const pal = themePalettes[theme];

    threeScene.fog.color.setHex(pal.fog);

    if (gridHelperRef) {
        gridHelperRef.material.color.setHex(pal.grid1);
        gridHelperRef.material.opacity = pal.gridOpacity;
    }

    threeBuildingGroup.children.forEach(child => {
        if (child instanceof THREE.LineSegments && child.userData) {
            const isSky = child.userData.isSky;
            child.material.color.setHex(isSky ? pal.buildingSky : pal.buildingNavy);
            child.material.opacity = isSky ? pal.buildingOpacity1 : pal.buildingOpacity2;
        }
    });

    if (threeParticlesMesh) {
        threeParticlesMesh.material.color.setHex(pal.particleColor);
        threeParticlesMesh.material.opacity = pal.particleOpacity;
    }
};

// --- THEME TOGGLE (LIGHT / DARK) ---
window.toggleTheme = function() {
    const html = document.documentElement;
    const body = document.getElementById("app-body") || document.body;
    const themeIcon = document.getElementById("theme-icon");
    const themeLabel = document.getElementById("theme-label");
    const ambientLight = document.getElementById("ambient-light-glows");
    const ambientDark = document.getElementById("ambient-dark-glows");

    if (window.currentTheme === "light") {
        window.currentTheme = "dark";
        html.classList.add("dark");
        html.classList.remove("light");
        body.style.backgroundColor = "#06090e";
        body.style.color = "#e2e8f0";

        if (themeIcon) themeIcon.textContent = "☀️";
        if (themeLabel) themeLabel.textContent = "CHẾ ĐỘ SÁNG";

        if (ambientLight) ambientLight.classList.replace("opacity-100", "opacity-0");
        if (ambientDark) ambientDark.classList.replace("opacity-0", "opacity-100");

        window.updateThreeJsTheme("dark");
    } else {
        window.currentTheme = "light";
        html.classList.remove("dark");
        html.classList.add("light");
        body.style.backgroundColor = "#f8f9ff";
        body.style.color = "#0f172a";

        if (themeIcon) themeIcon.textContent = "🌙";
        if (themeLabel) themeLabel.textContent = "CHẾ ĐỘ TỐI";

        if (ambientDark) ambientDark.classList.replace("opacity-100", "opacity-0");
        if (ambientLight) ambientLight.classList.replace("opacity-0", "opacity-100");

        window.updateThreeJsTheme("light");
    }
};

window.focusOtpInput = function(index) {
    document.getElementById(`otp-digit-${index}`)?.focus();
};
