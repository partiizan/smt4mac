# SMT4Mac v0.10.1 — Local monitoring fixes

- Lowered Vision's minimum relative text height so small names in a tall Local roster can be recognized.
- Non-friendly observations can alert after two consecutive scans even when OCR spelling changes. Perfect name matching is no longer required for red. Low-confidence/unstable captures still cannot produce green.
- Local threats trigger red without a configured current system or available intel logs. Those are needed for full green/nearby coverage, not Local red.
- Monitoring status now explains whether capture is stopped, no names were recognized, recognition is uncertain, the system is missing, or logs are unavailable.
- Scroll-to-zoom area selection (up to 6×), zoom buttons and scrollbars for panning. Selection coordinates remain relative to the captured window at every zoom. Preview now preserves the window's aspect ratio.
- Added a native small-text/tall-roster regression alongside the eight icon-color tests.

Quit the existing app and replace it with **SMT4Mac.app** from **SMT4Mac-0.10.1-AppleSilicon.zip**. Apple Silicon, macOS 14+. Screen Recording permission is required; macOS may ask again after this ad-hoc-signed update.

Open Local monitor & alert settings, find your EVE window, load preview, zoom and select Local names plus icons, then Start monitoring. Green/blue/purple icons are friendly; other/no icons are hostile. Check the recognized crop and row confidence. Red Local alerts remain latched until you verify in game and restart capture.

The reported in-game failure could not be reproduced against a live EVE window here. Automated tests cover the identified recognition/confirmation failure paths; real EVE capture still needs validation on your Mac. If unavailable persists, the new detailed status and crop preview should identify the remaining cause.
