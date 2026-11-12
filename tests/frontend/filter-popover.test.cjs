const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { resolve } = require('node:path');

const source = readFileSync(resolve(__dirname,
    '../../src/PropFlow.Web.Client/Shared/Forms/PropFlowFilterPopover.razor.js'), 'utf8');
const modulePromise = import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));

test('filter popover waits for its fields before becoming visible and taking focus', async () => {
    const { connect } = await modulePromise;
    const trigger = new EventTarget();
    const attributes = new Map();
    let focus;
    Object.assign(trigger, {
        getBoundingClientRect: () => ({ top: 410, bottom: 450, right: 382 }),
        getAttribute: key => attributes.get(key),
        setAttribute: (key, value) => attributes.set(key, value),
        focus: options => { focus = options; }
    });
    const panel = new EventTarget();
    const style = {};
    const fields = { firstElementChild: null, scrollHeight: 500, clientHeight: 265 };
    const panelAttributes = new Set();
    let closeFocus;
    const close = { focus: options => { closeFocus = options; } };
    let open = true;
    Object.assign(panel, {
        offsetHeight: 360, style, querySelector: selector => selector === '.pf-filter-fields' ? fields : close,
        hasAttribute: key => panelAttributes.has(key),
        setAttribute: key => panelAttributes.add(key),
        removeAttribute: key => panelAttributes.delete(key),
        getBoundingClientRect: () => ({ width: 360, height: Math.min(360, parseFloat(style.maxHeight)) }),
        matches: selector => selector === ':popover-open' && open,
        hidePopover: () => {
            panel.dispatchEvent(Object.assign(new Event('beforetoggle'), { newState: 'closed' }));
            assert.equal(panelAttributes.has('data-positioned'), false);
            open = false;
            toggle('closed');
        }
    });
    function toggle(state) {
        panel.dispatchEvent(Object.assign(new Event('toggle'), { newState: state }));
    }

    const signals = [];
    let disconnected = 0;
    const window = Object.assign(new EventTarget(), { innerWidth: 390, innerHeight: 540 });
    const add = window.addEventListener.bind(window);
    window.addEventListener = (event, callback, options) => {
        signals.push(options.signal);
        add(event, callback, options);
    };
    const original = {
        window: global.window, document: global.document,
        ResizeObserver: global.ResizeObserver, MutationObserver: global.MutationObserver
    };
    global.window = window;
    global.document = { activeElement: null };
    global.ResizeObserver = class { observe() {} disconnect() { disconnected++; } };
    let fieldsChanged;
    global.MutationObserver = class {
        constructor(callback) { fieldsChanged = callback; }
        observe() {}
        disconnect() {}
    };
    const closed = [];
    try {
        const root = { querySelector: selector => selector === '.pf-filter-trigger' ? trigger : panel };
        const controller = connect(root, { invokeMethodAsync: name => closed.push(name) });
        toggle('open');
        assert.equal(attributes.get('aria-expanded'), 'true');
        assert.equal(style.top, undefined);
        assert.equal(panelAttributes.has('data-positioned'), false);
        assert.equal(closeFocus, undefined);

        fields.firstElementChild = {};
        fieldsChanged();
        assert.equal(style.left, '22px');
        assert.equal(style.top, '44px');
        assert.equal(style.maxHeight, '396px');
        assert.equal(panelAttributes.has('data-positioned'), true);
        assert.deepEqual(closeFocus, { preventScroll: true });

        controller.close();
        assert.equal(attributes.get('aria-expanded'), 'false');
        assert.deepEqual(focus, { preventScroll: true });
        assert.deepEqual(closed, ['Closed']);
        assert.equal(disconnected, 1);
        assert.ok(signals.every(signal => signal.aborted));

        attributes.set('aria-disabled', 'true');
        const click = new Event('click', { cancelable: true });
        trigger.dispatchEvent(click);
        assert.equal(click.defaultPrevented, true);
        controller.disconnect();
        const afterDispose = new Event('click', { cancelable: true });
        trigger.dispatchEvent(afterDispose);
        assert.equal(afterDispose.defaultPrevented, false);
    } finally {
        Object.assign(global, original);
    }
});
