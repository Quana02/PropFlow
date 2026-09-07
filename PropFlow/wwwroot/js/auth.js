// --- Split-Screen Authentication Logic ---
let isLogin = true;
let isForgotKey = false;

window.toggleAuthMode = function() {
    if (isForgotKey) {
        hideForgotPassword(false);
    }

    isLogin = !isLogin;

    const leftPanel = document.getElementById('left-panel');
    const rightPanel = document.getElementById('right-panel');

    const welcomeLeft = document.getElementById('welcome-content-left');
    const registerLeft = document.getElementById('register-form-left');

    const loginRight = document.getElementById('login-form-right');
    const welcomeRight = document.getElementById('welcome-content-right');
    const forgotRight = document.getElementById('forgot-form-right');

    if (isLogin) {
        leftPanel.style.transform = 'translateX(0)';
        rightPanel.style.transform = 'translateX(0)';

        welcomeLeft.style.opacity = '1';
        welcomeLeft.style.zIndex = '10';
        welcomeLeft.style.pointerEvents = 'auto';

        registerLeft.style.opacity = '0';
        registerLeft.style.zIndex = '0';
        registerLeft.style.pointerEvents = 'none';

        loginRight.style.opacity = '1';
        loginRight.style.transform = 'translateX(0)';
        loginRight.style.zIndex = '10';
        loginRight.style.pointerEvents = 'auto';

        welcomeRight.style.opacity = '0';
        welcomeRight.style.zIndex = '0';
        welcomeRight.style.pointerEvents = 'none';

        forgotRight.style.opacity = '0';
        forgotRight.style.transform = 'translateX(2rem)';
        forgotRight.style.zIndex = '0';
        forgotRight.style.pointerEvents = 'none';
    } else {
        leftPanel.style.transform = 'translateX(100%)';
        rightPanel.style.transform = 'translateX(-100%)';

        welcomeLeft.style.opacity = '0';
        welcomeLeft.style.zIndex = '0';
        welcomeLeft.style.pointerEvents = 'none';

        registerLeft.style.opacity = '1';
        registerLeft.style.zIndex = '10';
        registerLeft.style.pointerEvents = 'auto';

        loginRight.style.opacity = '0';
        loginRight.style.zIndex = '0';
        loginRight.style.pointerEvents = 'none';

        welcomeRight.style.opacity = '1';
        welcomeRight.style.zIndex = '10';
        welcomeRight.style.pointerEvents = 'auto';

        forgotRight.style.opacity = '0';
        forgotRight.style.transform = 'translateX(2rem)';
        forgotRight.style.zIndex = '0';
        forgotRight.style.pointerEvents = 'none';
    }
}

// --- Forgot Password View Switchers ---
window.showForgotPassword = function() {
    isForgotKey = true;
    const loginRight = document.getElementById('login-form-right');
    const forgotRight = document.getElementById('forgot-form-right');

    // Animate Login out to the left
    loginRight.style.opacity = '0';
    loginRight.style.transform = 'translateX(-2rem)';
    loginRight.style.pointerEvents = 'none';
    loginRight.style.zIndex = '0';

    // Animate Forgot form into view from right
    forgotRight.style.opacity = '1';
    forgotRight.style.transform = 'translateX(0)';
    forgotRight.style.pointerEvents = 'auto';
    forgotRight.style.zIndex = '10';
}

window.hideForgotPassword = function(animated = true) {
    isForgotKey = false;
    const loginRight = document.getElementById('login-form-right');
    const forgotRight = document.getElementById('forgot-form-right');

    if (!animated) {
        loginRight.style.transition = 'none';
        forgotRight.style.transition = 'none';
    }

    // Animate Forgot form out to right
    forgotRight.style.opacity = '0';
    forgotRight.style.transform = 'translateX(2rem)';
    forgotRight.style.pointerEvents = 'none';
    forgotRight.style.zIndex = '0';

    // Animate Login back to origin
    loginRight.style.opacity = '1';
    loginRight.style.transform = 'translateX(0)';
    loginRight.style.pointerEvents = 'auto';
    loginRight.style.zIndex = '10';

    if (!animated) {
        // restore transitions
        setTimeout(() => {
            loginRight.style.transition = '';
            forgotRight.style.transition = '';
        }, 50);
    }
}

window.handleForgotSubmit = function() {
    const forgotRight = document.getElementById('forgot-form-right');
    const btn = forgotRight.querySelector('button[type="submit"]');
    const origHtml = btn.innerHTML;

    btn.innerHTML = '<span class="material-symbols-outlined animate-spin text-[18px]">sync</span><span>ĐANG XÁC THỰC...</span>';
    btn.disabled = true;

    setTimeout(() => {
        btn.innerHTML = '<span class="material-symbols-outlined text-[18px] text-green-400">check_circle</span><span>MÃ ĐÃ ĐƯỢC GỬI!</span>';
        btn.classList.add('border', 'border-primary');

        setTimeout(() => {
            btn.innerHTML = origHtml;
            btn.disabled = false;
            btn.classList.remove('border', 'border-primary');
            hideForgotPassword(true);
        }, 1400);
    }, 900);
}

// --- Three.js Background Initialization ---
window.initThreeJsBackground = function() {
    const container = document.getElementById('threejs-bg');
    if (!container) return;

    // Ensure single instance cleanup if called multiple times
    container.innerHTML = '';

    // Scene setup
    const scene = new THREE.Scene();
    // Add dark fog blending into the background color
    scene.fog = new THREE.FogExp2(0x10141a, 0.02);

    // Camera setup
    const camera = new THREE.PerspectiveCamera(75, window.innerWidth / window.innerHeight, 0.1, 1000);
    camera.position.z = 30;
    camera.position.y = 10;
    camera.lookAt(0, 0, 0);

    // Renderer setup
    const renderer = new THREE.WebGLRenderer({ alpha: true, antialias: true });
    renderer.setSize(window.innerWidth, window.innerHeight);
    renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2)); // optimize performance
    container.appendChild(renderer.domElement);

    // Create Ghost Building (Abstract wireframe structure)
    const group = new THREE.Group();

    // Base grid
    const gridHelper = new THREE.GridHelper(100, 50, 0x00f2ff, 0x00f2ff);
    gridHelper.material.opacity = 0.15;
    gridHelper.material.transparent = true;
    group.add(gridHelper);

    // Procedural wireframe buildings
    const buildingMaterial = new THREE.LineBasicMaterial({
        color: 0x00f2ff,
        transparent: true,
        opacity: 0.2
    });

    for (let i = 0; i < 20; i++) {
        const w = Math.random() * 4 + 2;
        const h = Math.random() * 20 + 10;
        const d = Math.random() * 4 + 2;

        const geometry = new THREE.BoxGeometry(w, h, d, 1, Math.floor(h/2), 1);
        const edges = new THREE.EdgesGeometry(geometry);
        const building = new THREE.LineSegments(edges, buildingMaterial);

        building.position.x = (Math.random() - 0.5) * 60;
        building.position.z = (Math.random() - 0.5) * 60;
        building.position.y = h / 2;

        group.add(building);
    }

    scene.add(group);

    // Ambient floating orbiting particles
    const particlesGeometry = new THREE.BufferGeometry();
    const particleCount = 1500;
    const posArray = new Float32Array(particleCount * 3);

    for(let i = 0; i < particleCount; i++) {
        // Create an orbiting cylinder/sphere distribution
        const radius = Math.random() * 30 + 5;
        const angle = Math.random() * Math.PI * 2;

        posArray[i * 3] = Math.cos(angle) * radius;
        posArray[i * 3 + 1] = (Math.random() - 0.5) * 60; // Spread vertically
        posArray[i * 3 + 2] = Math.sin(angle) * radius;
    }

    particlesGeometry.setAttribute('position', new THREE.BufferAttribute(posArray, 3));
    const particlesMaterial = new THREE.PointsMaterial({
        size: 0.15,
        color: 0x00f2ff,
        transparent: true,
        opacity: 0.8,
        blending: THREE.AdditiveBlending
    });

    const particlesMesh = new THREE.Points(particlesGeometry, particlesMaterial);
    scene.add(particlesMesh);

    // Animation Loop
    function animate() {
        requestAnimationFrame(animate);

        // Slow rotation
        group.rotation.y += 0.001;

        // Orbiting particles
        particlesMesh.rotation.y -= 0.002;
        particlesMesh.position.y = Math.sin(Date.now() * 0.001) * 2;

        renderer.render(scene, camera);
    }

    animate();

    // Window resize handling
    window.addEventListener('resize', () => {
        camera.aspect = window.innerWidth / window.innerHeight;
        camera.updateProjectionMatrix();
        renderer.setSize(window.innerWidth, window.innerHeight);
    });
}
