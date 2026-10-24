# Store HUD redesign — design

Date: 2026-10-24
Status: draft, awaiting approval

## Intent

The Panorama store HUD looks lopsided: the left side (96px category rail + list, 600px total) is
wider than the right detail panel (450px), so the centre column, the rotate controls and the
preview model all sit ~75px right of screen centre. The owner finds the unaligned panels "gross".
At the same time the preview moved from a third-person camera to a client-only `prop_dynamic`
(`StorePreviewService`), which removes the reason the store was restricted to living players.

This redesign makes the HUD symmetric, replaces the category rail with collapsible category
sections, makes locked/unaffordable items browsable instead of dead rows, gives the right panel
real content (per-item descriptions), and extends the live preview to dead and spectating players.

## Goals

1. **Symmetric layout.** Left and right panels are the same width; the centre column, the rotate
   controls and the preview model sit exactly on the screen's vertical centre line. The interim
   `margin: 0px 174px 0px 24px` patch on `.CenterColumn` is removed.
2. **Collapsible category sections (owner's choice, option B of the mockups).**
   - The left icon rail (`CatButton`s) is removed; the left side is a single panel.
   - Each item category is a header row (name + item count) that expands its items beneath it.
   - Only one section is open at a time; opening one closes the other.
   - The first category is open when the store opens.
   - Buy / Equip / Settings remain the top tabs; sections appear on Buy and Equip, each listing
     that tab's items for the category. Settings keeps its current content, in the same
     equal-width left panel.
   - A category with no items for the current tab still shows its header (count 0) and an
     "All items owned" / "Nothing equipped yet" line when expanded.
3. **Every item is clickable and previewable**, including items the player lacks the permission
   for or can't afford.
4. **Clear "can't buy" states.**
   - Permission-locked rows stay visually dimmed with the grey chip (short `PermissionError` text).
   - Unaffordable rows show their price chip in a muted/red style instead of green.
   - Selecting an item that can't be bought shows a disabled action that says why
     (e.g. "Need 250 more credits", or the permission requirement). The panel never offers a
     working Buy/Equip for something the player can't have.
   - Locked items are no longer sorted to the top; ordering is plain price order.
5. **Right panel descriptions.**
   - New optional per-item `Description` in the item JSON, shown in the right panel for the
     selected item.
   - New optional per-item permission description (longer than `PermissionError`), shown in the
     right panel when the permission isn't met.
   - Both are rendered with Panorama's HTML label support so the owner can style text inline.
6. **Live preview for dead and spectating players.** Opening the store while dead or spectating
   stops following the spectated player and parks the view in place, with the preview model
   standing in front of the viewer, rotatable, same as when alive.

## Non-goals

- No new item types. Today only `player_models` exists; sections are built to scale, not to
  introduce more content.
- No search, filtering, or pagination beyond the existing row cap.
- No change to purchase, equip, credits or database behaviour.
- No preview animation work beyond the existing `PreviewAnimation` config.

## Anti-goals (would be a failure even if it "works")

- Panels or controls visibly off the centre line at common resolutions.
- A locked or unaffordable item that offers a Buy/Equip button which then fails.
- A spectator left stuck in a parked/frozen view after the store closes, or unable to resume
  spectating normally.
- The preview prop visible to anyone except its owner.

## Constraints

- ModSharp module, C#, existing DI and module patterns (`StoreHudModule`, `StorePreviewService`,
  `PlayerHudLayoutHost`); Panorama XML/CSS under `content/panorama/.../store/`.
- Text reaches Panorama through the existing `Text(...)` / `Class(...)` dialog-variable path with
  per-session caches.
- Panorama is a CSS subset (`flow-children`, `fill-parent-flow`, `horizontal-align`); there is no
  flexbox. Fixed pixel widths are the existing idiom.
- Prop visibility relies on `ITransmitManager.AddEntityHooks` + `SetEntityState` (documented
  ModSharp pattern).
- Spectator control available from ModSharp: `IObserverPawn.GetObserverService()` →
  `ObserverMode` (`None, Fixed, InEye, Chase, Roaming, Directed`), `ObserverTarget`,
  `ForcedObserverMode`; `IBasePlayerPawn.SnapViewAngles`.

## Decided by the owner

- Collapsible sections (option B) over a drill-down browser or in-panel tabs.
- Previews apply only to the prop, never to the pawn (already implemented).
- Locked and unaffordable items are clickable and previewable.
- Per-item description and a permission-not-met description, shown in the right panel.
- Try HTML with inline styling (`<font color>`, `<span style>`); if the game strips it, fall back
  to classes and markup injection. Not worth blocking on.
- Spectate/dead preview: park the view in place, model in front.
- My earlier guesses (rail removed, equal widths, one section open, first open by default, counts
  on headers, right panel unchanged apart from width/content) were confirmed as "basically right".

## Calls I made (owner to check)

- **Panel width:** both side panels 520px (between today's 450 and 600), centre column takes the
  rest with equal side margins. Builder may tune, but the two sides must stay equal.
- **New JSON fields:** `Description` (string, optional, HTML allowed) and `PermissionDescription`
  (string, optional, HTML allowed). `PermissionError` stays as the short chip text.
  Fallbacks: no `Description` → right panel shows name/meta only; no `PermissionDescription` →
  the right panel uses `PermissionError`, then "You don't have access to this item."
- **Unaffordable detection** uses the credits the HUD already shows
  (`ICredits.GetCreditsBySlot`) against `item.Price`, re-evaluated on every render.
- **Disabled action text:** "Need {Price - credits} more credits" for affordability;
  for permission, the button reads "Locked" and the description explains why.
- **Equip tab + locked:** an owned item whose permission is no longer met shows the same locked
  state; the existing equip-time permission check stays as the safety net.
- **Spectator parking:** set the observer to `Roaming` at its current position and hold it there
  (zero movement) while the store is open; place the prop in front of the observer's eye
  position/yaw the same way as for a living pawn; on close restore the previous `ObserverMode`
  and `ObserverTarget`. If ModSharp can't hold a roaming observer still, the fallback is
  `Fixed` mode at the current position. Must be validated in-game first (see risks).
- **Team for previews while dead/spectating:** a dead player previews as their current team;
  a pure spectator (team Spectator/None) previews items as the item's team if single-team, else T.
  `"team"` colour items follow that choice.
- **Session lifecycle:** spectators' sessions are no longer "browse mode"; the store's pawn
  lifecycle closes (death, respawn, team change, round start) still apply, and additionally a
  spectating session closes if the observer pawn is replaced.
- **Remove dead code** left by the camera era where it no longer applies (e.g. "third-person"
  wording, `PreviewUnavailable` label once every player can preview).

## Left to the builder

- Exact CSS values (paddings, colours of the unaffordable chip, header styling), keeping the
  current visual language (`#12171ee8` panels, `#FF4655` accent, `#4CE165` price green).
- How section headers map onto the existing row-slot scheme (`row_0..row_47`) — e.g. dedicated
  header slots per category vs. reusing row slots — as long as the row cap and caches stay sound.
- Whether HTML needs a separate label (`html="true"`) or the existing detail labels can be
  switched over.

## Risks / unknowns to validate early

1. **HTML in server-set labels.** Unconfirmed that text injected via the dialog variable path
   renders as HTML, and that inline `style` survives. Spike with one label before building on it.
2. **Holding a spectator still.** Unconfirmed that a roaming observer can be frozen in place
   via ModSharp. Spike before committing to the spectator half.
3. **Animgraph pose on `prop_dynamic`.** Agent models may T-pose; `PreviewAnimation` exists but no
   sequence name is verified.
4. **Wall clipping.** The prop stands 150 units ahead with no trace; a wall in front hides it.
   Out of scope unless it proves common.
