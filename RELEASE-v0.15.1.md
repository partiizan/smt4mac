# SMT4Mac v0.15.1 — bracketed Local header detection

Fixes automatic Local detection rejecting EVE tab labels such as `Local [29]`. Also accepts parentheses, plain counts, and surrounding whitespace using correctly escaped regular expressions.

Regression coverage includes bracketed and plain headers, invalid headings, ambiguous detections, and an actual Vision OCR fixture with a left-side `Local [29]` tab and a separate right-side roster count of 28. The two displayed counts do not have to match.

Select the EVE window, click Auto-detect Local, review the suggested crop, then Start monitoring. Manual selection remains available. Detection still requires at least two readable pilot rows and a visible English Local header/member count. Actual user capture was reviewed visually; tests use a synthetic equivalent rather than publishing the user's screenshot.

All v0.15 multi-client monitoring and integrated regional kill reports remain available.
