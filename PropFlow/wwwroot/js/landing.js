window.initLandingThreeJs = function() {
  function initThree() {
    let container = document.getElementById('threejs-canvas-container');
    if (!container) return;
    
    // Check if Three is loaded
    if (typeof THREE === 'undefined') {
      const script = document.createElement('script');
      script.src = 'https://ajax.googleapis.com/ajax/libs/threejs/r125/three.min.js';
      script.onload = () => buildScene(container);
      document.head.appendChild(script);
    } else {
      buildScene(container);
    }
  }

  function buildScene(container) {
    if (container.children.length > 0) return;
    container.innerHTML = '';
    const width = window.innerWidth;
    const height = window.innerHeight;

    const scene = new THREE.Scene();
    const camera = new THREE.PerspectiveCamera(50, width / height, 0.1, 1000);
    camera.up.set(0, 1, 0);

    const renderer = new THREE.WebGLRenderer({ alpha: true, antialias: true, powerPreference: 'high-performance' });
    renderer.setSize(width, height);
    renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
    renderer.setClearColor(0x000000, 0);
    container.appendChild(renderer.domElement);

    const buildingGroup = new THREE.Group();
    scene.add(buildingGroup);

    const cyanColor = 0x00f2ff;
    const lineMaterial = new THREE.LineBasicMaterial({ 
      color: cyanColor, 
      transparent: true, 
      opacity: 0.55 
    });
    const edgeMaterial = new THREE.LineBasicMaterial({ 
      color: 0x38bdf8, 
      opacity: 0.9, 
      transparent: true 
    });

    function createBuildingPart(w, h, d, x, y, z) {
      const geometry = new THREE.BoxGeometry(w, h, d);
      const wireframe = new THREE.WireframeGeometry(geometry);
      const line = new THREE.LineSegments(wireframe, lineMaterial);
      line.position.set(x, y, z);
      buildingGroup.add(line);

      const edges = new THREE.EdgesGeometry(geometry);
      const edgeLines = new THREE.LineSegments(edges, edgeMaterial);
      edgeLines.position.set(x, y, z);
      buildingGroup.add(edgeLines);
    }

    // 3 blocks of Ghost Building
    createBuildingPart(2.2, 8.5, 2.2, 0, 0, 0);
    createBuildingPart(1.6, 6.2, 1.6, 1.8, -1.15, 0.9);
    createBuildingPart(1.6, 5.2, 1.6, -1.8, -1.65, -0.9);
    createBuildingPart(3.6, 0.4, 3.6, 0, -4.25, 0);

    buildingGroup.rotation.set(0, 0.4, 0);

    // Particle System
    const particlesGeom = new THREE.BufferGeometry();
    const particlesCount = 480;
    const posArray = new Float32Array(particlesCount * 3);

    for(let i = 0; i < particlesCount * 3; i += 3) {
      posArray[i] = (Math.random() - 0.5) * 24;
      posArray[i+1] = (Math.random() - 0.5) * 20;
      posArray[i+2] = (Math.random() - 0.5) * 24;
    }
    particlesGeom.setAttribute('position', new THREE.BufferAttribute(posArray, 3));
    const particlesMaterial = new THREE.PointsMaterial({
      size: 0.08,
      color: cyanColor,
      transparent: true,
      opacity: 0.85
    });
    const particlesMesh = new THREE.Points(particlesGeom, particlesMaterial);
    scene.add(particlesMesh);

    const configs = {
      home: { 
        camX: 1.8, camY: 0.5, camZ: 10.5, 
        lookX: 0.2, lookY: -0.2, lookZ: 0,
        modelX: 0, modelY: 0, modelScale: 1.0,
        modelRotY: 0.4
      },
      features: { 
        camX: 0.8, camY: -3.8, camZ: 7.0, 
        lookX: 0.0, lookY: 1.6, lookZ: 0,
        modelX: 0, modelY: 0, modelScale: 1.0,
        modelRotY: 0.65
      },
      pricing: { 
        camX: -0.8, camY: 3.2, camZ: 9.6, 
        lookX: 2.8, lookY: -0.6, lookZ: 0,
        modelX: 3.8, modelY: -0.4, modelScale: 0.72,
        modelRotY: 0.95
      }
    };

    let targetCam = { ...configs.home };
    let currentLookAt = new THREE.Vector3(targetCam.lookX, targetCam.lookY, targetCam.lookZ);
    let targetLookAt = new THREE.Vector3(targetCam.lookX, targetCam.lookY, targetCam.lookZ);

    let currentModelX = configs.home.modelX;
    let targetModelX = configs.home.modelX;
    let currentModelY = configs.home.modelY;
    let targetModelY = configs.home.modelY;
    let currentModelScale = configs.home.modelScale;
    let targetModelScale = configs.home.modelScale;
    let currentModelRotY = configs.home.modelRotY;
    let targetModelRotY = configs.home.modelRotY;

    camera.position.set(targetCam.camX, targetCam.camY, targetCam.camZ);
    camera.lookAt(currentLookAt);

    function updateAngle(sectionKey) {
      if (configs[sectionKey]) {
        const cfg = configs[sectionKey];
        targetCam.camX = cfg.camX;
        targetCam.camY = cfg.camY;
        targetCam.camZ = cfg.camZ;
        targetLookAt.set(cfg.lookX, cfg.lookY, cfg.lookZ);
        targetModelX = cfg.modelX;
        targetModelY = cfg.modelY;
        targetModelScale = cfg.modelScale;
        targetModelRotY = cfg.modelRotY;
      }
    }

    window.addEventListener('nav-change', (e) => {
      if (e.detail && e.detail.section) {
        updateAngle(e.detail.section);
      }
    });

    function onScroll() {
      const scrollY = window.scrollY || window.pageYOffset || document.documentElement.scrollTop;
      const maxScroll = (document.documentElement.scrollHeight - window.innerHeight) || 1;
      const progress = Math.min(Math.max(scrollY / maxScroll, 0), 1);

      if (progress < 0.35) {
        const t = progress / 0.35;
        targetCam.camX = configs.home.camX + (configs.features.camX - configs.home.camX) * t;
        targetCam.camY = configs.home.camY + (configs.features.camY - configs.home.camY) * t;
        targetCam.camZ = configs.home.camZ + (configs.features.camZ - configs.home.camZ) * t;
        targetLookAt.x = configs.home.lookX + (configs.features.lookX - configs.home.lookX) * t;
        targetLookAt.y = configs.home.lookY + (configs.features.lookY - configs.home.lookY) * t;
        targetLookAt.z = configs.home.lookZ + (configs.features.lookZ - configs.home.lookZ) * t;
        targetModelX = configs.home.modelX + (configs.features.modelX - configs.home.modelX) * t;
        targetModelY = configs.home.modelY + (configs.features.modelY - configs.home.modelY) * t;
        targetModelScale = configs.home.modelScale + (configs.features.modelScale - configs.home.modelScale) * t;
        targetModelRotY = configs.home.modelRotY + (configs.features.modelRotY - configs.home.modelRotY) * t;
      } else if (progress < 0.8) {
        const t = (progress - 0.35) / 0.45;
        targetCam.camX = configs.features.camX + (configs.pricing.camX - configs.features.camX) * t;
        targetCam.camY = configs.features.camY + (configs.pricing.camY - configs.features.camY) * t;
        targetCam.camZ = configs.features.camZ + (configs.pricing.camZ - configs.features.camZ) * t;
        targetLookAt.x = configs.features.lookX + (configs.pricing.lookX - configs.features.lookX) * t;
        targetLookAt.y = configs.features.lookY + (configs.pricing.lookY - configs.features.lookY) * t;
        targetLookAt.z = configs.features.lookZ + (configs.pricing.lookZ - configs.features.lookZ) * t;
        targetModelX = configs.features.modelX + (configs.pricing.modelX - configs.features.modelX) * t;
        targetModelY = configs.features.modelY + (configs.pricing.modelY - configs.features.modelY) * t;
        targetModelScale = configs.features.modelScale + (configs.pricing.modelScale - configs.features.modelScale) * t;
        targetModelRotY = configs.features.modelRotY + (configs.pricing.modelRotY - configs.features.modelRotY) * t;
      } else {
        targetCam.camX = configs.pricing.camX;
        targetCam.camY = configs.pricing.camY;
        targetCam.camZ = configs.pricing.camZ;
        targetLookAt.set(configs.pricing.lookX, configs.pricing.lookY, configs.pricing.lookZ);
        targetModelX = configs.pricing.modelX;
        targetModelY = configs.pricing.modelY;
        targetModelScale = configs.pricing.modelScale;
        targetModelRotY = configs.pricing.modelRotY;
      }
    }

    window.addEventListener('scroll', onScroll, { passive: true });

    function animate() {
      requestAnimationFrame(animate);

      camera.position.x += (targetCam.camX - camera.position.x) * 0.055;
      camera.position.y += (targetCam.camY - camera.position.y) * 0.055;
      camera.position.z += (targetCam.camZ - camera.position.z) * 0.055;

      currentLookAt.x += (targetLookAt.x - currentLookAt.x) * 0.055;
      currentLookAt.y += (targetLookAt.y - currentLookAt.y) * 0.055;
      currentLookAt.z += (targetLookAt.z - currentLookAt.z) * 0.055;
      camera.lookAt(currentLookAt);

      currentModelX += (targetModelX - currentModelX) * 0.055;
      currentModelY += (targetModelY - currentModelY) * 0.055;
      currentModelScale += (targetModelScale - currentModelScale) * 0.055;
      currentModelRotY += (targetModelRotY - currentModelRotY) * 0.055;

      buildingGroup.position.set(currentModelX, currentModelY, 0);
      buildingGroup.scale.set(currentModelScale, currentModelScale, currentModelScale);
      buildingGroup.rotation.y = currentModelRotY;

      particlesMesh.rotation.y += 0.0012;
      particlesMesh.rotation.x += 0.0006;

      renderer.render(scene, camera);
    }

    window.addEventListener('resize', () => {
      const w = window.innerWidth;
      const h = window.innerHeight;
      camera.aspect = w / h;
      camera.updateProjectionMatrix();
      renderer.setSize(w, h);
    });

    animate();
  }

  initThree();
};

window.initLandingNav = function() {
    const navLinks = document.querySelectorAll('header nav a');
    
    navLinks.forEach(link => {
        link.addEventListener('click', (e) => {
            e.preventDefault();
            const path = link.getAttribute('data-path');
            
            // Dispatch custom event for 3D model
            window.dispatchEvent(new CustomEvent('nav-change', { detail: { section: path } }));
            
            // Update active navigation state styling
            navLinks.forEach(l => {
                l.classList.remove('text-primary', 'font-bold');
                l.classList.add('text-on-surface-variant');
            });
            link.classList.add('text-primary', 'font-bold');
            link.classList.remove('text-on-surface-variant');
            
            // Smooth scroll to section if it exists on page
            const targetSection = document.getElementById(path);
            if(targetSection) {
                targetSection.scrollIntoView({ behavior: 'smooth' });
            }
        });
    });
};
