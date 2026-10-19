# Next build notes

## Separate yellow and red alert messages
- Audio works: user confirmed the voice/sound toggles were disabled. Remove the audio defect report.
- Add separate editable spoken messages for yellow and red conditions using the existing generated voice.
- Default yellow: "Caution, hostiles in the nearby area".
- Default red: "Alert, hostiles entering local".
- Include a test button for each condition; save both messages and respect existing voice/sound toggles and volume.
- Preserve prompt red announcements when escalating from yellow.

## Intel channel selection
- User expects Intel.WOMP to be the intel source. Screenshot confirms Corp_*.txt messages are entering the feed, including duplicate messages from multiple character logs.
- Provide explicit channel selection with Intel.WOMP selected for this setup; exclude Corp by default and allow intentional opt-in.
- Apply selection consistently to history, live ingestion, map reports, and threat evaluation. Remove excluded-channel reports from active state when selection changes.
- Review duplicate ingestion across character logs without suppressing distinct reports.
- Existing workaround: set the optional channel filename filter to Intel.WOMP under Log settings & diagnostics.

These are notes only; no new build requested yet.
