import { mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
const root = 'artifacts/authz';
mkdirSync(root, { recursive: true });
const roles = ['ADMIN', 'MANAGER', 'STAFF', 'ACCOUNTANT', 'RESIDENT'];
const [admin, manager, staff, accountant, resident] = roles;
const policies = {
  'AssetPermissions.ViewPolicy': [manager, staff],
  'PropertyAssetsAuthorizationPolicies.View': [manager, staff],
  'PropertyAssetsAuthorizationPolicies.Manage': [manager],
  'MaintenanceAuthorizationPolicies.View': [manager, staff],
  'MaintenanceAuthorizationPolicies.Manage': [manager],
  'MaintenanceAuthorizationPolicies.Work': [staff],
  'ResidentsAuthorizationPolicies.Read': [manager, staff, accountant],
  'ResidentsAuthorizationPolicies.Manage': [manager],
  'ApartmentsAuthorizationPolicies.Read': [manager, staff, accountant],
  'ApartmentsAuthorizationPolicies.Manage': [manager],
  'ResidentAccountPolicies.Read': [manager],
  'ResidentAccountPolicies.RequestEmailChange': [manager],
  'KnowledgePolicies.Read': [admin, manager, staff],
  'KnowledgePolicies.Manage': [admin, manager],
  'ResidentChatPolicy.Use': [resident],
  'SupportChatPolicies.Use': [manager, resident],
  'SupportChatPolicies.Manager': [manager],
  'SupportChatPolicies.Resident': [resident],
  'AdministrationAuthorizationPolicies.ManageInternalAccounts': [admin],
  'AdministrationAuthorizationPolicies.ViewSystemOverview': [admin],
  'AdministrationAuthorizationPolicies.ViewAdministrationActivity': [admin],
  'PaymentSettingsAuthorizationPolicies.Manage': [admin],
  'ServiceRequestsAuthorizationPolicies.Manage': [manager],
  'ServiceRequestsAuthorizationPolicies.WorkAssigned': [staff],
  'ServiceRequestsAuthorizationPolicies.SubmitAsResident': [resident],
  'finance.manage': [accountant], 'billing.configure': [manager], 'billing.read': [manager, accountant],
  'complaints.access': [manager, staff, resident], 'complaints.manage': [manager], 'complaints.work': [staff],
  'announcements.access': [manager, resident], 'announcements.publish': [manager],
};
function walk(directory) {
  return readdirSync(directory, { withFileTypes: true }).flatMap(entry => {
    const path = join(directory, entry.name).replaceAll('\\', '/');
    return entry.isDirectory() ? (['bin', 'obj'].includes(entry.name) ? [] : walk(path)) : [path];
  });
}
function auth(source, implicitPublic = false) {
  const checks = [...source.matchAll(/\b(?:Microsoft\.AspNetCore\.Authorization\.)?Authorize(?:\(([^)]+)\))?(?=[\s,\]])/g)]
    .map(match => match[1] ?? 'Authenticated');
  const isPublic = /\bAllowAnonymous\s*(?=[,\]])/.test(source) || (implicitPublic && !checks.length);
  if (isPublic) return { allow: ['Ẩn danh', ...roles], guard: source.includes('AllowAnonymous') ? 'AllowAnonymous' : 'Route công khai' };
  return { allow: checks.reduce((allowed, check) => {
    const role = check.match(/Roles\s*=\s*(?:"([^"]+)"|(\S+?)(?:,|$))/);
    const policy = check.match(/Policy\s*=\s*(?:"([^"]+)"|(\S+?)(?:,|$))/);
    if (role) {
      const choices = role[1]?.split(',') ?? (role[2] === 'ResidentAccountPolicies.OwnEmailRoles' ? roles.slice(0, 4) :
        role[2] === 'SystemRoleCodes.Admin' ? [admin] : []);
      if (!choices.length) throw Error('Unknown role ' + role[2]);
      allowed = allowed.filter(r => choices.includes(r));
    }
    if (policy) {
      const id = (policy[1] ?? policy[2]).split('.').slice(-2).join('.');
      if (!policies[id]) throw Error('Unknown policy ' + id);
      allowed = allowed.filter(r => policies[id].includes(r));
    }
    return allowed;
  }, [...roles]), guard: checks.join(' + ') || 'MISSING AUTHORIZATION' };
}
const modules = { Authentication:'01', Residents:'02', Resident:'02', Apartments:'03', Apartment:'03', PropertyAssets:'04',
  Facility:'04', ServiceRequests:'05', Complaints:'06', Maintenance:'07', Billing:'08', Payments:'09',
  AiChatbot:'12', Communication:'13', Reporting:'14', Administration:'15' };
function feature(file, path) {
  if (path.includes('/classification')) return 'FE-10';
  if (path.includes('/recommendation')) return 'FE-11';
  if (path.includes('/resident/weather') || path.includes('/buildings')) return 'FE-04';
  if (file.includes('/Portal/')) {
    if (/service-requests/.test(path)) return 'FE-05';
    if (/billing/.test(path)) return 'FE-08/09';
    if (/apartment/.test(path)) return 'FE-03';
    if (path === '/resident') return 'FE-14';
  }
  if (file.includes('/Finance/')) return /fee|invoice/.test(path) ? 'FE-08' : 'FE-09';
  if (path.includes('/administration-overview')) return 'FE-14';
  if (file.includes('/Controllers/ResidentServiceRequestDraft')) return 'FE-05/10';
  const area = file.match(/(?:Modules|Features)\/([^/]+)/)?.[1];
  return modules[area] ? 'FE-' + modules[area] : 'Thông tin công khai / kỹ thuật';
}
const files = walk('src');
const routes = [];
for (const file of files.filter(path => path.endsWith('.razor'))) {
  const source = readFileSync(file, 'utf8');
  for (const match of source.matchAll(/^\uFEFF?@page\s+"([^"]+)"/gm)) {
    const path = match[1];
    const meta = auth(source.split(/\r?\n/).filter(line => line.startsWith('@attribute')).join('\n'), true);
    const client = file.includes('.Web.Client/');
    const type = !client || path === '/forbidden' ? 'INTERNAL/TECHNICAL' : meta.allow.includes('Ẩn danh') ? 'PUBLIC'
      : path.includes('{') ? 'RESOURCE RESTRICTED' : meta.guard === 'Authenticated' ? 'AUTHENTICATED SHARED' : 'ROLE RESTRICTED';
    routes.push({ path, file, feature: feature(file, path), type, ...meta });
  }
}
const endpoints = [];
const controllers = new Map();
for (const file of files.filter(path => /Controller.*\.cs$/.test(path))) {
  const source = readFileSync(file, 'utf8');
  const match = source.match(/\bclass\s+(\w+Controller)\b/);
  if (!match) continue;
  const header = source.slice(0, match.index);
  if (/\bRoute\("/.test(header)) controllers.set(match[1], header);
}
for (const file of files.filter(path => /Controller.*\.cs$/.test(path))) {
  const source = readFileSync(file, 'utf8');
  const controller = source.match(/\bclass\s+(\w+Controller)\b/);
  if (!controller) continue;
  const header = controllers.get(controller[1]) ?? source.slice(0, controller.index);
  const prefixes = [...header.matchAll(/\bRoute\("([^"]+)"\)/g)].map(match => match[1]);
  if (!prefixes.length) continue;
  const matchedVerbs = new Set();
  // Attribute areas end before the action signature, even when the declarations share a line.
  for (const match of source.matchAll(/(^[ \t]*(?:\[(?:(?:[^"\]\r\n]|"(?:[^"\\]|\\.)*")+)\][ \t]*(?:\r?\n[ \t]*)?)+)[ \t]*public\s+(?:async\s+)?[^\r\n]*?\s+(\w+)\(/gm)) {
    const [full, attributes, action] = match;
    for (const verb of attributes.matchAll(/\bHttp(Get|Post|Put|Patch|Delete|Head|Options)(?:\("([^"]*)"\))?/g)) {
      matchedVerbs.add(match.index + verb.index);
      const suffix = verb[2] ?? '';
      const paths = suffix.startsWith('/') ? [suffix] : prefixes.map(prefix => ['/' + prefix, suffix].filter(Boolean).join('/'));
      for (const rawPath of paths) {
        const path = rawPath.replace('[controller]', controller[1].slice(0, -10));
        const meta = auth(header + '\n' + attributes);
        endpoints.push({ path, method: verb[1].toUpperCase(), feature: feature(file, path), file, action, ...meta });
      }
    }
  }
  for (const verb of source.matchAll(/\bHttp(Get|Post|Put|Patch|Delete|Head|Options)\b/g)) {
    if (!matchedVerbs.has(verb.index)) console.log('UNPARSED', file, source.slice(verb.index, verb.index + 95).split(/\r?\n/)[0]);
  }
}
routes.sort((a, b) => a.path.localeCompare(b.path));
endpoints.sort((a, b) => a.path.localeCompare(b.path) || a.method.localeCompare(b.method));
console.log('Routes', routes.length, 'API actions and aliases', endpoints.length);
console.log('Unclassified HTTP guards', endpoints.filter(e => e.guard === 'MISSING AUTHORIZATION'));
writeFileSync(join(root, 'routes.json'), JSON.stringify(routes, null, 2));
writeFileSync(join(root, 'endpoints.json'), JSON.stringify(endpoints, null, 2));

const expected = JSON.parse(readFileSync(new URL('./expected-authorization.json', import.meta.url)));
function verify(items, baseline, key) {
  const expectedByKey = new Map(baseline.map(row => [key(row), row]));
  const actualKeys = new Set();
  for (const actual of items) {
    const id = key(actual);
    const rule = expectedByKey.get(id);
    if (actualKeys.has(id)) throw Error('Trùng mục kiểm toán ' + id);
    actualKeys.add(id);
    if (!rule) throw Error('Chưa phân loại quyền cho ' + id);
    if (rule.guard !== actual.guard || rule.allow.slice().sort().join(',') !== actual.allow.slice().sort().join(','))
      throw Error('Khai báo quyền khác ma trận đã duyệt tại ' + id);
  }
  const removed = baseline.filter(row => !actualKeys.has(key(row)));
  if (removed.length) throw Error('Thiếu mục trong inventory ' + removed.map(key).join(', '));
}
verify(routes, expected.routes, row => row.path);
verify(endpoints, expected.endpoints, row => row.method + ' ' + row.path);
