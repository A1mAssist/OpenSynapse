# AVIF decoder

`avif.dll` is libavif 1.3.0, built from the upstream source with MSVC 19.44 x64:

- `BUILD_SHARED_LIBS=ON`
- `AVIF_CODEC_DAV1D=SYSTEM`
- `AVIF_LIBYUV=OFF`, `AVIF_BUILD_APPS=OFF`, `AVIF_BUILD_TESTS=OFF`

`dav1d.dll` is the Release x64 runtime from
`Kotek.Packages.Dav1d.Vcpkg.Runtimes.Win10.Msvc.x64` 1.4.3. `vcruntime140.dll` is
the matching VS 2022 x64 redistributable. The three DLLs total about 2.1 MB.
The mapping card uses them to preserve AVIF alpha; Windows' AVIF codec returns an
opaque black background instead.
