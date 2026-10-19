# SMT4Mac v0.12.0 beta

## Permanent monitoring workspace
- Map, Intel Stream and Local Monitor are separate, always-visible columns with movable dividers and saved widths.
- Local Monitor shows the selected crop live even with settings closed. Stopped, failed and stale captures clear the preview.
- Green Local clear can coexist with yellow Hostiles nearby. Yellow disappears when qualifying reports expire or are cleared. Immediate Local threats show red, with the confirmed pilot name. Local red remains latched until capture is restarted.
- Wormholes retain full minimize/restore and exposed-only resizing.

## Coverage and notifications
- Enter a current system: its constellation is populated automatically from the bundled map data.
- Yellow covers the entire constellation OR anything within three stargate jumps, including other constellations. Reports at the current system are red.
- Red and yellow each have independent voice, sound and popup options, editable messages and test buttons. A single slider controls voice volume. Red escalation bypasses the notification cooldown.
- No ESI character login is required.

## Intel and Local recognition
- Intel.WOMP is selected by default. Select intel channels explicitly in log settings; Corp is excluded unless enabled. Selection changes rebuild history and map/alert data.
- Identical reports in multiple character/session logs are deduplicated.
- Empty crop space and unbadged self rows are ignored. Border pixels are rejected. Uncertain badge geometry produces a warning instead of red.
- Hostile badges must be confirmed on the same recognized pilot across two consecutive scans. Full names, including spaces and numbers, remain one row.

## Install and limitations
Unzip and open SMT4Mac.app. Requires Apple Silicon and macOS 14 or later. The package includes the source code. The app is ad-hoc signed, not notarized; existing macOS first-open approval and Screen Recording permission steps still apply.

Screen recognition is beta and only covers visible rows. Verify the preview and trigger details against EVE; scroll or layout changes can require reselecting the crop. CI verifies synthetic OCR fixtures, core rules and desktop interactions; real EVE capture and audible playback require a test on your Mac.
