import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { dirname, join, resolve, sep } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';

const [browser, fixture, screenshotDirectory] = process.argv.slice(2);
assert.ok(browser && fixture, 'Usage: node scripts/test-table-pagination.mjs <Chrome executable> <rendered fixture> [screenshot directory]');
const fixtureDirectory = dirname(fixture);
const styles = await readFile(new URL('../src/PropFlow.Web.Client/obj/Debug/net8.0/scopedcss/Shared/Tables/TablePagination.razor.rz.scp.css', import.meta.url), 'utf8');
const module = await readFile(new URL('../src/PropFlow.Web.Client/Shared/Tables/TablePagination.razor.js', import.meta.url), 'utf8');
const markup = await readFile(fixture, 'utf8');
const page = '<!doctype html><html lang="vi"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">' +
    '<style>body{margin:0;font:16px "Segoe UI",sans-serif;background:#eef5fc}html[data-theme="dark"] body{background:#0c1827}' +
    'main{position:fixed;inset:0 0 auto;overflow:hidden}main.bottom{inset:auto 0 0}' + styles + '</style>' +
    '<div id="outside" style="position:fixed;top:40%;left:8px;width:16px;height:16px"></div><main>' + markup +
    '</main><script type="module">' + module +
    'window.connectPager=connect;</script>';

const profileRoot = resolve(fixtureDirectory);
const profile = await mkdtemp(join(profileRoot, 'chrome-pagination-'));
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
        await send('Input.dispatchKeyEvent', { type: 'keyDown', ...args, text: key === 'Enter' ? '\r' : undefined });
        await send('Input.dispatchKeyEvent', { type: 'keyUp', ...args });
    };

    const click = async (selector) => {
        const [x, y] = await evaluate(`(() => {
            const { left, top, width, height } = document.querySelector(${JSON.stringify(selector)}).getBoundingClientRect();
            return [left + width / 2, top + height / 2];
        })()`);
        for (const type of ['mousePressed', 'mouseReleased']) {
            await send('Input.dispatchMouseEvent', { type, x, y, button: 'left', clickCount: 1 });
        }
    };
    const { frameTree } = await send('Page.getFrameTree');
    await send('Page.setDocumentContent', { frameId: frameTree.frame.id, html: page });
    for (let attempt = 0; attempt < 100 && !await evaluate('typeof connectPager === "function"'); attempt++) await delay(50);
    await evaluate(`
        window.pager = document.querySelector('.table-pagination');
        window.trigger = pager.querySelector('.page-number');
        window.popup = pager.querySelector('.page-jump');
        window.input = popup.querySelector('input');
        window.handle = connectPager(pager, popup);
        window.submissions = [];
        popup.querySelector('form').addEventListener('submit', event => {
            event.preventDefault();
            submissions.push(input.value);
            handle.close();
        });
        window.snapshot = () => {
            const anchor = trigger.getBoundingClientRect();
            const rect = popup.getBoundingClientRect();
            return {
                open: popup.matches(':popover-open'), focus: document.activeElement === input,
                returned: document.activeElement === trigger, above: rect.bottom <= anchor.top,
                fits: rect.left >= 7 && rect.right <= innerWidth - 7 && rect.top >= 7 && rect.bottom <= innerHeight - 7,
                footerFits: document.querySelector('main').scrollWidth <= innerWidth,
                background: getComputedStyle(popup).backgroundColor
            };
        };
    `);

    assert.ok(await evaluate("input.value === '' && input.placeholder === 'Nhập số trang' && !popup.querySelector('label')"));
    assert.equal(await evaluate('getComputedStyle(trigger).borderRadius'), '999px');

    for (const theme of ['light', 'dark']) {
        await evaluate('document.documentElement.dataset.theme = ' + JSON.stringify(theme));
        for (const width of [320, 390, 768, 1440]) {
            await send('Emulation.setDeviceMetricsOverride', { width, height: 700, deviceScaleFactor: 1, mobile: false });
            for (const bottom of [false, true]) {
                await evaluate('document.querySelector("main").classList.toggle("bottom", ' + bottom + ')');
                await click('.page-number');
                await delay(60);
                const open = await evaluate('snapshot()');
                assert.ok(open.open && open.focus && open.fits && open.footerFits, JSON.stringify({ theme, width, bottom, ...open }));
                assert.equal(open.above, bottom);
                assert.equal(open.background, theme === 'dark' ? 'rgb(23, 40, 62)' : 'rgb(255, 255, 255)');

                if (screenshotDirectory && bottom && [390, 1440].includes(width)) {
                    const { data } = await send('Page.captureScreenshot', { format: 'png' });
                    await writeFile(join(screenshotDirectory, theme + '-' + width + '.png'), Buffer.from(data, 'base64'));
                }
                await key('Escape', 27);
                const closed = await evaluate('snapshot()');
                assert.ok(!closed.open && closed.returned, 'Escape closes and restores focus');
            }
        }
    }

    await click('.page-number');
    await click('#outside');
    assert.ok(!await evaluate("popup.matches(':popover-open')"), 'Click outside closes the popover');
    await click('.page-number');
    await send('Input.insertText', { text: '18' });
    assert.equal(await evaluate('input.value'), '18');
    await key('Enter', 13);
    assert.deepEqual(await evaluate('submissions'), ['18']);
    const submitted = await evaluate('snapshot()');
    assert.ok(!submitted.open && submitted.returned, 'Enter closes and restores focus');
    await evaluate('handle.disconnect()');
    console.log('PASS: 4 viewport widths × 2 themes × 2 placements, autofocus, Escape, outside click, Enter. Chrome PID ' + processHandle.pid);
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
