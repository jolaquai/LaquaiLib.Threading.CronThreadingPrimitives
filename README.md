# LaquaiLib.Threading.CronThreadingPrimitives

A collection of cron-driven async threading primitives for .NET.

## Packages

### [LaquaiLib.Threading.CronTimer](LaquaiLib.Threading.CronTimer/README.md)

An async-awaitable timer that fires at occurrences dictated by one or more cron expressions, modeled after `PeriodicTimer`.

### [LaquaiLib.Threading.CronThreadingPrimitives.Internal](LaquaiLib.Threading.CronThreadingPrimitives.Internal/README.md)

Shared internal helpers. Not intended for direct use.

## Contributing

Opening issues and submitting PRs are welcome. All changes must be appropriately covered by tests. Tests run exclusively under `net6.0`.

Support for `netstandard2.0` must always be maintained in the internal project. If possible, new functionality should target all frameworks. New dependencies may be introduced after I vet the decision to do so.

Or get in touch on Discord @eyeoftheenemy

## License

[Unlicense](LICENSE)
