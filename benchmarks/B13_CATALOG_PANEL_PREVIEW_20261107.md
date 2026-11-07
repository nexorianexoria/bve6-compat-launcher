# b13 catalog panel-image optimization — 2026-11-07

## Implementation (not game rendering)

The launcher previously read some panel definitions twice and fully decoded
every candidate pilot-lamp image when finding the largest preview. The revised
`Previews.PanelImage` reads each panel definition once, reads image dimensions
first, and fully decodes selected candidates only when needed to validate the
file. Stable ordering preserves first-largest selection when dimensions tie.

`CatalogData.Build` also shares panel-preview results for identical panel
definitions within a single catalog build. It does not retain the cache
between scans or mutate any vehicle or panel file.

## Measured results on installed scenario data

- 1,256 vehicles, baseline sequential preview resolution: 75,905 ms.
- Same files, optimized preview resolution: 51,047 ms.
- **All 1,256 returned preview paths matched** the baseline.
- CatalogData.Build baseline initial run: 26,029 ms, recheck: 37,761 ms.
- Optimized CatalogData.Build: 17,860 ms.
- Synthetic edge fixtures preserve selection for tied dimensions, largest
  candidate, corrupt large image, and night fallback.

These timings depend on disk cache and machine state. They do not measure
the BVE engine FPS or live-frame rendering.

## Verification

- Release x64 launcher compiled.
- `benchmarks/TestB13CatalogPreview.ps1` created and compared complete
  1,256-entry baseline and revised CSV files, stored in ignored
  `benchmarks/results/`.
- `benchmarks/CreateB13PreviewFixtures.ps1` created four independent
  test scenarios in ignored `benchmarks/results/`; outputs matched.
- BVE settings and source game data left untouched.

Only the b13 development branch receives this code. Public b11 release and
updater manifest stay unchanged.
