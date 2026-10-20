# SMT4Mac v0.15.0 — multiple Local monitors and unified kill intel

- Additional EVE clients have independent Local and extended-area status, crop previews, alert settings, and saved labeled profiles. Scans run sequentially in round-robin order. Voice and volume for live alerts are shared from the primary client.
- Auto-detect Local proposes a crop from an English Local heading, member count and aligned pilot rows. Review and adjust the proposal before Start monitoring; manual selection remains available. Detection is intentionally conservative, needs at least two readable pilot rows and does not work with hidden Local tabs. Keep the confirmed layout fixed.
- Client profiles persist; select the current EVE window and confirm the crop after relaunch. Window IDs are not trusted across launches. Minimized/missing windows show unavailable independently. Scan latency increases with more clients; not yet benchmarked on a user's multibox setup.
- Intel Stream mixes selected chat-channel reports with labeled regional zKillboard kills, ordered by event time. Toggle kills, search the combined stream, select to focus a system, or open the kill report. Kills never enter chat threat evaluation.
- Regional history covers up to 200 reports from the past 24 hours, respecting the provider's one-hour cache. The existing live feed supplements it. Reports can be delayed or incomplete. Public victim/affiliation names fall back to IDs if unavailable; no character SSO needed.
- Existing map, monitoring and voice behavior retained.

Use Add EVE client monitor in the Local Monitor column. In each setup window choose Find EVE windows, select the window, Auto-detect Local (or Load preview and select manually), check the crop and system, then Start monitoring.

Requires macOS 14+ and Apple Silicon. Automated testing covers core alerts, regional parsing/cache/deduplication, multi-client isolation/restarts, desktop rendering, native detection geometry/OCR and speech. Real EVE window layouts and audible performance still need beta testing.
