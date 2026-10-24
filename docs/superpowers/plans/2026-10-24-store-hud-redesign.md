# Store HUD Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Symmetric store HUD with collapsible category sections, browsable locked/unaffordable items, per-item descriptions, and live prop preview for dead and spectating players.

**Architecture:** Pure display rules move into a new static `StoreItemRules` (unit-tested). Panorama is static XML, so sections are rendered as two header sets — headers at or before the open section sit above the item list, headers after it sit below — driven from `StoreHudModule`. `StorePreviewService` is generalised from `IPlayerPawn` to any viewer pawn (player or observer) with a stored preview team.

**Tech Stack:** C# / .NET 10, ModSharp 2.1.189 (`Sharp.Shared`), CS2 Panorama XML/CSS, xUnit for the new test project.

**Spec:** `docs/superpowers/specs/2026-10-24-store-hud-redesign-design.md`

## Global Constraints

- Left and right panels are the same width: **520px** each. No asymmetric margins on `.CenterColumn` (remove the `margin: 0px 174px 0px 24px` patch; use `margin: 0px 24px`).
- Only one category section open at a time; the first category is open when the store opens; headers show `Name` + item count for the current tab.
- Buy / Equip / Settings stay as top tabs; Settings content unchanged.
- Every item row is clickable and previews on the prop, whatever its availability.
- Ordering: price descending, then name ascending. Locked items are not sorted first.
- Blocked action copy: `Need {Price - credits} more credits` (unaffordable), `Locked` (permission).
- Permission text fallback: `PermissionDescription` → `PermissionError` → `You don't have access to this item.`
- New optional JSON fields on items: `Description`, `PermissionDescription` (both may contain Panorama HTML).
- Preview team: `2` (T) or `3` (CT) if the player is on one; otherwise the item's team if it is 2 or 3; otherwise 2.
- The preview prop is transmitted only to its owner. Previews never touch the pawn.
- Panel/visual language unchanged: `#12171ee8` panels, `#FF4655` accent, `#4CE165` price green.
- Commit messages: no `Co-Authored-By:` trailer (owner rule).

## Review Focus

1. **Player buys something they can't afford via a stale button** — `buy_confirm` must be disabled and `DoBuyAsync` must refuse when `StoreItemRules.Evaluate` is not `Available` (server-side guard, not only CSS). Test: Task 1 `Evaluate_UnaffordableWhenCreditsBelowPrice`; Task 4 step adds the guard.
2. **Credits change while the store is open** (earned/spent) — availability is recomputed every render from `GetCreditsBySlot`, never cached on the session. Test: Task 1 `BlockedActionText_UsesCurrentCredits`.
3. **A tab with zero items in the open category** — header still shows (count 0), the empty line reads `All items owned` (Buy) or `Nothing equipped yet` (Equip), no stale rows from the previous section. Test: Task 1 `SplitSections_OpenIndexOutOfRange_AllAbove`; Task 4 Step 7(e).
4. **Spectator closes the store or the round restarts while parked** — observer mode and target are restored; a stale observer pawn is never written to. Covered by Task 5 manual checks 3–4.
5. **Clicking the already-open header** collapses it (open index −1) and the list hides; clicking it again reopens. Test: Task 1 `SplitSections_NoneOpen_AllAbove`.

---

### Task 0: Commit the prop-preview work already in the tree

The working tree holds the uncommitted prop preview (`StorePreviewService`, `IItemType.PreviewOnModel`, CSS centre patch, equipped-on-prop fix). Commit it on `dev` before starting so the redesign diffs are clean.

- [ ] **Step 1: Build** — Run: `dotnet build Store -nologo -v q` — Expected: `0 Error(s)`.
- [ ] **Step 2: Commit**

```bash
git add Store content
git commit -m "Replace store camera with client-only preview prop"
```

---

### Task 1: `StoreItemRules` + test project

**Files:**
- Create: `Store/Hud/StoreItemRules.cs`
- Create: `Store.Tests/Store.Tests.csproj`, `Store.Tests/StoreItemRulesTests.cs`
- Modify: `Store/Items/ItemDefinition.cs` (add fields)

**Interfaces:**
- Produces (namespace `InsanityGaming.Store.Hud`):
  - `public enum ItemAvailability { Available, Locked, Unaffordable }`
  - `public static class StoreItemRules`
    - `ItemAvailability Evaluate(ItemDefinition item, bool hasPermission, uint credits, bool buying)` — Locked if `item.Permission` is set and `!hasPermission`; else Unaffordable if `buying && credits < item.Price`; else Available. Locked wins over Unaffordable.
    - `List<ItemDefinition> Order(IEnumerable<ItemDefinition> items)` — price desc, then `Name` ordinal-ignore-case asc.
    - `string? BlockedActionText(ItemAvailability availability, uint price, uint credits)` — `null` for Available.
    - `string PermissionText(ItemDefinition item)` — fallback chain from Global Constraints.
    - `(int[] Above, int[] Below) SplitSections(int count, int openIndex)` — `Above` = `0..openIndex` inclusive, `Below` = `openIndex+1..count-1`; if `openIndex` is outside `0..count-1`, all indices are `Above` and `Below` is empty.
    - `int PreviewTeam(int playerTeam, int itemTeam)` — rule from Global Constraints.
- `ItemDefinition` gains `public string? Description { get; set; }` and `public string? PermissionDescription { get; set; }`.

- [ ] **Step 1: Create the test project** — `Store.Tests/Store.Tests.csproj`: `net10.0`, `IsPackable=false`, packages `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio` (current stable), `<ProjectReference Include="..\Store\Store.csproj" />`. Tests touch only `ItemDefinition` and `StoreItemRules` (no `Sharp.*` types).

- [ ] **Step 2: Write the failing tests** in `Store.Tests/StoreItemRulesTests.cs`

```csharp
[Fact] void Evaluate_LockedWhenPermissionMissing()
    => Assert.Equal(ItemAvailability.Locked, StoreItemRules.Evaluate(new() { Permission = "vip", Price = 10 }, false, 0, true));
[Fact] void Evaluate_LockedBeatsUnaffordable()
    => Assert.Equal(ItemAvailability.Locked, StoreItemRules.Evaluate(new() { Permission = "vip", Price = 999 }, false, 0, true));
[Fact] void Evaluate_UnaffordableWhenCreditsBelowPrice()
    => Assert.Equal(ItemAvailability.Unaffordable, StoreItemRules.Evaluate(new() { Price = 750 }, true, 500, true));
[Fact] void Evaluate_EquipTabIgnoresCredits()
    => Assert.Equal(ItemAvailability.Available, StoreItemRules.Evaluate(new() { Price = 750 }, true, 0, false));
[Fact] void Evaluate_ExactCreditsIsAvailable()
    => Assert.Equal(ItemAvailability.Available, StoreItemRules.Evaluate(new() { Price = 500 }, true, 500, true));
[Fact] void BlockedActionText_UsesCurrentCredits()
    => Assert.Equal("Need 250 more credits", StoreItemRules.BlockedActionText(ItemAvailability.Unaffordable, 750, 500));
[Fact] void BlockedActionText_Locked() => Assert.Equal("Locked", StoreItemRules.BlockedActionText(ItemAvailability.Locked, 750, 9999));
[Fact] void BlockedActionText_AvailableIsNull() => Assert.Null(StoreItemRules.BlockedActionText(ItemAvailability.Available, 1, 1));
[Fact] void PermissionText_PrefersDescription()
    => Assert.Equal("long", StoreItemRules.PermissionText(new() { PermissionDescription = "long", PermissionError = "short" }));
[Fact] void PermissionText_FallsBackToError()
    => Assert.Equal("short", StoreItemRules.PermissionText(new() { PermissionError = "short" }));
[Fact] void PermissionText_DefaultCopy()
    => Assert.Equal("You don't have access to this item.", StoreItemRules.PermissionText(new()));
[Fact] void Order_PriceDescThenName()
    // input B(100), a(100), C(500) → C, a, B
[Fact] void SplitSections_SecondOpen()
    // count 4, open 1 → Above [0,1], Below [2,3]
[Fact] void SplitSections_NoneOpen_AllAbove()
    // count 3, open -1 → Above [0,1,2], Below []
[Fact] void SplitSections_OpenIndexOutOfRange_AllAbove()
    // count 2, open 5 → Above [0,1], Below []
[Theory] void PreviewTeam_Rule(int player, int item, int expected)
    // (2,3)→2, (3,0)→3, (1,3)→3, (1,0)→2, (0,2)→2
```

- [ ] **Step 3: Run to verify failure** — Run: `dotnet test Store.Tests` — Expected: build error, `StoreItemRules` not found.
- [ ] **Step 4: Implement** `Store/Hud/StoreItemRules.cs` and the two `ItemDefinition` properties per Interfaces.
- [ ] **Step 5: Run** — `dotnet test Store.Tests` — Expected: all pass.
- [ ] **Step 6: Commit** — `git add Store.Tests Store/Hud Store/Items/ItemDefinition.cs` — `git commit -m "Add StoreItemRules for store availability, ordering and sections"`

---

### Task 2: HTML label spike (gate for descriptions)

**Files:**
- Modify (temporary, reverted in Step 4): `content/panorama/layout/custom_game/store/store_hud.xml` — add `html="true"` to `buy_meta`.
- Modify (temporary, reverted in Step 4): `Store/Modules/StoreHudModule.cs` `RenderDetail` — set `buy_meta` to `<b>bold</b> <font color='#4CE165'>font</font> <span style='color:#FF4655'>span</span>`.

- [ ] **Step 1:** Make the temporary change, build, deploy DLL and content.
- [ ] **Step 2:** In-game, select an item on Buy. Record which of `<b>`, `<font color>`, `<span style>` render styled vs. raw text.
- [ ] **Step 3:** Write the result as one line at the bottom of the spec under "Risks" (e.g. "HTML spike 2026-10-xx: b ✓, font ✓, span style ✗").
- [ ] **Step 4:** Revert the temporary code/JSON (keep nothing). Commit only the spec note: `git commit -m "Record Panorama HTML label spike result" docs/`

If **no** HTML renders: Task 4 still adds the description labels but without `html="true"`; descriptions show as plain text. Tell the owner before continuing.

---

### Task 3: Symmetric layout + section headers (Panorama)

**Files:**
- Modify: `content/panorama/layout/custom_game/store/store_hud.xml`
- Modify: `content/panorama/styles/custom_game/store/store_hud.css`

**Interfaces (panel ids Task 4 drives):**
- Remove `CatStrip` and `cat_0..cat_7` buttons.
- Inside `ListPanel`, replace `ListHeader` with: `sec_0..sec_7` (above `ListScroll`) and `secb_0..secb_7` (below `ListScroll`). Each is `<Button id="sec_N" class="SectionHeader hud-hidden">` containing `<Label id="sec_N_arrow" class="SectionArrow" text="{s:value}"/>`, `<Label id="sec_N_lbl" class="SectionName" text="{s:value}"/>`, `<Label id="sec_N_count" class="SectionCount" text="{s:value}"/>` (same with `secb_`). Header class `open` marks the open one.
- `ListScroll` gets id `list_scroll` (Task 4 hides it when no section is open). `list_empty` stays inside it.
- Right panel adds, in both `detail_buy` and `detail_equip`: `<Label id="buy_desc" class="DetailDesc" html="true" text="{s:value}"/>` / `equip_desc`, and `<Label id="buy_blocked" class="DetailBlocked hud-hidden" html="true" text="{s:value}"/>` / `equip_blocked` (permission text). `buy_confirm` keeps its id; its label gets `id="buy_confirm_lbl"` so Task 4 can set `CONFIRM PURCHASE` / blocked copy. (Drop `html="true"` if Task 2 found no HTML support.)
- Row chip gains class `chip-short` (unaffordable price).
- Remove `preview_unavailable` label and its CSS (every viewer can preview after Task 5).

- [ ] **Step 1: Edit XML** per Interfaces; update the comment on the centre column to "Centre is left clear for the store preview model".
- [ ] **Step 2: Edit CSS** — `.LeftColumn` and `.RightColumn` both `width: 520px`; `.LeftColumn` becomes a single panel (drop `flow-children: right`, rail rules `.CatStrip`/`.CatButton*`/`.CatIcon*`/`.CatLabel*`); `.CenterColumn` `margin: 0px 24px` (delete the 174px comment); add `.SectionHeader` (+`:hover`, `.open`), `.SectionArrow`, `.SectionName`, `.SectionCount`, `.DetailDesc`, `.DetailBlocked`, `.RowChip.chip-short` (muted red, e.g. `color: #FF465599; border: 1px solid #FF465560`).
- [ ] **Step 3: Verify** — Build content with the owner's usual Panorama pipeline; in-game the left and right panels are equal width and the rotate controls are centred on screen (check with a screenshot: their midpoint is at x = width/2 ± 2px).
- [ ] **Step 4: Commit** — `git commit -m "Store HUD: equal-width panels and section header slots" content/`

---

### Task 4: Module — sections, availability, descriptions

**Files:**
- Modify: `Store/Modules/StoreHudModule.cs`

**Interfaces:**
- Consumes: `StoreItemRules.*`, `ItemAvailability` (Task 1); panel ids from Task 3.
- Session: `TypeIndex` now means "open section", `-1` = none open. Opening the store sets it to `0`.
- Click ids: `sec_N` and `secb_N` → `ToggleSection(session, N)` (open N, or set −1 if N is already open). Remove `cat_` handling and `SelectCategory`, `RenderCategories`, `MaxCategories` usage for the rail (keep the 8-type cap constant, renamed `MaxSections`).

- [ ] **Step 1: Ordering + availability** — `SortedItems` returns `List<(ItemDefinition Item, ItemAvailability Availability)>` using `StoreItemRules.Order` and `StoreItemRules.Evaluate(item, HasPermission(...) or no permission, _credits.GetCreditsBySlot(slot), buy)`; delete the locked-first sort.
- [ ] **Step 2: `RenderSections(session, layout)`** replaces `RenderCategories`/the list header: for each type index use `SplitSections(_itemTypes.Length, session.TypeIndex)` to show it in `sec_` (Above) or `secb_` (Below) and hide the other slot; arrow `▾` when open else `▸`; count = rows for that type on the current tab; `open` class on the open header. Hide `list_scroll` when `TypeIndex` is −1. `SwitchTab` no longer jumps categories; it clears `Selected` only.
- [ ] **Step 3: Rows** — remove the lock gate in `SelectRow` (every row previews). Row classes: `disabled` when `Locked`; chip: Locked → `PermissionError ?? "Permission required"` with `chip-locked`; Unaffordable → `{Price}cr` with `chip-short`; Available on Buy → `{Price}cr` with `chip-price`.
- [ ] **Step 4: Detail** — set `buy_desc`/`equip_desc` to `item.Description ?? ""` (hide when empty). When the selected item is Locked: show `buy_blocked`/`equip_blocked` with `StoreItemRules.PermissionText(item)`. `buy_confirm`: class `disabled` when `Pending` or availability ≠ Available; `buy_confirm_lbl` = `BlockedActionText(...) ?? "CONFIRM PURCHASE"`. Equip team options get `disabled` when Locked.
- [ ] **Step 5: Server-side guards** — at the top of `DoBuyAsync` and `DoTeamOptionAsync`/`DoBothTeamsAsync` (equip path), re-evaluate availability and return with the matching toast (`You need {Price} credits to buy {Name}.` / existing permission toast) if not Available. Existing `PurchaseService` credit check stays.
- [ ] **Step 6: Build** — `dotnet build Store -nologo -v q` → `0 Error(s)`; `dotnet test Store.Tests` → pass.
- [ ] **Step 7: In-game check** — (a) first section open on open; (b) clicking another header moves the list under it and closes the first; clicking the open header collapses; (c) a locked item is previewable, shows its permission text, and its button is disabled; (d) an item above your credits shows the red chip and `Need N more credits`; earning credits while open re-enables it on next render; (e) a section with no items for the tab shows count 0 and `All items owned` / `Nothing equipped yet` (set `list_empty` text accordingly in Step 2).
- [ ] **Step 8: Commit** — `git commit -m "Store HUD: collapsible sections, item availability and descriptions" Store/Modules/StoreHudModule.cs`

---

### Task 5: Preview for dead and spectating players

**Files:**
- Modify: `Store/Services/StorePreviewService.cs`
- Modify: `Store/Modules/StoreHudModule.cs`

**Interfaces:**
- `StorePreviewService.Start(PlayerSlot slot, IBasePlayerPawn viewer, int previewTeam) : bool` — `viewer` is the living `IPlayerPawn` or the controller's `IObserverPawn`. For a player pawn: current behaviour (freeze MoveType, level view via `Teleport`). For an observer: save `ObserverMode`/`ObserverTarget` from `GetObserverService()`, set `ObserverMode = Roaming`, `SetMoveType(MoveType.None)`, `SnapViewAngles(new Vector(0, yaw, 0))`; place the prop from `GetEyePosition()` with z = eye z − `ObserverEyeHeight` (`64f`). Initial model: the pawn's model for a player pawn; for an observer, none until `Show` is called (spawn with the first equipped/selected item's model; if none, do not spawn and return `true` — the HUD still opens).
- `PreviewState` stores `int Team`; `Show` passes `(CStrikeTeam)state.Team` to `PreviewOnModel`.
- `Stop`: player pawn → restore MoveType (as today); observer → restore MoveType, `ObserverMode`, `ObserverTarget` if the observer is still valid.
- Module: `GetViewerPawn(slot) : IBasePlayerPawn?` = living player pawn, else `controller.GetObserverPawn()` if valid. `session.LivePreview` is removed; every session previews. `CloseLiveSession` closes every session on pawn lifecycle events. `ShowEquippedOnPreview` filters `Equipped` by `StoreItemRules.PreviewTeam(playerTeam, 0)`. Hide/remove the `CamControls` toggle and `PreviewOffLabel` logic.

- [ ] **Step 1: Spike first** — implement only the observer branch of `Start`/`Stop`, build, and in-game: die, open the store, confirm the view stops following the spectated player and stays put, and that closing restores spectating. If the observer cannot be held still in `Roaming`, try `ObserverMode.Fixed`; if neither holds, stop and report to the owner before continuing.
- [ ] **Step 2: Implement** the remaining Interfaces items.
- [ ] **Step 3: Build** — `0 Error(s)`; `dotnet test Store.Tests` passes.
- [ ] **Step 4: In-game checks** — (1) alive: unchanged behaviour; (2) dead: model appears in front, rotates, previews; (3) close while dead → spectating resumes on the previous target; (4) round restart while parked → session closes, no errors in log, view normal; (5) pure spectator (team Spectator) previews `team`-coloured items as T.
- [ ] **Step 5: Commit** — `git commit -m "Store preview for dead and spectating players" Store/`

---

## Self-review notes

- Spec coverage: symmetric layout (T3), sections (T3/T4), clickable locked/unaffordable (T4), blocked states + ordering (T1/T4), descriptions + HTML (T1/T2/T3/T4), spectator preview (T5), dead-code removal (T3/T5). Animation and wall clipping remain out of scope per spec.
- Empty-list copy follows the spec: `All items owned` (Buy), `Nothing equipped yet` (Equip, replacing `None owned`).
