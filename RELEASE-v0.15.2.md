# SMT4Mac v0.15.2 — Local detection resilience and diagnostics

- Accept trailing tab controls and icon-prefixed member counts; strip standing-icon prefixes using OCR text bounds.
- Recognize smaller text in full-window scans and use less restrictive confidence thresholds for proposing a crop. Ongoing hostile recognition thresholds are unchanged.
- When the member count cannot be read, look for an aligned pilot column with at least three recognizable standing badges beneath a visible Local header. Ambiguous detections remain rejected.
- Failure messages now state how many Local headers/text fragments were found, rather than giving the same generic error for every failure.
- Accept arbitrary digit counts, including one- and multi-digit examples. This does not change the requirement for enough visible rows to detect a roster.

Review the proposed crop before Start monitoring. These changes are tested with synthetic OCR/layout fixtures; the real user layout still needs verification on the user's Mac. Manual selection remains available.
