import { spawn } from 'node:child_process';
import { sign } from 'node:crypto';
import { once } from 'node:events';
import { mkdtemp, readFile, writeFile, rm } from 'node:fs/promises';
import { resolve, join } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';

const root = resolve('artifacts/authz');
const readJson = async path => JSON.parse(await readFile(path, 'utf8'));
const routes = await readJson(join(root, 'routes.json'));
const fixture = await readJson(join(root, 'browser-resources.json'));
const profile = await mkdtemp(join(root, 'chrome-'));
const sameOrigin = process.env.PROPFLOW_AUTHZ_SAME_ORIGIN === '1';
const site = sameOrigin ? 'https://127.0.0.1:47301' : 'https://localhost:47201';
const api = sameOrigin ? site : 'https://localhost:47101';
const configurationFile = process.env.PROPFLOW_AUTHZ_RELEASE === '1' ? 'appsettings.json' : 'appsettings.Development.json';
const webConfig = { ...await readJson('src/PropFlow.Web.Client/wwwroot/' + configurationFile), Api: { BaseUrl: api + '/' } };
const browser = spawn(process.env.PROPFLOW_AUTHZ_CHROME ?? 'C:/Program Files/Google/Chrome/Application/chrome.exe',
  ['--headless', '--disable-gpu', '--no-first-run', '--disable-background-networking',
    '--ignore-certificate-errors', '--remote-debugging-port=0', '--user-data-dir=' + profile, 'about:blank'],
  { stdio: 'ignore', windowsHide: true });
const exited = once(browser, 'exit');
console.log('Chrome PID', browser.pid);
const supplemental = process.env.PROPFLOW_AUTHZ_SUPPLEMENTAL === '1';
const scope = process.env.PROPFLOW_AUTHZ_SCOPE ?? 'Full';
const sessionCheckOnly = scope === 'Session';
const maintenanceCheckOnly = scope === 'Maintenance';
const themeCheckOnly = scope === 'Theme';
const routeEvidence = supplemental ? await readJson(join(root, 'browser-matrix.json')) : [];
const apiEvidence = supplemental ? await readJson(join(root, 'api-matrix.json')) : [];
const credentialEvidence = [];
const horizontalEvidence = [];
const uiEvidence = [];
const themeFailures = [];
const endpointList = await readJson(join(root, 'endpoints.json'));
const conventionalDenied = 'Bạn không có quyền truy cập trang này.';
const dynamic = {
  '/apartments/{Id:guid}': fixture.apartmentId,
  '/residents/{Id:guid}': fixture.residentId,
  '/maintenance/tasks/{TaskId:guid}': fixture.taskId,
  '/staff/complaints/{Id:guid}': fixture.complaintId,
  '/manager/complaints/{Id:guid}': fixture.complaintId,
  '/resident/complaints/{Id:guid}': fixture.complaintId,
  '/finance/invoices/{InvoiceId:guid}': fixture.invoiceId,
  '/manager/service-requests/{Id:guid}': fixture.requestId,
  '/manager/service-requests/{Id:guid}/property-context': fixture.requestId,
  '/resident/service-requests/{Id:guid}': fixture.requestId,
  '/staff/service-requests/{Code}': 'PHASE11-EVIDENCE'
};
const fallbackId = '42000000-0000-4000-8000-000000000001';
const allRoles = ['Ẩn danh', 'STAFF', 'ADMIN', 'MANAGER', 'ACCOUNTANT', 'RESIDENT'];
const homes = { ADMIN:'/admin/dashboard', STAFF:'/staff/service-requests', MANAGER:'/manager/service-requests',
  ACCOUNTANT:'/finance/dashboard', RESIDENT:'/resident' };
const redirectHome = { ...homes, MANAGER: '/buildings' };
const roleThemeSamples = {
  ADMIN: ['.overview-metrics .metric-copy strong', '.overview-charts .chart-card h2', '.overview-charts .chart-card header p'],
  MANAGER: ['.building-details h1', '.building-details .detail-panel h2', '.building-details .section-header p'],
  STAFF: ['.staff-request-hero h1', '.staff-workspace h2', '.staff-metrics article strong'],
  ACCOUNTANT: ['.finance-dashboard h1', '.finance-metrics .metric-value', '.finance-metrics .metric-note', '.debt-breakdown strong'],
  RESIDENT: ['.resident-greeting strong', '.resident-date', '.resident-dashboard-card h2', '.resident-dashboard-card .card-heading small', '.resident-text-action']
};
const roleThemeRoutes = {
  ADMIN: ['/admin/dashboard?tab=activity', '.admin-dashboard h1'],
  MANAGER: ['/manager/service-requests', '.manager-request-list h1'],
  STAFF: ['/my-maintenance', '.smt-title'],
  ACCOUNTANT: ['/finance/invoices', '.service-fee-page h1'],
  RESIDENT: ['/resident/apartment', '.resident-apartment-page h1']
};
let socket;
let apiStatuses = [];
let errors = [];
let loginResponseId;

try {
  let port;
  for (let i = 0; i < 100 && !port; i++) {
    try { port = (await readFile(join(profile, 'DevToolsActivePort'), 'utf8')).split('\n')[0].trim(); }
    catch (e) { if (e.code !== 'ENOENT') throw e; }
    if (!port) await delay(100);
  }
  const [tab] = await (await fetch('http://127.0.0.1:' + port + '/json/list')).json();
  socket = new WebSocket(tab.webSocketDebuggerUrl);
  await once(socket, 'open');
  let sequence = 0;
  const call = (method, params = {}, sessionId) => new Promise((resolveCall, reject) => {
    const id = ++sequence;
    const callback = event => {
      const message = JSON.parse(event.data);
      if (message.id !== id) return;
      clearTimeout(timer);
      socket.removeEventListener('message', callback);
      if (message.error) reject(Error(method));
      else resolveCall(message.result);
    };
    const timer = setTimeout(() => { socket.removeEventListener('message', callback); reject(Error(method)); }, 15000);
    socket.addEventListener('message', callback);
    socket.send(JSON.stringify({ id, method, params, sessionId }));
  });
  socket.addEventListener('message', event => {
    const message = JSON.parse(event.data);
    if (message.method === 'Fetch.requestPaused') {
      void call('Fetch.fulfillRequest', {
        requestId: message.params.requestId,
        responseCode: 200,
        responseHeaders: [{ name: 'Content-Type', value: 'application/json' }],
        body: Buffer.from(JSON.stringify(webConfig)).toString('base64')
      }, message.sessionId).catch(() => errors.push('WASM configuration override failed'));
    }
    if (message.method === 'Runtime.exceptionThrown') errors.push('Runtime.exceptionThrown');
    if (message.method !== 'Network.responseReceived' || message.sessionId) return;
    const { response, requestId } = message.params;
    const url = new URL(response.url);
    if (url.origin !== api) return;
    apiStatuses.push([url.pathname, response.status, message.params.type, message.params.loaderId]);
    if (url.pathname === '/api/v1/auth/login' && response.status === 200) loginResponseId = requestId;
  });
  await call('Network.enable');
  const configureApi = id => sameOrigin ? Promise.resolve() :
    call('Fetch.enable', { patterns: [{ urlPattern: site + '/' + configurationFile + '*' }] }, id);
  await configureApi();
  await call('Runtime.enable');
  await call('Page.enable');
  const evaluate = async (expression, awaitPromise = false) => {
    const result = await call('Runtime.evaluate', { expression, returnByValue: true, awaitPromise });
    if (result.exceptionDetails) throw Error('Chrome evaluation failed');
    return result.result.value;
  };
  async function waitFor(condition, max = 52) {
    for (let i = 0; i < max; i++) {
      try { if (await evaluate(condition)) return true; } catch {}
      await delay(175);
    }
    return false;
  }
  const navigate = path => call('Page.navigate', { url: site + path });
  const setShellTheme = async dark => {
    const expression = 'document.documentElement.dataset.theme === ' + JSON.stringify(dark ? 'dark' : 'light');
    if (!await evaluate(expression)) await evaluate('document.querySelector(".app-theme-button").click()');
    if (!await waitFor(expression)) throw Error('Không đổi được giao diện của AppShell');
  };
  const pageState = '({path: location.pathname, requested: location.href, title: document.title, heading: document.querySelector("h1")?.textContent?.trim(), denied: document.body?.innerText.includes(' + JSON.stringify(conventionalDenied) + '), login: location.pathname==="/login" && Boolean(document.querySelector("#username")), waiting: document.body?.innerText.includes("Đang kiểm tra quyền truy cập"), content: Boolean(document.querySelector("main, .auth-container, .landing-container, h1, .public-layout")), notFound: document.body?.innerText.includes("Trang không tồn tại")})';
  const snapshot = async name => {
    const { data } = await call('Page.captureScreenshot', { format: 'png' });
    await writeFile(join(root, name + '.png'), Buffer.from(data, 'base64'));
  };
  const submitLogin = async account => {
    loginResponseId = undefined;
    if (!await waitFor('Boolean(document.querySelector("form button[type=submit]:enabled"))'))
      throw Error('Login form still disabled for ' + account);
    const credentials = [['username', 'phase11_' + account.toLowerCase()], ['password', process.env.PHASE11_PASSWORD]];
    for (const [id, value] of credentials) {
      await evaluate('(()=>{const field=document.getElementById(' + JSON.stringify(id) + ');field.focus();field.select()})()');
      await call('Input.insertText', { text: value });
    }
    await evaluate('document.getElementById("password").blur()');
    await evaluate('document.getElementById("password").form.requestSubmit()');
    if (!await waitFor('location.pathname!=="/login"') || !loginResponseId) {
      const statuses = apiStatuses.filter(([path]) => /^\/api\/v1\/auth\/(csrf|login|logout|refresh)$/.test(path)).map(([path, status]) => [path, status]).slice(-9);
      throw Error('Login failed for ' + account + ', HTTP ' + JSON.stringify(statuses));
    }
  };
  function themeAppearance(selectors) {
    const color = value => {
      if (value.startsWith('rgb')) return value.match(/[\d.]+/g).map(Number);
      if (value.startsWith('color(srgb ')) return value.match(/[\d.]+/g).map((value, index) => Number(value) * (index < 3 ? 255 : 1));
      return null;
    };
    const luminance = rgb => rgb.slice(0, 3).map(value => {
      const channel = value / 255;
      return channel <= .04045 ? channel / 12.92 : ((channel + .055) / 1.055) ** 2.4;
    }).reduce((total, value, index) => total + value * [.2126, .7152, .0722][index], 0);
    const background = node => {
      const layers = [];
      for (let element = node; element; element = element.parentElement) {
        const shade = color(getComputedStyle(element).backgroundColor);
        if (!shade || shade[3] === 0) continue;
        layers.push(shade);
        if ((shade[3] ?? 1) === 1) break;
      }
      return layers.reverse().reduce((base, shade) => {
        const alpha = shade[3] ?? 1;
        return base.map((value, index) => shade[index] * alpha + value * (1 - alpha));
      }, [255, 255, 255]);
    };
    return selectors.flatMap(selector => [...document.querySelectorAll(selector)].map(node => {
      const ink = getComputedStyle(node).color;
      const paper = background(node);
      const shades = [luminance(color(ink)), luminance(paper)];
      return { selector, ink, paper: paper.map(Math.round),
        ratio: Number(((Math.max(...shades) + .05) / (Math.min(...shades) + .05)).toFixed(2)) };
    }));
  }
  async function verifyTheme(role, mode, selectors, capture) {
    const expected = JSON.stringify(mode);
    if (!await waitFor('document.documentElement.dataset.theme === ' + expected +
        ' && document.documentElement.classList.contains(' + expected + ') && !document.documentElement.classList.contains(' +
        JSON.stringify(mode === 'dark' ? 'light' : 'dark') + ')')) throw Error(role + ' did not keep ' + mode);
    const shell = role === 'RESIDENT' ? '.resident-header' : '.app-shell';
    if (!await waitFor('Boolean(document.querySelector(' + JSON.stringify(shell) + '))')) throw Error('Layout missing for ' + role);
    for (const selector of selectors) {
      if (!await waitFor('Boolean(document.querySelector(' + JSON.stringify(selector) + '))')) throw Error(role + ' UI missing ' + selector);
    }
    const appearance = '(' + themeAppearance + ')(' + JSON.stringify([shell, ...selectors]) + ')';
    const invalid = sample => sample.ratio < 4.5 ||
      (mode === 'dark' ? Math.max(...sample.paper) >= 96 : Math.min(...sample.paper) < 145);
    let samples = await evaluate(appearance);
    if (samples.some(invalid)) {
      const deadline = Date.now() + 900;
      do {
        await delay(50);
        samples = await evaluate(appearance);
      } while (samples.some(invalid) && Date.now() < deadline);
    }
    if (capture) {
      await evaluate('Promise.race([document.fonts.ready.then(() => true), new Promise(resolve => setTimeout(resolve, 1200, false))])', true);
      await snapshot('theme-' + role.toLowerCase() + '-' + capture);
    }
    if (capture === 'dark-mobile' && !await evaluate(`(() => {
      if (document.documentElement.scrollWidth > innerWidth) return false;
      const time = document.querySelector(".app-local-time");
      const title = document.querySelector(".app-page-heading > div:not(.app-local-time)");
      return !time || !title || title.getBoundingClientRect().bottom <= time.getBoundingClientRect().top;
    })()`)) throw Error(role + ' mobile page overflows or the header overlaps');
    if (role !== 'RESIDENT' && !await evaluate('getComputedStyle(document.querySelector(".app-theme-sun")).display === ' +
        JSON.stringify(mode === 'dark' ? 'block' : 'none') + ' && (getComputedStyle(document.querySelector(".app-theme-moon")).display === "none") === ' +
        (mode === 'dark'))) throw Error(role + ' theme button shows the wrong icon');
    const mismatch = samples.find(invalid);
    uiEvidence.push({ role, mode, capture, samples, status: mismatch ? 'CHECK' : 'PASS' });
    await writeFile(join(root, 'theme-browser.json'), JSON.stringify(uiEvidence, null, 2));
    if (mismatch) throw Error(role + ' ' + mode + ' unreadable or inconsistent: ' + JSON.stringify(mismatch));
  }
  const persist = async () => {
    await writeFile(join(root, 'browser-matrix.json'), JSON.stringify(routeEvidence, null, 2));
    const currentPaths = new Set(endpointList.map(row => row.method + ' ' + row.path));
    await writeFile(join(root, 'api-matrix.json'), JSON.stringify(apiEvidence.filter(row => currentPaths.has(row.method + ' ' + row.path)), null, 2));
    await writeFile(join(root, 'jwt-matrix.json'), JSON.stringify(credentialEvidence, null, 2));
    await writeFile(join(root, 'horizontal-matrix.json'), JSON.stringify(horizontalEvidence, null, 2));
    await writeFile(join(root, 'browser-ui-actions.json'), JSON.stringify(uiEvidence, null, 2));
  };
  const apiPath = path => path.replace(/\{([^}]+)\}/g, (_, name) =>
    /evidenceId/i.test(name) ? fixture.evidenceId :
    /:int$/i.test(name) ? '1' : /^(action|operation)$/i.test(name) ? 'CONNECT' :
    /invoice/i.test(name) ? fixture.invoiceId : /request|^id(?::guid)?$/i.test(name) ? fixture.requestId :
    /code/i.test(name) ? 'PHASE11-EVIDENCE' : fallbackId);

  async function probeDenied(role, token) {
    const probes = endpointList.filter(endpoint => !endpoint.allow.includes(role) &&
      !apiEvidence.some(row => row.role === role && row.method === endpoint.method && row.path === endpoint.path && row.verdict === 'PASS'));
    for (let cursor = 0; cursor < probes.length; cursor += 8) {
      const batch = probes.slice(cursor, cursor + 8);
      const requests = batch.map(endpoint => [endpoint.method, apiPath(endpoint.path),
        endpoint.method === 'POST' && (/\/evidence$|\/avatar$|\/my-tasks\/[^/]+\/result$/.test(endpoint.path))]);
      // Only policy-denied calls are probed. An invalid body also prevents unintended test mutations if a guard is missing.
      const results = await evaluate('(async () => Promise.all(' + JSON.stringify(requests) + '.map(async ([method,path,multipart]) => { try { const headers = ' +
        (token ? '({Authorization:"Bearer " + ' + JSON.stringify(token) + '})' : '({})') +
        '; let body; if(multipart) {body=new FormData();body.append("file",new Blob([new Uint8Array([137,80,78,71,13,10,26,10])],{type:"image/png"}),"fixture.png");} else if(!["GET","HEAD"].includes(method)) {headers["Content-Type"]="application/json";body="{}";}'
        + ' return (await fetch(' + JSON.stringify(api) + '+path,{method,headers,redirect:"error",body})).status; } catch { return 0; } })))()', true);
      for (let i = 0; i < batch.length; i++) {
        const { path, method } = batch[i];
        const expected = role === 'Ẩn danh' ? 401 : 403;
        const record = { role, path, method, expected, actual: results[i], verdict: results[i] === expected ? 'PASS' : 'CHECK' };
        const prior = apiEvidence.findIndex(row => row.role === role && row.path === path && row.method === method);
        if (prior < 0) apiEvidence.push(record); else apiEvidence[prior] = record;
      }
    }
  }

  async function checkTokens(role, token) {
    const [header, body, signature] = token.split('.');
    const claims = JSON.parse(Buffer.from(body, 'base64url').toString('utf8'));
    const encode = data => Buffer.from(JSON.stringify(data)).toString('base64url');
    const issue = overrides => {
      const payload = header + '.' + encode({ ...claims, ...overrides });
      return payload + '.' + sign('RSA-SHA256', Buffer.from(payload), process.env.Authentication__PrivateKeyPem).toString('base64url');
    };
    const variants = [
      ['valid', token, 200], ['malformed', 'malformed.jwt', 401],
      ['tamperedRole', header + '.' + encode({ ...claims, role: role === 'ADMIN' ? 'MANAGER' : 'ADMIN' }) + '.' + signature, 401],
      ['wrongSignature', header + '.' + body + '.' + Buffer.alloc(256).toString('base64url'), 401],
      ['wrongIssuer', issue({ iss: 'wrong-test-issuer' }), 401],
      ['wrongAudience', issue({ aud: 'wrong-test-audience' }), 401],
      ['expired', issue({ nbf: Math.floor(Date.now() / 1000) - 120, exp: Math.floor(Date.now() / 1000) - 60 }), 401]
    ];
    const results = await evaluate('Promise.all(' + JSON.stringify(variants.map(x => x[1])) + '.map(t=>fetch(' +
      JSON.stringify(api + '/api/v1/auth/me') + ',{headers:{Authorization:"Bearer "+t}}).then(r=>r.status)))', true);
    credentialEvidence.push(...variants.map(([variant,,expected], i) => ({ role, variant, expected, actual: results[i], verdict: expected === results[i] ? 'PASS' : 'CHECK' })));
  }

  async function checkHorizontal(account, token, pairs) {
    const own = account.endsWith('2') ? 1 : 0;
    const checks = pairs.flatMap(([feature, names, makePath]) => names.map((value,i) =>
      ({ feature, path: makePath(value), expected: i === own ? [200] : [403,404] })));
    const query = '(async()=>{const headers={Authorization:"Bearer "+ ' + JSON.stringify(token) + '};return Promise.all(' +
      JSON.stringify(checks.map(x => x.path)) + '.map(path=>fetch(' + JSON.stringify(api) + '+path,{headers}).then(r=>r.status)));})()';
    const results = await evaluate(query, true);
    horizontalEvidence.push(...checks.map((check,i) => ({ account, ...check, actual: results[i],
      verdict: check.expected.includes(results[i]) ? 'PASS' : 'CHECK' })));
  }
  const staffPairs = [
    ['FE-05', ['PHASE11-EVIDENCE','PHASE11-REQ-B'], code => '/api/v1/staff/service-requests/' + code],
    ['FE-06', [fixture.complaintId,fixture.otherComplaintId], id => '/api/v1/complaints/' + id],
    ['FE-07', [fixture.taskId,fixture.otherTaskId], id => '/api/v1/maintenance/my-tasks/' + id],
  ];
  const residentPairs = [
    ['FE-05', [fixture.requestId,fixture.otherRequestId], id => '/api/v1/resident/service-requests/' + id],
    ['FE-06', [fixture.complaintId,fixture.otherComplaintId], id => '/api/v1/complaints/' + id],
    ['FE-08', [fixture.invoiceId,fixture.otherInvoiceId], id => '/api/v1/resident/billing/invoices/' + id + '/print'],
    ['FE-13', [fixture.conversationId,fixture.otherConversationId], id => '/api/v1/support-chat/conversations/' + id + '/messages'],
  ];
  const selectedRoles = sessionCheckOnly || maintenanceCheckOnly
    ? ['STAFF'] : process.env.PROPFLOW_AUTHZ_BROWSER_ROLES?.split(',') ??
      (themeCheckOnly ? Object.keys(homes) : [...allRoles, 'STAFF2', 'RESIDENT2']);
  if (themeCheckOnly) {
    await call('Emulation.setEmulatedMedia', { features: [
      { name: 'prefers-color-scheme', value: 'light' }, { name: 'prefers-reduced-motion', value: 'reduce' }
    ] });
    await call('Emulation.setDeviceMetricsOverride', { width: 1440, height: 900, deviceScaleFactor: 1, mobile: false });
  }
  for (const account of selectedRoles) {
    const role = account.replace(/2$/, '');
    await call('Network.clearBrowserCookies');
    await call('Storage.clearDataForOrigin', { origin: site, storageTypes: 'local_storage,session_storage' });
    await navigate('/login');
    if (!await waitFor('Boolean(document.querySelector("#username"))', themeCheckOnly ? 210 : 52))
      throw Error('Login fixture unavailable for ' + role + ', page ' + await evaluate('location.pathname'));
    if (themeCheckOnly && await evaluate('document.documentElement.dataset.theme !== "dark"')) {
      await evaluate('document.querySelector(".public-theme-toggle").click()');
      if (!await waitFor('document.documentElement.dataset.theme === "dark"')) throw Error('Login did not switch to dark');
    }
    let token;
    if (role !== 'Ẩn danh') {
      await submitLogin(account);
      if (themeCheckOnly) {
        try {
          const toggle = role === 'RESIDENT' ? '#theme-toggle-btn' : '.app-theme-button';
          const logout = role === 'RESIDENT' ? '.profile-logout' : '.app-profile-options button';
          const [secondary, selector] = roleThemeRoutes[role];
          const samples = roleThemeSamples[role];
          await verifyTheme(role, 'dark', samples, 'dark');
          await call('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 1, mobile: false });
          await verifyTheme(role, 'dark', samples, 'dark-mobile');
          await call('Emulation.setDeviceMetricsOverride', { width: 1440, height: 900, deviceScaleFactor: 1, mobile: false });
          await navigate(secondary);
          if (!await waitFor('location.pathname === ' + JSON.stringify(secondary.split('?')[0]))) throw Error('Secondary route did not load for ' + role);
          await verifyTheme(role, 'dark', [selector]);
          await evaluate('document.querySelector(' + JSON.stringify(toggle) + ').click()');
          await verifyTheme(role, 'light', []);
          const previousDocument = await evaluate('performance.timeOrigin');
          await call('Page.reload');
          if (!await waitFor('performance.timeOrigin !== ' + previousDocument)) throw Error('Reload did not complete for ' + role);
          await verifyTheme(role, 'light', [selector]);
          const logoutStart = apiStatuses.length;
          await evaluate('document.querySelector(' + JSON.stringify(logout) + ').click()');
          if (!await waitFor('location.pathname === "/login" && Boolean(document.querySelector("#username")) && document.documentElement.dataset.theme === "light"'))
            throw Error(role + ' did not keep light after logout');
          const logoutFinished = () => apiStatuses.slice(logoutStart)
            .some(([path, status, type]) => path === '/api/v1/auth/logout' && status === 204 && type !== 'Preflight');
          for (let attempt = 0; attempt < 60 && !logoutFinished(); attempt++) await delay(175);
          if (!logoutFinished()) throw Error(role + ' did not complete logout on the server');
          await navigate('/login?theme=light-check');
          if (!await waitFor('location.search === "?theme=light-check" && Boolean(document.querySelector("#username")) && document.documentElement.dataset.theme === "light"'))
            throw Error(role + ' did not keep light on a new login document');
          await submitLogin(account);
          await verifyTheme(role, 'light', samples, 'light');
          if (role === 'ADMIN') {
            const { targetId } = await call('Target.createTarget', { url: 'about:blank' });
            try {
              const { sessionId } = await call('Target.attachToTarget', { targetId, flatten: true });
              await configureApi(sessionId);
              await call('Page.navigate', { url: site + '/terms' }, sessionId);
              let ready = false;
              for (let attempt = 0; attempt < 60 && !ready; attempt++) {
                const result = await call('Runtime.evaluate', {
                  expression: 'document.documentElement.dataset.theme === "light" && Boolean(document.querySelector(".public-theme-toggle"))',
                  returnByValue: true
                }, sessionId);
                ready = result.result?.value;
                if (!ready) await delay(175);
              }
              if (!ready) throw Error('Public page in second tab did not keep light mode');
              await call('Runtime.evaluate', { expression: 'document.querySelector(".public-theme-toggle").click()' }, sessionId);
              await verifyTheme(role, 'dark', samples, 'cross-tab');
            } finally {
              await call('Target.closeTarget', { targetId });
            }
          }
          console.log('Theme and contrast verified for', role);
        } catch (error) {
          themeFailures.push(role + ': ' + error.message);
          console.error(themeFailures.at(-1));
          await call('Emulation.setDeviceMetricsOverride', { width: 1440, height: 900, deviceScaleFactor: 1, mobile: false });
        }
        continue;
      }
      const response = await call('Network.getResponseBody', { requestId: loginResponseId });
      token = JSON.parse(response.base64Encoded ? Buffer.from(response.body, 'base64').toString() : response.body).accessToken;
      if (!token) throw Error('No fixture token after login for ' + role);
      if (maintenanceCheckOnly) {
        await navigate('/my-maintenance');
        const button = label => 'Array.from(document.querySelectorAll("button")).find(item => item.textContent.trim() === ' +
          JSON.stringify(label) + ')';
        const loaded = await waitFor('Boolean(' + button('Lịch sử bảo trì') + ') && document.body?.innerText.includes("Công việc kiểm thử A")');
        if (!loaded) throw Error('Không mở được trang nhiệm vụ STAFF');
        const tabs = await evaluate('Array.from(document.querySelectorAll(".smt-tab")).map(tab => tab.textContent.trim())');
        const activeTab = await evaluate('document.querySelector(".smt-tab.is-active")?.textContent.trim()');
        if (tabs[0] !== 'Tất cả nhiệm vụ' || tabs[1] !== 'Nhiệm vụ của tôi' || activeTab !== 'Nhiệm vụ của tôi')
          throw Error('Thứ tự hoặc tab mặc định FE-07 đã đổi');
        if (await evaluate('document.body.innerText.includes("Kiểm tra lịch sử hai đợt")'))
          throw Error('Nhiệm vụ đã gỡ vẫn xuất hiện trong danh sách hiện hành');
        await evaluate(button('Lịch sử bảo trì') + '.click()');
        const historyRow = 'Array.from(document.querySelectorAll("tr")).find(item => item.innerText.includes("Kiểm tra lịch sử hai đợt"))';
        if (!await waitFor('Boolean(' + historyRow + ')')) throw Error('Không thấy nhiệm vụ trong lịch sử');
        await evaluate(historyRow + '.querySelector(".smt-detail-btn").click()');
        const dialog = 'document.querySelector(".smt-history-detail")';
        if (!await waitFor('Boolean(' + dialog + ')')) throw Error('Không mở được chi tiết lịch sử');
        const content = await evaluate(dialog + '.innerText');
        if (!content.includes('Kết quả được phép đợt một') || !content.includes('Kết quả được phép đợt hai') ||
            content.includes('Nội dung khoảng trống') || content.includes('Nội dung sau lần gỡ cuối') ||
            !content.toLocaleLowerCase('vi').includes('lịch sử chỉ đọc')) throw Error('Dữ liệu dialog nằm ngoài phạm vi lịch sử');
        const responsive = [];
        for (const width of [390, 768, 1440]) {
          await call('Emulation.setDeviceMetricsOverride', { width, height: 900, deviceScaleFactor: 1, mobile: false });
          for (const dark of [false, true]) {
            await setShellTheme(dark);
            const fitsViewport = await evaluate(dialog + '.getBoundingClientRect().right <= innerWidth + 1 && ' +
              dialog + '.scrollWidth <= ' + dialog + '.clientWidth + 1');
            const background = await evaluate('getComputedStyle(' + dialog + ').backgroundColor');
            responsive.push({ width, dark, fitsViewport, background });
            await snapshot('hardening-history-' + width + (dark ? '-dark' : '-light'));
          }
        }
        await writeFile(join(root, 'hardening-history-browser.json'), JSON.stringify({ scopeVerified: true, responsive }, null, 2));
        if (responsive.some(row => !row.fitsViewport)) throw Error('Dialog lịch sử vượt khung trang');
        if (responsive.some((row, index) => row.dark && row.background === responsive[index - 1].background))
          throw Error('Nền dialog lịch sử chưa theo giao diện tối');
        continue;
      }
      if (sessionCheckOnly) {
        await evaluate('localStorage.setItem("role","ADMIN");sessionStorage.setItem("permissions",JSON.stringify(["MANAGE_INTERNAL_ACCOUNTS"]));');
        await navigate('/admin/dashboard');
        const deniedAfterTamper = await waitFor('location.pathname==="/admin/dashboard" && document.body?.innerText.includes(' + JSON.stringify(conventionalDenied) + ')');
        await navigate('/account');
        if (!await waitFor('location.pathname==="/account" && Boolean(document.querySelector(".logout-button"))')) throw Error('Logout button not found');
        const { targetId } = await call('Target.createTarget', { url: 'about:blank' });
        const { sessionId } = await call('Target.attachToTarget', { targetId, flatten: true });
        await configureApi(sessionId);
        await call('Page.navigate', { url: site + '/my-maintenance' }, sessionId);
        async function tabHas(expression, limit = 65) {
          for (let attempt = 0; attempt < limit; attempt++) {
            const state = await call('Runtime.evaluate', { expression, returnByValue: true }, sessionId);
            if (state.result?.value) return true;
            await delay(100);
          }
          return false;
        }
        const tabReady = await tabHas('location.pathname==="/my-maintenance" && document.body?.innerText.includes("Nhiệm vụ của tôi")');
        if (!tabReady) throw Error('Tab cùng phiên chưa mở được khu vực STAFF');
        apiStatuses = [];
        const logoutStart = Date.now();
        await evaluate('document.querySelector(".logout-button").click()');
        const logoutRedirect = await waitFor('location.pathname==="/login" && Boolean(document.querySelector("#username"))');
        const otherTabExited = await tabHas('location.pathname==="/login" && Boolean(document.querySelector("#username"))');
        const observedClientMs = Date.now() - logoutStart;
        await call('Target.closeTarget', { targetId });
        // Route state clears immediately. Wait for the separate server-side revocation response.
        let logoutStatus;
        for (let attempt = 0; attempt < 65 && logoutStatus !== 204; attempt++) {
          logoutStatus = apiStatuses.find(([path,code,type]) => path === '/api/v1/auth/logout' && type !== 'Preflight')?.[1];
          if (logoutStatus !== 204) await delay(175);
        }
        await navigate('/staff/service-requests');
        const protectedReopensLogin = await waitFor('location.pathname==="/login" && Boolean(document.querySelector("#username"))');
        await snapshot('staff-after-logout');
        const oldBearerStatus = await evaluate(
          'fetch(' + JSON.stringify(api + '/api/v1/auth/me') + ',{headers:{Authorization:' +
          JSON.stringify('Bearer ' + token) + '}}).then(r=>r.status)', true);
        await writeFile(join(root, sameOrigin ? 'hardening-12-browser-proxy-session.json' : 'browser-session-lifecycle.json'), JSON.stringify({
          role, deniedAfterTamper, logoutStatus, logoutRedirect, protectedReopensLogin, otherTabExited, observedClientMs, oldBearerStatus
        }, null, 2));
        if (!deniedAfterTamper || logoutStatus !== 204 || !logoutRedirect || !protectedReopensLogin ||
            !otherTabExited || oldBearerStatus !== 401) throw Error('Session lifecycle check failed');
        console.log('STAFF session lifecycle checks recorded');
        continue;
      }
      if (scope !== 'Routes' && account === role) await checkTokens(role, token);
      if (scope !== 'Routes' && (role === 'STAFF' || role === 'RESIDENT'))
        await checkHorizontal(account, token, role === 'STAFF' ? staffPairs : residentPairs);
      await persist();
    }
    if (account !== role) continue;
    if (scope !== 'Routes') {
      await probeDenied(role, token);
      await persist();
      console.log('HTTP denial probes recorded for', role);
    }
    if (scope === 'Api') continue;
    const runnable = routes.filter(row => row.type !== 'INTERNAL/TECHNICAL' &&
      (!supplemental || !routeEvidence.some(record => record.route === row.path && record.role === role && record.status === 'PASS')));
    // Prioritize the requested STAFF case and a positive control for each role.
    runnable.sort((left,right) => (left.path === (homes[role] ?? '/terms') ? -2 : left.path === '/admin/dashboard' ? -1 : 0) -
      (right.path === (homes[role] ?? '/terms') ? -2 : right.path === '/admin/dashboard' ? -1 : 0));
    for (const row of runnable) {
      const expect = row.allow.includes(role) ? 'ALLOW' : 'DENY';
      const path = row.path.replace(/\{[^}]+\}/g, dynamic[row.path] ?? fallbackId);
      const validResource = !row.path.includes('{') || Boolean(dynamic[row.path]);
      const expectedRedirect = role !== 'Ẩn danh' && (path === '/' || path === '/login') ? redirectHome[role] :
        role === 'RESIDENT' && ['/resident/announcements','/resident/service-requests'].includes(path) ? '/resident' : null;
      apiStatuses = [];
      errors = [];
      const navigation = await navigate(path);
      const rowCondition = '(()=>{const p='+pageState+';return (p.path==="/login" && p.login && ' + JSON.stringify(expect === 'DENY' && role === 'Ẩn danh') + ') || ((p.path === ' + JSON.stringify(path) + ' || p.path === ' + JSON.stringify(expectedRedirect) + ') && (p.denied||p.heading||p.content||p.notFound) && !p.waiting);})()';
      const ready = await waitFor(rowCondition);
      let state;
      try { state = await evaluate(pageState); } catch { state = {}; }
      const actual = !ready ? 'TIMEOUT' : state.denied ? 'DENIED' : state.login && path !== '/login' ? 'LOGIN' :
        state.notFound ? 'NOT FOUND' : state.path !== path ? 'REDIRECT' : 'RENDERED';
      const appCalls = apiStatuses.filter(([url,,type,loader]) => !url.startsWith('/api/v1/auth/') &&
        type !== 'Preflight' && loader === navigation.loaderId).map(([url,code]) => [url,code]);
      const sharedShellEndpoints = ['/api/v1/notifications/unread-count', '/api/v1/notifications',
        '/hubs/emergency-calls/negotiate', '/api/v1/emergency-calls/active'];
      const status = expect === 'DENY' ? ((actual === 'DENIED' || actual === 'LOGIN') &&
        appCalls.every(([url, code]) => code >= 400 || sharedShellEndpoints.includes(url)) ? 'PASS' : 'CHECK')
        : ((actual === 'RENDERED' || (actual === 'REDIRECT' && state.path === expectedRedirect)) && validResource &&
          !appCalls.some(([, code]) => code >= 500) ? 'PASS' : 'NOT VERIFIED');
      const record = { role, route: row.path, testedUrl: path, expect, actual, status, actualPath: state.path,
        api: appCalls, jsErrors: errors, validResource };
      const prior = routeEvidence.findIndex(item => item.role === role && item.route === row.path);
      if (prior < 0) routeEvidence.push(record);
      else routeEvidence[prior] = { ...routeEvidence[prior], ...record };
      if (row.path === homes[role] || (row.path === '/admin/dashboard' && role === 'STAFF') ||
          (row.path === '/terms' && role === 'Ẩn danh')) {
        await snapshot(role === 'Ẩn danh' ? 'anonymous-terms' : role + (expect === 'ALLOW' ? '-allowed' : '-denied'));
        if (role === 'STAFF' && expect === 'DENY') {
          const history = await call('Page.getNavigationHistory');
          if (history.currentIndex > 0) {
            await call('Page.navigateToHistoryEntry', { entryId: history.entries[history.currentIndex - 1].id });
            await waitFor('location.pathname==="/staff/service-requests" && Boolean(document.querySelector("h1"))');
            await call('Page.navigateToHistoryEntry', { entryId: history.entries[history.currentIndex].id });
            record.forwardDenied = await waitFor('location.pathname==="/admin/dashboard" && document.body?.innerText.includes(' + JSON.stringify(conventionalDenied) + ')');
          }
          await call('Page.reload', { ignoreCache: false });
          record.refreshDenied = await waitFor('document.body?.innerText.includes(' + JSON.stringify(conventionalDenied) + ')');
        }
      }
      await persist();
    }
    if (supplemental && role === 'STAFF') {
      const { targetId } = await call('Target.createTarget', { url: 'about:blank' });
      try {
        const { sessionId } = await call('Target.attachToTarget', { targetId, flatten: true });
        await configureApi(sessionId);
        await call('Page.navigate', { url: site + '/admin/dashboard' }, sessionId);
        const row = routeEvidence.find(item => item.role === 'STAFF' && item.route === '/admin/dashboard');
        row.newTabDenied = false;
        for (let attempt = 0; attempt < 65; attempt++) {
          const value = await call('Runtime.evaluate', {
            expression: 'document.body?.innerText.includes(' + JSON.stringify(conventionalDenied) + ')',
            returnByValue: true
          }, sessionId);
          if (value.result?.value) {
            row.newTabDenied = true;
            const { data } = await call('Page.captureScreenshot', { format: 'png' }, sessionId);
            await writeFile(join(root, 'staff-admin-new-tab-denied.png'), Buffer.from(data, 'base64'));
            break;
          }
          await delay(175);
        }
        await persist();
      } finally { await call('Target.closeTarget', { targetId }); }
      await navigate('/my-maintenance');
      const maintenanceLoaded = await waitFor('location.pathname==="/my-maintenance" && Boolean(document.querySelector(".smt-title"))');
      if (maintenanceLoaded) {
        const selected = () => evaluate('document.querySelector(".smt-tab.is-active")?.textContent.trim()');
        const initial = await selected();
        await evaluate('Array.from(document.querySelectorAll(".smt-tab")).find(b=>b.textContent.trim()==="Tất cả nhiệm vụ").click()');
        await waitFor('document.querySelector(".smt-tab.is-active")?.textContent.trim()==="Tất cả nhiệm vụ"');
        const allTab = await selected();
        const privilegedButtons = await evaluate('Array.from(document.querySelectorAll(".smt-table button")).filter(b=>/^(Phân công|Nghiệm thu|Hủy công việc)$/.test(b.textContent.trim())).length');
        uiEvidence.push({ role, route:'/my-maintenance', initial, allTab, privilegedButtons,
          verdict: initial === 'Nhiệm vụ của tôi' && allTab === 'Tất cả nhiệm vụ' && privilegedButtons === 0 ? 'PASS' : 'CHECK' });
        for (const width of [390, 768, 1440]) {
          await call('Emulation.setDeviceMetricsOverride', { width, height: 900, deviceScaleFactor: 1, mobile: false });
          for (const dark of [false, true]) {
            await setShellTheme(dark);
            await snapshot('staff-maintenance-' + width + (dark ? '-dark' : '-light'));
            uiEvidence.push({ role, route:'/my-maintenance', width, dark,
              fitsViewport: await evaluate('document.documentElement.scrollWidth <= innerWidth + 1') });
          }
        }
        await call('Emulation.clearDeviceMetricsOverride');
      }
      await persist();
    }
    console.log('Direct route checks recorded for', role);
  }
  if (themeFailures.length) throw Error(themeFailures.join('\n'));
  const checks = [...routeEvidence, ...apiEvidence, ...credentialEvidence, ...horizontalEvidence];
  const mismatches = checks.filter(row => (row.status ?? row.verdict) === 'CHECK');
  if (mismatches.length) throw Error(`Có ${mismatches.length} kết quả phân quyền khác kỳ vọng. Xem JSON trong artifacts/authz.`);
} finally {
  socket?.close();
  if (browser.exitCode === null && browser.signalCode === null) browser.kill();
  await exited;
  if (profile.startsWith(root + '\\chrome-')) await rm(profile, { recursive: true, force: true, maxRetries: 8, retryDelay: 200 });
  console.log('Chrome stopped', browser.pid);
}
