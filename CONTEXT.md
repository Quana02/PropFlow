# PropFlow Domain Glossary

## UserAccount

An authentication identity with credentials, one assigned primary business role, and an account or access status. It is separate from business-domain records.

## Predefined Business Role

One of the five fixed roles: RESIDENT, STAFF, ACCOUNTANT, MANAGER, or ADMIN. A UserAccount has exactly one primary business role at a time.

## Fixed Permission Mapping

The application-defined, read-only relationship between a predefined role and its permissions. Changing a UserAccount's role automatically changes its effective permissions. Individual permission grants, revocations, custom roles, and mapping changes are not administrative functions.

## Internal User Account

A UserAccount provisioned for a member of the management organization with the MANAGER, STAFF, or ACCOUNTANT role. FE-15 administers these accounts.

## Current Building

The one condominium represented by a PropFlow deployment. All live business data in that deployment belongs to this condominium implicitly. Building is a shared property profile and timezone source, not a tenant boundary, selectable scope, user assignment, or dashboard filter. Supporting multiple condominiums means operating separate PropFlow deployments, not switching buildings inside one deployment.

## Resident

A resident business-domain record, separate from UserAccount. Resident Management belongs to FE-02; registration and self-account authentication functions belong to FE-01. Canonical identity is the normalized pair IdentityType + IdentityNumber; normalized non-null email is unique, while phone is required for new FE-02 onboarding but is not a globally unique Resident key.

## Residency

A time-bounded Resident-to-Apartment association owned by FE-02. It carries the resident's household context for that apartment and preserves that context as history. A Resident may have active residencies in multiple Apartments, but cannot have overlapping active residency rows for the same Apartment. Re-entry creates a new historical row.

## Resident Self Registration

An FE-01 account-onboarding flow that matches one ACTIVE, unlinked Resident by the same four normalized values: email, phone, identity type and identity number. Eligibility additionally requires an active residency or current ownership. Historical-only relationships do not qualify. Authentication uses Residents public Contracts and does not read Residents or Apartments DbContexts.

## Household Head

The single active Residency with the Household Head role for an apartment household. It is not a permanent property of a Resident record.

## Apartment Ownership

A time-bounded FE-03 relationship between an Apartment and an owner represented by a Resident business record. An Apartment may have multiple current owners. Ownership is distinct from residency and is retained as history when an ownership ends. An Apartment may be INACTIVE only when it has neither an active residency nor a current ownership (`EndDate == null`); an INACTIVE Apartment cannot receive a new current ownership.

## Residency Type

The lawful basis on which a Resident occupies an Apartment: OWNER_OCCUPIED, TENANT, or AUTHORIZED_OCCUPANT. It is independent of the household role.

## Household Role

The position within an active apartment household: HOUSEHOLD_HEAD or HOUSEHOLD_MEMBER. A household head is not necessarily the apartment owner, and TENANT is not a household role.

## Household Member Relationship

The family relationship of an active Household Member residency to its Household Head residency, such as spouse, child, parent, sibling, or other relative. It is distinct from the residency role.

## Business Scope Authorization

The domain-level check applied after fixed RBAC authorization. A role and fixed permission do not permit access outside the authorized building, assignment, ownership, residency, or other applicable business scope.
