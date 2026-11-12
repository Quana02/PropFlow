// Extend the shared shell theme; never replace landing typography/spacing tokens.
// MainLayout may load this script again after navigation, so keep it idempotent.
(() => {
    const config = window.tailwind.config || {};
    const theme = config.theme || {};
    const extend = theme.extend || {};
    window.tailwind.config = {
        ...config,
        darkMode: config.darkMode || "class",
        theme: {
            ...theme,
            extend: {
                ...extend,
                fontFamily: {
                    sans: ['Inter', 'sans-serif'],
                    display: ['Space Grotesk', 'sans-serif'],
                    ...extend.fontFamily
                },
                colors: {
                    primary: '#0284c7',
                    'primary-dark': '#0369a1',
                    accent: '#10b981',
                    surface: '#ffffff',
                    body: '#0f172a',
                    muted: '#64748b',
                    ...extend.colors
                }
            }
        }
    };
})();
