# Adding a game

The launcher shell owns accounts, the server directory, saved locations, consent, and operation progress. `IGameAdapter` owns a game's installation detection, prerequisites, account identity, and launch process. `Games.All` is the explicit registry of supported adapters.

To add a game:

1. Implement an adapter with a stable game ID, display name, default documents location, installation detection, location validation, account requirements, game-specific options shown after clicking Join, and launch behavior.
2. Add the corresponding game ID, connection schema, registration validation, readiness checks, and join authorization to the account/server-directory service.
3. Define allowed content types and destinations for that game. Extend the manifest validator and vault destination resolver together. The initial resolver supports only Assetto Corsa car and track directories; do not reuse those paths for another game.
4. Add tests for safe launch arguments, saved settings, game-specific prerequisites, manifest validation, and missing-content installation.
5. Register the adapter only when it can actually join supported servers. Expose the game in the website's directory filter at the same time.

Each game retains its own installation and documents settings. Steam is a shared optional location; another adapter may use a different platform. The initial account flow uses Steam for Assetto Corsa; other adapters need their own identity mechanism and API contract.

Server-controlled manifests may describe approved content, never executables, scripts, protocol handlers, registry modifications, or arbitrary command-line arguments. Only trusted adapter code decides how a game starts. Normal mods can include executable code that a game loads: user approval is a trust decision, not a malware scan.

The shared vault can reuse the same verified package across servers. Games still access content at their expected paths, so only one version of a particular game content ID can be active at a time. Hold the shared game-session mutex through installation and the game session; do not switch content while that game is running.
