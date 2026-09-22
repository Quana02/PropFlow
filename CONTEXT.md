# PropFlow Domain Glossary

## UserAccount

An authentication identity with credentials, one assigned primary business role, and an account or access status. It is separate from business-domain records.

## Predefined Business Role

One of the five fixed roles: RESIDENT, STAFF, ACCOUNTANT, MANAGER, or ADMIN. A UserAccount has exactly one primary business role at a time.

## Fixed Permission Mapping

The application-defined, read-only relationship between a predefined role and its permissions. Changing a UserAccount's role automatically changes its effective permissions. Individual permission grants, revocations, custom roles, and mapping changes are not administrative functions.

## Internal User Account

A UserAccount provisioned for a member of the management organization with the MANAGER, STAFF, or ACCOUNTANT role. FE-15 administers these accounts.

## Resident

A resident business-domain record, separate from UserAccount. Resident Management belongs to FE-02; registration and self-account authentication functions belong to FE-01.

## Business Scope Authorization

The domain-level check applied after fixed RBAC authorization. A role and fixed permission do not permit access outside the authorized building, assignment, ownership, residency, or other applicable business scope.
