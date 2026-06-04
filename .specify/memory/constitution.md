<!--
SYNC IMPACT REPORT
==================
Version change: (uninitialized template) → 1.0.0
Rationale: Initial ratification of the Data API builder (DAB) constitution,
derived from .github/copilot-instructions.md and the repository structure.

Modified principles: N/A (initial adoption)
Added sections:
  - Core Principles (7 principles)
  - Additional Constraints (Security, API Surface, Tooling)
  - Development Workflow & Quality Gates
  - Governance
Removed sections: None

Templates requiring updates:
  - ✅ .specify/templates/plan-template.md — "Constitution Check" section is
       a placeholder that references this file; no edit required (gates will
       be populated by /speckit.plan against the principles defined here).
  - ✅ .specify/templates/spec-template.md — no constitution-specific
       mandatory sections introduced; no edit required.
  - ✅ .specify/templates/tasks-template.md — task categorization unchanged
       (test tasks remain optional per template, but Principle II requires
       them for any data-access / resolver / engine behavior change; plan
       authors must enforce this when generating tasks).
  - ✅ .github/copilot-instructions.md — already aligned (source of these
       principles).
  - ⚠ docs/readme.md — no action required; principles are not duplicated
       there. Re-evaluate if contributor-facing docs grow.

Deferred items / TODOs:
  - TODO(RATIFICATION_DATE): Recorded as 2026-06-04 (date of this initial
       drafting). If the project wishes to backdate to original project
       inception, amend in a PATCH bump.
-->

# Data API Builder Constitution

## Core Principles

### I. Multi-Engine Parity (NON-NEGOTIABLE)

Any feature that touches data access MUST explicitly declare the set of
supported database engines from `{MsSql, PostgreSql, MySql, CosmosDb_NoSql,
DwSql}` in its spec, and MUST add integration tests under the matching
`TestCategory` in `src/Service.Tests` for every supported engine before
merge. Engines deliberately excluded from a feature MUST be called out in
the spec with a reason; silent omission is a defect.

**Rationale**: DAB's value proposition is uniform behavior across
heterogeneous backends. Drift between engines is the most common source of
user-reported regressions.

### II. Integration-Test-First for Resolver & Engine Changes (NON-NEGOTIABLE)

Every behavior change to resolvers, query/mutation engines, GraphQL
schema generation, or REST request handling MUST be accompanied by
integration tests in `src/Service.Tests` under the correct `TestCategory`
(`MsSql`, `PostgreSql`, `MySql`, `CosmosDb_NoSql`, `DwSql`) prior to
merge. Unit tests alone are insufficient for these layers — generated SQL,
parameter binding, and end-to-end serialization MUST be exercised against
a real engine.

**Rationale**: DAB is fundamentally a translation layer; correctness is
only observable when the full path executes against a live database.

### III. REST + GraphQL Parity

New entity- or column-level capabilities (new scalar types, filtering
operators, pagination behaviors, permission semantics, etc.) MUST be
exposed in both the REST and GraphQL surfaces unless the feature spec
explicitly scopes one of them out with a documented reason. Tests MUST
cover both surfaces when both are in scope.

**Rationale**: Users choose one API or the other per workload; capability
gaps between the two break the "same data, two protocols" guarantee.

### IV. Config Schema Discipline

Any change to the shape of runtime configuration (new fields, renamed
fields, changed defaults, new enum values) MUST be accompanied by a
matching update to `schemas/dab.draft.schema.json`, and `dab validate`
MUST continue to pass against the updated `src/Service.Tests/dab-config.*`
fixtures. Breaking config changes require a MAJOR version note in the
release; additive changes require at minimum a MINOR note.

**Rationale**: The JSON schema is the contract consumed by editors, CI
validation, and downstream tooling. Drift between code and schema silently
breaks user workflows.

### V. No Secrets in Source

Connection strings, account keys, JWT secrets, and any other credentials
MUST be referenced via `@env('VAR_NAME')` in all committed config files.
`.env` files, literal connection strings, and tokens MUST NEVER be
committed. PRs introducing such material MUST be rejected and the secret
treated as compromised (rotate before merge).

**Rationale**: Public OSS repo; leaked credentials are exfiltrated within
minutes and cannot be recalled.

### VI. Formatting & Style Compliance

`dotnet format src/Azure.DataApiBuilder.sln --verify-no-changes` MUST pass
on every PR. The `.editorconfig` under `src/` is authoritative for C#
style. Generated SQL captured in tests MUST be formatted per the
engine-specific rules in `.github/copilot-instructions.md`:
PostgreSQL via sqlformat.org (drop unnecessary double quotes); SQL Server
and MySQL via poorsql.com with trailing commas enabled and a 4-space
indent string (MySQL additionally capped at 100 chars wide).

**Rationale**: Mechanical, reviewer-friendly diffs; stable string
comparisons in test assertions across contributors and platforms.

### VII. Minimal-Surface Changes

Features MUST NOT refactor unrelated code, add abstractions for a single
caller, or introduce new helpers when an existing pattern fits. New
functionality SHOULD mirror established flows — e.g., a new scalar type
MUST traverse `Config → Resolvers → GraphQL SDL` the same way
`varchar(max)` does today, rather than introducing a parallel pipeline.
Cross-cutting refactors require their own spec.

**Rationale**: Keeps diffs reviewable, limits regression blast radius,
and preserves the codebase's learnable shape.

## Additional Constraints

**Security**: All authn/authz behavior changes MUST preserve the supported
provider matrix (AppService, EasyAuth, StaticWebApps, JWT) and role-based
permission semantics declared in config. Row-level-security paths that
rely on `set-session-context` for SQL Server MUST be covered by
integration tests under `TestCategory=MsSql`. Code MUST be reviewed
against the OWASP Top 10; insecure patterns block merge.

**API Surface**: REST endpoints follow the Microsoft REST API Guidelines
under base path `/api` (configurable). GraphQL is served under `/graphql`
(configurable) with introspection enabled only in development mode. MCP
tools are served under `/mcp` (configurable). The `/health` endpoint MUST
remain available and unauthenticated by default.

**Tooling**: .NET 8 SDK is the build target (`global.json`). The
`dab` CLI commands (`init`, `add`, `start`, `validate`) are part of the
public contract — flag or argument renames are breaking changes.

## Development Workflow & Quality Gates

1. **Branching**: Feature work happens on a branch; direct commits to the
   default branch are not allowed.
2. **Commits**: SHOULD be signed (GPG or SSH) per
   `.github/copilot-instructions.md`. Secret scanning MUST be clean.
3. **Local validation before PR**:
   - `dotnet build src/Azure.DataApiBuilder.sln`
   - `dotnet format src/Azure.DataApiBuilder.sln --verify-no-changes`
   - `dotnet test --filter "TestCategory=<engine>"` for every engine the
     change targets (Principle I).
4. **PR review**: Reviewers MUST verify Principles I–VII explicitly when
   the diff touches data access, config, or API surfaces. Any violation
   MUST be either fixed or recorded as a justified exception in the PR
   description and the corresponding feature spec.
5. **CI**: Format check, build, and per-engine integration test
   categories are required checks. A PR that disables or skips a
   previously-passing test MUST justify the skip in the PR body.

## Governance

This constitution supersedes ad hoc conventions. When in conflict with
informal practice, the constitution wins; update the constitution if the
practice is genuinely better.

**Amendment procedure**:

- **PATCH bumps** (typos, clarifications, non-semantic rewording,
  tightening of already-implied rules): MAY be made directly in a PR
  titled `docs: constitution vX.Y.(Z+1) — <summary>`; one maintainer
  approval required. No spec/plan needed.
- **MINOR bumps** (new principle, new constraint, materially expanded
  guidance, new mandatory section): REQUIRE a Spec Kit spec describing
  the motivation and impact, a plan if templates or tooling must change,
  and two maintainer approvals.
- **MAJOR bumps** (removal of a principle, redefinition that invalidates
  prior compliance, breaking governance change): REQUIRE a spec, a plan,
  a migration note for in-flight work, and consensus from the maintainer
  set.

**Versioning policy** follows semantic versioning of the constitution
itself, independent of the DAB product version. Amendments MUST update
`Last Amended` and, on every change, emit a Sync Impact Report at the top
of this file (HTML comment) listing affected templates and follow-ups.

**Compliance review**: Maintainers SHOULD audit recently merged PRs
against this constitution at least once per release cycle and file
remediation issues for any drift.

**Runtime development guidance**: Day-to-day contributor guidance lives
in `.github/copilot-instructions.md`. When that file and this
constitution disagree, the constitution wins and the instructions file
MUST be updated to match.

**Version**: 1.0.0 | **Ratified**: 2026-06-04 | **Last Amended**: 2026-06-04
