const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { resolve } = require('node:path');
const source = readFileSync(resolve(__dirname,
    '../../src/PropFlow.Web.Client/Shared/Feedback/PropFlowConfirmationDialog.razor.js'), 'utf8');
const modulePromise = import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));

// InvokeAsync<IJSObjectReference> wraps the returned JS object itself. A manually
// wrapped result would make .NET invoke close on a reference descriptor instead.
test('disconnected dialog returns an object with close for the Blazor bridge', async () => {
    global.DotNet = { createJSObjectReference: () => ({ __jsObjectId: 1 }) };
    const { openDialog } = await modulePromise;
    const registration = openDialog({ isConnected: false });
    assert.equal(typeof registration.close, 'function');
    registration.close();
});

test('connected dialog registration closes and releases observers once', async () => {
    const { openDialog } = await modulePromise;
    let closes = 0, disconnects = 0;
    global.DotNet = { createJSObjectReference: () => ({ __jsObjectId: 2 }) };
    global.HTMLElement = class {};
    global.MutationObserver = class {
        observe() {}
        disconnect() { disconnects++; }
    };
    global.document = Object.assign(new EventTarget(), { body: {}, activeElement: null });
    const dialog = Object.assign(new EventTarget(), {
        isConnected: true, open: false, dataset: {},
        querySelector: () => ({ click() {}, focus() {} }),
        contains: () => false,
        showModal() { this.open = true; },
        close() { this.open = false; closes++; }
    });
    const registration = openDialog(dialog);
    assert.equal(typeof registration.close, 'function');
    registration.close();
    registration.close();
    assert.equal(closes, 1);
    assert.equal(disconnects, 1);
    assert.equal(dialog.open, false);
});
