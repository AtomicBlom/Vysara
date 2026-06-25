# MU5 — Text shaping (HarfBuzz): scope & plan

Scoping for the next text milestone: replacing the current advance-based, per-codepoint glyph layout with real
shaping (kerning, and — for the default Cascadia Code font — programming ligatures). This doc captures the current
pipeline, the one architectural decision that gates everything, a recommendation, and a phased plan. It does **not**
change behaviour yet; it is the map for the work.

## Where text stands today

- **Atlas is keyed by Unicode codepoint.** `FontAtlas` holds `Dictionary<int, Glyph>` keyed by codepoint; `Glyph`
  carries an EM `Advance`, plane bounds, and atlas UVs. The baked `ui-default.font.json` stores one entry per
  codepoint with a per-glyph `advance` — **no kerning pairs, no glyph ids, no shaping tables**.
- **Layout is advance-based LTR, no shaping.** `UIDrawList.AddGlyphRun` walks the string char-by-char: emit the
  glyph quad at the pen, then `penX += font.Advance(ch)`. `FontAtlas.MeasureWidth` sums per-codepoint advances.
- **Measure/layout consumers (the seam's call sites):** `TextBlock.MeasureOverride`, `TextBox` (caret X via
  `MeasureWidth`), `UIDrawList.AddGlyphRun` (the compositor), and `SUIsei.Text.Tool/PreviewRenderer` (its own loop).
- **The baker (`SUIsei.Text`) parses only `head`/`OS/2`/`cmap`/`glyf`/`CFF`** — enough to rasterize MTSDF cells. It
  parses **no `GSUB`/`GPOS`/`kern`**. `MUxDecisions.md` already lists *"subset-`.ttf` emission for HarfBuzz"* and
  *kerning/`GPOS`* as deferred — i.e. HarfBuzz shaping was always the intended MU5 direction.
- **Cascadia Code is monospaced** → there is no meaningful `kern`. Its shaping value is **programming ligatures**
  (`->` `=>` `!=` `==` …) via `GSUB`. A ligature is a single glyph with **no codepoint**, so it cannot be addressed
  by the current codepoint-keyed atlas at all.

## The one decision that gates the milestone

**Atlas addressing: codepoint → glyph id.** HarfBuzz shapes a UTF-8 run into a sequence of `(glyphId, xAdvance,
xOffset, yOffset, cluster)`. Ligatures and contextual forms are glyph ids with no codepoint. Therefore the atlas and
`FontAtlas` lookup must move from codepoint keys to **glyph-id keys** (with a codepoint→gid `cmap` kept for the
non-shaped fast path and for `TextBox`-style caret math). This ripples through:

- `SUIsei.Text` baker: select glyphs by id (include the ligature/alternate glyphs a font's `GSUB` can produce, not
  just `cmap` codepoints), and write `glyphId` (+ the source codepoint where one exists) into the atlas JSON.
- `FontAtlas`: key `_glyphs` by glyph id; expose codepoint→gid (`cmap`) for measurement without shaping.
- `Glyph`: carry `GlyphId` alongside (or instead of) `Codepoint`.
- The atlas JSON format version bumps; the committed `ui-default` atlas is **regenerated** (page pixels are
  unchanged — only the JSON keying/glyph set changes — but it must be re-baked and re-verified).
- `SUIsei.Text.Tool/PreviewRenderer` and the `FontAtlasTests` that assert codepoint lookup/`MeasureWidth`.

This is the load-bearing, mostly one-way change. Everything else (the shaper, the seam) is comparatively local.

## Shaper backend: HarfBuzzSharp vs. hand-rolled

| | **HarfBuzzSharp** (recommended) | Hand-rolled `GSUB`/`GPOS` in `SUIsei.Text` |
|---|---|---|
| Correctness | Industry-standard; ligatures, kerning, marks, complex scripts | We'd reimplement a large spec; ligatures alone are tractable, full coverage is not |
| Dependency | **Native binaries per RID** (incl. `win-arm64` — this dev box) | None; stays single-dependency (StbImageSharp) |
| NativeAOT | HarfBuzzSharp ships AOT-friendly bindings, but the native lib + RID story needs verifying on `win-arm64` first | Trivially AOT-clean (pure managed) |
| Effort | Add package + shaper seam + glyph-id atlas | Glyph-id atlas + a real `GSUB` (and later `GPOS`) implementation in the reader |

**Recommendation: HarfBuzzSharp**, consistent with the `MUxDecisions.md` intent. The hand-rolled path only wins if
staying zero-native-dependency is a hard constraint; if so, scope it to `GSUB` ligature substitution + `kern`/`GPOS`
pair kerning and accept no complex-script support. **Open question for confirmation before P3 lands:** is adding a
native dependency acceptable here, and does HarfBuzzSharp resolve + run under .NET 10 on `win-arm64`? (P0 below
answers the runtime half.)

## The seam (backend-independent)

Introduce one shaping seam both backends satisfy, so the call sites stop hand-rolling advances:

```
readonly record struct ShapedGlyph(Glyph Glyph, float PenXEm, float XOffsetEm, float YOffsetEm, int Cluster);
interface ITextShaper { ShapedRun Shape(ReadOnlySpan<char> text, FontAtlas font, ...); }  // ShapedRun: glyphs + WidthEm
```

`AddGlyphRun`, `MeasureWidth`, `TextBlock`, `TextBox`, and `PreviewRenderer` consume `ShapedRun` instead of looping
on codepoints. The default shaper reproduces today's behaviour (codepoint→glyph, sum advances) so nothing changes
until a real shaper is installed. `FontAtlas` owns the active `ITextShaper` (swappable). Keep the hot path
allocation-aware (the compositor caches draw lists, so per-relayout shaping is acceptable, but avoid per-frame work).

## Status

- **P2 — shaping seam: DONE** (`ITextShaper`/`ShapedGlyph` + `SimpleTextShaper`; `FontAtlas.MeasureWidth` and
  `UIDrawList.AddGlyphRun` route through `FontAtlas.Shaper`; behaviour-preserving, tested).
- **P1 — glyph-id atlas (opt-in): DONE.** Runtime: `Glyph.GlyphId` + dual-mode `FontAtlas` (`HasGlyphIds`,
  `TryGetGlyphById`, `TryGetGlyphId`); a codepoint-only atlas is unchanged. Baker: `"keying": "glyphId"` (default
  off) emits `glyphId`(+`unicode`) — the **tool and committed `ui-default` stay code-point-keyed**, honouring the
  "make it an option" requirement. Verified end to end against CascadiaCode.ttf.
- **Pluggability split: DONE.** `ITextShaper` now resolves against an `IGlyphSource` (code-point/glyph-id → `Glyph`,
  advances, EM metrics) rather than a concrete `FontAtlas`, so the **shaping backend and the imagery backend swap
  independently**: a shaper is written against the interface, and a non-MTSDF imagery source (e.g. a bitmap atlas)
  can implement `IGlyphSource`. MTSDF-specific bits (the distance range) stay off the interface. Resolution stays
  inside the shaper (not the compositor) because a code-point-keyed atlas has no glyph ids — routing through the
  source handles both the default and an id-emitting shaper.
- **Platform shapers (DWrite / Core Text): deferred by decision.** A Windows `DWriteTextShaper : ITextShaper` (and a
  macOS Core Text one) is a clean drop-in at this seam — an *optional, capability-gated* backend like Vulkan/DX12,
  not the default. Not pursued now. A full DWrite engine replacement (its *bitmap* rasterizer in place of MTSDF) is a
  separate, larger call: it would be a parallel render path, Windows-only, abandoning resolution-independent,
  cross-platform-parity, AOT-clean MTSDF — so it stays out unless small-text quality ever forces it.
- **GSUB harvest: DONE.** `GsubHarvest` walks the LookupList collecting outputs from Single/Multiple/Alternate/
  Ligature/Reverse + unwrapped Extension subtables (Contextual/Chaining carry none of their own); `OpenTypeFont`
  exposes `CollectGsubOutputGlyphs`; the baker unions these into a glyph-id atlas. Cross-validated against HarfBuzz.
- **HarfBuzz shaper: DONE.** `SUIsei.HarfBuzz` (separate project, keeps `SUIsei` core dependency-light) provides
  `HarfBuzzTextShaper : ITextShaper` over `HarfBuzzSharp`, resolving shaped glyph ids through `IGlyphSource`.
- **Bake + verify: DONE.** The gallery bakes a glyph-id Cascadia atlas at startup, installs the shaper via the new
  `UIHost.CreateRoot(compositor:)` argument, and a headless capture + subagent confirmed `-> => != == >= <= :: |>`
  render as their Cascadia ligature glyphs.

**MU5 is complete:** seam + `IGlyphSource` decoupling, opt-in glyph-id atlas, GSUB harvest, HarfBuzz shaper, and a
live ligature demo. Cross-platform shaping (Linux/macOS native assets) and platform shapers (DWrite / Core Text)
remain available at the seam but are not pursued. (Aside, unrelated to text: the gallery `--selftest` reports a
parallel-vs-serial raster diff of 30 — a pre-existing rasterizer issue, present before this work.)

## Phased plan

- **P0 — De-risk the native lib (no commitment):** add `HarfBuzzSharp` to a throwaway spike, shape `"office =>"`
  against `CascadiaCode.ttf`, and confirm it loads + shapes under .NET 10 on `win-arm64`. Answers the dependency
  question empirically. *(Gate: report result before P3.)*
- **P1 — Glyph-id atlas:** re-key `FontAtlas`/`Glyph` by glyph id; bake `glyphId` + `cmap` into the JSON (format
  bump); regenerate + re-verify the committed `ui-default` atlas; update `FontAtlasTests`/`PreviewRenderer`.
- **P2 — Shaping seam:** add `ShapedGlyph`/`ShapedRun`/`ITextShaper` + a default (current-behaviour) shaper; route
  all call sites through it. No visible change; full test suite stays green. *(Safe to land independently.)*
- **P3 — HarfBuzz shaper:** implement `HarfBuzzTextShaper` (shape → gid/positions/cluster → atlas `Glyph`); feed it
  a font blob (subset emitted by the baker, per `MUxDecisions.md`, or the bundled `.ttf`). Install it as the default.
- **P4 — Wire + verify:** ligatures and kerning flow through measure + layout; `TextBox` caret uses cluster→x
  mapping. Verify programming ligatures render in the gallery (`-> => != ==`) via a headless capture + subagent check.

P2 is the zero-regret increment and can land first under either backend. P1 is the gating change and should be
confirmed before starting. P0 is cheap and answers the native-dependency question with evidence.

## P0 result (spike run — throwaway, outside the repo)

`HarfBuzzSharp` 14.2.0 + `HarfBuzzSharp.NativeAssets.Win32` restored and **ran natively on this `win-arm64` box
under .NET 10** (process arch Arm64) — the native dependency is viable here. Shaping `CascadiaCode.ttf` surfaced a
detail that sharpens P1:

- **Cascadia's programming ligatures are `calt` 1-char → 1-contextual-glyph substitutions, not many-to-one `liga`.**
  `"x => y"` shaped to six glyphs (`425, 861, 1425, 1431, 861, 426`): the `=` and `>` each became a *distinct
  contextual glyph* (`1425`, `1431`) that together draw the `=>` shape. `"!="`/`"=="` likewise map to contextual
  forms (`812`, `1774`, `1377`). Plain `"office"` substitutes nothing.
- **Consequence:** glyph **count** is preserved (so caret/cluster mapping stays simple — one glyph per source char),
  **but** the rendered glyph ids (`1425`, `1431`, `812`, `1774`, …) are reachable only through `GSUB`, never through
  `cmap`. So P1 is mandatory *and* the baker must enumerate **GSUB-reachable** glyphs (walk the lookups, or bake the
  full glyf/CFF glyph set), not just the `cmap` codepoint set, or ligature glyphs will be missing from the atlas.

This reinforces the recommendation (HarfBuzzSharp; hand-rolling `calt` contextual chains is not worth it) and pins
P1's real work: glyph-id keying **plus** GSUB-reachable glyph coverage in the baker.
