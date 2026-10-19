# SMT4Mac v0.11 — Map and Local monitoring beta

- Removed the left route-planning/search sidebar and its controls. Region and ADM <4/<5 selectors now sit above the expanded map.
- Known Ansiblex links render in green. Existing imported networks are retained; import/clear CSV controls are in the map toolbar. No network is fabricated and access/online status remains unverified.
- Drag the map/intel divider to resize the intel stream.
- Collapse the bottom wormhole/live-feed drawer to a slim bar; click to expand upward. Drag its top edge while expanded to resize. Panel sizes and collapsed state persist across launches.
- Local rows with an empty standing-badge slot are ignored as the current player. Blue/green/purple are friendly; visible neutral/hostile badges still alert. Empty space below the roster is not a pilot and remains scanned for arrivals.
- Healthy Local with missing intel/location configuration now says LOCAL CLEAR · INTEL INCOMPLETE instead of implying capture is unavailable. Full ALL CLEAR still requires healthy configured intel coverage. An entirely unreadable capture never implies all clear.
- Includes native OCR color/badge tests and desktop layout/ingestion regression tests. Live EVE capture must still be validated on your Mac.

Quit SMT4Mac, replace the app with this version, and restart. Apple Silicon, macOS 14+. Screen Recording permission may need re-enabling after the ad-hoc-signed update. Select only Local names and the badge column, including space for new arrivals; keep all relevant members visible. Red Local alerts remain latched until capture is restarted.

ESI login and a Rust migration remain deferred. Rust is a post-beta goal.
