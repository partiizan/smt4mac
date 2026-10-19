# Next build notes

## Audio alerts
- User reports that both actual audio alerts and the audio test are silent in v0.11.1.
- Investigate both sound and speech paths; show playback errors and verify output on a real Mac.

## Intel channel selection
- User expects Intel.WOMP to be the intel source. Screenshot confirms Corp_*.txt messages are entering the feed, including duplicate messages from multiple character logs.
- Provide explicit channel selection with Intel.WOMP selected for this setup; exclude Corp by default and allow intentional opt-in.
- Apply selection consistently to history, live ingestion, map reports, and threat evaluation. Remove excluded-channel reports from active state when selection changes.
- Review duplicate ingestion across character logs without suppressing distinct reports.
- Existing workaround: set the optional channel filename filter to Intel.WOMP under Log settings & diagnostics.

These are notes only; no new build requested yet.
