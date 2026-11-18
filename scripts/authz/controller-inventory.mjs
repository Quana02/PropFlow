// Attribute-route composition mirrors MVC: action absolute templates replace controller prefixes.
export function controllerOperations(source, header, controller) {
  const prefixRoutes = [...header.matchAll(/\bRoute\("([^"]*)"\)/g)].map(match => match[1]);
  const prefixes = prefixRoutes.length ? prefixRoutes : [''];
  const rows = [];
  for (const match of source.matchAll(/(^[ \t]*(?:\[(?:(?:\[\]|[^"\]\r\n]|"(?:[^"\\]|\\.)*")+)\][ \t]*(?:\r?\n[ \t]*)?)+)[ \t]*public\s+(?:async\s+)?[^\r\n]*?\s+(\w+)\(/gm)) {
    const [, attributes, action] = match;
    const actionRoutes = [...attributes.matchAll(/\bRoute\("([^"]*)"\)/g)].map(route => route[1]);
    for (const verb of attributes.matchAll(/\bHttp(Get|Post|Put|Patch|Delete|Head|Options)(?:\("([^"]*)"\))?/g)) {
      const suffixes = verb[2] !== undefined ? [verb[2]] : actionRoutes.length ? actionRoutes : [''];
      for (const suffix of suffixes) {
        const absolute = suffix.startsWith('/') || suffix.startsWith('~/');
        for (const prefix of absolute ? [''] : prefixes) {
          if (!prefix && !suffix && !prefixRoutes.length) continue; // conventional routing is outside this attribute-route inventory
          const path = '/' + (absolute ? suffix.replace(/^(?:~\/|\/)/, '') : [prefix, suffix].filter(Boolean).join('/'))
            .replaceAll('[controller]', controller.slice(0, -10)).replaceAll('[action]', action);
          rows.push({ path, method: verb[1].toUpperCase(), action, attributes });
        }
      }
    }
  }
  return [...new Map(rows.map(row => [row.method + ' ' + row.path, row])).values()];
}
