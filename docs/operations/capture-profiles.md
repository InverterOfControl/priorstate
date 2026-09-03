# Capture profiles

A capture profile is a **named, versioned** set of browser settings — user agent, viewport,
JavaScript settle time, cookie banner handling. `DE-Standard v1` is seeded on first start.

## Why they are not sliders

The obvious design gives the operator a settings screen with adjustable viewport, user agent and
wait times. It is also the design that loses an argument.

If capture settings can be changed freely at any time, the opposing side has a straightforward
line: the configuration was chosen to produce this result. Answering it means proving what the
settings were at the moment of capture, from a system whose settings the operator controls.

A named, versioned profile removes the argument. The protocol says `DE-Standard v1`. That
designation refers to one immutable row, created before the capture and never edited.

## How changes work

Changing a setting **creates a new version**. It never edits the existing one.

- Existing snapshots keep the version they were captured under, so old protocols keep describing
  what actually happened.
- The new version applies only to captures made after it.
- The previous version is marked superseded — a timestamp, not a deletion.
- The change is written to the audit log.

This is enforced in the database, not just in the API: the migration installs a trigger that
allows `SupersededAt` to be set exactly once and rejects every other update or delete on the
table. See [architecture](/guide/architecture).

## What the baseline profile records

`DE-Standard v1` is a neutral, unauthenticated desktop visit:

| Setting | Value | Why |
|---|---|---|
| Authenticated session | No | An authenticated view is not what a visitor saw. |
| Ad blocker | No | Blocking content changes the rendered page. |
| Viewport | 1920 × 1080 | A common desktop size, stated rather than assumed. |
| Cookie banner | Left as served | Least intervention, easiest to defend. |
| Settle time | 5000 ms | Time for client-rendered content to appear. |

Chromium and crawler versions are not part of the profile's intent — they are read from the
container that actually ran and recorded on the snapshot. What the profile asked for and what
happened are stored separately on purpose.

## Creating a version

```
POST /api/capture-profiles
{
  "name": "DE-Standard",
  "rationale": "Increased settle time to 8s: the pricing page renders its table client-side and 5s was occasionally too short.",
  "conditions": { ... }
}
```

The rationale is required and is shown alongside the version. Write it for a reader who is asking
why the settings changed — because that is who will eventually read it.
