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
