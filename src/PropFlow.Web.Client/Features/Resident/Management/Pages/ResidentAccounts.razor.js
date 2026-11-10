export function open(dialog) {
    dialog.oncancel = event => {
        event.preventDefault();
        dialog.querySelector('[data-dialog-close]').click();
    };
    dialog.showModal();
}

export function close(dialog) {
    dialog.close();
}
