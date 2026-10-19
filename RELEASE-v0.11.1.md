# SMT4Mac v0.11.1 — Spaced pilot names

Fixes a false-red path in Local monitoring: Vision sometimes returns separate observations for each word and numeric suffix of a pilot name. These were classified as separate pilots, letting white text from earlier words be mistaken for a neutral badge.

Text fragments on the same horizontal roster line now merge before badge classification. Only the leftmost fragment supplies the badge position. Adjacent player rows remain separate; the current player's empty badge is ignored and unused space below the roster contributes no rows.

Native tests reproduce split names, numeric suffixes, adjacent friendly players, an unbadged multiword self name, and a large blank area. Existing badge-color and small-text tests remain required.

Quit the existing app and replace it with this build. Restart capture to clear any previously latched red alert. Keep the badge column and names in the selection, including space for arrivals. Check the recognized list: each full pilot name should appear exactly once. Live EVE validation is still needed on your Mac.
