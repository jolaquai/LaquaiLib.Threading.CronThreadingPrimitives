# LaquaiLib.Threading.CronThreadingPrimitives

Cron-driven async threading primitives for .NET, modeled after `PeriodicTimer`.

## Projects

### [LaquaiLib.Threading.CronTimer](LaquaiLib.Threading.CronTimer/)

The main public package. Provides `CronTimer` — an async-awaitable timer that fires at occurrences dictated by one or more cron expressions rather than a fixed interval.

- Accepts standard 5-field (`min hour dom mon dow`) and 6-field (`sec min hour dom mon dow`) cron expressions
- Accepts pre-parsed `CronExpression` objects from [Cronos](https://github.com/HangfireIO/Cronos) directly, allowing mixed formats
- Supports custom `TimeZoneInfo` for occurrence evaluation
- When multiple expressions are supplied, fires at the earliest next occurrence across all of them
- Single-awaiter pattern identical to `PeriodicTimer`: only one `WaitForNextTickAsync` call in flight at a time
- Returns `true` on each tick, `false` when disposed; throws `OperationCanceledException` on cancellation
- Handles arbitrarily long cron delays (beyond the ~49-day `Timer` ceiling) via intermediate re-arms

```csharp
using LaquaiLib.Threading.CronTimer;

using var timer = new CronTimer("0 9 * * 1-5"); // 09:00 every weekday (UTC)

while (await timer.WaitForNextTickAsync(cancellationToken))
{
    // ...
}
```

### [LaquaiLib.Threading.CronThreadingPrimitives.Internal](LaquaiLib.Threading.CronThreadingPrimitives.Internal/)

Internal helpers consumed by the other projects. Not intended for direct use.

## License

[Unlicense](LICENSE)
