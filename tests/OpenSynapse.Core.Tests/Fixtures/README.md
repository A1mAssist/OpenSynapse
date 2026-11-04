# Test fixtures

## studio-two-tone.mp4

1 s, 64x64, 30 fps H.264 (Constrained Baseline); the first 0.5 s is red and the
second 0.5 s is blue. Generated locally with ffmpeg 8.1.1 (no third-party media):

```powershell
ffmpeg -y -f lavfi -i "color=c=0xFF0000:s=64x64:r=30:d=0.5" `
  -f lavfi -i "color=c=0x0000FF:s=64x64:r=30:d=0.5" `
  -filter_complex "[0:v][1:v]concat=n=2:v=1:a=0,format=yuv420p[v]" -map "[v]" `
  -c:v libx264 -profile:v baseline -level 3.0 -preset veryfast -crf 12 -g 30 `
  -movflags +faststart tests\OpenSynapse.Core.Tests\Fixtures\studio-two-tone.mp4
```

The decode is not lossless: sampled centres are RGB (254, 0, 0) and (0, 0, 254).

64x64 rather than 32x32 because `MediaComposition.GetThumbnailAsync` never
completes for a 32x32 H.264 clip on this Windows install, while 64x64 decodes in
about 600 ms per frame.

## studio-long-65s.mp4

65 s, 64x64, 30 fps H.264 (Constrained Baseline); the first 60 s is green and
the last 5 s is red. Generated locally with ffmpeg 8.1.1:

```powershell
ffmpeg -y -f lavfi -i "color=c=0x00FF00:s=64x64:r=30:d=60" `
  -f lavfi -i "color=c=0xFF0000:s=64x64:r=30:d=5" `
  -filter_complex "[0:v][1:v]concat=n=2:v=1:a=0,format=yuv420p[v]" -map "[v]" `
  -c:v libx264 -profile:v baseline -level 3.0 -preset veryfast -crf 18 -g 30 `
  -movflags +faststart tests\OpenSynapse.Core.Tests\Fixtures\studio-long-65s.mp4
```

The test decodes only 16 frames over the full 65 s so the red tail is reachable
past the old 60 s cut-off without spending minutes in thumbnail extraction.

## studio-hevc-two-tone.mp4

1 s, 64x64, 30 fps HEVC (Main, `hvc1` tag); the first 0.5 s is red and the
second 0.5 s is blue. Generated locally with ffmpeg 8.1.1 and libx265:

```powershell
ffmpeg -y -f lavfi -i "color=c=0xFF0000:s=64x64:r=30:d=0.5" `
  -f lavfi -i "color=c=0x0000FF:s=64x64:r=30:d=0.5" `
  -filter_complex "[0:v][1:v]concat=n=2:v=1:a=0,format=yuv420p[v]" -map "[v]" `
  -c:v libx265 -x265-params log-level=error -tag:v hvc1 -movflags +faststart `
  tests\OpenSynapse.Core.Tests\Fixtures\studio-hevc-two-tone.mp4
```

HEVC decoding depends on the Windows HEVC Video Extensions or a hardware
decoder MFT. When frame extraction fails, `StudioVideoPlayback` returns one
shell thumbnail frame (`IsThumbnailFallback == true`) and records
`DecodeWarning`; when the thumbnail also fails it returns null with a non-empty
diagnostic. The renderer then keeps its procedural fallback, so an unsupported
HEVC file does not become a black screen.

## Progressive loading

`StudioVideoPlayback.TryCreateProgressiveAsync` decodes frame 0, returns a
sampler immediately, and fills the remaining slots on one background worker.
`LoadedFrameCount` starts at 1 and reaches `FrameCount` when the worker finishes;
`BackgroundLoad` exposes that worker for tests and diagnostics. A cancellation
token is checked before every publish and is passed to `GetThumbnailAsync`, so
switching media or disposing the studio stops the worker without writing a late
frame. A failed slot reuses the previous frame and records `DecodeWarning`; the
first frame still prevents a black screen.

## Memory and temporal resolution

`StudioVideoPlayback` caches at most 240 frames of 32 x 32 BGRA (240 * 4096 B =
983,040 B, about 0.94 MiB, plus array overhead). Videos up to 60 s keep the
250 ms cadence. Longer videos divide the same budget over the full duration:
5 min -> 1.25 s per frame, 30 min -> 7.5 s, 2 h -> 30 s. The cache never grows
with duration; instead temporal resolution drops, and playback still loops over
the whole clip.
