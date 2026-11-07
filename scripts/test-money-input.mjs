import { readFile } from 'node:fs/promises';
import assert from 'node:assert/strict';
import test from 'node:test';

const source = await readFile(new URL('../src/PropFlow.Web.Client/Shared/Forms/PropFlowMoneyBehavior.razor.js', import.meta.url), 'utf8');
const { formatEdit, attach, detach } = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));

test('typing every digit, without waiting for a render, preserves the entire amount', () => {
    for (const [digits, expected] of [['4000000', '4.000.000'], ['400000000', '400.000.000'], ['999999999999999999', '999.999.999.999.999.999']]) {
        let previous = '';
        for (const digit of digits) previous = formatEdit(previous, previous + digit).text;
        assert.equal(previous, expected);
    }
});

test('appending to an existing water price does not produce a decimal', () => {
    assert.equal(formatEdit('10.000', '10.0000').text, '100.000');
    assert.equal(formatEdit('10.000', '10.000000').text, '10.000.000');
});

test('paste, deletion, clearing, invalid decimals and mid-string caret', () => {
    assert.equal(formatEdit('10.000', '400000000').text, '400.000.000');
    assert.equal(formatEdit('10.000', '400.000.000').text, '400.000.000');
    assert.equal(formatEdit('1.000.000', '1.000.00').text, '100.000');
    assert.equal(formatEdit('1.000', '').text, '');
    assert.equal(formatEdit('', '10,5').text, '10,5');
    assert.equal(formatEdit('', '10.5').text, '10.5');
    const result = formatEdit('1.234.567', '1.2934.567', 4);
    assert.equal(result.text, '12.934.567');
    assert.equal(result.text.slice(0, result.caret).replaceAll('.', ''), '129');
});

test('input is masked before the consumer reads it; listeners detach cleanly', () => {
    class Input extends EventTarget {
        value = '10.000'; selectionStart = 6;
        setSelectionRange(start) { this.selectionStart = start; }
    }
    const input = new Input();
    attach(input);
    input.dispatchEvent(new Event('beforeinput'));
    input.value += '0'; input.selectionStart = input.value.length;
    input.dispatchEvent(new Event('input'));
    assert.equal(input.value, '100.000');
    detach(input);
    input.value = '1000000';
    input.dispatchEvent(new Event('input'));
    assert.equal(input.value, '1000000');
});
