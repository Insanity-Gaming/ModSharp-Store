# Store HUD content (Panorama)

The store HUD ships in two independent halves. **Both must be redeployed after any HUD change**;
deploying only one looks exactly as if the other change never happened.

1. **DLL** — `dotnet build Store.sln -c Release` (CI packages `game/sharp/modules/store/`).
2. **Content** — the Panorama files in this folder, served through the Workshop addon.

## Layout

```
content/panorama/
├── layout/custom_game/store/store_hud.xml      # the store layout (one private entity per player)
├── styles/custom_game/store/store_hud.css      # IG palette, included by the layout
└── images/custom_game/store/
    ├── ig_logo.png                              # top-bar logo
    └── cat_player_models.png                    # unused since the category rail became section headers
```

The server creates the layout with the **compiled** path
`panorama/layout/custom_game/store/store_hud.vxml_c` (`StoreHudModule.LayoutResource`).
Precache uses the source paths (`.vxml` / `.vcss`).

## Deploy

1. Sync `content/panorama/**` into the addon's content tree:
   `<CS2 install>/content/csgo_addons/igservrcontent/panorama/...`
2. Compile the layout (this also compiles its included stylesheet and images):

   ```
   "<CS2 install>/game/bin/win64/resourcecompiler.exe" -game "<CS2 install>/game/csgo" -f store_hud.xml
   ```

   `-f` forces a rebuild — the compiler's CRC check can be wrong during fast edit cycles.
   Output lands in `game/csgo_addons/igservrcontent/panorama/...`.
3. Republish the Workshop addon.

## Rules (from the SurfTimer custom HUD baseline)

- Only `Panel`, `Label`, `Image` and `Button`. No `style=`, no event handlers, no `<script>`.
  `Button` takes only `id` and `class` (nest a `Label` for the caption).
- The outermost panel under `<root>` has no `id` (compile error); `store_root` sits inside it.
- Every panel the server drives needs an `id`. Never style a server-driven panel by `#id` — an id
  selector beats the runtime class overrides.
- Labels take only `id`, `class`, `hittest` and `text` (no `html=` until it is proven in-game).
- State is classes only: `show`, `hud-hidden`, `on`, `open`, `sel`, `disabled`, `chip-price`,
  `chip-locked`, `chip-short`, `toast-ok|err|info`. Text is `{s:value}` on labels.
- CSS: `px`/`%`, `s`/`ms`, `deg` only; `flow-children` instead of flex; `visibility: collapse` to
  hide; transitions as longhands. `fill-parent-flow()` only along the parent's `flow-children`
  direction. A clean compile proves nothing about CSS values — check in-game for the parse-warning
  dialog.
- Comments in CSS and XML: plain ASCII, no apostrophes or quotes (a stray `'` can be read as an
  unterminated string).
- The fixed pools (8 sections × 2 header slots `sec_`/`secb_`, 48 rows, 8 settings rows) must match
  the constants in `Store/Modules/StoreHudModule.cs` (`MaxSections`, `MaxRows`, `MaxSettings`).

## C# / XML id cross-check

There is no shared build step, so a typo in an id fails silently. From the repo root:

```bash
python content/check_hud_ids.py
```

It compares every id `StoreHudModule.cs` drives (static ids plus the `sec_`/`secb_`/`row_`/`set_`/`team_`
pools, sized from its `Max*` constants) against `store_hud.xml`, and exits non-zero on any mismatch.
