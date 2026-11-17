import { writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';

const extraRoutes = {
  ADMIN: ['/admin/dashboard?tab=accounts', '/admin/dashboard?tab=payment', '/admin/dashboard?tab=activity'],
  RESIDENT: ['/resident?tab=processing', '/resident?tab=notifications']
};
const themeButtons = '.app-theme-button, #theme-toggle-btn, .public-theme-toggle';
const maintenanceTabs = {
  '/maintenance': ['.fe07-workspace-tabs [role="tab"]', 'is-selected'],
  '/my-maintenance': ['.smt-tab', 'is-active']
};

function inspectTheme() {
  const canvas = document.createElement('canvas');
  canvas.width = canvas.height = 1;
  const context = canvas.getContext('2d', { willReadFrequently: true });
  const colors = new Map();
  const rgba = value => {
    if (!colors.has(value)) {
      context.clearRect(0, 0, 1, 1);
      context.fillStyle = value;
      context.fillRect(0, 0, 1, 1);
      colors.set(value, Array.from(context.getImageData(0, 0, 1, 1).data));
    }
    return colors.get(value);
  };
  const luminance = color => color.slice(0, 3).reduce((total, value, index) => {
    const channel = value / 255;
    return total + (channel <= .04045 ? channel / 12.92 : ((channel + .055) / 1.055) ** 2.4) * [.2126, .7152, .0722][index];
  }, 0);
  const surface = node => {
    const layers = [];
    for (let current = node; current; current = current.parentElement) {
      const style = getComputedStyle(current);
      if (style.backgroundImage !== 'none') return null;
      const color = rgba(style.backgroundColor);
      if (color[3]) layers.push(color);
      if (color[3] === 255) break;
    }
    return layers.reverse().reduce((base, color) => {
      const opacity = color[3] / 255;
      return base.map((value, index) => opacity * color[index] + (1 - opacity) * value);
    }, document.documentElement.dataset.theme === 'dark' ? [8, 13, 20] : [255, 255, 255]);
  };
  const findings = new Map();
  const dark = document.documentElement.dataset.theme === 'dark';
  let textSamples = 0;
  for (const node of document.body.querySelectorAll('body *:not(svg, svg *, script, style)')) {
    if (node.closest('[inert], [aria-hidden="true"], :disabled, svg, iframe, .material-symbols-outlined')) continue;
    const bounds = node.getBoundingClientRect();
    if (bounds.width < 8 || bounds.height < 8 || bounds.right <= 0 || bounds.left >= innerWidth) continue;
    const style = getComputedStyle(node);
    if (style.visibility !== 'visible' || Number(style.opacity) < .55) continue;
    const text = Array.from(node.childNodes).filter(child => child.nodeType === Node.TEXT_NODE)
      .map(child => child.textContent.trim()).filter(Boolean).join(' ');
    const selector = node.localName + Array.from(node.classList).slice(0, 3).map(name => '.' + name).join('');
    const group = node.closest('main, article, section, form, table')?.classList[0] ?? '';
    const background = surface(node);
    if (!background) continue;
    const paper = luminance(background);
    const ownBackground = rgba(style.backgroundColor);
    if (dark && bounds.width >= 200 && bounds.height >= 32 && ownBackground[3] >= 160 && paper > .42 &&
        !node.closest('button, a, [role="button"], input[type="range"], .app-chat-trigger')) {
      findings.set('surface ' + selector, { type: 'surface', selector, group, text: text.slice(0, 90), background: style.backgroundColor });
    }
    if (!text || rgba(style.color)[3] !== 255) continue;
    textSamples++;
    const ink = luminance(rgba(style.color));
    const size = parseFloat(style.fontSize);
    const minimum = size >= 24 || (size >= 18.66 && Number(style.fontWeight) >= 700) ? 3 : 4.5;
    const ratio = (Math.max(ink, paper) + .05) / (Math.min(ink, paper) + .05);
    if (ratio < minimum) {
      findings.set(['contrast', selector, group, style.color, background.map(Math.round)].join(' '), {
        type: 'contrast', selector, group, text: text.slice(0, 90), ratio: Number(ratio.toFixed(2)), minimum,
        foreground: style.color, background: background.map(Math.round)
      });
    }
  }
  return { textSamples, overflow: Math.max(0, document.documentElement.scrollWidth - innerWidth), findings: [...findings.values()] };
}

export async function auditThemeRoutes({ role, routes, dynamic, call, evaluate, waitFor, navigate, root }) {
  const anonymous = role === 'Ẩn danh';
  const candidates = routes.filter(row => row.allow.includes(role) &&
    (anonymous ? row.type === 'PUBLIC' : !['PUBLIC', 'INTERNAL/TECHNICAL'].includes(row.type)));
  const auditRoutes = [...candidates.map(row => row.path), ...extraRoutes[role] ?? []];
  const results = [];
  const report = join(root, `theme-scan-${anonymous ? 'public' : role.toLowerCase()}.json`);
  const themeState = mode => `document.documentElement.dataset.theme === ${JSON.stringify(mode)} && document.documentElement.classList.contains(${JSON.stringify(mode)})`;
  const setTheme = async mode => {
    if (!await evaluate(themeState(mode))) await evaluate(`(() => {
      const toggle = document.querySelector(${JSON.stringify(themeButtons)});
      if (toggle) toggle.click(); else window.propFlowTheme.toggle();
    })()`);
    if (!await waitFor(themeState(mode), 15)) throw Error(`Không đổi được chế độ ${mode}`);
  };
  const capture = async () => {
    const samples = [];
    for (const width of [1440, 390]) {
      await call('Emulation.setDeviceMetricsOverride', { width, height: 900, deviceScaleFactor: 1, mobile: false });
      for (const mode of ['dark', 'light']) {
        await setTheme(mode);
        await delay(150);
        const inspect = `(${inspectTheme})()`;
        let sample = await evaluate(inspect);
        if (sample.findings.some(finding => finding.type === 'surface' || finding.ratio < 2)) {
          await delay(750);
          if (!await evaluate(themeState(mode))) throw Error(`Chế độ ${mode} đổi sau khi thay kích thước`);
          sample = await evaluate(inspect);
        }
        samples.push({ width, mode, ...sample });
      }
    }
    return samples;
  };
  for (const route of auditRoutes) {
    if (route.includes('{') && !dynamic[route]) {
      results.push({ route, status: 'missing fixture' });
      continue;
    }
    const path = route.replace(/\{[^}]+\}/g, dynamic[route] ?? '');
    const redirected = role === 'RESIDENT' && ['/resident/announcements', '/resident/service-requests'].includes(path);
    const expectedPath = redirected ? '/resident' : path.split('?')[0];
    try {
      await setTheme('dark');
      const spa = await evaluate('typeof window.Blazor?.navigateTo === "function"');
      if (spa) await evaluate(`Blazor.navigateTo(${JSON.stringify(path)})`);
      else await navigate(path);
      const rendered = `location.pathname === ${JSON.stringify(expectedPath)} && document.querySelector('h1,h2,main') && !document.body.innerText.includes('Đang kiểm tra quyền truy cập')`;
      if (!await waitFor(rendered, 38)) throw Error('Không thấy nội dung trang');
      await delay(400);
      const state = await evaluate(`({ path: location.pathname + location.search, title: document.title, denied: document.body.innerText.includes('Bạn không có quyền truy cập trang này.') })`);
      if (state.denied) throw Error('Trang từ chối tài khoản fixture');
      if (!await evaluate(themeState('dark'))) throw Error('Chế độ tối bị mất khi chuyển trang');
      await evaluate(`(() => {
        if (document.querySelector('#theme-audit-motion')) return;
        const styles = document.createElement('style');
        styles.id = 'theme-audit-motion';
        styles.textContent = '* { transition-duration: 0s !important; }';
        document.head.append(styles);
      })()`);
      const samples = await capture();
      results.push({ route, ...state, status: 'rendered', samples });
      const desktopDark = samples.find(sample => sample.width === 1440 && sample.mode === 'dark');
      console.log('Theme route', role, route, 'samples', desktopDark.textSamples, 'signals', desktopDark.findings.length);
      const lightSurface = samples.find(sample => sample.mode === 'dark' && sample.findings.some(finding => finding.type === 'surface'));
      if (lightSurface) {
        await call('Emulation.setDeviceMetricsOverride', { width: lightSurface.width, height: 900, deviceScaleFactor: 1, mobile: false });
        await setTheme('dark');
        const { data } = await call('Page.captureScreenshot', { format: 'png' });
        const name = route.replace(/[^a-z0-9]+/gi, '-').replace(/^-|-$/g, '') || 'home';
        await writeFile(join(root, `theme-surface-${anonymous ? 'public' : role.toLowerCase()}-${name}-${lightSurface.width}.png`), Buffer.from(data, 'base64'));
      }
      const [tabSelector, selected] = maintenanceTabs[route] ?? [];
      if (tabSelector) {
        const tabs = `document.querySelectorAll(${JSON.stringify(tabSelector)})`;
        const alternatives = await evaluate(`Array.from(${tabs}).flatMap((button, index) =>
          button.classList.contains(${JSON.stringify(selected)}) ? [] :
          [{ index, name: Array.from(button.childNodes)
            .filter(node => node.nodeType === Node.TEXT_NODE || (node.nodeType === Node.ELEMENT_NODE && node.getAttribute('aria-hidden') !== 'true'))
            .map(node => node.textContent.trim()).filter(Boolean).join(' ') }])`);
        for (const { index, name } of alternatives) {
          const button = `${tabs}[${index}]`;
          await evaluate(`${button}.click()`);
          if (!await waitFor(`${button}.classList.contains(${JSON.stringify(selected)})`, 25))
            throw Error(`Không mở được mục ${name}`);
          await delay(400);
          results.push({ route, view: name, ...state, status: 'rendered', samples: await capture() });
          console.log('Theme tab', role, route, name);
        }
      }
    } catch (error) {
      results.push({ route, status: 'unverified', error: error.message });
      console.error('Theme route', role, route, error.message);
    } finally {
      await call('Emulation.setDeviceMetricsOverride', { width: 1440, height: 900, deviceScaleFactor: 1, mobile: false });
      await writeFile(report, JSON.stringify(results, null, 2));
    }
  }
  const failures = results.filter(result => result.status !== 'rendered');
  console.log('Theme route coverage', role, `${results.length - failures.length}/${results.length}`);
  return failures.map(result => `${role} ${result.route}: ${result.error ?? result.status}`);
}
