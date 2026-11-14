# Third-party components

MooVideo source: GPL-3.0-or-later. The complete application source is included in `Source/`. The MooView icon is reused from the user's existing MooView project.

## libmpv

- Build: shinchiro mpv-dev-x86_64 20261111, mpv git bcde6b93cc.
- Binary: https://github.com/shinchiro/mpv-winbuild-cmake/releases/tag/20261111
- Build recipes / pinned dependency revisions for this binary: https://github.com/shinchiro/mpv-winbuild-cmake/tree/20261111
- mpv source revision: https://github.com/mpv-player/mpv/commit/bcde6b93cc
- Installation reference: https://mpv.io/installation/
- License: GPL family; see included GPL text and upstream Copyright file. Components linked by the upstream build retain their respective notices. This build is unmodified.

## FFmpeg and ffprobe

- Gyan essentials build 9.0.2, copied unchanged from the existing local tools.
- Distribution and source links: https://www.gyan.dev/ffmpeg/builds/
- Source revision for this binary: https://github.com/FFmpeg/FFmpeg/commit/946fcce07b
- Build configuration and linked libraries: run `App/tools/ffmpeg.exe -version`; the upstream build page above documents its build and distribution.
- GPL-3.0-or-later build. The distribution's LICENSE is included under Licenses/FFmpeg-GPL.txt.

## Microsoft .NET

- Self-contained .NET 10 Windows Desktop runtime, Microsoft NuGet packages 10.0.0.
- Source and notices: https://github.com/dotnet/runtime and https://github.com/dotnet/winforms
- Primarily MIT; runtime third-party notices are included in Licenses.

## Verification media (not shipped in the app)

- https://fate-suite.ffmpeg.org/hap/hap1.mov
- https://fate-suite.ffmpeg.org/hap/hapy.mov
- https://fate-suite.ffmpeg.org/notchlc/alpha-ramp.mov
- https://fate-suite.ffmpeg.org/notchlc/chroma-alpha.mov
- Remaining media was generated locally using FFmpeg test patterns and sine waves.

The application's complete editable source is in `Source/`. The version-specific links above identify the source and build recipes for the unmodified native binaries. These upstream source locations must remain available alongside the release binaries; the native source archives are not duplicated in this package.
