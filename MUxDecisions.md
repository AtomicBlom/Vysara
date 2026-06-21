# MUx — Native MSDF font generation: decisions

Living record of the best-effort decisions taken while building the MUx track (native, cross-platform MTSDF
font-atlas generation) inside the SUIsei submodule. The design brief is **PlanningUI.md §D10**; this file records
where the implementation deviates from, narrows, or pins down that brief.

## Scope taken for v1

- **CPU/software path only.** The §D10 default was GPU-compute with a CPU fallback. Per direction, we ship **only
  the multi-threaded software path** for now and treat GPU as a future addition. The software path is the one §D10
  earmarked as the correctness oracle, so this is the conservative half to build first. The generator is structured
  so a GPU backend can slot in behind the same `IMsdfRasterizer` seam later.
- **Two deliverables:** a reusable **runtime library** (`SUIsei.Text`) and a **`dotnet tool`** (`loom-msdf`) that
  wraps it. The library is usable at runtime (in-memory `Bake` → `FontAtlas`), not just at build time.
- **Hand-rolled OpenType parser, no external font dependency.** Per direction we wrote our own TTF/OTF reader
  rather than adopting a managed parser (SixLabors.Fonts / Typography). Goal: render *a* font of each outline type,
  not full OpenType fidelity. TrueType `glyf` (quadratic) is implemented and verified against the in-repo
  `CascadiaCode.ttf`. CFF/Type2 (cubic, the OTF outline format) is implemented behind the same outline seam; see
  the CFF note below for its verification status.

## Parallelization & vectorization

- **Parallelism:** `System.Threading.Tasks.Parallel` over glyphs, and over scanline rows within each glyph's cell,
  so a fleet of cores is saturated whether the charset is many small glyphs or a few large ones. Determinism is
  preserved because each (glyph, row, texel) writes an independent output — no cross-thread reduction.
- **Vectorization:** the float→byte page quantization and value-mapping use `System.Numerics.Vector<T>` where the
  data is regular. The per-texel distance inner loop (cubic root-finding + signed-distance reduction with the
  msdfgen orthogonality tiebreak) is kept **scalar `double`** for correctness and bit-stability; it is irregular
  and not a clean SIMD target. Fine-grained SIMD of the distance kernel is left as a future optimization.

## Numeric & algorithmic fidelity

- **`double` precision throughout the geometry/distance math**, matching msdfgen (which is `double`). The atlas
  page is quantized to RGBA8 only at the very end.
- **Algorithm is a faithful port of msdfgen**: `edgeColoringSimple` (by-angle corner detection), the per-channel
  **perpendicular-distance selector** (bisector-gated, so the pseudo-distance never notches a convex corner), the
  quadratic closest-point cubic solve, and the `solveCubic`/`solveQuadratic` equation solver. This maximizes
  "visually indistinguishable from the msdf-atlas-gen baseline" (the §D10.5 acceptance bar).
- **Overlapping-contour combiner (load-bearing).** Real fonts build glyphs from *overlapping* contours — Cascadia's
  `E`, for instance, is a stem rectangle plus three separate arm rectangles. A naïve global-min-over-all-edges
  combiner treats the internal overlap edges (e.g. the stem's right edge sitting *inside* an arm) as real
  boundaries and carves false notches/bands through solid strokes. We port msdfgen's winding-aware
  **overlapping-contour combiner**: each contour is sampled into its own selector, contours are merged by winding
  sign, and the contour responsible for the nearest true boundary of the union/difference is chosen. Per-contour
  winding = sign of the signed area (CW fill ⇒ +1, CCW hole ⇒ −1). This was the single biggest correctness fix —
  before it, every cornered glyph had visible artifacts; after it, output matches msdf-atlas-gen.
- **Error correction by true-distance consistency.** A final pass collapses RGB to the true signed distance (the
  alpha channel — a reliable inside/outside oracle) at any texel where the reconstructed median's inside/outside
  disagrees with it. This erases residual false edges from channel medial-axis coincidences while leaving genuine
  corner sharpening (where median and true distance agree) untouched — simpler and more robust here than porting
  msdfgen's newer distance-field error-correction algorithm wholesale.
- **Verified** at HUD size (32px bake, 48px render) against the committed msdf-atlas-gen `ui-default`: a subagent
  judged the two renderings of `Hello, SUIsei MSDF 0123 {}` visually equivalent, no artifacts. The generated atlas
  loads through the unmodified `FontAtlas` (drop-in confirmed).
- **MTSDF channels:** RGB = the per-channel signed *pseudo*-distance (median reconstructs the true distance and
  preserves corners); **A = the plain true signed distance** (min over all edges, no per-color split) — the
  channel SUIsei reserves for outline/glow/weight. `value = distance/pxRange + 0.5`, clamped, ×255.

## Coordinate / layout conventions (the FontAtlas contract — §D10.2)

The emitted JSON + PNG must be a **drop-in** for `SUIsei/FontAtlas.cs`. Conventions pinned by reading that loader,
the committed `ui-default.font.json`, and `SUIsei.Rasterizer`'s sampler:

- **unitsPerEm** from `head`; **metrics** from `OS/2` typo fields: `lineHeight = (sTypoAscender - sTypoDescender +
  sTypoLineGap)/unitsPerEm`, `ascender = -sTypoAscender/unitsPerEm`, `descender = -sTypoDescender/unitsPerEm`
  (y-down). Verified to reproduce the committed Cascadia values (`1.162109375 / -0.927734375 / 0.234375`).
- Distance math is done in **y-up EM/pixel space** (the FreeType/msdfgen frame) so TrueType's native winding
  (CW-outer ⇒ inside-positive) gives correct signs with no reorientation for well-formed fonts. A
  **winding-based safety net** (`Shape.WindingAt`, nonzero rule) detects and flips an inverted overall sign, so
  reversed/odd fonts still come out inside-positive.
- **planeBounds** (EM, y-down, baseline origin) = the ink box expanded by `pxRange/2` on all sides, y-flipped.
  **atlasBounds** = the integer texel rect `[gx, gy, gx+pw, gy+ph]` of the glyph's cell in the page (no half-texel
  offset). This pairing makes a cell-pixel *center* (`px+0.5`, `py+0.5`) map exactly to a sampled texel center
  under `SUIsei.Rasterizer`/`ui.slang`'s `uv*size - 0.5` bilinear footprint — self-consistent generation↔sampling.
  (msdf-atlas-gen reports atlasBounds at `+0.5`; its generation footprint is offset to match. Both are internally
  consistent; we pick the no-offset convention. The atlas is therefore *not byte-identical* to msdf-atlas-gen — the
  §D10.5 bar is "visually indistinguishable / drop-in", not byte-identical.)
- Page is RGBA8, **decoded linear/UNORM** (never sRGB) — `FontAtlas.Load` already enforces this.

## Fontset (`*.fontset.json`) — §D3.1

Supported keys for v1: `name`, `fonts[].path`, `fonts[].ranges` (Unicode block names *or* explicit
`"U+0020-U+007E"` / `[start,end]` ranges), `fonts[].role` (`fallback` lowers priority), `pxRange`, `glyphSize`,
`atlasPageSize`. A `charset` shortcut (`"ascii"`) is accepted. `weight`/`script`-driven selection is recorded but
not yet acted on (a v2 concern). Glyph selection per codepoint walks fonts in declared order, non-fallback first.
Default charset when a primary font lists no ranges: **printable ASCII U+0020–U+007E** (matches the committed
`ui-default`).

## PNG encoding

Hand-rolled RGBA8 PNG encoder using the **built-in `System.IO.Compression.ZLibStream`** + a hand-rolled CRC-32.
**Zero external dependencies, fully NAOT-clean** — consistent with `SUIsei.Rasterizer/BmpWriter.cs` hand-rolling
BMP, and with the project's "no external binary" goal. (`StbImageSharp`, SUIsei's only dep, decodes but does not
encode.)

## CFF / OTF status

The CFF (`CFF ` table, Type2 charstrings → cubic Béziers) path is implemented behind the shared
`IGlyphOutlineSource` seam, including the INDEX/DICT readers, global/local subrs, and the Type2 operator set needed
for outlines (`rlineto`/`rrcurveto`/`hh|vv|hv|vh-curveto`/`rcurveline`/`rlinecurve`/`*moveto`/`endchar`). **It is
unverified against a real OTF** because no `.otf` is available on this machine or in the repo to test with — there
was nothing to render. Treat CFF as "implemented, glyf-verified architecture, OTF-untested" until an OTF font can
be dropped in. TrueType `glyf` is the verified path.

## What is explicitly deferred (future / not v1)

GPU-compute generation; runtime bake-on-miss; font *hinting*; variable-font axes; kerning/`GPOS`; full Unicode
block tables (only common blocks mapped); subset-`.ttf` emission for HarfBuzz; weight/script-aware font selection.
