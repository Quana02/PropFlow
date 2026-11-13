import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { join, resolve, sep } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';

const [browser, fixtureDirectory, screenshotDirectory] = process.argv.slice(2);
assert.ok(browser && fixtureDirectory, 'Usage: node scripts/test-confirmation-dialog.mjs <Chrome executable> <rendered fixture directory> [screenshot directory]');

const source = new URL('../src/PropFlow.Web.Client/Shared/Feedback/', import.meta.url);
const stylesheet = await readFile(new URL('../src/PropFlow.Web.Client/obj/Debug/net8.0/scopedcss/Shared/Feedback/PropFlowConfirmationDialog.razor.rz.scp.css', import.meta.url), 'utf8');
const module = await readFile(new URL('PropFlowConfirmationDialog.razor.js', source), 'utf8');
const tones = ['confirm', 'danger', 'info', 'decision'];
const markup = await Promise.all(tones.map(tone => readFile(join(fixtureDirectory, tone + '.html'), 'utf8')));
const page = '<!doctype html><html lang="vi"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">' +
    '<style>body{margin:0;background:#eef5fd;color:#14253c;font:16px/1.45 "Segoe UI",sans-serif}' +
    'html[data-theme="dark"] body{background:#0b1727;color:#e8f3ff}' +
    'main{max-width:65rem;margin:auto;padding:2rem}.preview-card{border:1px solid #5f8ab35e;border-radius:1rem;padding:1rem;min-height:10rem}' +
    'html:has(.pf-dialog[open]){overflow:hidden}' + stylesheet + '</style>' +
    '<main><p>PropFlow · Xem trước hộp thoại</p><div class="preview-card"><h1>Thông tin thao tác</h1><button id="pf-preview-launcher">Mở hộp thoại</button></div></main>' +
    markup.join('') + '<script type="module">' + module +
    'window.DotNet={createJSObjectReference: value => value}; window.openDialog=openDialog;</script>';

const profileRoot = resolve(fixtureDirectory);
const profile = await mkdtemp(join(profileRoot, 'chrome-dialog-'));
const processHandle = spawn(browser, [
    '--headless', '--disable-gpu', '--disable-background-networking', '--disable-sync',
    '--no-first-run', '--no-default-browser-check', '--remote-debugging-port=0',
    '--user-data-dir=' + profile, 'about:blank'
], { windowsHide: true, stdio: 'ignore' });
const exited = once(processHandle, 'exit');
exited.catch(() => {});
let socket;
let send;

try {
    let port;
    for (let attempt = 0; attempt < 120 && !port; attempt++) {
        try { port = (await readFile(join(profile, 'DevToolsActivePort'), 'utf8')).split('\n')[0].trim(); }
        catch (error) { if (error.code !== 'ENOENT') throw error; }
        if (!port) await delay(100);
    }
    assert.ok(port, 'Chrome DevTools did not become available. PID ' + processHandle.pid);
    const targets = await (await fetch('http://127.0.0.1:' + port + '/json/list')).json();
    const target = targets.find(item => item.type === 'page' && item.url === 'about:blank') ?? targets.find(item => item.type === 'page');
    assert.ok(target, 'No Chrome page target');
    socket = new WebSocket(target.webSocketDebuggerUrl);
    await once(socket, 'open');
    let sequence = 0;
    send = (method, params = {}) => new Promise((resolveCommand, rejectCommand) => {
        const id = ++sequence;
        const timer = setTimeout(() => { socket.removeEventListener('message', receive); rejectCommand(new Error('Timed out: ' + method)); }, 5000);
        function receive(event) {
            const reply = JSON.parse(event.data);
            if (reply.id !== id) return;
            socket.removeEventListener('message', receive);
            clearTimeout(timer);
            if (reply.error) rejectCommand(new Error(reply.error.message));
            else resolveCommand(reply.result);
        }
        socket.addEventListener('message', receive);
        socket.send(JSON.stringify({ id, method, params }));
    });

    const evaluate = async (expression) => {
        const { result, exceptionDetails } = await send('Runtime.evaluate', { expression, returnByValue: true });
        assert.ok(!exceptionDetails, exceptionDetails?.exception?.description);
        return result.value;
    };
    const key = async (key, virtualKey, modifiers = 0) => {
        const args = { key, code: key, windowsVirtualKeyCode: virtualKey, modifiers };
        await send('Input.dispatchKeyEvent', { type: 'keyDown', ...args });
        await send('Input.dispatchKeyEvent', { type: 'keyUp', ...args });
    };
    const clickBackdrop = async () => {
        for (const type of ['mousePressed', 'mouseReleased']) {
            await send('Input.dispatchMouseEvent', { type, x: 4, y: 4, button: 'left', clickCount: 1 });
        }
    };
    const open = (tone) => evaluate("show(" + JSON.stringify(tone) + ")");
    const { frameTree } = await send('Page.getFrameTree');
    await send('Page.setDocumentContent', { frameId: frameTree.frame.id, html: page });
    for (let attempt = 0; attempt < 100 && !await evaluate('typeof window.openDialog === "function"'); attempt++) await delay(50);
    assert.equal(await evaluate("document.querySelectorAll('dialog').length"), tones.length,
        await evaluate('location.href + " " + document.body?.innerText.slice(0, 180)'));
    assert.equal(await evaluate('typeof window.openDialog'), 'function');
    await evaluate(String.raw`
        window.dismissals = 0;
        window.show = tone => {
            const launcher = document.querySelector('#pf-preview-launcher');
            if (!launcher) throw new Error('Preview launcher missing: ' + document.body.innerHTML.slice(0, 300));
            window.launcher = launcher;
            launcher.focus();
            window.dialog = document.querySelector('[data-tone=' + tone + ']');
            window.handle = openDialog(dialog);
            dialog.querySelector('[data-dialog-close]').onclick = () => { dismissals++; handle.close(); };
            return document.activeElement.className;
        };
        window.snapshot = () => {
            const { left, right, top, bottom } = dialog.getBoundingClientRect();
            return {
                open: dialog.open, left, right, top, bottom, width: innerWidth, height: innerHeight,
                focus: document.activeElement.className, restored: document.activeElement === window.launcher,
                color: getComputedStyle(dialog.querySelector('.pf-dialog__primary')).backgroundColor,
                surface: getComputedStyle(dialog).backgroundColor,
                footer: getComputedStyle(dialog.querySelector('footer')).flexDirection,
                noOverflow: dialog.querySelector('footer').scrollWidth === dialog.querySelector('footer').clientWidth
            };
        };
    `);

    for (const theme of ['light', 'dark']) {
        await evaluate('document.documentElement.dataset.theme = ' + JSON.stringify(theme));
        for (const width of [320, 390, 768, 1366, 1440]) {
            await send('Emulation.setDeviceMetricsOverride', { width, height: 720, deviceScaleFactor: 1, mobile: false });
            for (const tone of tones) {
                const info = tone === 'info';
                const safe = info ? 'pf-dialog__primary' : 'pf-dialog__secondary';
                assert.equal(await open(tone), safe);
                await delay(185);
                const bounds = await evaluate('snapshot()');
                assert.equal(bounds.width, width);
                assert.ok(bounds.open && bounds.noOverflow);
                assert.ok(bounds.left >= 12 && bounds.right <= width - 12 && bounds.top >= 12 && bounds.bottom <= bounds.height - 12);
                assert.equal(bounds.surface, theme === 'dark' ? 'rgb(19, 34, 56)' : 'rgb(255, 255, 255)');
                assert.equal(bounds.footer, width <= 390 || tone === 'decision' ? 'column' : 'row');
                const colors = theme === 'dark' ? ['rgb(255, 173, 182)', 'rgb(130, 199, 255)'] : ['rgb(174, 38, 54)', 'rgb(7, 94, 180)'];
                assert.equal(bounds.color, colors[tone === 'danger' ? 0 : 1]);

                if (screenshotDirectory && [390, 1440].includes(width) && tone !== 'decision') {
                    const { data } = await send('Page.captureScreenshot', { format: 'png' });
                    await writeFile(join(screenshotDirectory, tone + '-' + theme + '-' + width + '.png'), Buffer.from(data, 'base64'));
                }

                await evaluate('dialog.querySelector(".pf-dialog__primary").focus()');
                await key('Tab', 9);
                assert.equal((await evaluate('snapshot()')).focus, 'pf-dialog__close');
                await key('Tab', 9, 8);
                assert.equal((await evaluate('snapshot()')).focus, 'pf-dialog__primary');

                const backdropDismisses = info || tone === 'confirm';
                await clickBackdrop();
                assert.equal((await evaluate('snapshot()')).open, !backdropDismisses);
                if (!backdropDismisses) await key('Escape', 27);
                const closed = await evaluate('snapshot()');
                assert.ok(!closed.open && closed.restored, 'Focus after closing ' + tone);
            }
        }
    }

    await open('danger');
    await evaluate('dialog.querySelector("[data-dialog-close]").disabled = true');
    await key('Escape', 27);
    assert.ok((await evaluate('snapshot()')).open, 'A busy modal must not dismiss on Escape');
    await evaluate('dialog.querySelector("[data-dialog-close]").disabled = false');
    await key('Escape', 27);
    assert.ok((await evaluate('snapshot()')).restored, 'Focus returns after a busy dialog becomes dismissible');

    await open('danger');
    await evaluate(String.raw`
        const formField = document.createElement('input');
        dialog.querySelector('.pf-dialog__body').append(formField);
        window.confirmed = 0;
        dialog.querySelector('.pf-dialog__primary').onclick = () => confirmed++;
        formField.focus();
    `);
    await key('Enter', 13);
    assert.equal(await evaluate('confirmed'), 0, 'Enter in a field must not submit a danger action');
    await evaluate(String.raw`
        const nested = document.querySelector('[data-tone=info]');
        const nestedHandle = openDialog(nested);
        nested.querySelector('[data-dialog-close]').onclick = () => nestedHandle.close();
    `);
    await key('Escape', 27);
    assert.ok(await evaluate("dialog.open && document.activeElement.tagName === 'INPUT'"), 'Closing a nested dialog restores focus inside its parent');
    await key('Escape', 27);
    assert.ok((await evaluate('snapshot()')).restored, 'Closing the parent restores focus to its launcher');
    await evaluate(String.raw`
        const shell = document.createElement('div');
        shell.className = 'app-shell';
        document.body.append(shell);
        shell.append(document.querySelector('[data-tone=confirm]'));
    `);
    await open('confirm');
    assert.equal((await evaluate('snapshot()')).surface, 'rgb(255, 255, 255)', 'The internal shell controls its own light theme');
    await evaluate('dialog.parentElement.classList.add("app-shell-dark")');
    assert.equal((await evaluate('snapshot()')).surface, 'rgb(19, 34, 56)', 'An open dialog follows the shell theme toggle');
    await evaluate('handle.close()');
    console.log('PASS: 4 variants × 5 viewport widths × 2 themes, keyboard focus, backdrop, Escape, and focus return. Chrome PID ' + processHandle.pid);
} finally {
    if (send && socket?.readyState === WebSocket.OPEN) {
        try { await send('Browser.close'); } catch { /* Chrome may close the socket before replying. */ }
    }
    socket?.close();
    if (processHandle.exitCode === null && processHandle.signalCode === null) processHandle.kill();
    await exited;
    assert.ok(resolve(profile).startsWith(profileRoot + sep), 'Unexpected browser profile path');
    await rm(profile, { recursive: true, force: true, maxRetries: 5, retryDelay: 100 });
}
