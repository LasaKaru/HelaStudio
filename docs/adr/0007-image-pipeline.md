# 7. SkiaSharp for the image pipeline

Date: 2026-09-01

## Status

Accepted

## Context

One uploaded icon has to become every size Android and iOS want: five launcher
densities twice over, an adaptive-icon foreground, a 1024px App Store icon, tab
bar icons at three scales. That needs an image library, and it sits on the
critical path of every customer build.

The .NET options differ far more in licensing than in capability.

| Library              | Licence                                  | Native dependency           |
| -------------------- | ---------------------------------------- | --------------------------- |
| SixLabors.ImageSharp | Six Labors Split License                 | none — pure managed         |
| SkiaSharp            | MIT, over Google's BSD-licensed Skia     | per-runtime native binaries |
| Magick.NET           | Apache-2.0, plus the ImageMagick licence | per-runtime native binaries |

`sprints/SPRINT-04.md` recommends ImageSharp, for a good reason: no native
dependency is a real advantage on the arm64 Oracle host, where a missing
platform build would be discovered late and awkwardly.

⚠️ Two things about ImageSharp's licence were not visible from the
recommendation.

**It has a revenue trigger.** The Split License grants Apache-2.0 terms only
while the consumer is open source, a non-profit, or a for-profit under **1M USD
annual gross revenue**. Above that, a paid commercial licence is required.
Shellwright is a commercial product with a revenue plan; adopting ImageSharp
means committing to a future payment on behalf of a business that has not
agreed to it.

**Version 4 enforces it at build time.** `dotnet build -c Release` fails with
"No Six Labors license found" unless a key is supplied. This was discovered the
hard way: the Debug build was clean and every test passed, and only the Release
build failed. Staying on 3.x avoids it, but that is a decision to freeze a
dependency on the version before its vendor started enforcing payment — which
is not a position to build a pipeline on.

## Decision

**Use SkiaSharp.**

MIT over BSD. No revenue trigger, no licence key, no expiry, nothing to
re-evaluate when the business grows. The generator takes
`SkiaSharp.NativeAssets.Linux` because the API host and the build runners are
Linux; other runtime identifiers get their own package if anything ever runs
there.

The resampler is Mitchell cubic, stated explicitly rather than defaulted. Icons
are downscaled by large factors — 1024px to 48px — where nearest or bilinear
sampling aliases badly, and Mitchell trades a little sharpness for an absence of
ringing, which is the right trade on a logo.

Everything sits behind `IImagePipeline`, three methods wide.

## Consequences

The licence question is closed. Nothing here has to be revisited when revenue
crosses a threshold, and no build can fail for want of a key.

The cost is native binaries. `SkiaSharp.NativeAssets.Linux` pulls in
`libfontconfig1`; a container without it fails at load rather than at build.
⚠️ If the Oracle host or a build image ever lacks it, the fix is
`SkiaSharp.NativeAssets.Linux.NoDependencies` — the pipeline renders no text, so
nothing is lost by it. Worth knowing before somebody debugs a missing shared
library at three in the morning.

Determinism is not a property either library gives away, so it is asserted
rather than assumed: rendering twice must be byte-identical, and the PNG output
must contain no `tIME`, `tEXt`, `gAMA` or other ancillary chunk that would vary
between runs or machines. Both are tests, and they run on every pull request.

⚠️ The library version belongs to the toolchain descriptor. A bump changes
pixels, and a change in pixels must invalidate the build cache deliberately
rather than being discovered as a mysterious rebuild.

The three-method interface is the escape hatch. If Skia ever becomes the wrong
answer — a platform it cannot reach, a native-dependency problem that will not
resolve — replacing it is one class and one golden-file approval, and the diff
will show exactly which pixels changed.
