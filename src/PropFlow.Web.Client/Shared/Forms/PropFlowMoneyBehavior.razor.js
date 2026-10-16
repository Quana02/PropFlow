const states = new WeakMap();
const grouped = /^-?(?:[0-9]+|[0-9]{1,3}(?:\.[0-9]{3})+)$/;

// Work on digit strings: never round financial amounts with JavaScript numbers.
export function formatEdit(previous, text, caret = text.length) {
    let normalized = text;
    let prefix = 0, suffix = 0;
    while (prefix < previous.length && prefix < text.length && previous[prefix] === text[prefix]) prefix++;
    while (suffix < previous.length - prefix && suffix < text.length - prefix && previous[previous.length - suffix - 1] === text[text.length - suffix - 1]) suffix++;
    const inserted = text.slice(prefix, text.length - suffix);
    if ((!previous || grouped.test(previous)) && /^[0-9]*$/.test(inserted)) normalized = text.replaceAll('.', '');
    else if (grouped.test(text)) normalized = text.replaceAll('.', '');
    if (!/^-?[0-9]+$/.test(normalized)) return { text, caret };
    const sign = normalized.startsWith('-') ? '-' : '';
    const digits = normalized.replace('-', '').replace(/^0+(?=[0-9])/, '');
    const formatted = sign + digits.replace(/\B(?=([0-9]{3})+(?![0-9]))/g, '.');
    const before = Math.min((text.slice(0, caret).match(/[0-9]/g) || []).length, digits.length);
    let position = sign.length, seen = 0;
    while (position < formatted.length && seen < before) {
        if (/[0-9]/.test(formatted[position])) seen++;
        position++;
    }
    return { text: formatted, caret: position };
}

export function attach(input) {
    detach(input);
    const state = { previous: input.value, before: null, change: null };
    state.before = () => { state.previous = input.value; };
    state.change = event => {
        if (event.isComposing) return;
        const result = formatEdit(state.previous, input.value, input.selectionStart ?? input.value.length);
        input.value = result.text;
        input.setSelectionRange(result.caret, result.caret);
        state.previous = result.text;
    };
    input.addEventListener('beforeinput', state.before);
    // Format before Blazor's delegated listener reads the event value. This also
    // keeps the caret stable when editing in the middle or typing rapidly.
    input.addEventListener('input', state.change, { capture: true });
    states.set(input, state);
}

export function detach(input) {
    const state = states.get(input);
    if (!state) return;
    input.removeEventListener('beforeinput', state.before);
    input.removeEventListener('input', state.change, { capture: true });
    states.delete(input);
}
