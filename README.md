# SMT4Mac — Native macOS client

A native macOS map, intel and route-planning beta. Includes the self-contained `.app`; no Terminal launcher or .NET installation is required.

## New in v0.8

- Start and destination suggest system names from the first character, case-insensitively. Type `p-z` to find **P-ZMZV**; click a suggestion or use Down/Enter to complete the name.
- Removed the Pilots tab, character/fleet tracking, EVE login, callback listener and Keychain integration.
- Background intel/live-list refreshes cannot navigate the map. Click a report/system row or press Enter on the selected row to navigate. Region changes clear system details that no longer belong to the visible map.

## Retained from v0.7

- Fixed native EVE chat messages with embedded byte-order marks (U+FEFF) before the opening bracket. UTF-16 log headers and BOM-prefixed messages now parse correctly, including incremental writes.
- Added **ADM highlight: Off / below 5.0 / below 4.0** under the region picker. Gold rings mark matching systems; thresholds are strict and missing ADM is excluded. Selection persists across launches. Cached data remains marked unverified.
- Added visible Intel diagnostics: files found/readable, bytes read, latest batch counts, recent versus out-of-window reports, last data/report times, and per-file access errors.
- The automatic log location resolves from the current user's home directory. The UI displays `~/Documents/EVE/logs/chatlogs`; hover to see the resolved location. No personal username is built into the path. Previously chosen custom folders remain respected; **Use automatic EVE folder** clears an override.

After installing, the app rereads recent history automatically. Your earlier test message will only be imported if it is still within 15 minutes; send a fresh system-name message to verify.

## Download

[Download v0.8 for Apple Silicon](https://github.com/partiizan/smt4mac/releases/download/v0.8.0/SMT-Mac-Beta-0.8-AppleSilicon.zip) · [All releases](https://github.com/partiizan/smt4mac/releases)

The ZIP includes the compiled app and corresponding source. The installed app is currently named **SMT Mac Beta.app**.

## Install

1. Quit the previous SMT Mac Beta.
2. Unzip the package and move **SMT Mac Beta.app** into Applications, replacing the earlier beta.
3. Open the app. This is ad-hoc signed, not Apple-notarized. If macOS blocks it, use System Settings → Privacy & Security → Open Anyway after reviewing the app's origin.
4. Allow Documents access when requested. Intel automatically scans `~/Documents/EVE/logs/chatlogs` every two seconds. If an old custom folder was saved, click **Use automatic EVE folder**.

Existing region, ADM and log settings are retained. Public ADM, activity, wormholes, storms, kills and local logs need no login.

## What's included

| Area | Beta behavior |
|---|---|
| Kills | zKillboard R2Z2 feed from connection onward; current-region list; recent kill markers; double-click opens the killmail |
| Activity | ESI last-hour ship/pod/NPC kills and ship jumps; proportional map rings; counts on selected-system details |
| Wormholes | Public Eve-Scout Thera/Turnur connection lists, known-space endpoint markers, signatures, ship-size limit and expiry |
| Storms | Eve-Scout Rescue storm centers/types; strong one-gate and weak three-gate areas |
| Ansiblex | Import bidirectional links from a simple CSV; persist the network; purple map links; optional inclusion in shortest-hop routing |
| Capitals | Hull class and Jump Drive Calibration; real 3D light-year distances; minimum-jump routes; per-leg and total LY; highsec destinations and Pochven excluded |
| Route control | Ordered comma-separated waypoints and avoided systems; gate-only, gates+Ansiblex, or capital routing |
| Existing features | Regional map, search, public sovereignty ADM/indexes, automatic local intel, channel filtering and manual reports |

## Ansiblex import

Create a UTF-8 text/CSV file with **two exact system names per line**, separated by a comma. No header row. Lines starting with `#` are comments. Each row is a bidirectional link; importing replaces the saved network only after every row validates. See `ansiblex-template.csv`.

Select **Gates + Ansiblex** after import. Access lists, operational status, fuel, ship restrictions and structure IDs are not checked. Only import links you know you can use. This beta does not discover private alliance networks automatically.

## Capital planning

Select **Capital jumps**, choose the hull class and JDC level, then supply start/end and optional waypoints/avoidance. The planner minimizes jump count; it does not optimize fuel, fatigue or total LY among equal-hop paths. Jump freighters and Black Ops may depart highsec, but all jump destinations must be eligible low/null systems. It uses the included upstream coordinate snapshot, independent of map layout coordinates.

A geometrically reachable system is not a guaranteed usable cyno. Confirm cynos, jammers, beacons, docking, access, fuel, ship restrictions and fatigue in game. Pochven and restricted Jovian regions are excluded as destinations. Routes across regions are visible in the route list; the regional canvas shows legs whose endpoints are in view.

## Refresh and freshness

- Intel: every 2 seconds; reports expire on-map after 15 minutes.
- Activity: checked every 5 minutes; ESI data is an hourly aggregate and may be cached longer.
- Wormholes: every 2 minutes; expired connections are removed. Mass and actual collapse are not independently verified.
- Storms: every 15 minutes; depends on the public HTML table. A parsing or network failure is displayed and retained data may be stale.
- Kills: polls about every 6 seconds and catches up in bounded batches. Public reports may arrive late; list retains up to 300 from the last hour, markers last 15 minutes. This is not a full historical killboard.
- ADM: existing 5-minute cached ESI behavior.

Rate-limit backoff and server cache expiry are respected. Feed status labels distinguish errors from successful checks. The beta relies on external services, which can change independently.

## Suggested hands-on tests

1. Launch with EVE running and send a system name in your intel channel. Confirm automatic appearance; restart the app and try a new chat session.
2. Type `p-z` into each route endpoint, accept P-ZMZV, and verify suggestions narrow as you type.
3. Visit Period Basis, Delve and Querious while intel and public feeds refresh. Confirm the map stays in the selected region.
4. Switch all four activity layers. Inspect counts in selected-system details. Open a killmail from the current-region list.
5. Select a Thera/Turnur connection and a storm. Compare signatures, expiry and locations with the public sources.
6. Import a known Ansiblex CSV, compare gate-only and bridge-enabled routes, then test an avoided endpoint and a waypoint.
7. Plan a known capital route for your hull/JDC. Confirm every leg is within range. Compare to in-game range and existing operational routes.

For a bug report include macOS version, selected tab/region, exact steps, status message and a screenshot. 

## Known beta boundaries

This is not complete Windows SMT parity. No in-game waypoint writes, standings overlays, private wormhole mapping, automatic bridge discovery, fuel/fatigue simulation, alert sounds, desktop overlay, auto-updater, Intel-Mac package or Developer ID notarization. Website-only alliance logos and font scaling were not carried into the v0.2 local baseline.

## Build and sources

.NET 8, Avalonia 11.3.8. `bash packaging/build-mac.sh osx-arm64` builds the self-contained app on macOS. Tests live under `tests/`; the workflow builds and validates on an Apple Silicon macOS runner.

Source branch: https://github.com/partiizan/smt4mac

Bundled universe, 3D coordinates and regional layouts derive from Slazanger/SMT commit `6b3b4c6a213a349549ef737c89584fc3f048033b`. Retain `UPSTREAM-LICENSE.txt`.

Service references:
- Public ESI: https://esi.evetech.net/
- Eve-Scout: https://api.eve-scout.com/v2/public/signatures?system_name=Thera
- Storms: https://evescoutrescue.com/home/stormtrack.php
- zKillboard R2Z2: https://r2z2.zkillboard.com/ephemeral/sequence.json

## Project provenance

This standalone repository was extracted from the native mac-local-v0.8 branch of partiizan/adm-dashboard at commit 300c1d4986b58622ffd222e1fe904c52c2950087. The ADM Dashboard website remains a separate project. Upstream SMT attribution and its MIT license are retained.
