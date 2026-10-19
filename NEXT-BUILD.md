# Next build notes

## Automatic constellation coverage
- Resolve the manually entered system against bundled universe/constellation data (no ESI login).
- Automatically populate the yellow target with that system's constellation and monitor it for yellow threats from enabled intel channels.
- Recompute the constellation whenever the manual system changes. Show the resolved constellation clearly; invalid/incomplete input must not retain stale coverage.
- Example system: RF-CN3 (the RN-CN3 entry in the request appears to be a typo).
- Preserve red priority for the current system / detected Local hostiles; other systems within the resolved constellation produce yellow.

## Separate yellow and red alert settings
- Audio works: user confirmed the voice/sound toggles were disabled. Remove the audio defect report.
- Add separate editable spoken messages for yellow and red conditions using the existing generated voice.
- Default yellow: "Caution, hostiles in the nearby area".
- Default red: "Alert, hostiles entering local".
- Each condition gets independent Voice, Sound, and Popup toggles, plus an editable alert message and its own test button. Save all per-condition preferences.
- One shared voice-volume slider on a single controller line applies to both conditions.
- Preserve prompt red announcements when escalating from yellow.

## Intel channel selection
- User expects Intel.WOMP to be the intel source. Screenshot confirms Corp_*.txt messages are entering the feed, including duplicate messages from multiple character logs.
- Provide explicit channel selection with Intel.WOMP selected for this setup; exclude Corp by default and allow intentional opt-in.
- Apply selection consistently to history, live ingestion, map reports, and threat evaluation. Remove excluded-channel reports from active state when selection changes.
- Review duplicate ingestion across character logs without suppressing distinct reports.
- Existing workaround: set the optional channel filename filter to Intel.WOMP under Log settings & diagnostics.

These are notes only; no new build requested yet.
