const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');

test('avatar menu click reaches Blazor document delegation so invoice loading can run', () => {
  const source = fs.readFileSync(path.join(root, 'src/PropFlow.Web.Client/wwwroot/js/resident.js'), 'utf8');
  const start = source.indexOf('    function initializeProfileMenu()');
  const end = source.indexOf('    function initializeSearchShortcut()', start);
  assert.ok(start >= 0 && end > start);
  const classes = new Set();
  const triggerHandlers = [], documentHandlers = [];
  const trigger = { dataset: {}, addEventListener: (_, handler) => triggerHandlers.push(handler), setAttribute() {}, contains: value => value === trigger };
  const popover = { hidden: true, classList: { contains: x => classes.has(x), add: x => classes.add(x), remove: x => classes.delete(x) }, contains: value => value === popover };
  const document = { getElementById: id => id === 'profileDropdownBtn' ? trigger : id === 'profilePopover' ? popover : null, addEventListener: (_, handler) => documentHandlers.push(handler) };
  new Function('document', 'let outsideClickBound = false; ' + source.slice(start, end) + '; initializeProfileMenu();')(document);
  let blazorClicks = 0;
  documentHandlers.push(() => blazorClicks++);
  const event = { target: trigger, stopped: false, stopPropagation() { this.stopped = true; } };
  triggerHandlers.forEach(handler => handler(event));
  if (!event.stopped) documentHandlers.forEach(handler => handler(event));
  assert.equal(popover.hidden, false);
  assert.equal(blazorClicks, 1);
});

test('profile section links resolve to the profile route rather than the base home route', () => {
  const source = fs.readFileSync(path.join(root, 'src/PropFlow.Web.Client/Features/Resident/Portal/Pages/ResidentProfile.razor'), 'utf8');
  const nav = source.match(/<nav class="profile-sections"[\s\S]*?<\/nav>/)[0];
  for (const id of ['personal-title', 'resident-details-title', 'resident-vehicles-title', 'security-title']) {
    assert.ok(nav.includes(`href="/resident/profile#${id}"`));
  }
  assert.ok(!nav.includes('href="#'));
});

test('profile navigation scrolls and focuses the requested heading without navigating away', () => {
  const source = fs.readFileSync(path.join(root, 'src/PropFlow.Web.Client/wwwroot/js/resident.js'), 'utf8');
  const start = source.indexOf('        scrollToProfileSection(id) {');
  const end = source.indexOf('        closeProfileMenu()', start);
  const method = source.slice(start, end).trim().replace(/,$/, '');
  for (const reducedMotion of [false, true]) {
    const calls = [];
    const heading = { scrollIntoView: args => calls.push(['scroll', args]), setAttribute() {}, focus: args => calls.push(['focus', args]) };
    const doc = { getElementById: id => id === 'security-title' ? heading : null };
    const nav = new Function('document', 'global', 'return ({' + method + '});')(doc, { matchMedia: () => ({ matches: reducedMotion }) });
    nav.scrollToProfileSection('security-title');
    assert.deepEqual(calls, [['scroll', { behavior: reducedMotion ? 'auto' : 'smooth', block: 'start' }], ['focus', { preventScroll: true }]]);
    nav.scrollToProfileSection('missing');
    assert.equal(calls.length, 2);
  }
});
