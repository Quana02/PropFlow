# FE-08 and FE-09 Documentation Gap Analysis

## Review basis

The review compared the complete contents, tables, notes, workflows, boundaries, business rules, acceptance criteria, headers, and footers of the following documents:

- FE-01 Actor Responsibility and Major Feature Baseline
- FE-04 Building Facility and Equipment Management
- FE-04 Current Condominium Deployment revision
- FE-15 System Administration and Role-Based Access Control
- FE-08 Fee and Invoice Management
- FE-09 Payment and Debt Management

FE-01, FE-04, and FE-15 are Report 1 feature-review documents. They do not contain screen-item catalogs, message catalogs, API payload definitions, physical database-column specifications, Japanese localization rules, footnotes, appendixes, or revision-history tables. FE-08 and FE-09 therefore retain the same specification level and explicitly defer those artifacts rather than inventing details.

## Issues and resolutions

| ID | Location | Difference found | Root cause | Resolution | Business requirement impact |
|---|---|---|---|---|---|
| GAP-01 | FE-08, Sub-feature Summary and Report 1 Ready Version | The summary defined FE-08.1 through FE-08.10, while the final section reassigned FE-08.9 and FE-08.10 and introduced FE-08.11 and FE-08.12. | The actor-specific Report 1 wording evolved without updating the main catalog. | Established one 12-item catalog and reused the same IDs, names, actors, and meanings throughout the document. | None. Existing Accountant, Resident, and Manager capabilities were retained. |
| GAP-02 | FE-08 and FE-09, document structure | Section order and headings differed from FE-04 and FE-15. | The finance documents were drafted independently. | Standardized both documents to purpose and scope, actors, concepts, final sub-features, detailed review, domain relationships, authorization, actor matrix, workflows, boundaries, rules, acceptance criteria, detailed-design coverage, Report 1 wording, and final review result. | None. |
| GAP-03 | FE-08, actor coverage | The document had no actor-by-sub-feature matrix. | Actor responsibilities were present only in prose and the summary table. | Added a matrix covering Resident, Staff, Accountant, Manager, and Admin for every FE-08 sub-feature. | None. |
| GAP-04 | FE-09, actor coverage | The document had no actor-by-sub-feature matrix. | Actor responsibilities were present only in prose and the sub-feature table. | Added a matrix covering Resident, Staff, Accountant, Manager, and Admin for every FE-09 sub-feature. | None. |
| GAP-05 | FE-08 and FE-09, authentication and authorization | Authentication, fixed RBAC, and business-scope checks were described inconsistently or only indirectly. | The documents predated the corrected FE-15 fixed-RBAC baseline. | Added the common FE-01 authentication to FE-15 fixed authorization to feature business-scope sequence, including server-side enforcement. | None. |
| GAP-06 | FE-08 and FE-09, current-property scope | The documents could be read as supporting selectable buildings or cross-condominium filters. | They predated the FE-04 current-condominium deployment revision. | Added the rule that Finance does not expose a Building selector or cross-condominium filtering in the baseline. | None. It applies the approved deployment model. |
| GAP-07 | FE-08, feature boundaries | FE-01 authentication was not listed, and FE-15 was described only as roles and permissions. | Boundary coverage was incomplete. | Added explicit FE-01 and corrected FE-15 boundaries, while preserving FE-02, FE-03, FE-09, FE-13, and FE-14 ownership. | None. |
| GAP-08 | FE-09, feature boundaries | FE-01 and FE-15 were absent. | Boundary coverage was incomplete. | Added explicit authentication, internal-account administration, fixed authorization, and business-scope boundaries. | None. |
| GAP-09 | FE-08 and FE-09, naming | Capitalization and naming varied between “Sub-feature,” “Sub-Feature,” slash-separated labels, and ampersand-separated labels. | No shared editorial pass had been applied. | Standardized headings and table labels while retaining official feature names and IDs. | None. |
| GAP-10 | FE-08 and FE-09, workflows | Workflow presentation and traceability varied; FE-08 had no numbered workflow section. | The documents used different initial templates. | Added numbered workflows with explicit feature handoffs and actor authorization steps. | None. |
| GAP-11 | FE-09, payment update behavior | The document stated that only confirmed payments reduce balances but did not state when the user interface may show the authoritative result. | UI timing was outside the original Report 1 wording. | Clarified that the UI remains processing until backend confirmation and that pending or rejected payments never reduce balances. | None. This restates the existing confirmed-payment rule. |
| GAP-12 | FE-08 and FE-09, search and sort conditions | Potential search criteria were mentioned, but no default sort rule or contract status was stated. | Detailed UI and API design had not been approved. | Retained server-side approved criteria and stated that no default sort order is fixed at Report 1 level. | None. |
| GAP-13 | FE-08 and FE-09, screen/API/message/database coverage | The checklist categories could be mistaken for missing Report 1 requirements. | The reference documents do not specify these detailed artifacts. | Added a Detailed Specification Coverage section marking screen items, validation messages, message IDs, API payloads, DB columns, localization, and default sorting as deferred to approved detailed design. | None. No technical contract was invented. |
| GAP-14 | FE-08 and FE-09, final status | Neither document used the FE-04 or FE-15 final review-result pattern consistently. | Template divergence. | Added a final review result that records the corrected baseline and ownership boundary. | None. |

## Checklist disposition

| Checklist area | Result |
|---|---|
| Screen title and label naming | Deferred because the reference documents do not define screen catalogs. Feature and action names are standardized. |
| Section order and description format | Corrected in both documents. |
| Input and output item format | Deferred to detailed screen and API specifications. |
| Table layout and terminology | Standardized across FE-08 and FE-09. |
| Japanese wording | Not applicable. All reviewed documents are English and contain no Japanese language standard. |
| Button names | Deferred to detailed screen specifications. |
| Error messages and message IDs | Deferred to the approved message catalog. |
| Validation format | Business validation is retained in business rules and acceptance criteria; field-level validation is deferred. |
| Required and optional notation | Deferred to screen and API field catalogs. |
| API requests and responses | Deferred to approved typed and versioned contracts. |
| Database and column naming | Deferred to approved data design; domain ownership is stated. |
| Authentication and authorization | Corrected to FE-01 authentication, FE-15 fixed authorization, and feature-owned business-scope checks. |
| Navigation, transitions, processing, and sequence | Business workflows are standardized; screen-level transitions are deferred. |
| Initial display, search, and sort | Search ownership is stated; screen defaults and sort order are deferred. |
| Registration and update conditions | Financial create/update conditions remain in sub-features and business rules. |
| Exception handling | Business exceptions remain explicit; transport and message behavior are deferred to API and UI specifications. |
| Notes, footnotes, appendixes, and references | No footnotes, endnotes, comments, or appendixes exist in the reviewed DOCX files. Scope and boundary notes are incorporated into numbered sections. |
| Revision history | None of the reference documents contains a revision-history table, so no new incompatible format was introduced. |
