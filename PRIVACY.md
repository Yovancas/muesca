# Privacy policy

Muesca (formerly ClaudeBar for Windows) is a local app: your Claude Code credentials, transcripts, prompts and usage
numbers never leave your computer. The one thing it sends on its own is an **anonymous usage
count**, so we know roughly how many people use it. This page explains exactly what that is.

## What is sent

At most **once a day** (by your local date), Muesca sends one HTTPS request to
`https://claudebar-stats.claudebar-stats-worker.workers.dev/v1/ping` with this body and nothing else:

```json
{"v":"0.5.0","e":"free","l":"es","d":1,"m":1,"n":0}
```

| Field | Meaning |
|-------|---------|
| `v` | App version |
| `e` | Edition: `free` (Microsoft Store), `pro` (Pro purchased) or `github` (GitHub / winget edition) |
| `l` | Interface language (`en`, `es`, `de`…) |
| `d` | Always `1`: first ping today |
| `m` | `1` if it's the first ping this month |
| `n` | `1` only on the first ping of a new installation (`0` if you were already using Muesca and updated) |

The request has no cookies and its User-Agent is just `ClaudeBar` (the old name, kept on purpose: see [docs/branding.md](docs/branding.md)).

## What is not sent

No identifier of any kind (no install ID, account, e-mail or random token), no computer or user
name, no Windows version, no hardware details, and nothing about your Claude Code usage, quota,
projects or files. Each ping is indistinguishable from any other ping with the same six values.

## What the server keeps

The server only adds the boxes up **per day, version, edition and language** (for example, "on
2026-10-30, 120 people on 0.5.0/free/es were active"). No row represents a person. It **does not
store IP addresses and keeps no request logs**.

Cloudflare, which runs the server, necessarily processes your IP address in transit to deliver the
request, and uses it for a rate limit of 20 pings per minute that lives in memory and is not stored.

The server code is public in this repository: [`server/stats-worker/`](server/stats-worker/).

## When nothing is sent

- Before you have seen the welcome screen of a new installation.
- If you were already using Muesca, the first time the new version starts it shows a one-time
  notice and sends nothing for at least 10 minutes.
- When the counter is turned off.
- If the request fails (no network, server error or rate limit), nothing is queued: it simply
  tries again in the next hourly check.

## How to turn it off

Any of these stops it immediately:

- **Settings › System › Anonymous usage count** in the Muesca panel.
- The switch on the welcome screen.
- The environment variable `CLAUDEBAR_NO_STATS=1`.

Other network calls Muesca makes (Anthropic quota and plan, service status, public prices,
updates) are described in [SECURITY.md](SECURITY.md).

## Lyrics (optional, Pro)

When synced lyrics are on, Muesca sends the **title and artist** of the song that is playing to
[lrclib.net](https://lrclib.net) to download its lyrics. Nothing else is sent: no account, no
identifier and nothing about Claude Code. Like any server, lrclib.net sees your IP address when it
receives the request. The lyrics it finds are kept only in a cache on your computer.

Lyrics are a Pro feature and are only requested while music and lyrics are both on. You can turn
them off in **Settings › Notch › LRCLIB lyrics**; with them off, Muesca makes no request to
lrclib.net at all.

## Microsoft Store edition

The Microsoft Store edition checks your Pro license and processes purchases through the Microsoft
Store (`Windows.Services.Store`): Microsoft handles the payment, under the
[Microsoft Privacy Statement](https://privacy.microsoft.com/privacystatement). Muesca never sees or
asks for your payment details.

It never checks GitHub for updates: the Microsoft Store updates it.

## Contact

Questions or requests: open an issue at
[github.com/Yovancas/muesca/issues](https://github.com/Yovancas/muesca/issues).

Spanish version: [PRIVACY.es.md](PRIVACY.es.md).
