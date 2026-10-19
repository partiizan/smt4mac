# SMT4Mac v0.14.0 — installed alert voices

- Modern AVSpeechSynthesizer speech with queued red/yellow announcements.
- Installed voice selector, refresh and preview in Local monitoring & alerts. Preview works independently of notification toggles.
- Automatic selection prefers the highest-quality installed South African English voice, then installed English, then the system fallback.
- Enhanced/Premium voices appear when downloaded and exposed by macOS. Exact Siri-only voices are not guaranteed to be available to third-party apps.
- Voice choice persists alongside the existing shared voice volume. Missing saved voices safely fall back to automatic selection.
- Existing monitoring settings migrate without reconfiguration. Threat detection, intel and map behavior are unchanged.

Requires macOS 14 or later, Apple Silicon. Download voices in System Settings → Accessibility → Read & Speak (Spoken Content on older versions) → System voice → Manage Voices, then refresh the app voice list.

Validation: core, operations and desktop integration suites; native OCR fixtures; installed voice enumeration, explicit/automatic/fallback selection and utterance volume/message tests; signed app packaging. Audible quality still needs checking on your Mac with its installed voice assets.
