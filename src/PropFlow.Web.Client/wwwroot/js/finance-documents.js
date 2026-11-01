export function download(bytes, contentType, filename) {
    const url = URL.createObjectURL(new Blob([bytes], {type: contentType}));
    const link = document.createElement('a'); link.href = url; link.download = filename;
    document.body.appendChild(link); link.click(); link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 60000);
}
export function print(bytes) {
    const frame = document.createElement('iframe');
    frame.style.position = 'fixed'; frame.style.width = '0'; frame.style.height = '0'; frame.style.border = '0';
    frame.srcdoc = new TextDecoder().decode(bytes);
    frame.onload = () => { frame.contentWindow.focus(); frame.contentWindow.print(); };
    document.body.appendChild(frame);
    setTimeout(() => frame.remove(), 120000);
}
