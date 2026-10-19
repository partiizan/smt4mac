# SMT4Mac v0.10 — Local monitoring beta

Download **SMT4Mac-0.10-AppleSilicon.zip**. Requires Apple Silicon and macOS 14 or newer. The archive includes SMT4Mac.app and its source; no .NET installation is needed.

## New in this release
- App bundle and executable renamed to SMT4Mac. Existing settings are retained.
- On-device Local screen capture and OCR using ScreenCaptureKit and Apple Vision. Select an EVE window and drag a capture area around Local names and standing icons. Optional second area reads the current system; a manual system is also supported.
- Green ALL CLEAR, yellow HOSTILES NEARBY, red RED ALERT and gray MONITORING UNAVAILABLE. Old Pause and Try demo buttons removed.
- Yellow coverage: one system, nearby gate jumps, a pinned constellation, or follow the current constellation. Defaults: 3 jumps and 5-minute report expiry.
- Voice, sound and popup alerts, custom spoken message and voice volume.
- Case-insensitive intel history search with highlighted matches. Searches up to 5,000 retained messages, including messages without system names. Search never limits alert evaluation.
- ESI character authentication remains deferred. Existing public sovereignty and live feeds are unchanged.

## Set up Local monitoring
1. Quit the old app and move SMT4Mac.app to Applications. This beta is ad-hoc signed, not notarized; macOS may require Privacy & Security → Open Anyway.
2. Open **Local monitor & alert settings**. Click **Find EVE windows** and grant Screen Recording permission to SMT4Mac, then restart if requested. Documents permission is still required for chat logs.
3. Select your EVE window, load its preview, and drag around the Local member names **and standing icons**, excluding headers, chat and member counts.
4. Enter your own pilot and any known friendly names in the exact-name friendly list. Optional blue/green badge recognition is experimental and must be checked against your EVE overview settings. Unknown rows conservatively trigger red after two matching scans.
5. Enter your current system manually, or select the optional system-name area from the preview. Configure yellow coverage and audio, then start monitoring.

Capture is explicitly started each session. Areas are window-relative, but must be selected again if the EVE panel layout changes. Closing the setup window leaves capture active; Stop capture ends screen reading while log ingestion continues.

## Important beta boundaries
- Only visible Local rows can be read. This does not discover off-screen pilots. Green means no detected threats in the monitored view and recent parsed intel, not verified safety.
- Red Local detections remain latched until you verify in game and restart capture. Scrolling a pilot out of view is not treated as their departure.
- Empty, low-confidence or stale captures cannot produce green. Missing/ambiguous system OCR disables location-dependent calculations rather than silently using an old location.
- Intel threat classification is conservative and keyword-based; questions and ordinary system mentions do not automatically alert. Unrecognized phrasing may be missed. Reports show their source and time.
- Screen captures and recognized names stay in memory on your Mac. No screenshot upload or storage is performed.
- Capture targets one EVE window at a time. Auto-discovery of the Local panel and simultaneous multiclient monitoring remain future work.
- Native capture/OCR is compiled on macOS; actual EVE screen permissions, fonts, standing colors and Local recognition require in-game validation on your Mac. Automated tests do not establish combat reliability.
