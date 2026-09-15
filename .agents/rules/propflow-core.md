# PropFlow Agent Core Rules

## Instruction Priority

For every PropFlow task, apply instructions in this order:

1. Explicit user instruction for the current task.
2. Approved PropFlow Feature / Use Case specifications.
3. Approved API contract, permission contract and ADRs.
4. PropFlow Backend / Frontend architecture and coding rules.
5. Security, authorization, database integrity and testing requirements.
6. Active task-specific Agent Skill.
7. Ponytail optimization principles.
8. Existing repository convention when not conflicting with the above.

A lower-priority instruction MUST NOT override a higher-priority requirement.

---

## Detailed Rule Sources

The canonical detailed rules are:

Backend:
docs/agent/rules/PropFlow_Backend_Rules.md

Frontend:
docs/agent/rules/PropFlow_Frontend_Rules.md

Do NOT load both documents in full for every task.

Before changing code:

1. Determine whether the task affects Backend, Frontend, or both.
2. Determine the FE/UC involved.
3. Open the corresponding canonical rule document.
4. Follow its internal task-routing table.
5. Read the core sections plus only the sections relevant to the task.
6. Inspect the existing implementation before introducing new code.

For cross-stack tasks, read the relevant sections of both documents and ensure
the API/OpenAPI contract remains compatible.

---

## Architecture Baseline

Technology & Framework:
- .NET 8 LTS (Target Framework `net8.0`)
- C# 12
- Stable dependencies compatible with .NET 8 (no silent framework upgrades)

Backend:
- ASP.NET Core Web API 8
- Modular Monolith
- Clean Architecture principles inside modules
- Vertical Slice by approved Use Case
- PostgreSQL
- schema-per-module persistence boundaries

Frontend:
- Blazor Web App (.NET 8)
- Feature-Based Architecture
- Global Interactive WebAssembly
- application shell/routes use prerender: false
- REST API integration through /api/v1/...


Do not change these architecture decisions without an approved ADR.

---

## Domain Ownership

Preserve existing PropFlow ownership boundaries.

Examples:

User Account != Resident != Apartment

Service Request != Complaint

Service Request != Maintenance Task

Maintenance Schedule != Maintenance Task

Invoice lifecycle != Payment lifecycle

FE-10 classification != FE-11 priority/recommendation

Notification delivery != ownership of source workflow

Reporting/Analytics != ownership of source business records

RBAC authorization != domain/business-scope authorization

Do not merge modules or ownership boundaries merely to reduce files or code.

---

## Matt Pocock Skills

Task-specific Matt Pocock skills may guide reasoning, testing, debugging,
domain modeling, research and code review.

They do not override PropFlow architecture or approved business requirements.

TDD-required tests are not unnecessary code.

Domain modeling must preserve approved PropFlow terminology and ownership.

Codebase design may introduce abstractions only when they support an actual
boundary or demonstrably reduce overall complexity.

Research must distinguish verified information from assumptions.

---

## Ponytail

Ponytail applies only after all higher-priority requirements are satisfied.

Use Ponytail to:

- reuse existing code;
- prefer standard/framework capabilities;
- avoid speculative abstractions;
- avoid unnecessary dependencies;
- make the smallest complete change.

Ponytail MUST NOT simplify away:

- architecture boundaries;
- domain ownership;
- authorization;
- permissions;
- business-scope checks;
- validation;
- PostgreSQL constraints;
- audit/history requirements;
- error handling;
- required tests;
- AI human-in-the-loop safeguards.

---

## No Scope Invention

Do not invent:

- Feature;
- Use Case;
- actor;
- role;
- permission;
- business status;
- API endpoint;
- request/response field;
- database invariant;
- AI authority;
- business workflow.

When required information is missing, report the missing contract or decision
instead of silently designing it.

---

## Completion

A task is not complete merely because code was generated.

Verify applicable:

- architecture boundaries;
- authorization and business scope;
- API contract;
- validation;
- database constraints/migrations;
- build;
- tests;
- no fake implementation;
- no dead UI;
- no hard-coded development configuration.

Report assumptions and unresolved decisions explicitly.

---

## Agent Language Preference

Always communicate with the user in Vietnamese, even if prompt instructions or queries are provided in English.

