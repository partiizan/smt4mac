# SMT4Mac v0.15.2

See [v0.15.2 changes and monitoring behavior](RELEASE-v0.15.2.md). Native screen monitoring requires macOS 14+. ESI character login remains deferred.

# SMT4Mac — Native macOS client

A native macOS map and intel-monitoring beta. Includes the self-contained `.app`; no Terminal launcher or .NET installation is required.

## New in v0.15

Multiple independent Local monitors with labeled alerts, conservative auto-detection of the Local roster (review the crop before starting), and regional zKillboard reports integrated into the existing Intel Stream. See the release notes for setup, caching and detection limitations.

## Previous: v0.14

Modern macOS speech synthesis, installed voice selection and preview, with automatic preference for South African English. Downloaded Enhanced/Premium voices are supported when exposed by macOS; exact Siri-only voices may not be available. Voice selection and shared volume persist across launches.

## Previous: v0.13

Local status automatically clears after a healthy friendly-only scan. Local and Extended Area now have permanent independent bars and notifications. Stop/start retains the crop and rejects old-session results. Intel pilot sightings no longer need hostile keywords, and the top member-count/person-icon header is excluded from pilot OCR. See the release notes for details.

## Previous: v0.12

- Permanent map, Intel Stream and Local Monitor columns, with a live crop preview and independent Local/nearby status tags.
- Automatic constellation **or** three-jump yellow coverage from your current system. Yellow disappears when reports clear or expire.
- Separate red/yellow voice, sound, popup and message settings; shared voice volume.
- Intel.WOMP selected by default; explicit channel selection, optional Corp opt-in and duplicate-session suppression.
- Border rejection and same-pilot confirmation reduce false red alerts from oversized crops. Uncertain recognition warns instead of declaring a hostile.

## Previous: v0.9

- Intel and Live panels stay visible together. Drag the horizontal divider to resize them.
- The Live panel contains a structured Eve-Scout Thera/Turnur table. Search destination, region or signature; filter by hub. Each row shows region/security, explicitly labeled hub/local signatures, maximum ship size and estimated expiry. Map focuses the destination; Copy copies the connection details. Unknown destinations cannot be mapped.
- Connections are sorted by expiry, near-expiry entries are amber, expired entries are hidden and source failures are clearly labeled. Hover expiry for UTC expiry and reported update time. Eve-Scout opens the source site.
- Removed hourly ship/pod/NPC/jump activity layers and their API polling. Kills, wormhole markers and storm areas remain available by default.
- Log configuration and diagnostics are under an expander, keeping the intel stream visible. Existing log paths, routes and ADM settings are preserved.
- Minimum window size is 1280 × 850; default is 1560 × 1000. Map geography uses the real bundled data, not the illustrative design preview.

## Previous: v0.8

- Start and destination suggest system names from the first character, case-insensitively. Type `p-z` to find **P-ZMZV**; click a suggestion or use Down/Enter to complete the name.
- Removed the Pilots tab, character/fleet tracking, EVE login, callback listener and Keychain integration.
- Background intel/live-list refreshes cannot navigate the map. Click a report/system row or press Enter on the selected row to navigate. Region changes clear system details that no longer belong to the visible map.

## Retained from v0.7

- Fixed native EVE chat messages with embedded byte-order marks (U+FEFF) before the opening bracket. UTF-16 log headers and BOM-prefixed messages now parse correctly, including incremental writes.
- Added **ADM highlight: Off / below 5.0 / below 4.0** under the region picker. Gold rings mark matching systems; thresholds are strict and missing ADM is excluded. Selection persists across launches. Cached data remains marked unverified.
- Added visible Intel diagnostics: files found/readable, bytes read, latest batch counts, recent versus out-of-window reports, last data/report times, and per-file access errors.
- The automatic log location resolves from the current user's home directory. The UI displays `~/Documents/EVE/logs/chatlogs`; hover to see the resolved location. No personal username is built into the path. Previously chosen custom folders remain respected; **Use automatic EVE folder** clears an override.

After installing, the app rereads recent history automatically. Your earlier test message will only be imported if it is still within 24 hours; send a fresh system-name message to verify.

## Download

[Download v0.15.2 for Apple Silicon](https://github.com/partiizan/smt4mac/releases/download/v0.15.2/SMT4Mac-0.15.2-AppleSilicon.zip) · [All releases](https://github.com/partiizan/smt4mac/releases)

The ZIP includes the compiled app and corresponding source. The installed app is named **SMT4Mac.app**.

## Install

1. Quit the previous SMT client.
2. Unzip the package and move **SMT4Mac.app** into Applications, replacing the earlier beta.
3. Open the app. This is ad-hoc signed, not Apple-notarized. If macOS blocks it, use System Settings → Privacy & Security → Open Anyway after reviewing the app's origin.
4. Allow Documents access when requested. Intel automatically scans `~/Documents/EVE/logs/chatlogs` every two seconds for selected channels (Intel.WOMP by default). Use **Select intel channels…** in log settings for other channels. If an old custom folder was saved, click **Use automatic EVE folder**.

Existing region, ADM and log settings are retained. Public ADM, wormholes, storms, kills and local logs need no login.

## What's included

| Area | Beta behavior |
|---|---|
| Kills | zKillboard R2Z2 feed from connection onward; current-region list; recent kill markers; double-click opens the killmail |
| Wormholes | Public Eve-Scout Thera/Turnur connection lists, known-space endpoint markers, signatures, ship-size limit and expiry |
| Storms | Eve-Scout Rescue storm centers/types; strong one-gate and weak three-gate areas |
| Ansiblex | Import bidirectional links from a simple CSV; persist the network; green map links; access and online status unverified |
| Existing features | Regional map, public sovereignty ADM/indexes, automatic local intel, channel filtering and keyword search |

## Ansiblex import

Create a UTF-8 text/CSV file with **two exact system names per line**, separated by a comma. No header row. Lines starting with `#` are comments. Each row is a bidirectional link; importing replaces the saved network only after every row validates. See `ansiblex-template.csv`.

Known links appear automatically as green connectors when both endpoints are on the displayed regional map. Access lists, operational status, fuel, ship restrictions and structure IDs are not checked. This beta does not discover private alliance networks automatically.

## Layout

Drag either vertical divider to resize the map, Intel Stream and Local Monitor columns. The wormhole/live-feed drawer has a collapse/expand bar and a draggable top edge when expanded. Width, expanded height and collapsed state persist. Region and ADM selectors remain above the map; routing controls have been removed.

## Refresh and freshness

- Intel: every 2 seconds; reports expire on-map after 15 minutes.
- Wormholes: every 2 minutes; expired connections are removed. Mass and actual collapse are not independently verified.
- Storms: every 15 minutes; depends on the public HTML table. A parsing or network failure is displayed and retained data may be stale.
- Kills: polls about every 6 seconds and catches up in bounded batches. Public reports may arrive late; list retains up to 300 from the last hour, markers last 15 minutes. This is not a full historical killboard.
- ADM: existing 5-minute cached ESI behavior.

Rate-limit backoff and server cache expiry are respected. Feed status labels distinguish errors from successful checks. The beta relies on external services, which can change independently.

## Suggested hands-on tests

1. Launch with EVE running and send a system name in your intel channel. Confirm automatic appearance; restart the app and try a new chat session.
2. Resize both panel dividers, collapse/restore the drawer, and restart to confirm saved dimensions.
3. Visit Period Basis, Delve and Querious while intel and public feeds refresh. Confirm the map stays in the selected region.
4. Keep Intel and Live visible while reports arrive. Open a killmail from the current-region list.
5. Select a Thera/Turnur connection and a storm. Compare signatures, expiry and locations with the public sources.
6. Import a known Ansiblex CSV and check the green connectors. Clear the network and confirm they disappear.
7. Monitor blue/green/purple members alongside your unbadged self row, with blank space below. Confirm no red alert; then verify a neutral arrival triggers red.

For a bug report include macOS version, selected tab/region, exact steps, status message and a screenshot. 

## Known beta boundaries

This is not complete Windows SMT parity. No in-game waypoint writes, standings overlays, private wormhole mapping, automatic bridge discovery, fuel/fatigue simulation, desktop overlay, auto-updater, Intel-Mac package or Developer ID notarization. Website-only alliance logos and font scaling were not carried into the v0.2 local baseline.

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

Constellation assignments in `data/constellations.json` were imported from Slazanger/SMT `EVEData/data/Systems.dat` on 2026-10-19; see UPSTREAM-LICENSE.txt. They cover all 5,202 bundled systems.
