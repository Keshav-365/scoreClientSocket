# scoreClientSocket — Third-Party Integration Guide

scoreClientSocket is the public edge service of the live-score platform. Third-party
integrators connect **only** to this service — never directly to `ScoreProvider` (the
internal service that pulls raw data from Betfair/CricFeed). It exposes two ways to get
score data:

- **REST API** (`/api/*`) — request/response snapshots (scorecard links, iframe embeds,
  connection stats, one-off event lookups).
- **Real-time socket** (SignalR hub at `/clientScore`) — subscribe to an event and receive
  push updates as the match progresses.

Both require an **agent key**, and both flow through the same authentication/IP-whitelist
check (`AgentAuthFilter`).

---

## 1. Authentication

Every request — REST or socket — must present a valid, non-expired **agent key**, issued
per integration partner. Contact the platform owner to get a key; each key is configured
server-side with:

| Field | Meaning |
|---|---|
| `Name` | Your integration's label |
| `Key` | The secret you send on every request |
| `ExpiryDate` | Key stops working after this date (`yyyy-MM-dd`) |
| `AllowedIPs` | Optional per-agent IP whitelist. Empty = allowed from any IP. If populated, requests must originate from one of the listed IPs (checked via `X-Forwarded-For` / `CF-Connecting-IP` / the raw connection IP) |

### How to send the key

| Call type | Transport | Example |
|---|---|---|
| REST API | HTTP header `X-App` (configurable server-side, `X-App` is the default) | `X-App: <your-key>` |
| REST API (alternative) | Query string | `?key=<your-key>` |
| Socket connection | Query string on the hub URL — **required**, headers don't work here | `wss://host/clientScore?key=<your-key>` |

The socket **must** use the query string because a browser's native WebSocket upgrade
request cannot carry custom headers — only the initial SignalR "negotiate" HTTP call could
use a header, and the subsequent WebSocket upgrade would then fail auth. Using `?key=`
works for both legs.

### Failure responses

| HTTP status | Body `message` | Cause |
|---|---|---|
| 401 | `Agent key required.` | No key present (header or query string) |
| 401 | `Invalid agent key.` | Key doesn't match any configured agent |
| 401 | `Invalid expiry date configured for agent.` | Server misconfiguration |
| 401 | `Agent '<name>' key has expired on <date>.` | Past `ExpiryDate` |
| 403 | `IP '<ip>' is not whitelisted for agent '<name>'.` | `AllowedIPs` is non-empty and your IP isn't in it |

All rejection bodies share this shape:

```json
{ "success": false, "data": null, "message": "...", "status": 401 }
```

### Trying it in Swagger

If Swagger is enabled (`/swagger`), click **Authorize** and paste your key — it gets
attached to every "Try it out" call automatically.

---

## 2. REST API reference

Base URL: `https://<your-scoreClientSocket-host>` (ask the platform owner for the
production host). All routes below are prefixed with `/api`.

Global limits that apply to **every** route in this section:
- **Rate limit**: 5 requests/second per caller IP (429 `Too Many Requests` beyond that;
  server-configurable, may differ per environment).
- **CORS**: open to any origin unless the deployment restricts it.

### `GET /api/Scorecard` (alias: `/api/bfrateScoreborad`)
Returns a scorecard widget URL for an event (cached).

| Query param | Type | Required | Notes |
|---|---|---|---|
| `eventId` | string | yes | |
| `link` | int | no (default 0) | scorecard variant/link id |
| `pid` | int | no (default 0) | provider id |
| `color` | string | no | appended to the returned URL as `&color=` |
| `font` | string | no | appended to the returned URL as `&font=` |

Response:
```json
{
  "EventID": 12345,
  "scoreUrl": "https://.../scoreboard?id=12345&color=000000",
  "streamingUrl": "https://..."
}
```
Empty `eventId` or a cache/provider miss returns an empty object (all fields default).

### `GET /api/ScoreIframe`
Returns an embeddable iframe payload for an event (proxied live from ScoreProvider, not
cached).

| Query param | Type | Required |
|---|---|---|
| `eventId` | string | yes |
| `link` | int | no (default 0) |
| `color` | string | no |
| `font` | string | no |

Response (`clsResponse` shape used across this API):
```json
{ "success": true, "data": { /* provider-supplied iframe payload */ }, "message": "", "status": 200 }
```
`success: false` with a message means `eventId` was missing or ScoreProvider is
unreachable/not configured.

### `GET /api/CheckWidget`
HEAD-checks whether a widget URL is reachable — used before showing a live-video iframe so
you don't render a broken embed.

| Query param | Type | Required |
|---|---|---|
| `url` | string | yes |

Response: `{ "available": true | false }`

### `GET /api/widget-proxy`
Server-side reverse proxy for the Betfair video player, stripping `X-Frame-Options` /
`Content-Security-Policy` so it can be iframed from any origin. **Only**
`https://videoplayer.betfair.com/...` URLs are allowed (SSRF guard) — anything else returns
`400 Bad Request`.

| Query param | Type | Required |
|---|---|---|
| `url` | string | yes, must start with `https://videoplayer.betfair.com/` |

Response: the proxied HTML (`Content-Type` passed through), or `502` if the upstream is
unreachable.

### `GET /api/EventList`
All events currently in this instance's local cache.
```json
{ "issuccess": true, "count": 2, "data": [ /* EventState objects, see §3 */ ] }
```

### `GET /api/EventInfo`
Single cached event's state.

| Query param | Type | Required |
|---|---|---|
| `eventId` | int | yes |

```json
{ "issuccess": true, "data": "<JSON-serialized EventState>" }
```
`404` if the event isn't in this instance's cache (each instance has its own local cache —
see the Architecture note in §3).

### `GET /api/ClearEvent`
Removes an event from the cache. `eventId=0` (or omitted) clears **all** events on **every**
instance (broadcasts the clear). Mainly an operational/admin tool, not typically needed by
integrators.

### `GET /api/ClearlinkCache`
Clears cached scorecard links (from `/api/Scorecard`). `eventId="0"` (default) clears all.

### `GET /api/ConnectionCount`, `GET /api/ConnectionStats`, `GET /api/InstanceStats`
Operational/monitoring endpoints (active connection counts, daily stats, Cloud Run instance
lifecycle). Not needed for a typical score integration — see the controller source if you
need them for your own health dashboards.

### `GET /api/ip`
Diagnostic endpoint — returns this server's own outbound IP and current time. Not
score-related.

---

## 3. Real-time socket (SignalR)

### Connecting

```
wss://<host>/clientScore?key=<your-agent-key>
```

Using the [`@microsoft/signalr`](https://www.npmjs.com/package/@microsoft/signalr) client:

```js
const connection = new signalR.HubConnectionBuilder()
  .withUrl(`https://<host>/clientScore?key=${encodeURIComponent(AGENT_KEY)}`)
  .withAutomaticReconnect()
  .build();

connection.on('Score', (fullScore) => { /* see §4 */ });
connection.on('ShortScore', (shortScore) => { /* see §4 */ });

await connection.start();
```

### Client → server methods (what you call)

| Method | Args | Effect |
|---|---|---|
| `getscore` | `strEventIDs` — comma-separated event ids, e.g. `"123,456"` | Subscribes you to full-score updates for those events. Sends you the cached `Score` immediately (if any), then pushes every future update. |
| `disconnectscore` | `strEventIDs` | Unsubscribes from full-score updates for those events. |
| `getShortScore` | `strEventIDs` | Same as `getscore` but for the `ShortScore` stream. |
| `disconnectShortScore` | `strEventIDs` | Unsubscribes from short-score updates. |
| `getupdateScore` | `strEventIDs` | **Heartbeat — call this periodically (see below).** Keeps the event "active" so the server keeps polling it; does not by itself trigger a resend. |
| `Ping` | none | Replies with a `Pong` event. Simple liveness check. |

**Heartbeat requirement:** the server has a pull-based poll loop (`ScorePoll`, default every
5s) that only refreshes events with recent client interest. An event is considered active
for `ScorePoll.ActiveWindowSeconds` (default **90 seconds**) after the last `getscore` /
`getupdateScore` / `getShortScore` call for it. Call `getupdateScore` on an interval well
under 90s (score-webapp uses 15s) for every event id you're watching, or you may stop
receiving updates if the platform's server-to-server push connection has a gap.

### Server → client events (what you receive)

| Event | Payload | When |
|---|---|---|
| `Score` | full score object (see §4) | On `getscore` (cache hit), and on every live update for that event |
| `ShortScore` | short score object (see §4) | On `getShortScore` (cache hit), and on every live update for that event |
| `Pong` | none | Reply to your `Ping` call |

There is no explicit "connected" event — a successful `connection.start()` (or your
SignalR client's `onreconnected`) is your signal that you're live.

### Reconnection

Use `.withAutomaticReconnect()` (as above). On reconnect, re-issue `getscore` /
`getShortScore` for every event id you still care about — subscriptions are per-connection
and are not restored automatically by the server.

### Manual testing

`scoreClientSocket`'s own root page (e.g. `http://<host>/`) is a minimal test harness: paste
the hub URL and your agent key, click **Connect Socket**, then add event ids to watch full
score and short score updates live in the browser.

---

## 4. Score data reference — what you get per sport

`scoredata` (the `Score` event payload) and `shortscoredata` (the `ShortScore` event
payload) are **opaque pass-through objects** as far as scoreClientSocket is concerned — it
relays exactly what `ScoreProvider` produces, without inspecting or reshaping them. Their
actual shape (and the sport-specific differences below) is defined upstream.

### ⚠️ Wire format note: keys are abbreviated, not the "nice" names

These payload classes carry Newtonsoft `[JsonProperty("...")]` aliases for talking to
Betfair's own API (e.g. `na` ↔ `"name"`), but the **live socket push does not use those
aliases** — it goes out through SignalR's default `System.Text.Json` protocol, which
serializes the raw (already-abbreviated) C# member names. So what you actually receive over
the wire uses short keys like `na`, `sc`, `hts`, `eid`, `etid`, `gsq`, `isrv` — **not**
`name`, `score`, `halfTimeScore`, etc. The glossary in each table below is your field-name
reference.

### `eventTypeId` (`etid` / `eti`) → sport

| Value | Sport |
|---|---|
| 1 | Soccer |
| 2 | Tennis |
| 3 | Golf |
| 4 | Cricket |

### Full score (`Score` event)

**Base fields (all sports that emit a full score):**

| Field | Type | Meaning |
|---|---|---|
| `etid` | int | event type id (sport, see table above) |
| `eid` | int | event id |
| `sc` | object | the score object — see per-sport breakdown below (`sc.hm` = home team/player, `sc.aw` = away) |
| `st` | string | status |
| `ms` | string | match status |
| `te` | int | soccer match clock, seconds (only meaningful for soccer) |
| `ert` | int | elapsed regular time (soccer) |
| `fte` | object | `{ h, m, s }` — full time elapsed as hour/min/sec |
| `cset` | int | current set number (tennis) |
| `hs` | bool | "has sets" flag (tennis) |
| `ud` | array | update/event log: `[{ ut (time), uid, mt, ert, ty (type), uty (updateType), team, tname }]` |

**`sc.hm` / `sc.aw` (per-team/player fields, `betfairTeam`):**

| Field | Type | Meaning | Sports |
|---|---|---|---|
| `na` | string | name | all |
| `sc` | string | score (goals for soccer; points for tennis) | all |
| `hts` | string | half-time score | soccer |
| `ftc` | string | full-time score | soccer |
| `pes` | string | penalties score | soccer |
| `peseq` | array | penalties sequence (omitted when empty) | soccer |
| `games` | string | games won in current set | tennis |
| `sets` | string | sets won | tennis |
| `gsq` | array of string | game sequence (point-by-point) | tennis |
| `isrv` | bool? | is this side currently serving | tennis |
| `sbr` | int? | service breaks | tennis |
| `nyfc` / `nrfc` / `nfc` | int | yellow / red / total cards (omitted when 0) | soccer |
| `nfco` / `nfcofh` / `nfcosh` | int | corners (total / 1st half / 2nd half, omitted when 0) | soccer |
| `bp` | int | booking points (omitted when 0) | soccer |
| `hl` | bool | highlight flag (omitted when false) | soccer |
| `in1` / `in2` | object | `{ rn (runs), wi (wickets), ov (overs) }` — cricket innings 1/2 | cricket (see caveat below) |

`sc` (the score wrapper itself, one level up from `sc.hm`/`sc.aw`) can also carry aggregate
soccer counters (`nyfc`, `nrfc`, `nfc`, `nfco`, `nfcofh`, `nfcosh`, `bp`) mirroring the
per-team ones above.

**Cricket caveat:** the model has cricket fields (`in1`/`in2`, `sfb` = ball-by-ball state,
`cday`, `mt` = match type) — but in the live pipeline today, **cricket events never emit a
`Score` event at all**. Cricket data comes from a separate CricFeed WebSocket source that
only produces short-score data (see below). If you're integrating cricket, subscribe to
`ShortScore` only — don't wait on `Score` for cricket events.

### Short score (`ShortScore` event)

Base shape (no key aliasing at all — these ARE the wire names):

| Field | Type | Meaning |
|---|---|---|
| `eti` | int | event type id (sport) |
| `eid` | string | event id |
| `en` | string | event name, e.g. `"Team A v Team B"` |
| `te1n` / `te2n` | string | team/player 1 and 2 names |
| `t1s` / `t2s` | string | team/player 1 and 2 scores |
| `pt` | int | period/time (meaning depends on sport, see below; omitted when 0) |
| `t1set` / `t2set` | string | sets won (tennis) |
| `t1p` / `t2p` | string | points/games (tennis) |
| `currBatting` | string | which team is currently batting (cricket) |
| `currBattingTeamScore` | string | current batting team's score (cricket) |

**Per sport:**

| Sport | `t1s`/`t2s` | `pt` | `t1set`/`t2set`, `t1p`/`t2p` | `currBatting*` |
|---|---|---|---|---|
| Soccer (1) | goals | match clock (seconds) | not set | not set |
| Tennis (2) | games in current set | current set number | sets won / points-games | not set |
| Cricket (4) | not built this way — see below | — | not set | populated |
| Golf (3) | — | — | — | — |

- **Soccer/tennis** short scores are derived server-side from the same full-score payload
  (`ShortScore` is a summarized view of `Score`) — see the table above for exactly which
  full-score field feeds which short-score field.
- **Golf** never produces a short score via this path.
- **Cricket** short scores come from a *different, independent* source — a live CricFeed
  WebSocket subscription — whose messages already arrive pre-shaped as `ShortScore` JSON
  (hence `currBatting`/`currBattingTeamScore` being populated only here). Cricket events
  only ever receive `ShortScore`, never `Score`.

### Sample payloads

**Soccer `Score`:**
```json
{
  "etid": 1, "eid": 30123456, "st": "IN_PLAY", "ms": "1st Half", "te": 1523, "ert": 1523,
  "fte": { "h": 0, "m": 25, "s": 23 },
  "sc": {
    "hm": { "na": "Team A", "sc": "1", "hts": "0", "ftc": "0", "nyfc": 2, "nfco": 3 },
    "aw": { "na": "Team B", "sc": "0", "hts": "0", "ftc": "0" }
  }
}
```

**Soccer `ShortScore`:**
```json
{ "eti": 1, "eid": "30123456", "en": "Team A v Team B", "te1n": "Team A", "te2n": "Team B", "t1s": "1", "t2s": "0", "pt": 1523 }
```

**Tennis `Score`:**
```json
{
  "etid": 2, "eid": 30234567, "cset": 2, "hs": true,
  "sc": {
    "hm": { "na": "Player A", "sc": "30", "games": "3", "sets": "1", "isrv": true, "gsq": ["15-0","15-15","30-15"] },
    "aw": { "na": "Player B", "sc": "15", "games": "2", "sets": "0", "isrv": false }
  }
}
```

**Tennis `ShortScore`:**
```json
{ "eti": 2, "eid": "30234567", "en": "Player A v Player B", "te1n": "Player A", "te2n": "Player B", "t1s": "3", "t2s": "2", "pt": 2, "t1set": "1", "t2set": "0", "t1p": "30", "t2p": "15" }
```

**Cricket `ShortScore`** (field values illustrative — exact shape/values are whatever
CricFeed's own socket sends):
```json
{ "eti": 4, "eid": "30345678", "en": "Team A v Team B", "te1n": "Team A", "te2n": "Team B", "t1s": "156/4 (18.2)", "t2s": "", "currBatting": "Team A", "currBattingTeamScore": "156/4" }
```
(No `Score` event for cricket — see caveat above.)

---

## 5. Quick-start checklist

1. Get an agent key (and, if you need IP restriction, give the platform owner the IPs your
   backend calls from).
2. For REST: send `X-App: <key>` header (or `?key=`) on every `/api/*` call.
3. For the socket: connect to `wss://<host>/clientScore?key=<key>`.
4. Call `getscore("<eventId>")` and/or `getShortScore("<eventId>")` for each event you want.
5. Re-call `getupdateScore("<eventId>")` at least every ~60–90s per event to keep it active.
6. Handle `Score` / `ShortScore` events — remember cricket only sends `ShortScore`.
7. On reconnect, re-subscribe (`getscore`/`getShortScore`) — the server doesn't remember
   your subscriptions across a dropped connection.
