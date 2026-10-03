export function focusFirstInvalid() {
    requestAnimationFrame(() => {
        const dialog = document.querySelector('.resident-modal');
        if (!dialog) return;
        const message = dialog.querySelector('.validation-message');
        if (!message) return;
        const control = message.closest('label')?.querySelector('input, textarea, button');
        (control ?? message).scrollIntoView({ behavior: 'smooth', block: 'center' });
        control?.focus({ preventScroll: true });
    });
}
