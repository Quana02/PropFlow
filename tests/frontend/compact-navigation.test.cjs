const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const source = fs.readFileSync(path.join(root, 'src/PropFlow.Web.Client/wwwroot/js/app-shell-nav.js'), 'utf8');
const modulePromise = import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));

async function fixture(activePosition = 600, reducedMotion = false) {
    const { initializeScrolling } = await modulePromise;
    const observers = [];
    class Observer {
        constructor(callback) { this.callback = callback; this.disconnected = false; observers.push(this); }
        observe() {}
        disconnect() { this.disconnected = true; }
        fire(records = []) { this.callback(records); }
    }
    global.MutationObserver = Observer;
    global.ResizeObserver = Observer;
    global.window = {
        matchMedia: () => ({ matches: reducedMotion }),
        getComputedStyle: () => ({ scrollPaddingTop: '0px', scrollPaddingBottom: '0px' })
    };
    const eventSource = () => {
        const listeners = new Map();
        return {
            listeners,
            addEventListener: (name, handler) => listeners.set(name, handler),
            removeEventListener: name => listeners.delete(name)
        };
    };
    const ownerDocument = { activeElement: null };
    const up = { ...eventSource(), ownerDocument };
    const down = { ...eventSource(), ownerDocument };
    const scrolls = [];
    let bottom = 400;
    const content = { querySelector: selector => selector === "a[title]" ? { offsetHeight: 44 } : active };
    const viewport = {
        ...eventSource(),
        scrollTop: 0, clientHeight: 300, scrollHeight: 800,
        getBoundingClientRect: () => ({ top: 100, bottom }),
        querySelector: () => content,
        scrollBy: options => scrolls.push(options),
        focus: options => { assert.deepEqual(options, { preventScroll: true }); ownerDocument.activeElement = viewport; },
        contains: target => target === viewport
    };
    const item = position => ({
        getBoundingClientRect: () => ({ top: 100 + position - viewport.scrollTop, bottom: 144 + position - viewport.scrollTop })
    });
    let active = activePosition === null ? null : item(activePosition);
    const rail = {
        ...eventSource(),
        querySelector: selector => ({ ".app-nav-scroll": viewport, ".app-nav-scroll-up": up, ".app-nav-scroll-down": down })[selector]
    };
    const handle = initializeScrolling(rail);
    return { viewport, listeners: viewport.listeners, observers, handle, item, rail, up, down, scrolls, ownerDocument,
        setActive(position) { active = position === null ? null : item(position); observers[0].fire(); },
        setScroll(top) { viewport.scrollTop = top; viewport.listeners.get("scroll")(); },
        resize(value) { bottom = value; viewport.clientHeight = value - 100; observers[1].fire(); } };
}
const flush = () => new Promise(resolve => queueMicrotask(resolve));

test('initial active item outside six rows is revealed by moving only the nav viewport', async () => {
    const f = await fixture();
    await flush();
    assert.equal(f.viewport.scrollTop, 344);
    assert.doesNotMatch(source, /\.scrollIntoView\s*\(/);
    f.handle.dispose();
});
test('normal rerenders keep the user scroll; changed active links reveal with minimal movement', async () => {
    const f = await fixture(); await flush();
    f.viewport.scrollTop = 80;
    f.observers[0].fire(); await flush();
    assert.equal(f.viewport.scrollTop, 80);
    f.setActive(0); await flush();
    assert.equal(f.viewport.scrollTop, 0);
    f.setActive(50); await flush();
    assert.equal(f.viewport.scrollTop, 0);
    f.handle.dispose();
});
test('late authorization content, keyboard focus and a short resized viewport stay accessible', async () => {
    const f = await fixture(null); await flush();
    f.setActive(600); await flush();
    assert.equal(f.viewport.scrollTop, 344);
    f.listeners.get('focusin')({ target: { closest: () => f.item(0) } });
    assert.equal(f.viewport.scrollTop, 0);
    f.resize(250); await flush();
    assert.equal(f.viewport.scrollTop, 494);
    f.handle.dispose();
});
test('dispose disconnects observers/listeners and prevents queued work', async () => {
    const f = await fixture();
    // Queue a new route change, then dispose in the same turn.
    f.viewport.scrollTop = 0;
    f.setActive(800);
    f.handle.dispose(); await flush();
    assert.equal(f.viewport.scrollTop, 0);
    assert.equal(f.listeners.size, 0);
    assert.equal(f.rail.listeners.size + f.up.listeners.size + f.down.listeners.size, 0);
    assert.ok(f.observers.every(observer => observer.disconnected));
    f.observers[0].fire(); await flush();
    assert.equal(f.viewport.scrollTop, 0);
});

test('chevrons follow scroll position, menu changes and viewport resize', async () => {
    const f = await fixture(null); await flush();
    assert.deepEqual([f.up.hidden, f.down.hidden, f.up.disabled, f.down.disabled], [false, false, true, false]);
    f.setScroll(120);
    assert.deepEqual([f.up.disabled, f.down.disabled], [false, false]);
    f.ownerDocument.activeElement = f.down;
    f.setScroll(499.5);
    assert.deepEqual([f.up.disabled, f.down.disabled], [false, true]);
    assert.equal(f.ownerDocument.activeElement, f.viewport);

    f.viewport.scrollHeight = 300;
    f.viewport.scrollTop = 0;
    f.observers[0].fire([{ type: 'childList' }]); await flush();
    assert.deepEqual([f.up.hidden, f.down.hidden], [true, true]);
    f.resize(280); await flush();
    assert.deepEqual([f.up.hidden, f.down.hidden, f.up.disabled, f.down.disabled], [false, false, true, false]);
    f.handle.dispose();
});

test('chevrons scroll a few rows and respect reduced motion', async () => {
    const f = await fixture(null); await flush();
    f.down.listeners.get("click")();
    f.setScroll(121);
    f.up.listeners.get("click")();
    assert.deepEqual(f.scrolls, [{ top: 121, behavior: 'smooth' }, { top: -121, behavior: 'smooth' }]);
    f.handle.dispose();

    const reduced = await fixture(null, true); await flush();
    reduced.down.listeners.get("click")();
    assert.deepEqual(reduced.scrolls, [{ top: 121, behavior: 'instant' }]);
    reduced.handle.dispose();
});

test('wheel stays native in the viewport and stays on the rail over its controls', async () => {
    const f = await fixture(null); await flush();
    const wheel = f.rail.listeners.get("wheel");
    let prevented = 0;
    const event = { target: f.viewport, deltaY: 3, deltaMode: 1, preventDefault: () => prevented++ };
    wheel(event);
    assert.equal(prevented, 0);
    wheel({ ...event, target: f.down });
    assert.equal(prevented, 1);
    assert.equal(f.viewport.scrollTop, 48);
    f.viewport.scrollHeight = f.viewport.clientHeight;
    wheel(event);
    assert.equal(prevented, 2);
    assert.equal(f.viewport.scrollTop, 48);
    f.handle.dispose();
});

test('scroll constraints are local, hide both scrollbar implementations, and cap six uniform rows', () => {
    const css = fs.readFileSync(path.join(root, 'src/PropFlow.Web.Client/Layout/Navigation/Shared/AppNavMenu.razor.css'), 'utf8');
    const rules = css.match(/\.app-nav-scroll \{([^}]+)\}/)[1];
    assert.match(rules, /6 \* var\(--app-nav-item-size\)/);
    assert.match(rules, /100dvh - 5rem/);
    assert.match(rules, /overflow-y: auto/);
    assert.match(rules, /overflow-x: hidden/);
    assert.match(rules, /overscroll-behavior-y: contain/);
    assert.match(rules, /scrollbar-width: none/);
    assert.match(css, /\.app-nav-scroll::\-webkit-scrollbar \{ display: none/);
});

test('a restored dock reports its edge so the shell can reserve space on the same side', async () => {
    const { initialize } = await modulePromise;
    const listeners = new Map();
    const previous = Object.fromEntries(['window', 'innerWidth', 'innerHeight', 'sessionStorage', 'ResizeObserver']
        .map(key => [key, Object.getOwnPropertyDescriptor(globalThis, key)]));
    try {
        globalThis.innerWidth = 1366;
        globalThis.innerHeight = 768;
        globalThis.window = {
            innerWidth,
            addEventListener: (type, callback) => listeners.set(type, callback),
            removeEventListener: type => listeners.delete(type),
            clearTimeout,
        };
        globalThis.sessionStorage = { getItem: () => JSON.stringify({ left: 24, top: 200 }) };
        globalThis.ResizeObserver = class { observe() {} disconnect() {} };
        const dragHandle = { addEventListener() {}, removeEventListener() {} };
        const rail = {
            querySelector: selector => selector === '.app-nav-drag' ? dragHandle : null,
            offsetWidth: 64,
            offsetHeight: 350,
            style: {},
            dataset: {},
        };

        const handle = initialize(rail);
        assert.equal(rail.dataset.dockSide, 'left');
        assert.equal(rail.style.left, '24px');
        rail.style.left = '1280px';
        listeners.get('resize')();
        assert.equal(rail.dataset.dockSide, 'right');
        assert.equal(rail.style.left, '1278px');
        handle.dispose();
        assert.equal(listeners.size, 0);
    } finally {
        for (const [key, descriptor] of Object.entries(previous)) {
            if (descriptor) Object.defineProperty(globalThis, key, descriptor);
            else delete globalThis[key];
        }
    }
});
