# LaquaiLib.Threading.CronTimer

A `PeriodicTimer`-like timer driven by one or more **cron expressions** instead of a fixed interval. Backed by [Cronos](https://github.com/HangfireIO/Cronos) for ultra-fast, lightweight cron parsing.

## Overview

`CronTimer` fires at the next occurrence dictated by any of the provided cron expressions. When multiple expressions are supplied the timer fires at whichever is earliest. Both standard 5-field (`min hour dom mon dow`) and 6-field (`sec min hour dom mon dow`) expressions are accepted when constructing from strings.

Like `PeriodicTimer`, only a **single** `WaitForNextTickAsync` call may be in flight at any time. Disposing the timer wakes any pending waiter with `false`.

## Building

```bash
dotnet build
```

## Testing

```bash
dotnet test
```

## Packaging

The library automatically generates a NuGet package on Release builds:

```bash
dotnet build -c Release
```

The generated `.nupkg` and `.snupkg` (symbols) files can be found in `bin/Release/`.

## Installation

```bash
dotnet add package LaquaiLib.Threading.CronTimer
```

Or from a local build:

```bash
dotnet nuget add source ./bin/Release --name local
dotnet add package LaquaiLib.Threading.CronTimer
```

## Usage

### Basic loop — fires every minute

```csharp
using LaquaiLib.Threading.CronTimer;

using var timer = new CronTimer("* * * * *");

while (await timer.WaitForNextTickAsync())
{
    // executed once per minute
}
```

### 6-field expression — fires every second

```csharp
using var timer = new CronTimer("* * * * * *");
```

### Multiple expressions — fires at the earliest occurrence

```csharp
// fires at :00 and :30 of every hour
using var timer = new CronTimer("0 * * * *", "30 * * * *");
```

### Pre-parsed `CronExpression` objects

When constructing from `CronExpression` instances the expressions may use different formats (5-field and 6-field can be mixed freely):

```csharp
using Cronos;

var everySecond = CronExpression.Parse("* * * * * *", CronFormat.IncludeSeconds);
var everyMinute = CronExpression.Parse("* * * * *",   CronFormat.Standard);

using var timer = new CronTimer(everySecond, everyMinute);
```

### Custom time zone

```csharp
var tz = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
using var timer = new CronTimer(tz, "0 9 * * 1-5"); // 09:00 Mon–Fri Eastern
```

### Cancellation and disposal

`WaitForNextTickAsync` accepts a `CancellationToken`. The method returns `false` (without throwing) when the timer is disposed; it throws `OperationCanceledException` when the token is cancelled.

```csharp
using var cts = new CancellationTokenSource();

using var timer = new CronTimer("* * * * *");

while (await timer.WaitForNextTickAsync(cts.Token))
{
    // ...
}
```

## License

Unlicense
