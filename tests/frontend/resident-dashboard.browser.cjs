// Real-browser layout check using the dashboard's actual row markup and stylesheet.
// A fresh profile and localhost-only fixture keep user sessions and data untouched.
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const http = require('node:http');
const { spawn, execFile } = require('node:child_process');
const { once } = require('node:events');
const wait = ms => new Promise(resolve => setTimeout(resolve, ms));
const root = path.resolve(__dirname, '../..');
const announcementSource = fs.readFileSync(path.join(root, 'src/PropFlow.Web.Client/Features/Communication/Components/ResidentAnnouncementCard.razor'), 'utf8');
const emptyMarkup = announcementSource.match(/<div class="announcement-empty"[\s\S]*?<\/div>/)?.[0];
if (!emptyMarkup) throw new Error('Announcement empty state was not found.');
const announcementMarkup = (`<article class="announcement-card"><header><div><h2>Thông báo ban quản lý</h2><small>Bản tin chính thức dành cho cư dân</small></div><span class="total">0 thông báo</span></header><div class="notice-list">${emptyMarkup}</div><footer><a>Xem tất cả thông báo ›</a></footer></article>`)
  .replace(/<([a-z][a-z0-9]*)(?=[\s>])/gi, '<$1 data-ann');
// Mirror CSS isolation for this fixture so the shared card's styles stay scoped.
const announcementCss = fs.readFileSync(path.join(root, 'src/PropFlow.Web.Client/Features/Communication/Components/ResidentAnnouncementCard.razor.css'), 'utf8')
  .replace(/([^{}]+)\{/g, (_, selectors) => selectors.split(',').map(selector => selector.trim() + '[data-ann]').join(',') + '{');
const css = fs.readFileSync(path.join(root, 'src/PropFlow.Web.Client/wwwroot/css/resident-portal.css'), 'utf8') + '\n' + announcementCss;
const razor = fs.readFileSync(path.join(root, 'src/PropFlow.Web.Client/Features/Resident/Portal/Pages/ResidentDashboard.razor'), 'utf8');
const rowSource = razor.match(/<button class="glass-inner request-list-item"[\s\S]*?<\/button>/)?.[0];
if (!rowSource) throw new Error('Dashboard request row was not found.');
const titles = [
  'Tôi muốn đăng ký người viếng thăm, khách tên Nguyễn Thị Minh Anh đến thăm gia đình ở căn hộ',
  'Tôi muốn đăng ký xe máy màu đen biển số 81A1-13201',
  'Yêu cầu sửa chữa ' + 'thiếtbị'.repeat(35)
];
const rows = titles.map(title => rowSource.replace(/@onclick="[^"]*"/g, '')
  .replaceAll('@request.Title', title).replaceAll('@request.RequestNumber', 'YC-20261113-BEEDCDAEF4054F76B0')
  .replaceAll('@StatusBadgeClass(request.Status)', 'badge-amber')
  .replaceAll('@StatusLabel(request.Status)', 'Chờ xử lý')
  .replaceAll('@FormatSubmittedAt(request.SubmittedAt)', '13/11/2026 08:30')).join('');
const iconSource = fs.readFileSync(path.join(root, 'src/PropFlow.Web.Client/Features/Resident/Portal/Components/ResidentServiceIcon.razor'), 'utf8');
function serviceIcon(kind, accent) {
  const branch = kind === 'OTHER' ? /default:([\s\S]*?)break;/ : new RegExp('case "' + kind + '":([\\s\\S]*?)break;');
  const paths = iconSource.match(branch)?.[1];
  if (!paths) throw new Error('Missing service SVG for ' + kind);
  return '<span class="service-icon ' + accent + '">' + iconSource.match(/<svg\b[^>]*>/)[0].replace('@Kind', kind) + paths + '</svg></span>';
}
const html = `<!doctype html><html lang="vi"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><link rel="stylesheet" href="/resident.css"></head>
<body style="margin:0"><div class="resident-portal"><div class="resident-page-frame"><main class="resident-main"><div class="resident-card-grid">
<article class="resident-dashboard-card glass-panel" data-purpose="request-status-card"><div><header class="card-heading"><span><h2>Tiến độ xử lý yêu cầu</h2><small>Cập nhật từ dữ liệu của bạn</small></span><em class="badge badge-sky">Đã xong 6 / 17</em></header><div class="request-progress"><span style="width:35%"></span></div><div class="resident-card-list"><div class="resident-request-state" data-empty style="display:none">Bạn chưa gửi yêu cầu nào.</div>${rows}</div></div><footer class="card-actions"><button class="resident-text-action">Xem tất cả 17 yêu cầu ›</button><button class="resident-primary-action">+ Tạo yêu cầu mới</button></footer></article>
<article class="resident-dashboard-card glass-panel" data-purpose="services-catalog-card"><div><header class="card-heading"><span><h2>Dịch vụ cư dân</h2><small>Gửi yêu cầu, thanh toán và liên hệ</small></span><em class="badge badge-neutral">4 mục</em></header><div class="resident-service-grid">
<button class="service-btn emergency-service-button">${serviceIcon('EMERGENCY', 'rose')}<strong>Khẩn cấp</strong><small>Gọi thoại với BQL</small></button>
<button class="service-btn glass-inner">${serviceIcon('OTHER', 'sky')}<strong>Yêu cầu dịch vụ</strong></button>
<button class="service-btn glass-inner">${serviceIcon('BILLING', 'teal')}<strong>Hóa đơn</strong></button>
<button class="service-btn glass-inner">${serviceIcon('CONTACT', 'cyan')}<strong>Liên hệ BQL</strong></button>
</div></div><footer class="service-card-footer"><span><i></i>Kết nối với Manager đang sẵn sàng</span><a>Liên hệ BQL →</a></footer></article>
${announcementMarkup}
</div></main></div></div></body></html>`;
const browserPath = [process.env.RESIDENT_TEST_BROWSER, 'C:/Program Files/Google/Chrome/Application/chrome.exe', 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe', '/usr/bin/chromium', '/usr/bin/google-chrome'].find(p => p && fs.existsSync(p));
if (!browserPath) throw new Error('Set RESIDENT_TEST_BROWSER to a Chromium executable.');
const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'propflow-resident-layout-'));
const server = http.createServer((req, res) => {
  res.setHeader('Content-Type', req.url === '/resident.css' ? 'text/css' : 'text/html; charset=utf-8');
  res.end(req.url === '/resident.css' ? css : html);
});
let browser, socket, sequence = 0, port;
const pending = new Map();
function send(method, params = {}, sessionId) {
  return new Promise((resolve, reject) => {
    const id = ++sequence;
    const timer = setTimeout(() => { pending.delete(id); reject(new Error('Browser command timed out.')); }, 15000);
    pending.set(id, { resolve: x => { clearTimeout(timer); resolve(x); }, reject: e => { clearTimeout(timer); reject(e); } });
    socket.send(JSON.stringify({ id, method, params, ...(sessionId ? { sessionId } : {}) }));
  });
}
(async () => {
  try {
    server.listen(0, '127.0.0.1'); await once(server, 'listening'); port = server.address().port;
    browser = spawn(browserPath, ['--headless=new', '--remote-debugging-port=0', '--remote-debugging-address=127.0.0.1', '--user-data-dir=' + profile, '--no-first-run', '--no-default-browser-check', '--disable-background-networking', '--disable-sync', 'about:blank'], { windowsHide: true, stdio: 'ignore' });
    console.log(JSON.stringify({ browserPid: browser.pid, serverPort: port }));
    const activePort = path.join(profile, 'DevToolsActivePort');
    const deadline = Date.now() + 15000;
    while (!fs.existsSync(activePort) && Date.now() < deadline) await wait(50);
    const [debugPort, endpoint] = fs.readFileSync(activePort, 'utf8').trim().split(/\r?\n/);
    socket = new WebSocket('ws://127.0.0.1:' + debugPort + endpoint); await once(socket, 'open');
    socket.addEventListener('message', event => {
      const message = JSON.parse(event.data), callback = pending.get(message.id);
      if (callback) { pending.delete(message.id); message.error ? callback.reject(new Error(message.error.message)) : callback.resolve(message.result); }
    });
    const target = await send('Target.createTarget', { url: 'http://127.0.0.1:' + port });
    const session = (await send('Target.attachToTarget', { targetId: target.targetId, flatten: true })).sessionId;
    for (let attempt = 0; attempt < 100; attempt++) {
      const ready = await send('Runtime.evaluate', { expression: 'document.querySelectorAll(".request-list-item").length === 3 && document.styleSheets.length > 0', returnByValue: true }, session);
      if (ready.result?.value) break;
      await wait(50);
    }
    const checks = [];
    for (const width of [1670, 1280, 820, 390, 320]) {
      await send('Emulation.setDeviceMetricsOverride', { width, height: 900, deviceScaleFactor: 1, mobile: false }, session);
      for (const { dark, empty } of [{dark:false, empty:true}, {dark:true, empty:true}, {dark:false, empty:false}, {dark:true, empty:false}]) {
        const result = await send('Runtime.evaluate', { expression: `document.documentElement.classList.toggle('dark', ${dark}); document.querySelectorAll('.request-list-item').forEach(row => row.style.display = ${empty} ? 'none' : ''); document.querySelector('[data-empty]').style.display = ${empty} ? 'grid' : 'none'; new Promise(resolve => requestAnimationFrame(() => {
          const card = document.querySelector('[data-purpose="request-status-card"]'), bounds = card.getBoundingClientRect();
          const allRows = [...card.querySelectorAll('.request-list-item')], rows = allRows.filter(row => row.getClientRects().length > 0);
          const overflow = rows.some(row => row.scrollWidth > row.clientWidth + 1 || row.getBoundingClientRect().right > bounds.right + 1 || [...row.children].some(child => child.getBoundingClientRect().right > bounds.right + 1));
          const statusVisible = rows.every(row => { const rect = row.querySelector('.badge').getBoundingClientRect(); return rect.left >= bounds.left && rect.right <= bounds.right; });
          const fontSize = parseFloat(getComputedStyle(allRows[0].querySelector('strong')).fontSize);
          const fontFamily = getComputedStyle(allRows[0]).fontFamily;
          const services = document.querySelector('[data-purpose="services-catalog-card"]');
          const rgb = color => color.match(/[0-9.]+/g).slice(0,3).map(Number);
          const luminance = color => rgb(color).map(v => v / 255).map(v => v <= .04045 ? v / 12.92 : ((v + .055) / 1.055) ** 2.4).reduce((sum,v,i) => sum + v * [.2126,.7152,.0722][i], 0);
          const contrast = (a,b) => (Math.max(luminance(a),luminance(b)) + .05) / (Math.min(luminance(a),luminance(b)) + .05);
          const iconChecks = [...services.querySelectorAll('.service-icon')].map(icon => {
            const svg = icon.querySelector('svg'), style = getComputedStyle(icon);
            return { kind: svg?.dataset.serviceIcon, contrast: contrast(style.color,style.backgroundColor), visible: !!svg && svg.getBoundingClientRect().width >= 24 && svg.getBoundingClientRect().height >= 24 };
          });
          const iconsVisible = iconChecks.length === 4 && iconChecks.every(icon => icon.visible && icon.contrast >= 3);
          const emergency = services.querySelector('.emergency-service-button');
          const emergencyReadable = contrast(getComputedStyle(emergency.querySelector('strong')).color,getComputedStyle(emergency).backgroundColor) >= 4.5 && contrast(getComputedStyle(emergency.querySelector('small')).color,getComputedStyle(emergency).backgroundColor) >= 4.5;
          const tiles = [...services.querySelectorAll('.service-btn')].map(tile => tile.getBoundingClientRect());
          const balancedServices = innerWidth > 760 && innerWidth <= 1024 ? tiles.every(tile => Math.abs(tile.top - tiles[0].top) < 1) : Math.abs(tiles[0].top - tiles[1].top) < 1 && Math.abs(tiles[2].top - tiles[3].top) < 1 && tiles[2].top > tiles[0].top;
          const filledServices = services.querySelector('footer').getBoundingClientRect().top - services.querySelector('.resident-service-grid').getBoundingClientRect().bottom <= 25;
          const notice = document.querySelector('.announcement-card'), serviceBounds = services.getBoundingClientRect(), noticeBounds = notice.getBoundingClientRect();
          const noticeFits = notice.querySelector('footer').getBoundingClientRect().bottom <= noticeBounds.bottom + 1;
          const balancedLayout = innerWidth > 1024 ? Math.abs(bounds.top - serviceBounds.top) < 1 && Math.abs(bounds.top - noticeBounds.top) < 1 && Math.abs(bounds.bottom - serviceBounds.bottom) < 1 && Math.abs(bounds.bottom - noticeBounds.bottom) < 1 && bounds.right < serviceBounds.left && serviceBounds.right < noticeBounds.left : bounds.bottom < serviceBounds.top && serviceBounds.bottom < noticeBounds.top;
          const list = card.querySelector('.resident-card-list');
          const compactProgress = innerWidth <= 1024 || ${empty} || list.scrollHeight > list.clientHeight && list.clientHeight <= 320;
          resolve({ width: innerWidth, dark: ${dark}, emptyRequests: ${empty}, overflow, statusVisible, fontSize, balancedServices, filledServices, balancedLayout, compactProgress, noticeFits, iconChecks, emergencyReadable, passed: iconsVisible && emergencyReadable && !overflow && statusVisible && balancedServices && filledServices && balancedLayout && compactProgress && noticeFits && fontSize >= 14 && fontFamily.startsWith('Inter') && document.documentElement.scrollWidth <= innerWidth + 1 });
        }))`, awaitPromise: true, returnByValue: true }, session);
        checks.push(result.result.value);
      }
      if (process.env.RESIDENT_LAYOUT_PREVIEW_DIR && [1670, 390].includes(width)) {
        await send('Runtime.evaluate', { expression: "document.documentElement.classList.remove('dark')" }, session);
        const screenshot = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: true }, session);
        fs.mkdirSync(process.env.RESIDENT_LAYOUT_PREVIEW_DIR, { recursive: true });
        fs.writeFileSync(path.join(process.env.RESIDENT_LAYOUT_PREVIEW_DIR, `resident-dashboard-${width}.png`), Buffer.from(screenshot.data, 'base64'));
      }
    }
    console.log(JSON.stringify({ passed: checks.every(x => x.passed), checks }));
    if (checks.some(x => !x.passed)) process.exitCode = 1;
  } catch (e) { console.error(e.message); process.exitCode = 1; }
  finally {
    if (socket?.readyState === WebSocket.OPEN) { try { await send('Browser.close'); } catch {} socket.close(); }
    if (browser && browser.exitCode === null) {
      await Promise.race([once(browser, 'exit'), wait(5000)]);
      if (browser.exitCode === null) {
        if (process.platform === 'win32') await new Promise(resolve => execFile('taskkill', ['/PID', String(browser.pid), '/T', '/F'], { windowsHide: true }, resolve));
        else browser.kill('SIGKILL');
      }
    }
    server.closeAllConnections(); await new Promise(resolve => server.close(resolve));
    for (const entry of pending.values()) entry.reject(new Error('Browser closed')); pending.clear();
    const resolved = path.resolve(profile);
    if (resolved.startsWith(path.resolve(os.tmpdir()) + path.sep) && path.basename(resolved).startsWith('propflow-resident-layout-')) fs.rmSync(resolved, { recursive: true, force: true, maxRetries: 5, retryDelay: 100 });
    console.log(JSON.stringify({ cleanupComplete: true, serverPort: port }));
  }
})();
