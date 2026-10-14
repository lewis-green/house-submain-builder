# House Panel Designer

Designs the electrical panel for a house, one submain at a time. Enter how many
dimmed lighting, switched and LED tape circuits a submain carries; the app works
out how many Shelly Pro devices that needs, lays them out in a DIN enclosure to
house best practice, draws the result to scale, and produces a printable panel
drawing with its circuit schedule and a bill of materials.

## Running it

Everything in containers, which is how it is meant to be deployed:

```bash
docker-compose up --build
```

Then open **http://localhost:8080**. The UI is served by nginx, which proxies
`/api` through to the API container, so the browser only ever talks to one
origin.

### Running it for development

You need Docker, the .NET 10 SDK and Node 22.

**Three terminals.**

```bash
# 1. Database
docker-compose up -d postgres

# 2. API — migrates and seeds the catalogue on startup, listens on :5020
dotnet run --project src/PubInvest.HouseConfig.Api

# 3. UI — listens on :5173 and proxies /api to the API
cd ui && npm install && npm run dev
```

Then open **http://localhost:5173**.

If your `docker compose` is the plugin subcommand rather than the standalone
`docker-compose` binary, use whichever you have.

### Signing in

Auth is off by default, at both ends: `HouseConfig__AuthEnabled` is `false` on
the API and the UI is given no identity provider, so the app opens straight onto
the houses list.

To turn it on, give the UI container a realm and a client and bring it up again:

```bash
OIDC_AUTHORITY=https://keycloak.example/realms/house \
OIDC_CLIENT_ID=panel-ui \
docker-compose up -d ui
```

plus `HouseConfig__AuthEnabled=true` and the matching `Keycloak` settings on the
API. The UI reads its two settings **when the container starts**, not when the
image was built: `docker-entrypoint.d/30-runtime-config.sh` writes them to
`/config.json` and the app fetches that before its first render. So the image
published from `main` is the one every environment runs — a new realm is a
restart, not a rebuild. nginx serves `/config.json` with `Cache-Control:
no-store`, because a stale copy would point a redeployed app at an identity
provider it no longer uses.

Leaving either variable blank leaves sign-in off, which is what development
does: `ui/public/config.json` ships blank and Vite serves it as-is. To exercise
sign-in against a local Keycloak with `npm run dev`, fill that file in — but
don't commit it filled.

## Seeing it work

1. **Add a house** on the first screen.
2. **Add submain**. Pick the 3-row enclosure, say how the panel is installed —
   whether the cables enter at the bottom, and whether it needs its own isolator
   — then enter circuit counts, say 4 dimmed and 2 switched. The preview updates
   as you type and tells you what the panel needs and whether it fits.
3. **Generate panel**. The drawing appears to scale: termination across the top
   row — circuit terminals, then a +24V and a -24V joint for each tape run, then
   the two-pole isolator hard against the right — and the Shelly kit below it.
   The submain and its panel are saved in one transaction, so a design the
   generator refuses, or a request that fails on the way, leaves nothing in the
   house.
4. **Tap a device** to name its channels: a room and a circuit name for each
   one, with the rooms already used in the house offered as you type. Both reach
   the drawing and the schedule.
5. **Long-press and drag** a device to move it. Invalid targets go red; the move
   is saved and survives re-generation.
6. **Issue & download PDF** on the panel screen. Page 1 is the drawing, page 2
   the circuit schedule and bill of materials. Revisions are numbered by the
   server, and the screen says when the PDF on offer is behind the panel.
7. **Bill of materials** lists the parts to order, with anything not mounted in
   the panel (the LED driver) marked as external.

### The catalogue is not confirmed yet

Several parts in `seed/catalogue.v1.json` still carry `ASSUMED` part numbers —
the two-pole isolator, the 24V joint blocks, the external driver wattages, the
jumper bar's ways, the line jumper bar that bridges a multi-cable circuit's
terminals (`ASSUMED-LINE-JUMPER-BAR-10`) and the end stops per bank. Confirm them before ordering from
a generated bill of materials.

The bill of materials carries **no prices**. It is a parts list — part number,
description, quantity, and whether the part is mounted in the panel — and
costing happens wherever your pricing actually lives.

**Confirmed from real hardware:** the Shelly Pro Dimmer is 2 channels at 1 DIN
module, the Shelly Pro relay is 4 channels at 3 modules, and three WAGO
2003-7646 fit in one module. Everything else needs checking against a datasheet
before anyone orders from a generated BOM.

Parts can be corrected in the **Catalogue** screen (linked from the houses list)
without a redeploy — widths there are entered in DIN modules, not internal slot
units.

## Tests

```bash
dotnet test          # 333 tests (127 Domain, 27 Data, 179 Api). Needs Docker: the Data and Api suites use Testcontainers.
cd ui && npm test    # 235 Vitest tests in jsdom
cd ui && npm run build   # the real typecheck: `tsc -b`, covering src, tests and e2e
cd ui && npm run lint    # oxlint, 0 warnings
cd ui && npm run test:e2e   # 33 Playwright tests in WebKit (see below)
```

CI runs all of these on every push and pull request, builds both container
images, and pushes them to GHCR from `main`.

### End to end, in Mobile Safari's engine

`ui/e2e/` is a Playwright suite that runs in WebKit with iPhone 15 emulation —
touch drag with the real Safari event sequence, the bottom sheet, the PDF link,
horizontal overflow at four widths and again with 200% text, the wizard, Home
Screen metadata, and a few checks with a mouse (30 tests). It brings up its own stack and takes it down again, so it needs
nothing running first and never touches a development database:

```bash
cd ui
npx playwright install webkit   # once
npm run test:e2e
```

What it starts (see `ui/e2e/stack.ts`): a throwaway `postgres:17` container
on port 55434, the API on port 5021 pointed at it (`dotnet run`, so the API
must have been restored and built once), and Vite on port 5199 proxying
`/api` to that API. All three ports, the `docker` and `dotnet` binaries, can
be overridden with `E2E_DB_PORT`, `E2E_API_PORT`, `E2E_UI_PORT`, `E2E_DOCKER`
and `E2E_DOTNET`. The container is removed afterwards, data and all.

## How it fits together

```
src/PubInvest.HouseConfig.Domain/   the generator: a pure function, no EF, no I/O
src/PubInvest.HouseConfig.Data/     EF Core, Postgres, catalogue seeding
src/PubInvest.HouseConfig.Api/      minimal API, revisions, PDF and BOM export
ui/                                 React 19 + Vite + TypeScript + Tailwind
seed/catalogue.v1.json              devices, enclosures and the default ruleset
docs/superpowers/                   the design spec and the four implementation plans
```

Three ideas are worth knowing before reading the code:

**Widths are counted in thirds of a DIN module.** Three WAGO 2003-7646 fit in
one module, so a third is the smallest real unit; counting in thirds keeps every
width a whole number and the packer free of rounding. A dimmer is 3, a relay 9, a
terminal 1. `DinUnits.PerModule` is the constant; anything a person reads is
converted back to modules first.

**Terminals have an order, and a circuit can land on several.** Tap the run of
terminals on the panel screen to open the terminal order: reorder the circuits
along the rail (C1 is always the first terminal, and the numbers follow
position) and set how many terminals each circuit lands on, up to 12, for
several cables to one channel. A circuit on several terminals is labelled
C3.1, C3.2 and so on, and its line tiers are bridged with a cut-to-length line
jumper bar, counted per bridged circuit as ceil(n/10) with offcuts not shared;
its part number (`ASSUMED-LINE-JUMPER-BAR-10`) is assumed until confirmed, and
the panel warns if a circuit is bridged but the ruleset has no usable line bar.
Terminals follow the
terminal order, so they can't be dragged. Terminal order is independent of the
circuit sequence: moving terminals never changes which Shelly channel a circuit
is on. Saving is one request, `PUT /submains/{id}/terminals`, that saves the
order and rebuilds the panel in the same transaction (409 if the panel changed
elsewhere, 400 for a bad list, 422 if the terminals won't fit — nothing is
saved); `GET /submains/{id}/terminals` reads the current order and counts.

**The server decides every layout.** The UI never computes one — it draws what
`/design/preview` or `/design/generate` returned. That is what keeps the drawing,
the schedule and the parts list from ever disagreeing.

**Circuits ask for devices; fixtures are asked for by hand.** A light, a
switched load, a blind and a tape run are circuits: they carry a room and a
name, and the ruleset says which device drives each kind. An energy meter or a
LAN switch feeds nothing, so no count of anything will ever imply one — those are
added to the submain by device type and quantity, and packed like everything
else.

**A ruleset is versioned, and the seeder upgrades it.** Catalogue rows are left
alone once they exist, but a shipped ruleset whose version has gone up replaces
the stored one. Without that, a database seeded before a rule existed would keep
the old rules for ever — adding a preferred device for a new kind of circuit
would leave every existing house generating nothing for it.

**Two facts about the install, not the rules.** Whether the cables enter at the
top or the bottom, and whether this submain is isolated upstream, are stored on
the submain and changeable on the panel screen. The packer always builds
downward from row 0; a bottom-fed panel is the same layout mirrored, so one
ladder of rules describes both and the spare row ends up away from the glands.
Changing either on the panel screen is one request — `PATCH /submains/{id}`
with `rebuild: true` — that saves the option and regenerates the panel in the
same transaction, so a rebuild the generator refuses leaves the option as it
was. (`POST /projects/{id}/submains` with `generate: true` is the wizard's
equivalent.)

**An issued revision embeds what it used.** `PanelRevision` stores the layout,
the ruleset payload and the catalogue entries as they were, so editing a rule or
a part next month cannot retroactively change what a drawing in someone's folder
said. The same snapshot is how the server knows whether a PDF is still current:
`GET /submains/{id}/revisions` compares what each revision embeds with what
issuing now would embed, so a renamed circuit or room — which moves no device
and bumps no layout version — still marks the PDF on offer as out of date. The
snapshot includes the two install options, so an option saved without a rebuild
reads as out of date too.

**Sign-in is a separate chunk.** `main.tsx` imports the OIDC provider and gate
only when the runtime config names an identity provider, so installs that leave
sign-in off (development, and any deployment with the two settings blank) never
download that code.
