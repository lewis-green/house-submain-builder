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

Auth is off in development (`HouseConfig:AuthEnabled` is `false` in
`appsettings.Development.json`), so there is nothing to sign in to. If your
`docker compose` is the plugin subcommand rather than the standalone
`docker-compose` binary, use whichever you have.

## Seeing it work

1. **Add a house** on the first screen.
2. **Add submain**. Pick the 3-row enclosure, then enter circuit counts — say 4
   dimmed and 2 switched. The preview updates as you type and tells you what the
   panel needs and whether it fits.
3. **Generate panel**. The drawing appears to scale: termination across the top
   row — circuit terminals, then a +24V and a -24V joint for each tape run, then
   the two-pole isolator hard against the right — and the Shelly kit below it.
4. **Tap a device** to name its channels. Names propagate straight back to the drawing.
5. **Long-press and drag** a device to move it. Invalid targets go red; the move
   is saved and survives re-generation.
6. **Issue & download PDF** on the panel screen. Page 1 is the drawing, page 2
   the circuit schedule and bill of materials.
7. **Bill of materials** lists the parts to order, with anything not mounted in
   the panel (the LED driver) marked as external.

### The catalogue is not confirmed yet

Several parts in `seed/catalogue.v1.json` still carry `ASSUMED` part numbers —
the two-pole isolator, the 24V joint blocks, the external driver wattages, the
jumper bar's ways and the end stops per bank. Confirm them before ordering from
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
dotnet test          # 173 tests. Needs Docker: the Data and Api suites use Testcontainers.
cd ui && npm test    # 84 tests
cd ui && npm run build   # the real typecheck: `tsc -b` is stricter than `tsc --noEmit`
```

CI runs all of these on every push and pull request, builds both container
images, and pushes them to GHCR from `main`.

`ui/e2e/` holds a Playwright suite covering the touch-drag path. It has never
been run here — the chromium download times out on this network. `npx playwright
install chromium` then `npm run test:e2e`, with the API and database already up.

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

**The server decides every layout.** The UI never computes one — it draws what
`/design/preview` or `/design/generate` returned. That is what keeps the drawing,
the schedule and the parts list from ever disagreeing.

**An issued revision embeds what it used.** `PanelRevision` stores the layout,
the ruleset payload and the catalogue entries as they were, so editing a rule or
a part next month cannot retroactively change what a drawing in someone's folder
said.
