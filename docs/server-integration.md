# Server directory and asset integration

The development client targets `https://api.matrixgames.app`. The website directory is `https://matrixgames.app/servers`. These endpoints need the matching backend deployment; source publication alone does not enable them.

## Accounts

`POST /v1/launcher/auth/start` with `{}` returns `deviceCode`, `userCode`, `verificationUrl`, `expiresIn: 600`, and `interval: 5`. Open the verification URL, show the code in the launcher, and poll `POST /v1/launcher/auth/poll` with `{ "deviceCode": "..." }` every five seconds. A pending response is `{ "pending": true }`.

The website signs in through the existing Google OAuth flow. The user explicitly approves the code with `POST /v1/launcher/auth/approve` and `{ "userCode": "..." }`, using their website cookie and an allowed Origin. Approval is single-use. Polling returns a launcher bearer token with a 30-day lifetime, not Google credentials. `GET /v1/launcher/me` checks the token; `POST /v1/launcher/auth/logout` revokes it. Send tokens only to the account API in `Authorization: Bearer ...` headers.

## Server registration

`GET /v1/servers` is public and returns `games` plus `servers`. Entries include `id`, `game`, `name`, `online`, `players`, `capacity`, `track`, and `models` (objects with `id` and `name`). Community online status reflects the owner's last heartbeat, not an independent network probe. The launcher checks the game server again before launching.

Authenticated owners use `POST /v1/servers/register`:

```json
{
  "game": "assetto-corsa",
  "name": "My server",
  "host": "203.0.113.8",
  "gamePort": 9600,
  "httpPort": 8081,
  "track": "my_track",
  "models": ["my_car"],
  "capacity": 16,
  "manifest": {
    "schema": 1,
    "game": "assetto-corsa",
    "assets": []
  }
}
```

The address above is a documentation placeholder. Use your public IPv4 address. Fill `assets` with valid entries for the track and every listed car; an empty manifest is rejected. This first community-server integration supports unpassworded Assetto Corsa servers. Managed servers use their own signed admission flow.

The response returns `id`, a one-time `heartbeatToken`, and `heartbeatEverySeconds: 30`. Keep the token private and send `POST /v1/servers/<id>/heartbeat` with `{ "token": "...", "players": 3 }` every 30 seconds. Listings go offline after 90 seconds without a heartbeat. Registration does not start, rent, or configure a game server.

Owners can `POST /v1/servers/<id>/update` with the full registration body or `/remove` with `{}`. Updating content resets online status until the next heartbeat. Limits apply, including at most ten listings per account. Cookie-authenticated writes require an allowed website Origin; native integrations use bearer authentication.

## Server-owned manifest

Each asset describes a server-owner-provided HTTPS archive and the exact subtree to install:

```json
{
  "id": "my_car",
  "kind": "car",
  "name": "My car",
  "url": "https://cdn.example.org/mods/my_car.7z",
  "sha256": "REPLACE_WITH_64_LOWERCASE_HEX_CHARACTERS",
  "bytes": 123456,
  "expandedBytes": 456789,
  "archiveRoot": "content/cars/my_car",
  "checks": [
    { "path": "data.acd", "sha256": "REPLACE_WITH_64_LOWERCASE_HEX_CHARACTERS" },
    { "path": "my_car.kn5", "bytes": 234567 },
    { "path": "skins/default/livery.png", "bytes": 1234 }
  ]
}
```

This is a format example, not a downloadable asset. Use actual archive sizes and hashes. `expandedBytes` is the sum of uncompressed regular-file sizes across the **whole archive**, even when only a subtree is installed. `archiveRoot` is relative to the archive root; `checks` paths are relative to the installed subtree. Include required models, skins, and relevant resources. Car manifests must include a SHA-256 check for `data.acd`; track manifests require `data/surfaces.ini`. Track `kind` is `track`.

The service canonicalizes manifests and returns a SHA-256 `revision`. `GET /v1/servers/<id>/manifest` requires authentication and returns `{ "server": "id", "manifest": { ... , "revision": "..." } }`. A join requires that same revision so content changes trigger another review.

HTTPS downloads must resolve directly without redirects, credentials in URLs, or interactive authentication. HTTP Range responses are supported for resuming. Archives are bounded to 20 GiB compressed and 50 GiB expanded per asset. Unsafe Windows paths, links, duplicate destinations, and a size/hash mismatch are rejected. Only the declared subtree is extracted; bundled installers outside it are not run or installed. The owner supplies the CDN; the launcher has no built-in asset host.

## Join

The website opens `matrixgames://join?game=assetto-corsa&server=<id>`. The Assetto Corsa adapter then opens its car chooser using that server's roster. No car is preselected by the website or join link. The desktop client verifies local content, obtains consent for missing assets, installs them, and only then calls `POST /v1/servers/<id>/join` with:

```json
{ "steamId": "76561198000000000", "model": "my_car", "manifestRevision": "CURRENT_MANIFEST_REVISION" }
```

The API checks identity, online status, model selection, and manifest revision. It returns a short-lived game-specific launch grant. Community grants use schema 3 and an empty password; managed Assetto grants use schema 2 and signed admission passwords. The client validates the grant against the chosen server and probes the server's track before writing a backed-up `race.ini` and starting its locally configured `acs.exe`.

The server must still authenticate players and enforce game ownership. A directory listing or heartbeat is not proof that a host is trustworthy.
