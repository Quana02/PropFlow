const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { resolve } = require('node:path');

const source = readFileSync(resolve(__dirname,
    '../../src/PropFlow.Web.Client/Shared/Feedback/PropFlowConfirmationDialog.razor.js'), 'utf8');
const modulePromise = import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));

// Blazor marshals an IJSObjectReference return value itself. Pre-wrapping the
// controller returns an object-ID descriptor instead of the callable controller.
function environment(connected) {
    const originals = { document: global.document, HTMLElement: global.HTMLElement,
        DotNet: global.DotNet, MutationObserver: global.MutationObserver };
    class Element extends EventTarget {
        constructor() { super(); this.isConnected = true; }
        focus() { global.document.activeElement = this; }
    }
    const document = new EventTarget();
    const previous = new Element();
    const button = new Element();
    const heading = new Element();
    document.body = new Element();
    document.activeElement = previous;
    const signals = []; let closes = 0;
    const dialog = new Element();
    Object.assign(dialog, {
        isConnected: connected,
        open: false,
        dataset: { dismissOnBackdrop: 'false' },
        querySelector: selector => selector === '[data-dialog-close]' ? button : selector === 'h2' ? heading : null,
        contains: element => element === button || element === heading,
        showModal() { this.open = true; document.activeElement = heading; },
        close() { closes++; this.open = false; document.activeElement = document.body; }
    });
    for (const target of [document, dialog]) {
        const add = target.addEventListener.bind(target);
        target.addEventListener = (name, handler, options) => {
            signals.push(options.signal);
            add(name, handler, options);
        };
    }
    global.document = document;
    global.HTMLElement = Element;
    global.DotNet = { createJSObjectReference: () => ({ __jsObjectId: 123 }) };
    const observers = [];
    global.MutationObserver = class {
        constructor(callback) { this.callback = callback; this.disconnected = false; this.disconnects = 0; observers.push(this); }
        observe() {}
        disconnect() { this.disconnected = true; this.disconnects++; }
    };
    return { dialog, previous, document, signals, observers, get closes() { return closes; },
        restore() { Object.assign(global, originals); }
    };
}

test('dialog returns a callable controller for Blazor and releases modal listeners and focus', async () => {
    const { openDialog } = await modulePromise;
    const dom = environment(true);
    try {
        const handle = openDialog(dom.dialog);
        assert.equal(typeof handle.close, 'function', 'IJSObjectReference must target a controller with close(), not an object-ID descriptor');
        assert.equal(dom.dialog.open, true);
        handle.close();
        assert.equal(dom.dialog.open, false);
        assert.ok(dom.signals.length > 0);
        assert.ok(dom.signals.every(signal => signal.aborted));
        assert.ok(dom.observers.every(observer => observer.disconnected));
        assert.equal(dom.document.activeElement, dom.previous);
        assert.doesNotThrow(() => handle.close());
        assert.equal(dom.closes, 1);
        assert.ok(dom.observers.every(observer => observer.disconnects === 1));
    } finally { dom.restore(); }
});

test('removing the modal releases listeners and focus before Blazor disposes the reference', async () => {
    const { openDialog } = await modulePromise;
    const dom = environment(true);
    try {
        const handle = openDialog(dom.dialog);
        dom.dialog.isConnected = false;
        dom.document.activeElement = dom.document.body;
        for (const observer of dom.observers) observer.callback();
        assert.equal(dom.dialog.open, false);
        assert.ok(dom.signals.every(signal => signal.aborted));
        assert.ok(dom.observers.every(observer => observer.disconnected));
        assert.equal(dom.document.activeElement, dom.previous);
        assert.doesNotThrow(() => handle.close());
    } finally { dom.restore(); }
});

test('dialog removed before interop completes still returns a callable no-op close controller', async () => {
    const { openDialog } = await modulePromise;
    const dom = environment(false);
    try {
        const handle = openDialog(dom.dialog);
        assert.equal(typeof handle.close, 'function');
        assert.doesNotThrow(() => handle.close());
        assert.equal(dom.dialog.open, false);
        assert.equal(dom.signals.length, 0);
    } finally { dom.restore(); }
});
