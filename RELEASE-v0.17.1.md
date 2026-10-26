# SMT4Mac v0.17.1 — ESI login diagnostics and compatibility

- Accepts a single-string scope claim as well as a scope array. Signature, issuer, audience, expiry and required scope checks remain enforced.
- Reports which login step failed, with safe HTTP status / error categories instead of the previous generic message. No tokens or authorization codes are displayed.
- Failure to save the nonsecret Client ID no longer disconnects a successfully authenticated session.
- Browser callback explicitly says authorization was received and connection is not complete until the app displays Connected: character name.
- Use known IDs explains missing system selections and empty results, and preserves manually entered IDs when there are no known records.

Keep the same Client ID, scope and callback: http://localhost:42873/callback/

Real character login still requires user verification. The reported v0.17.0 failure could not be conclusively attributed without its hidden exception; this patch fixes reproducible claim-format and settings-save failure paths and makes other failures diagnosable.
