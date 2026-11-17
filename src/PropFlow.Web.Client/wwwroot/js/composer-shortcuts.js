// Delegation covers composers opened later in dialogs or after navigation.
// This listener belongs to the document lifetime and only handles opted-in textareas.
document.addEventListener('keydown', event => {
    const input = event.target;
    if (!(input instanceof HTMLTextAreaElement) || !input.hasAttribute('data-enter-submit') ||
        event.key !== 'Enter' || event.isComposing || event.keyCode === 229 ||
        event.altKey || event.metaKey || !input.form) return;

    event.preventDefault();
    if (event.repeat || input.disabled || input.readOnly || input.matches(':disabled')) return;

    if (event.ctrlKey) {
        const start = input.selectionStart, end = input.selectionEnd;
        if (input.maxLength >= 0 && input.value.length - (end - start) + 1 > input.maxLength) return;
        input.setRangeText('\n', start, end, 'end');
        // Keep Blazor binding, live manager drafts and AI review invalidation in sync.
        input.dispatchEvent(new Event('input', { bubbles: true }));
        return;
    }

    const submit = input.form.querySelector('button[type="submit"], input[type="submit"]');
    if (!input.value.trim() || !submit || submit.matches(':disabled')) return;
    input.form.requestSubmit(submit);
});
