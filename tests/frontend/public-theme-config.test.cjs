const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const app = read('src/PropFlow.Web/Components/App.razor');

test('FE-04.1 extends the actual shell config without erasing landing tokens, including repeated layout loads', () => {
    const context = { tailwind: {} };
    context.window = context;
    vm.createContext(context);
    const config = app.match(/<script id="tailwind-config">([\s\S]*?)<\/script>/)[1];
    vm.runInContext(config, context);
    const before = JSON.parse(JSON.stringify(context.tailwind.config.theme.extend));
    const extension = read('src/PropFlow.Web.Client/wwwroot/js/fe041-tailwind.js');
    for (let load = 0; load < 2; load++) {
        vm.runInContext(extension, context);
        const after = context.tailwind.config.theme.extend;
        for (const [group, tokens] of Object.entries(before))
            for (const [key, value] of Object.entries(tokens))
                assert.deepEqual(JSON.parse(JSON.stringify(after[group]?.[key] ?? null)), value, `${group}.${key}`);
        assert.equal(after.spacing['margin-edge'], '40px');
        assert.equal(after.fontSize['display-lg'][0], '46px');
        assert.equal(after.fontFamily.sans[0], 'Inter');
        assert.equal(context.tailwind.config.darkMode, 'class');
    }
});

test('the shell loads the Tailwind CDN only once', () => {
    assert.doesNotMatch(read('src/PropFlow.Web.Client/Layout/MainLayout.razor'), /<script[^>]+(?:cdn.tailwindcss.com|fe041-tailwind.js)/);
    assert.equal((app.match(/<script src="https:\/\/cdn.tailwindcss.com/g) || []).length, 1);
});

test('auth form rules cannot override shared header buttons and links', () => {
    const css = read('src/PropFlow.Web.Client/Features/Authentication/Components/AuthLayout.razor.css');
    const selectors = css.replace(/\/\*[\s\S]*?\*\//g, '').match(/[^{}]+(?=\{)/g);
    for (const selector of selectors)
        for (const part of selector.split(','))
            assert.doesNotMatch(part.trim(), /^::deep (?:button|a)(?=[\s:.#]|$)/);
    const header = read('src/PropFlow.Web.Client/Layout/Shared/PublicHeader.razor');
    assert.match(header, /class="public-theme-toggle" onclick="toggleTheme\(\)"/);
    assert.match(read('src/PropFlow.Web.Client/Features/Authentication/Components/AuthLayout.razor'), /<PropFlow.Web.Client.Layout.Shared.PublicHeader/);
    assert.match(read('src/PropFlow.Web.Client/Pages/Home.razor'), /<PropFlow.Web.Client.Layout.Shared.PublicHeader/);
});
