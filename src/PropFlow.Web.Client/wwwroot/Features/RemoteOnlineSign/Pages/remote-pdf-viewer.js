import * as pdfjsLib from '/lib/pdfjs-4.10.38/build/pdf.mjs';
import { displaySize, pdfRectToDisplay, displayRectToPdf, clampDisplayRect } from './remote-pdf-geometry.mjs';

pdfjsLib.GlobalWorkerOptions.workerSrc = '/lib/pdfjs-4.10.38/build/pdf.worker.mjs';
const instances = new WeakMap();
const renderTokens = new WeakMap();
const pageScrollGuards = new WeakMap();
const hostDotNet = new WeakMap();
const invoke = (dotnet, method, ...args) => {
  if (!dotnet) return;
  void dotnet.invokeMethodAsync(method, ...args).catch(() => { });
};
const mark = (stage, detail = {}) => { const telemetry = globalThis.__remotePdfTelemetry; if (telemetry) { telemetry.events.push({ stage, at: performance.now(), ...detail }); telemetry.stage = stage; } };

function addField(stage, field, pageWidth, pageHeight, rotation, editable, dotnet) {
  const display = displaySize(rotation, pageWidth, pageHeight); let rect = pdfRectToDisplay(rotation, pageWidth, pageHeight, field);
  const box = document.createElement('div'); box.className = `remote-pdf-field ${field.role.toLowerCase()}${editable ? ' editable' : ''}`; box.dataset.role = field.role;
  // Dynamic elements are not CSS-isolation scoped. Keep their essential layout
  // styles here; the component stylesheet supplies the visual treatment.
  Object.assign(box.style, { position: 'absolute', zIndex: '2', boxSizing: 'border-box' });
  const paint = () => { box.style.left = `${rect.left / display.width * 100}%`; box.style.top = `${rect.top / display.height * 100}%`; box.style.width = `${rect.width / display.width * 100}%`; box.style.height = `${rect.height / display.height * 100}%`; mark('field-paint', { role: field.role, left: box.style.left, top: box.style.top, width: box.style.width, height: box.style.height }); };
  paint(); box.textContent = field.role === 'INVESTOR' ? 'CHỦ ĐẦU TƯ' : 'CƯ DÂN'; if (!editable) return box;
  const handle = document.createElement('span'); handle.className = 'resize'; handle.setAttribute('aria-label', 'Đổi kích thước vùng ký'); box.append(handle);
  let mode = null; let start = null;
  const emit = () => { const pdf = displayRectToPdf(rotation, pageWidth, pageHeight, rect); field.x = pdf.x; field.y = pdf.y; field.width = pdf.width; field.height = pdf.height; mark('field-emit', { role: field.role, ...pdf }); invoke(dotnet, 'FieldChanged', field.role, field.page, pdf.x, pdf.y, pdf.width, pdf.height); };
  box.addEventListener('pointerdown', event => { invoke(dotnet, 'FieldSelected', field.role); mode = event.target.classList.contains('resize') ? 'resize' : 'move'; start = { x: event.clientX, y: event.clientY, rect: { ...rect } }; box.setPointerCapture(event.pointerId); mark('field-pointerdown', { role: field.role, mode, pointerId: event.pointerId, x: event.clientX, y: event.clientY }); event.preventDefault(); });
  box.addEventListener('pointermove', event => { if (!mode || !start) return; const host = stage.getBoundingClientRect(); const dx = (event.clientX - start.x) * display.width / host.width; const dy = (event.clientY - start.y) * display.height / host.height; rect = mode === 'move' ? { ...start.rect, left: start.rect.left + dx, top: start.rect.top + dy } : { ...start.rect, width: start.rect.width + dx, height: start.rect.height + dy }; rect = clampDisplayRect(rect, display.width, display.height); paint(); emit(); mark('field-pointermove', { role: field.role, mode, dx, dy, left: rect.left, top: rect.top }); });
  box.addEventListener('pointerup', event => { mark('field-pointerup', { role: field.role, pointerId: event.pointerId }); mode = null; start = null; }); return box;
}

export async function render(host, bytes, selectedPage, scale, rotation, fields, editable, dotnet, token = null) {
  const current = () => token === null || renderTokens.get(host) === token;
  try { await dispose(host); if (!current()) return 0; host.replaceChildren(); mark('getDocument-start', { selectedPage }); const task = pdfjsLib.getDocument({ data: new Uint8Array(bytes) }); const pdf = await task.promise; if (!current()) { await pdf.destroy(); await task.destroy(); return 0; } mark('getDocument-complete', { pages: pdf.numPages }); const pages = document.createElement('div'); pages.className = 'remote-pdf-pages'; host.append(pages);
    for (let pageNumber = 1; pageNumber <= pdf.numPages; pageNumber++) { if (!current()) { await pdf.destroy(); await task.destroy(); return 0; } const page = await pdf.getPage(pageNumber); const base = page.getViewport({ scale: 1, rotation: 0 }); const viewport = page.getViewport({ scale, rotation }); const stage = document.createElement('div'); const canvas = document.createElement('canvas'); stage.className = 'remote-pdf-stage'; stage.dataset.page = String(pageNumber); stage.style.width = `${viewport.width}px`; stage.style.height = `${viewport.height}px`; canvas.width = Math.ceil(viewport.width); canvas.height = Math.ceil(viewport.height); stage.append(canvas); pages.append(stage); await page.render({ canvasContext: canvas.getContext('2d'), viewport }).promise; fields.filter(field => field.page === pageNumber).forEach(field => stage.append(addField(stage, { ...field }, base.width, base.height, rotation, editable, dotnet))); }
    const observer = dotnet ? new IntersectionObserver(entries => {
      if (pageScrollGuards.get(host)) return;
      const visible = entries.filter(x => x.isIntersecting).sort((a, b) => b.intersectionRatio - a.intersectionRatio)[0];
      if (visible) invoke(dotnet, 'PageChanged', Number(visible.target.dataset.page));
    }, { root: host, threshold: [0.55] }) : null;
    if (observer) pages.querySelectorAll('.remote-pdf-stage').forEach(stage => observer.observe(stage));
    hostDotNet.set(host, dotnet);
    if (!current()) { await pdf.destroy(); await task.destroy(); return 0; } instances.set(host, { pdf, task, observer }); mark('render-complete', { pages: pdf.numPages }); await scrollToPage(host, selectedPage); mark('done'); return pdf.numPages;
  } catch (error) { mark('failed', { error: String(error) }); throw error; }
}

function pageAtCenter(host) {
  const stages = [...host.querySelectorAll('.remote-pdf-stage')];
  if (!stages.length) return null;
  const rect = host.getBoundingClientRect();
  const mid = rect.top + rect.height / 2;
  let best = null, bestDist = Infinity;
  for (const stage of stages) {
    const r = stage.getBoundingClientRect();
    const dist = Math.abs(r.top + r.height / 2 - mid);
    if (dist < bestDist) { bestDist = dist; best = Number(stage.dataset.page); }
  }
  return best;
}

function guardPageEvents(host) {
  pageScrollGuards.set(host, true);
  let released = false;
  const release = () => {
    if (released) return;
    released = true;
    pageScrollGuards.delete(host);
    const dotnet = hostDotNet.get(host);
    const page = pageAtCenter(host);
    if (dotnet && page !== null) invoke(dotnet, 'PageChanged', page);
  };
  host.addEventListener('scrollend', release, { once: true });
  setTimeout(release, 900);
}

export async function scrollToPage(host, pageNumber) { const stage = host.querySelector(`.remote-pdf-stage[data-page="${pageNumber}"]`); if (!stage) return; guardPageEvents(host); host.scrollTo({ top: Math.max(0, stage.offsetTop - 12), behavior: 'smooth' }); }
export async function scrollToField(host, role) { const field = host.querySelector(`.remote-pdf-field[data-role="${role}"]`); if (!field) return; await scrollToPage(host, Number(field.closest('.remote-pdf-stage')?.dataset.page)); field.focus?.({ preventScroll: true }); }
export async function dispose(host) { const instance = instances.get(host); if (!instance) return; instance.observer?.disconnect(); try { await instance.pdf.destroy(); } catch { } try { await instance.task.destroy(); } catch { } instances.delete(host); }

export async function renderLocal(host, inputId, selectedPage, scale) {
  const input = document.getElementById(inputId);
  const file = input?.files?.[0];
  if (!file) throw new Error('PDF preview source is unavailable.');
  const bytes = new Uint8Array(await file.arrayBuffer());
  const token = (renderTokens.get(host) ?? 0) + 1;
  renderTokens.set(host, token);
  return await render(host, bytes, selectedPage, scale, 0, [], false, null, token);
}

export async function renderBytes(host, bytes, selectedPage, scale) {
  const token = (renderTokens.get(host) ?? 0) + 1;
  renderTokens.set(host, token);
  return await render(host, bytes, selectedPage, scale, 0, [], false, null, token);
}
