(() => {
    const key = "propflow-theme";
    const root = document.documentElement;
    const valid = theme => theme === "light" || theme === "dark";

    function apply(theme) {
        if (root.dataset.theme === theme) return;
        root.classList.toggle("dark", theme === "dark");
        root.classList.toggle("light", theme === "light");
        root.dataset.theme = theme;
        window.dispatchEvent(new Event("propflow:themechange"));
    }

    let saved;
    try { saved = localStorage.getItem(key); } catch { /* Storage is optional. */ }
    apply(valid(saved) ? saved : (matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light"));

    window.propFlowTheme = {
        toggle() {
            const next = root.dataset.theme === "dark" ? "light" : "dark";
            try { localStorage.setItem(key, next); } catch { /* Keep the current tab usable. */ }
            apply(next);
        }
    };

    window.addEventListener("storage", event => {
        if (event.key === key && valid(event.newValue)) apply(event.newValue);
    });
})();
