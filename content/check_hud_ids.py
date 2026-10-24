"""Cross-checks store HUD panel ids between StoreHudModule.cs and store_hud.xml.

There is no shared build step, so a typo in an id fails silently in-game. Run from the repo root:
    python content/check_hud_ids.py
Exits non-zero when an id the C# drives is missing from the XML, or the XML has an id nothing drives.
"""
import re
import sys

CS = "Store/Modules/StoreHudModule.cs"
XML = "content/panorama/layout/custom_game/store/store_hud.xml"

cs = open(CS, encoding="utf-8").read()
xml = open(XML, encoding="utf-8").read()


def const_int(name):
    return int(re.search(rf"const int\s+{name}\s*=\s*(\d+);", cs).group(1))


max_sections, max_rows, max_settings = (const_int(n) for n in ("MaxSections", "MaxRows", "MaxSettings"))

# Static ids: panel-id constants and the click/tab/team literals.
static = set(re.findall(r'const string \w+\s*=\s*"([a-z_]+)"', cs))
static |= set(re.findall(r'"(tab_\w+|team_\w+|cam_\w+|close)"', cs))
static -= {"store_hud"}  # entity targetname prefix, not a panel
static -= {"sec_", "secb_"}  # section header pool prefixes, expanded below

# Pooled ids the C# builds from prefixes. Section headers have two slots per category: sec_ above
# the item list and secb_ below it.
pooled = set()
for i in range(max_sections):
    for prefix in ("sec", "secb"):
        pooled |= {f"{prefix}_{i}"} | {f"{prefix}_{i}_{part}" for part in ("arrow", "lbl", "count")}
for i in range(max_rows):
    pooled |= {f"row_{i}"} | {f"row_{i}_{part}" for part in ("tile", "name", "team", "eq", "chip")}
for i in range(max_settings):
    pooled |= {f"set_{i}", f"set_{i}_title", f"set_{i}_desc"}
pooled |= {f"{team}_eq" for team in ("team_t", "team_ct", "team_both")}

expected = static | pooled
actual = set(re.findall(r'id="([a-z0-9_]+)"', xml))

missing = sorted(expected - actual)
unused = sorted(actual - expected)

if missing:
    print("Driven by C# but missing from the XML:", ", ".join(missing))
if unused:
    print("In the XML but not driven by C#:", ", ".join(unused))
if not missing and not unused:
    print(f"OK: {len(actual)} ids match.")

sys.exit(1 if missing or unused else 0)
