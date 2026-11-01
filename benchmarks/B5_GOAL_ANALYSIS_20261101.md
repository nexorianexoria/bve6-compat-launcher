# b5 ATS/FPS matched-window analysis

Exploratory results; not causal evidence or proof of displayed FPS.
The operating-window swap chains have not been mapped to a specific BVE window.
The test compares ATS stalls against random equal-length intervals sampled
from the same 30-second time bins. Render activity and ATS timing can
share causes (scene complexity, CPU scheduling, etc.).
ATS host events >5ms inside FPS capture: 58
Profiled ATS events dropped: 0
Window: 50ms before / 100ms after each event; slow present interval threshold: 50ms.
Matched random comparisons: 500 (deterministic seed).

## Swap chain 0x223A1A8E298 (PID 29068)
- Frames submitted: 7739
- Mean interval: 31.00ms (rate equivalent: 32.3/s)
- 95th/99th interval: 43.17/52.10ms
- Max interval: 227.32ms
- Intervals >33.33ms: 2899
- Intervals >50ms: 125
- Mean CPU/GPU busy reported by PresentMon: 29.91/3.03ms
- ATS-event windows with >50ms interval: 25/58
- Same-time-bin random windows: mean 5.7/58; 10-90% range 3-9
- Fraction of random replicates >= observed (plus-one estimate): 0.002

## Swap chain 0x22424479DF8 (PID 29068)
- Frames submitted: 15607
- Mean interval: 15.37ms (rate equivalent: 65.0/s)
- 95th/99th interval: 16.50/17.05ms
- Max interval: 47.82ms
- Intervals >33.33ms: 3
- Intervals >50ms: 0
- Mean CPU/GPU busy reported by PresentMon: 14.34/1.26ms
- ATS-event windows with >50ms interval: 0/58
- Same-time-bin random windows: mean 0.0/58; 10-90% range 0-0
- Fraction of random replicates >= observed (plus-one estimate): 1.000

## Limits
- A render swap chain has not been proven to represent the BVE driving window.
- Close temporal overlap is not proof that ATS caused the long frame interval.
- Overlapping windows from clustered ATS events are not independent observations.
- CPUStartQPC is a CPU-start timestamp; the associated MsBetweenPresents
  interval can relate to other work preceding that timestamp.
- A 30-second-bin-matched exploratory comparison does not control
  instantaneous scene complexity, changes in view, or CPU scheduling.
- One route, one vehicle and one session cannot establish performance gains.
