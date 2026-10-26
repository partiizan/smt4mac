# SMT4Mac v0.17.0 — Named facilities and collapsible Intel

- Map → Local Monitor → Intel Stream. Collapse Intel to the right edge; restore its previous width. Ingestion and alerts continue while hidden. Layout preferences persist.
- Hover over a system for facility counts. Select it for a scrollable NPC / Upwell list below the map.
- NPC names load from public ESI on selection. Facilities… provides a searchable regional list and an explicit regional NPC name loader. Public NPC results are cached locally; failed refreshes are labeled.
- Upwell entries show name, type, source, observation time and ID. Public EVE Ref results remain incomplete.
- Structure ESI… provides optional browser login with PKCE, validated EVE JWTs, only esi-universe.read_structures.v1, and direct ID lookups. No corporation roles or inventory scopes requested.
- Tokens and authenticated structure records are memory-only. Disconnect or quit clears them; connect again after relaunch.
- Private results merge into the map for this session. ACL failures and unknown IDs are shown; access does not enumerate every accessible structure and does not guarantee docking.

## ESI setup
1. Register an Authentication & API Access application at https://developers.eveonline.com/applications.
2. Enable scope esi-universe.read_structures.v1.
3. Set callback exactly to http://localhost:42873/callback/ (including trailing slash).
4. Open **Structure ESI…**, enter the Client ID (no secret), and connect.
5. Authorize your character in the browser, return to the app, and enter known numeric structure IDs. The selected system's known public IDs can be filled automatically.

This beta uses a fixed loopback port only during login, with a three-minute timeout. No random ports or ongoing local web service. Port conflicts are reported. Only the nonsecret Client ID is saved.

## Validation
Automated PKCE / callback / JWT / ACL / rate-limit checks use synthetic credentials. Real EVE character login and private structure ACL access require hands-on verification with your registered application. macOS screen permissions and OCR behavior remain unchanged.
