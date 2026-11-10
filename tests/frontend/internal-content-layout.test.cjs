const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const client = path.resolve(__dirname, '../../src/PropFlow.Web.Client');
const read = file => fs.readFileSync(path.join(client, file), 'utf8');

test('internal role layouts delegate the content gutter to AppShell', () => {
    for (const layout of ['MainLayout', 'AdminLayout', 'AccountantLayout']) {
        assert.match(read(`Layout/${layout}.razor`), /<AppShell>[\s\S]*@Body[\s\S]*<\/AppShell>/);
    }

    assert.match(read('Layout/Shared/AppShell.razor'), /class="app-page-body">@ChildContent/);
    const shellStyles = read('Layout/Shared/AppShell.razor.css');
    assert.match(shellStyles, /padding-inline: var\(--content-padding-inline\)\s+calc\(var\(--content-padding-inline\) \+ var\(--navigation-rail-reserved-width\)\)/);
    assert.match(shellStyles, /:has\(\.app-floating-nav\[data-dock-side="left"\]\) \.app-page-body/);

    for (const layout of ['Layout/LandingLayout', 'Features/Authentication/Components/AuthLayout',
        'Features/Resident/Portal/Components/ResidentPortalLayout']) {
        assert.doesNotMatch(read(`${layout}.razor`), /AppShell|app-page-body|internal-workspace/);
    }
});

test('internal page roots use the available width and leave horizontal padding to AppShell', () => {
    const pages = {
        'Apartment/Pages/ApartmentManagement': 'apartment-page',
        'Apartment/Pages/ApartmentDetail': 'detail-page',
        'Resident/Management/Pages/ResidentDetail': 'detail-page',
        'Facility/Pages/CurrentBuildingDetails': 'building-details',
        'Facility/Pages/FacilityList': 'fe04-page',
        'Facility/Pages/EquipmentList': 'fe04-page',
        'AiChatbot/Pages/KnowledgeDocuments': 'knowledge-workspace',
        'Maintenance/Pages/MaintenanceTaskDetail': 'maintenance-task-detail-page',
        'ServiceRequests/Pages/ManagerServiceRequestList': 'manager-request-list',
        'ServiceRequests/Pages/ManagerServiceRequestDetail': 'manager-request-page',
        'ServiceRequests/Pages/ManagerServiceRequestPropertyContext': 'context-page',
        'ServiceRequests/Pages/StaffServiceRequestWork': 'staff-work-page',
        'ServiceRequests/Pages/StaffServiceRequestList': 'staff-request-page',
    };
    const horizontalConstraint = /(?:^|;)\s*(?:max-(?:width|inline-size)\s*:|width\s*:\s*min\(|margin(?:-inline)?\s*:[^;]*\bauto\b|padding(?:-inline|-left|-right)\s*:)/;

    for (const [file, root] of Object.entries(pages)) {
        const css = read(`Features/${file}.razor.css`);
        const rules = [...css.matchAll(new RegExp(`(?:^|[}\\s])\\.${root}\\s*\\{([^}]*)\\}`, 'g'))];
        assert.ok(rules.length, file);
        for (const [, declarations] of rules) {
            assert.doesNotMatch(declarations, horizontalConstraint, file);
            for (const [, padding] of declarations.matchAll(/(?:^|;)\s*padding\s*:\s*([^;]+)/g)) {
                const values = padding.trim().split(/\s+/);
                assert.equal(values[1] ?? values[0], '0', file);
                assert.equal(values[3] ?? values[1] ?? values[0], '0', file);
            }
        }
    }
});

test('shared workspaces retain resident widths and opt in only for internal variants', () => {
    const variants = [
        ['Complaints/Components/ComplaintWorkspace', 'complaint-workspace', /Role is "MANAGER" or "STAFF"/, /width:\s*min\(100%,96rem\)/],
        ['Communication/Components/AnnouncementWorkspace', 'announcement-workspace', /Manager && !Embedded/, /width:\s*min\(100%,96rem\)/],
        ['Communication/SupportChat/SupportChatWorkspace', 'support-workspace', /ForManager/, /max-width:\s*1280px/],
    ];

    for (const [file, root, internalCondition, residentWidth] of variants) {
        const markup = read(`Features/${file}.razor`);
        const modifier = markup.match(/@\(([^\n]*?) \? "internal-workspace" : null\)/);
        assert.match(modifier?.[1] ?? '', internalCondition, file);
        const styles = read(`Features/${file}.razor.css`);
        const residentRule = styles.match(new RegExp(`\\.${root}:not\\(\\.internal-workspace\\)\\s*\\{([^}]*)\\}`));
        assert.match(residentRule?.[1] ?? '', residentWidth, file);
        assert.match(residentRule?.[1] ?? '', /margin:\s*0 auto/, file);
        const commonRule = styles.match(new RegExp(`\\.${root}\\s*\\{([^}]*)\\}`));
        assert.doesNotMatch(commonRule?.[1] ?? '', /(?:max-)?width\s*:|margin\s*:/, file);
    }
});
