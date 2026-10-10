# Package artifact integrity

The twelve reusable ScadaSpace libraries are built as NuGet artifacts, not published to nuget.org. Download both `packages-engines` and `packages-controls` from the same successful Actions run and place their `.nupkg` files in one local NuGet feed. Third-party dependencies still resolve from their configured package feeds.

The first successful deployment run exposed a packaging verification gap: the Controls pack command completed without creating an archive, and the artifact uploader treated missing files as a warning. The control project now explicitly declares `IsPackable=true`. A mandatory archive check verifies the package identity, compiled library, direct ScadaSpace dependency declarations and absence of bundled font files. Missing engine/control artifact files fail CI. A successful `dotnet pack` exit code alone is no longer counted as evidence that a reusable control package was produced.

The independent engine consumer executes four public-API scenarios using package references, outside the repository and with no project references. The controls archive check verifies package contents; it is not a separate native UI runtime test or proof that every public API has been exercised. The browser suites validate the shared Uno application with the control library referenced by the application project.
