# SMT4Mac v0.13.0 beta

- Two permanent independent bars: **Local** is green ALL CLEAR or red HOSTILE IN LOCAL; **Extended Area** is yellow for reported threats or neutral ALL CLEAR. Missing capture, location or log access is shown as unavailable.
- Local red automatically clears on the first valid scan containing only friendly/unbadged rows. No manual reset. Detection still requires a hostile pilot on two scans. Keep the selected Local window fixed and its roster visible.
- Stop/start retains the selected window/crop, including when reopening settings. Results from old capture sessions are discarded. Healthy friendly recognition no longer depends on every OCR name being identical to the prior frame.
- Extended coverage remains other systems in the same constellation OR within three gate jumps. Details include jump distance. Current-system intel no longer controls the independent screen-based Local bar.
- System/pilot reports such as `P-ZMZV Doov nv` qualify without hostile keywords. Clear reports, questions and explicit test/friendly messages are excluded. Reports expire using the configured intel expiry.
- Nearby notifications remain active during Local red. Spoken alerts queue instead of interrupting each other; voice/sound/popup preferences remain separate.
- The topmost numeric member-count header is excluded when paired with the grey person-shaped icon. Multiword and numbered pilot names remain included.

Requires Apple Silicon and macOS 14+. Includes SMT4Mac.app and source. Ad-hoc signed; not notarized. Screen recognition remains beta and cannot see pilots outside the selected visible crop. CI covers core rules, simulated capture/restart flows and native Vision fixtures; test real EVE capture and sound on your Mac.
