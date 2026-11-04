# b13 ATS lookup decoder follow-up — 2026-11-04

## Status and scope

Development-only change on `feature/0.29b13-performance`. Public b11 (`main`
and `update.json`) and the installed launcher are unchanged. This improves
the shared 32-bit ATS bridge decoder, used by both the x64 frontend and x86
backend in BVE6. It does **not** modify BVE rendering, native 64-bit ATS,
external ATS DLLs, routes, vehicles, the G/H melody behavior, or BveEX
ExtractionHelper.

The previous b13 implementation (commit `6e12f247`) already read all 2,048
hex characters with one `streambuf::sgetn` call, but checked each hex nibble
with conditional branches. The revised `read_fast_array`:

- Reads the same 2,048 bytes, accepting/rejecting the same input and delimiter.
- Uses a compile-time 256-entry table for lowercase hexadecimal digits,
  rejecting other bytes. Decodes two nibbles at a time.
- Preserves all 256 signed 32-bit integer bit patterns and the original
  stream failure handling. No heap allocation is introduced.
- Leaves the x86 host's padded `panels[2048]`/`sounds[2048]` intact.

## Standalone A/B tests

MSVC `/O2 /EHsc /std:c++17` on the same Windows machine, 16,000 decodes per
round, 7 alternating rounds. Values include zero, -1, INT32_MIN, INT32_MAX,
and pseudorandom values. Test code also checks invalid hex input, short
input, invalid delimiter and output equality:

| Comparison | Prior encoder/decoder | Revised decoder |
|---|---:|---:|
| Prior b13 vs lookup, ns per 2 KiB array | 4,032 | 1,286 |
| Public b11 vs lookup, separate run, ns per array | 8,491 | 1,311 |

The first result uses
`benchmarks/fixtures/benchmark_fast_array_decode_pairs.cpp` with the previous
b13 decoder retained inside the test. The second uses the original b11 code
in `benchmarks/fixtures/benchmark_fast_array_decode.cpp`.
These are CPU microbenchmarks **not BVE FPS measurements**.

An alternative byte-pair encoder was also tried locally. In its 20,000-call
alternating test it measured 1,025 ns for the current encoder and 1,044 ns for
the proposal. Since it did not improve performance, it was **not adopted**.

## Actual x64/x86 bridge with the same mock ATS

All test runs use the same synthetic x86 ATS DLL, harness and existing
protocol, including handles, all 256 panel and sound positions, high indexes,
negative sound values and buffered diagnostics. The tool refuses to run when
BVE is active. The x64 frontend DLL and x86 backend EXE both built in Release.

`benchmarks/TestB13NativeCodec.ps1`, 16,000 frames x 7 alternating pairs:

| Bridge builds | Median of session-average roundtrips |
|---|---:|
| Public b11 baseline | 138 μs |
| New b13 lookup decoder | 126 μs |

To isolate just the newest change, the original `6e12f247` code was built in
a temporary detached Git worktree. That worktree was removed after copying
the binaries into the ignored `dist-b13-perf/previous-b13-bridge` folder.
The same script, with `-BaselineBridgeDir` and `-ReportFileName`, compared
12,000 frames x 7 alternating pairs:

| Bridge builds | Median of session-average roundtrips |
|---|---:|
| Prior b13 before lookup optimization | 130 μs |
| New b13 lookup decoder | 127 μs |

Detailed CSV: `dist-b13-perf/b13-lookup-vs-prior-ab-results.csv` (ignored
test output). Some individual pairs are noisy. The above results are not
interchangeable across separate test sessions and do not prove an increase
in rendered game FPS.

## Compatibility regression

- `TestB13Cadence.ps1`: PASS at 16,667 μs and 33,333 μs intervals for both
  tested bridge versions, including synthetic sound changes and deferred
  diagnostic logging.
- `TestB11StartupSettings.ps1`: PASS using b13 portable launcher UI, including
  settings backup and original settings hashes.
- `TestB11PluginAndMonitorUi.ps1`: PASS with Windows monitor name, physical
  resolution, refresh rate, controller settings and unmodified live settings.
- `TestB9OtherVehicles.ps1`: PASS for non-recommended vehicle selection.
- `TestPanelViewDiagnostics.ps1`: PASS.

## Local preview and limits

The existing ignored `dist-b13-perf/portable` preview has been updated with
the rebuilt bridge binaries. Its UI executable is still exactly b11 (SHA256
`97195BDBE12E6DDC006D98F146F55DF215596273FD99CD325A624996027098F4`).
The new bridge SHA256 values are:

- Frontend `Bve5VehicleCompatibility_.dll`:
  `26F736010777B51A29082EAFE4407EF5DB6D16BFB24C28198B4722C7BCC4263C`
- Backend `Bve5VehicleCompatibility.exe`:
  `74C4586D779740BF27B886D0D6FE8E75B76075FD90D59272AF3C2B7B576B6723`

No matched live BVE6 FPS A/B test or real vehicle ATS compatibility run
has been performed for this lookup change. The two historical PresentMon
swap chains have not been mapped conclusively to the driving window.
Do not publish or update `update.json` without actual vehicle verification
and user approval. The current public b11 update path remains untouched.
