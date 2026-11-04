# b13 ATS bridge performance development — 2026-11-04

## Scope and restrictions

This branch starts from the **public b11 release** and does not contain the
earlier experimental b12 Mita melody or ExtractionHelper changes. BVE itself,
its rendering engine, live settings, route/vehicle assets, and external ATS
plugins are untouched. The latest public b11 release and update manifest are
unchanged. The optimization affects only the 32-bit ATS bridge path in BVE6,
not native 64-bit ATS or other parts of BVE.

## Code change

`read_fast_array` used to consume each of 2,048 hexadecimal characters using
`streambuf::sbumpc()` and check EOF before every character. It now reads
the 2,048 bytes in one `sgetn` stream-buffer operation into a stack array,
then decodes all 256 32-bit integers without allocating heap memory.

The on-wire protocol, integer bit patterns, accepted whitespace, lowercase
hex validation, trailing delimiter and error handling remain unchanged.
Neither native bridge input nor ATS plugin behavior was intentionally changed.

## Independent codec microbenchmark

`benchmarks/fixtures/benchmark_fast_array_decode.cpp` embeds the original
b11 decoder and tests both implementations using identical data. It checks:
all 256 positions, signed/negative values, invalid hexadecimal characters,
short input, and a non-whitespace trailing delimiter. Timing uses 16,000
decodes in each of 7 alternating repeated rounds. Three trial runs returned:

| Run | Original b11 ns per 2 KiB array | Optimized ns |
|---|---:|---:|
| 1 | 8,389 | 4,635 |
| 2 | 8,591 | 4,672 |
| 3 | 8,752 | 4,821 |

This is **only a codec microbenchmark**. It does not indicate 45% better
game FPS or 45% faster total ATS execution.

## Complete ATS bridge offline A/B test

`benchmarks/TestB13NativeCodec.ps1` runs a verified test-only x86 ATS
against the **actual b11 release bridge binaries** and the optimized
candidate bridge binaries. Each pair alternates which version executes
first; 7 pairs, each executing 16,000 ATS frames, using the same fixture,
CPU and host conditions. Both paths pass all panel/sound/handle correctness
assertions, including high array indexes and negative sound states.

| Run pair | b11 average bridge roundtrip (μs) | Optimized average (μs) |
|---|---:|---:|
| 0 | 158 | 134 |
| 1 | 157 | 140 |
| 2 | 155 | 150 |
| 3 | 149 | 145 |
| 4 | 151 | 130 |
| 5 | 148 | 144 |
| 6 | 163 | 143 |

Median of session-average values: **155 μs b11 / 143 μs optimized**.
These samples contain OS scheduling noise and do not measure real-game FPS.
The synthetic ATS is very light; the gains may be smaller for real plugins
whose own work dominates each frame.

With optional phase profiling enabled for 6,000 synthetic ATS frames,
the frontend decode stage reported mean time **13,503 ns b11 / 7,354 ns
optimized**. These are separate single-session observations.

`benchmarks/TestB13Cadence.ps1` also passed fixed-frame tests at 16,667 μs
and 33,333 μs cadence for both builds, with synthetic sound changes and
late-flushed host/frontend diagnostics. Each fixture retains the established
25 ms pause inside the ATS at frame 123. No data corruption or test hangs
were observed.

## Test build

`dist-b13-perf/portable/BVE6互換ランチャー.exe` is a local **preview**,
not a published release. Its launcher UI executable is bit-for-bit identical
to the public b11 UI; only the 64-bit bridge DLL and 32-bit host EXE were
replaced with optimized binaries. It includes a preview-readme explaining
that FPS results and real route/ATS behavior have not yet been verified.
Windows Defender custom-scan of the preview directory reported no new
detection. This is not proof that all systems will treat it as safe.

## Remaining limitations

- No matched-run real BVE6 FPS measurement has been completed.
- No performance claim is made for BVE's proprietary rendering engine,
  native 64-bit ATS, BveEX, image/map file loading, or unrelated input plugins.
- Do not change Windows GPU, power, security or process-priority settings
  without a measurable and reversible benefit.
- Some routes explicitly need long drawing distances. Do not unconditionally
  lower every route to the same draw distance; the b11 user-selectable
  lightweight/balanced/quality settings remain available.
- Do not publish automatically until real vehicle compatibility is verified.
