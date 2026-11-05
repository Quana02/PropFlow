import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const source = await readFile(new URL('../src/PropFlow.Web.Client/Shared/Forms/PropFlowDatePicker.razor.js', import.meta.url), 'utf8');
const picker = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);
globalThis.document = { addEventListener() {}, removeEventListener() {} };
globalThis.cancelAnimationFrame = () => {};
globalThis.window = { innerWidth: 1920, innerHeight: 1080, addEventListener() {}, removeEventListener() {} };
function check(viewport, anchorLeft) {
    window.innerWidth = viewport;
    const properties = {};
    const calendar = { offsetHeight: 360, matches: () => false, addEventListener() {}, removeEventListener() {} };
    const root = {
        style: { setProperty: (key, value) => properties[key] = parseFloat(value) },
        querySelector: selector => selector === '.pf-date__control'
            ? { getBoundingClientRect: () => ({ left: anchorLeft, top: 500, bottom: 540, width: 154 }) }
            : selector === '.pf-date__calendar' ? calendar : {},
        closest: () => null,
    };
    picker.open(root, {});
    const width = properties['--pf-date-width'];
    assert.ok(width >= Math.min(288, viewport - 16), `Calendar squeezed to ${width}px for ${viewport}px viewport`);
    assert.ok(properties['--pf-date-left'] >= 8);
    assert.ok(properties['--pf-date-left'] + width <= viewport - 8);
    picker.close(root);
}
check(1920, 707);
check(375, 250);
check(280, 200);
console.log('PASS: narrow date fields keep a readable calendar within desktop and mobile viewports.');
