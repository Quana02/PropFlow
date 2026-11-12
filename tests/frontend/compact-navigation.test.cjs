const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const source = fs.readFileSync(path.join(root, 'src/PropFlow.Web.Client/wwwroot/js/app-shell-nav.js'), 'utf8');
const modulePromise = import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));

async function fixture(activePosition = 600) {
    const { initializeScrolling } = await modulePromise;
    const observers = [];
    class Observer {
        constructor(callback) { this.callback = callback; this.disconnected = false; observers.push(this); }
        observe() {}
        disconnect() { this.disconnected = true; }
        fire() { this.callback([]); }
    }
    global.MutationObserver = Observer;
    global.ResizeObserver = Observer;
    const listeners = new Map();
    let bottom = 400;
    const viewport = {
        scrollTop: 0, clientHeight: 300,
        getBoundingClientRect: () => ({ top: 100, bottom }),
        querySelector: () => active,
        addEventListener: (name, handler) => listeners.set(name, handler),
        removeEventListener: name => listeners.delete(name)
    };
    const item = position => ({
        getBoundingClientRect: () => ({ top: 100 + position - viewport.scrollTop, bottom: 144 + position - viewport.scrollTop })
    });
    let active = activePosition === null ? null : item(activePosition);
    const handle = initializeScrolling({ querySelector: () => viewport });
    return { viewport, listeners, observers, handle, item,
        setActive(position) { active = item(position); observers[0].fire(); },
        resize(value) { bottom = value; observers[1].fire(); } };
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
    assert.ok(f.observers.every(observer => observer.disconnected));
    f.observers[0].fire(); await flush();
    assert.equal(f.viewport.scrollTop, 0);
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
    assert.doesNotMatch(source, /addEventListener\(["']wheel/);
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
