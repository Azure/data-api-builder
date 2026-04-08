# PR Review Assignment Bot

Automated GitHub Actions workflow that assigns reviewers to pull requests using weighted load balancing.

**Workflow file:** [workflows/auto-assign-reviewers.yml](workflows/auto-assign-reviewers.yml)
**Branch:** `Usr/sogh/prreviewbot` (PR [#3326](https://github.com/Azure/data-api-builder/pull/3326))
**Status:** Dry-run mode. Ready for team testing.

---

## How It Works

### Trigger Events

| Event | When it fires |
|---|---|
| `pull_request_target: opened` | A new PR is created |
| `pull_request_target: reopened` | A closed PR is reopened |
| `pull_request_target: labeled` | A label is added to a PR |
| `workflow_dispatch` | Manual run from the Actions tab (with dry-run toggle) |

> A cron schedule (`*/10 * * * *`) is available but currently commented out.

### Opt-In Mechanism

A PR must have the **`assign-for-review`** label to be considered. Draft PRs are automatically skipped.

The label is added **manually** by the PR author when they consider the PR ready for review. Future improvements may automate this (e.g., auto-add on publish, or remove the label requirement entirely and trigger on all non-draft PRs).

### Reviewer Pool

| GitHub Handle | Name | Status |
|---|---|---|
| `souvikghosh04` | Souvik | Active |
| `Aniruddh25` | Aniruddh | Active (see note) |
| `aaronburtle` | Aaron | Active |
| `anushakolan` | Anusha | Active |
| `RubenCerna2079` | Ruben | Active |

> Jerry (`JerryNixon`) has been removed from the pool — PM role, should not review code PRs.

> **Decision needed:** Ruben raised that Aniruddh already reviews a disproportionate number of PRs. Options: (a) remove Ani from the bot pool so he's not auto-assigned, (b) keep him as equal, or (c) add a reduced-weight mechanism. Team leaning toward removing both Jerry and Ani to keep the pool to 4 engineers (Souvik, Aaron, Anusha, Ruben).

Each PR gets **2 reviewers** assigned (configurable via `REQUIRED_REVIEWERS`).

### Weighted Load Balancing

The bot calculates a *weight* for each PR based on its labels, then distributes reviewers so the total weighted load is balanced across the pool.

| Label | Weight |
|---|---|
| *(no size label)* | 1 |
| `size-medium` | 2 |
| `size-large` | 3 |
| `priority-high` | +1 (additive) |

**Examples:**
- A default PR = weight 1
- A `size-large` + `priority-high` PR = weight 4

Size labels are **manual and optional**. If no size label is set, the PR gets a default weight of 1 and the bot still assigns reviewers — it just won't do weighted load balancing. Auto-detection of PR size is a possible future enhancement.

### Reviewer Lifecycle & Availability

A reviewer goes through a defined lifecycle per PR assignment. The bot must respect this when deciding who is available.

#### Active Review State (Blocked)

A reviewer is considered **actively reviewing** a PR — and therefore **blocked** — from the moment they are assigned until they complete one of the following actions:
- **Approve** the PR
- **Request changes** on the PR
- **Comment** (submit a review with comments)
- **The PR is closed or merged**

While a reviewer is in the active-review state for a PR, that PR counts toward their load. The bot **prefers unblocked reviewers** (those with no pending active reviews) when selecting candidates. If all reviewers in the pool have active reviews, the bot falls back to round-robin by least total load (see below).

#### Round-Robin Fallback

When **every reviewer** in the pool has active assignments, the bot does not stop — it continues assigning using the existing weighted load-balancing logic:
- Candidates are sorted by current weighted load (ascending).
- The reviewer(s) with the **fewest active assignments** are picked first.
- This naturally round-robins through the pool as load accumulates.

This ensures PRs are never left without reviewers even when the team is busy.

#### Freeing a Reviewer

A reviewer's assignment on a PR is **released** (no longer counts toward their load) when any of these occur:
1. The reviewer **submits a review** (approve, request changes, or comment).
2. The PR is **merged**.
3. The PR is **closed**.

Once freed, the reviewer becomes available for new assignments with a reduced load count.

#### Re-Review Requests

If a PR author **requests a re-review** (e.g., after addressing feedback and pushing new changes), the assignment lifecycle **starts from scratch**:
- The bot treats the PR as needing reviewers again.
- The same rules apply — load balancing, author exclusion, active-review checks.
- Previously assigned reviewers may or may not be re-assigned depending on current load.
- This can be triggered by removing and re-adding the `assign-for-review` label, or by a `reopened` event.

### Assignment Algorithm

The bot operates in two modes depending on the trigger:

**Single-PR mode** (`pull_request_target`): Only processes the triggering PR. Early-exits if the PR is ineligible (draft, missing label, stale, or already fully assigned). Still builds the global load map for fair selection.

**Full-scan mode** (`workflow_dispatch` / schedule): Scans all open, non-stale, eligible PRs and assigns reviewers to those that need them. Larger/higher-priority PRs are processed first.

In both modes, the core assignment logic per PR is:
1. Exclude the PR author and already-assigned reviewers from candidates.
2. **Prefer unblocked reviewers** (no active reviews) over blocked ones.
3. Within the same availability tier, sort by weighted load (ascending), then alphabetical.
4. Pick the top N candidates needed.
5. Assign (or log in dry-run mode) and update the in-memory load map.

### Optimizations

| Optimization | Effect |
|---|---|
| **Single-PR fast path** | `pull_request_target` only assigns the triggering PR instead of scanning all PRs |
| **Early exit** | Skips immediately if trigger PR is ineligible or already fully assigned |
| **Stale PR filter** | PRs not updated in 90 days are excluded from eligibility and load calculation |
| **Sorted fetch** | PRs fetched sorted by `updated_at` desc for cache-friendly access |
| **Conditional `listReviews`** | Only calls `listReviews` for PRs that have pool assignees (skips zero-assignee PRs) |

### Reviewer Counting

The bot uses the **Assignees** field (`pr.assignees`) to track who has been bot-assigned to a PR. This avoids the CODEOWNERS problem where `requested_reviewers` is pre-populated with all code owners.

To determine active vs. freed reviewers, the bot calls `pulls.listReviews` for each eligible PR:
- **Active reviewer:** Assigned to the PR but has **not** submitted a review yet → counts toward load.
- **Freed reviewer:** Has submitted a review (approve, request changes, or comment) → does **not** count toward load.

Only reviewers from the configured pool are counted. CODEOWNERS or external reviewers are ignored.

### Dry-Run Mode

The bot is currently in **dry-run mode** (`DRY_RUN = true`). In this mode it logs `[DRY RUN] Would assign [...]` but does not actually add reviewers.

To go live, set `DRY_RUN` to `false` in the workflow (or select `false` when triggering manually via `workflow_dispatch`).

### Concurrency

The workflow uses a concurrency group (`pr-review-assignment`) with `cancel-in-progress: false` to prevent overlapping runs from producing inconsistent assignments.

---

## Configuration Reference

| Setting | Location | Default | Description |
|---|---|---|---|
| `REVIEWERS` | Script constant | 5-person pool | GitHub usernames eligible to be assigned |
| `REQUIRED_REVIEWERS` | Script constant | `2` | Number of reviewers per PR |
| `STALE_DAYS` | Script constant | `90` | PRs not updated in this many days are skipped |
| `DRY_RUN` | Script constant / workflow input | `true` | When true, logs only — no actual assignments |
| `assign-for-review` | PR label | — | Label that opts a PR into auto-assignment (removed after assignment when live) |
| Size labels | PR labels | `size-medium`, `size-large` | Increase PR weight for load balancing |
| `priority-high` | PR label | — | Adds +1 to PR weight |

---

## How to Validate (Dry-Run Testing)

1. Open (or reopen) a PR and add the **`assign-for-review`** label.
2. Go to the **Actions** tab in the repo.
3. Find the latest **"PR Review Assignment Bot"** workflow run.
4. Open the **"Assign reviewers to eligible PRs"** step log.
5. Look for `[DRY RUN] Would assign [...]` lines — these show who would be assigned and to which PR.
6. Verify the assignments look correct (author excluded, load balanced, right reviewer count).

You can also trigger a manual run from the **Actions** tab → **PR Review Assignment Bot** → **Run workflow** and choose dry-run `true` or `false`.

---

## Changelog

| Date | Change |
|---|---|
| March 26 | Initial workflow created with `pulls.requestReviewers`, dry-run mode |
| April 8 | **BUG FIX:** CODEOWNERS inflated `requested_reviewers` → switched to `pr.assignees` |
| April 8 | **BUG FIX:** `pulls.requestReviewers` wrote to Reviewers field → switched to `issues.addAssignees` |
| April 8 | **BUG FIX:** Dry-run toggle `\|\|` expression always true → fixed with `context.eventName` check |
| April 8 | Removed Jerry from pool (PM role) |
| April 8 | Added active-review blocking, reviewer-freed logic, round-robin fallback |
| April 8 | Added optimizations: single-PR fast path, early exit, stale PR filter (90 days), conditional `listReviews` |
| April 8 | Paginated `listReviews` to handle PRs with 30+ reviews (review comment fix) |
| April 8 | Auto-remove `assign-for-review` label after successful assignment (review comment fix) |

---

## Open Items

| # | Item | Owner | Status |
|---|---|---|---|
| 1 | **Decide on Aniruddh's inclusion** — remove to reduce his load, or keep as equal | Team | Pending |
| 2 | **Re-enable workflow & test dry-run for 1 week** | Souvik + Team | Next step |
| 3 | **Flip to live mode** after dry-run validation | Souvik | Blocked on #2 |
| 4 | Consider auto-detecting PR size from line count | Souvik | Future |
| 5 | Consider removing label requirement (auto-assign all non-draft PRs) | Team | Future |

---

## Testing Plan

1. **Re-enable the workflow** in dry-run mode.
2. **Team adds `assign-for-review` labels** to published PRs for 1 week.
3. **Check logs** — verify `[DRY RUN] Would assign [...]` output:
   - Author excluded, 2 reviewers selected, load balanced.
   - Stale PRs (>90 days) are skipped.
   - Unblocked reviewers preferred over blocked ones.
   - Round-robin fallback when everyone has active reviews.
   - Re-review (label re-add) triggers fresh assignment.
4. **Flip to live** — set `DRY_RUN` to `false`.
5. **Monitor** for 1-2 weeks, refine as needed.

---

## Team Communication

### Original Communication (March 26)

> Hi Team,
>
> We now have a GitHub Actions workflow that automatically assigns reviewers to pull requests.
>
> **How it works:**
> - Scans all open PRs for the `assign-for-review` label — this is the opt-in mechanism.
> - Assigns 2 reviewers per PR from the pool: Souvik, Aniruddh, Aaron, Anusha, Ruben, Jerry.
> - Uses weighted load balancing so reviewers are distributed fairly:
>   - `size-medium` = 2x weight, `size-large` = 3x, default = 1x.
>   - `priority-high` adds +1 to the weight.
>   - Reviewers with the lightest current load are picked first.
> - Larger/higher-priority PRs are assigned first for best reviewer availability.
> - The PR author is automatically excluded from being assigned on their own PR.
>
> **Current state:** The bot is in dry-run mode and has been tested against draft PRs. It logs who would be assigned but doesn't actually add reviewers yet. Once the team is comfortable, we flip DRY_RUN to false to go live.
>
> Thanks,
> Souvik

### Updated Communication (Draft)

> **Subject: PR Review Assignment Bot — Ready for Testing**
>
> Hi Team,
>
> The PR review assignment bot has been updated with all the fixes from our demo discussion. Here's the summary:
>
> **What it does:**
> - Scans PRs for the `assign-for-review` label and assigns **2 reviewers** from the pool: Souvik, Aniruddh, Aaron, Anusha, Ruben.
> - Uses **weighted load balancing** — reviewers with the lightest active load are picked first.
> - **Prefers available reviewers** — those not currently reviewing any PR are prioritized. If everyone is busy, it round-robins by least load.
> - Reviewers are **freed** once they submit a review (approve, request changes, or comment).
> - PR author is automatically excluded. Stale PRs (>90 days) are skipped.
>
> **What changed:**
> - Fixed CODEOWNERS bug — assignments now use the **Assignees** field (not Reviewers), so CODEOWNERS no longer interferes.
> - Fixed API — bot now uses `issues.addAssignees` to write to the correct field.
> - Fixed dry-run toggle logic.
> - Added reviewer lifecycle: active-review blocking, freed-on-submit, round-robin fallback.
> - Optimized: single-PR mode for faster `pull_request_target` runs, stale PR filter.
> - Removed Jerry from pool (PM role).
>
> **Next step:** Add the `assign-for-review` label to your published PRs. Check the Actions tab logs to verify the `[DRY RUN]` output looks correct. Once we're confident, we flip to live.
>
> Thanks,
> Souvik
