# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
Pre-1.0 caveat: while the major version is `0`, breaking changes may land in
minor versions.

## [0.1.0] - 2026-10-09

### Added

- `SignalGateClient`, configured through `SignalGateClientOptions`, with:
  - `CheckAsync`, which sends one check before an action and returns a `CheckResult` verdict;
  - `Log`, which queues an event after an action and delivers it in the background; it never throws and
    never blocks;
  - `CloseAsync`, `DisposeAsync` and `Dispose`, which deliver queued events within a deadline and release
    the HTTP resources the client owns.
- `SignalGateEvent` and `EncryptedPayload` for the event and the browser snippet's payload, with
  `SignalGateEvent.TryCreate` to skip calls when no payload was captured.
- Fail-open checks: by default a timeout, a network error, a 5xx status or a success response without a
  usable verdict returns `allow` with `FailedOpen = true`.
- Typed exceptions deriving from `SignalGateException`: `SignalGateConfigException`,
  `SignalGateTimeoutException`, `SignalGateNetworkException` and `SignalGateServerException`.
- Log delivery retries after a 5xx status, a timeout or a network error, waiting 200, 400 and 800 ms
  between attempts by default (configurable with `LogMaxRetries` and `LogRetryBaseMs`).
- `SignalGateMetrics` with eight counters for checks and log delivery.
- `ISignalGateLogger` hook for diagnostic events, with the API key always redacted.
- Targets `net8.0` and `net10.0`, with no runtime dependencies.
- Compatible with trimming and Native AOT.

[0.1.0]: https://github.com/SignalGate/signalgate-dotnet/releases/tag/v0.1.0
