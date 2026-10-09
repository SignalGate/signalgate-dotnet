# Contributing to the SignalGate .NET SDK

Thanks for your interest in contributing!

## Development setup

Requires the .NET 10 SDK (pinned in `global.json`) and the .NET 8 runtime; the
tests run on both.

```bash
git clone https://github.com/SignalGate/signalgate-dotnet.git
cd signalgate-dotnet
dotnet build SignalGate.slnx -c Release
dotnet test --solution SignalGate.slnx -c Release --no-build
```

The tests drive the client through an in-process fake HTTP handler, so the
suite makes no network calls and needs no API key.

Before opening a pull request, run the same checks as CI:

```bash
dotnet format SignalGate.slnx --verify-no-changes
dotnet build SignalGate.slnx -c Release
dotnet test --solution SignalGate.slnx -c Release --no-build
dotnet pack src/SignalGate/SignalGate.csproj -c Release -o artifacts --no-build
```

The library must stay compatible with Native AOT. To check it locally, publish
the smoke test for your platform's runtime identifier (`<rid>`, for example
`linux-x64`, `osx-arm64` or `win-x64`) and run it:

```bash
dotnet publish tests/SignalGate.AotSmoke/SignalGate.AotSmoke.csproj -c Release -r <rid> -warnaserror -o aot
./aot/SignalGate.AotSmoke
```

Native AOT needs the platform's native toolchain (clang on Linux, the Xcode
command line tools on macOS, the Visual Studio C++ build tools on Windows) and
cannot build for another operating system. On Windows the binary is
`aot\SignalGate.AotSmoke.exe`. The `aot/` folder is ignored by git.

## Making changes

1. Fork the repo and create a branch off `main`.
2. Make your change. Keep the public API stable; changes to existing behavior
   need a very good reason. The build treats warnings as errors, and every
   public member needs XML documentation.
3. Public API changes are recorded in
   `src/SignalGate/PublicAPI/*/PublicAPI.Unshipped.txt`, one file per target
   framework. The build fails until both files list the change.
4. Add or update tests for anything you change. Every test needs a `Timeout`
   (see the existing tests), and tests close clients through the helpers in
   `tests/SignalGate.Tests/Fakes`.
5. If you change a C# example in `README.md`, update the matching
   `#region readme:<name>` block under `tests/SignalGate.DocSnippets/Snippets/`.
   A test fails when the README and the compiled snippets differ.
6. Add an entry to `CHANGELOG.md` if the change is user-visible.
7. Open a pull request against `main` describing what changed and why.

## Reporting bugs

Open an issue at <https://github.com/SignalGate/signalgate-dotnet/issues> with
the SDK version, the .NET version, your operating system and a minimal
reproduction. Never include an API key.

**Security vulnerabilities: do not open a public issue.** See
[SECURITY.md](SECURITY.md) for the private disclosure path.

## Releases

Maintainers handle versioning, tagging and publishing. Contributors don't need
to change version numbers in pull requests.

## License

By contributing, you agree that your contributions will be licensed under the
[Apache License 2.0](LICENSE).
