# SMT4Mac v0.16.0 — NPC and known Upwell map filters

- Facilities dropdown: Off, NPC stations, Known Upwell, or both. Selection persists.
- Cyan squares mark systems with NPC stations in the bundled map; purple diamonds show known Upwell counts per system. Other map layers remain available.
- Facilities button lists the selected region's matching systems and structures. Click a row to focus its system. Upwell entries include names, hull types, structure IDs, source last-seen timestamps and whether the source could query them.
- Public EVE Ref structure data refreshes hourly and is cached locally. Outages retain cached data with a stale-data warning. Entries without known system/type data cannot be mapped.
- No ESI login or corporation roles are needed for this public dataset. It is incomplete, especially for private nullsec structures; no marker must not be interpreted as no structures. Docking access is not verified. NPC data identifies station-bearing systems, not individual station names/counts.

Private structure discovery through authenticated ESI remains deferred along with character SSO. Data source: https://docs.everef.net/datasets/structures
