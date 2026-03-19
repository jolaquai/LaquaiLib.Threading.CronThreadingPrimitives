namespace LaquaiLib.Threading.CronTimer;

public sealed class CronTimerTests
{
    /// <summary>6-field cron: fires every second.</summary>
    private const string EverySecond = "* * * * * *";
    /// <summary>5-field cron: fires every minute.</summary>
    private const string EveryMinute = "* * * * *";
    /// <summary>5-field cron: fires once a year (Jan 1 midnight) — effectively never during tests.</summary>
    private const string FarFuture = "0 0 1 1 *";

    // ───────────────────────────────────────────────
    //  Constructor — string[] overloads
    // ───────────────────────────────────────────────

    [Fact]
    public void Ctor_String_ValidFiveField()
    {
        using var timer = new CronTimer(EveryMinute);
    }

    [Fact]
    public void Ctor_String_ValidSixField()
    {
        using var timer = new CronTimer(EverySecond);
    }

    [Fact]
    public void Ctor_String_MultipleSameFormat()
    {
        using var timer = new CronTimer("0 * * * *", "30 * * * *");
    }

    [Fact]
    public void Ctor_String_EmptyArray_Throws()
    {
        Assert.Throws<ArgumentException>(() => new CronTimer(Array.Empty<string>()));
    }

    [Fact]
    public void Ctor_String_InvalidExpression_Throws()
    {
        Assert.Throws<ArgumentException>(() => new CronTimer("not valid"));
    }

    [Fact]
    public void Ctor_String_MixedFormats_Throws()
    {
        // CronHelpers.TryParse requires all expressions to share the same format
        Assert.Throws<ArgumentException>(() => new CronTimer(EveryMinute, EverySecond));
    }

    [Fact]
    public void Ctor_String_NullTimeZone_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CronTimer((TimeZoneInfo)null, EveryMinute));
    }

    [Fact]
    public void Ctor_String_CustomTimeZone()
    {
        using var timer = new CronTimer(TimeZoneInfo.Local, EveryMinute);
    }

    // ───────────────────────────────────────────────
    //  Constructor — CronExpression[] overloads
    // ───────────────────────────────────────────────

    [Fact]
    public void Ctor_CronExpression_Valid()
    {
        using var timer = new CronTimer(CronExpression.Parse(EverySecond, CronFormat.IncludeSeconds));
    }

    [Fact]
    public void Ctor_CronExpression_EmptyArray_Throws()
    {
        Assert.Throws<ArgumentException>(() => new CronTimer(Array.Empty<CronExpression>()));
    }

    [Fact]
    public void Ctor_CronExpression_NullElement_Throws()
    {
        var valid = CronExpression.Parse(EverySecond, CronFormat.IncludeSeconds);
        Assert.Throws<ArgumentException>(() => new CronTimer(valid, null));
    }

    [Fact]
    public void Ctor_CronExpression_NullArray_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CronTimer((CronExpression[])null));
    }

    [Fact]
    public void Ctor_CronExpression_NullTimeZone_Throws()
    {
        var expr = CronExpression.Parse(EverySecond, CronFormat.IncludeSeconds);
        Assert.Throws<ArgumentNullException>(() => new CronTimer((TimeZoneInfo)null, expr));
    }

    [Fact]
    public void Ctor_CronExpression_MixedFormatsAllowed()
    {
        // CronExpression[] constructor permits different formats
        var fiveField = CronExpression.Parse(EveryMinute, CronFormat.Standard);
        var sixField = CronExpression.Parse(EverySecond, CronFormat.IncludeSeconds);
        using var timer = new CronTimer(fiveField, sixField);
    }

    // ───────────────────────────────────────────────
    //  WaitForNextTickAsync — normal ticking
    // ───────────────────────────────────────────────

    [Fact]
    public async Task WaitForNextTickAsync_ReturnsTrue()
    {
        using var timer = new CronTimer(EverySecond);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Assert.True(await timer.WaitForNextTickAsync(cts.Token));
    }

    [Fact]
    public async Task WaitForNextTickAsync_SequentialWaits_EachReturnsTrue()
    {
        using var timer = new CronTimer(EverySecond);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        for (var i = 0; i < 3; i++)
            Assert.True(await timer.WaitForNextTickAsync(cts.Token));
    }

    [Fact]
    public async Task WaitForNextTickAsync_MultipleCronExpressions_Fires()
    {
        var everySecond = CronExpression.Parse(EverySecond, CronFormat.IncludeSeconds);
        var everyMinute = CronExpression.Parse(EveryMinute, CronFormat.Standard);
        using var timer = new CronTimer(everySecond, everyMinute);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Assert.True(await timer.WaitForNextTickAsync(cts.Token));
    }

    [Fact]
    public async Task WaitForNextTickAsync_WithTimeZone_Fires()
    {
        using var timer = new CronTimer(TimeZoneInfo.Local, EverySecond);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Assert.True(await timer.WaitForNextTickAsync(cts.Token));
    }

    // ───────────────────────────────────────────────
    //  WaitForNextTickAsync — signal coalescing
    // ───────────────────────────────────────────────

    [Fact]
    public async Task WaitForNextTickAsync_CoalescedSignal_CompletedSynchronously()
    {
        using var timer = new CronTimer(EverySecond);
        // Let at least one tick fire while nobody is waiting
        await Task.Delay(2000);
        var vt = timer.WaitForNextTickAsync(TestContext.Current.CancellationToken);
        Assert.True(vt.IsCompletedSuccessfully);
        Assert.True(vt.Result);
    }

    // ───────────────────────────────────────────────
    //  WaitForNextTickAsync — cancellation
    // ───────────────────────────────────────────────

    [Fact]
    public async Task WaitForNextTickAsync_PreCanceledToken_Throws()
    {
        using var timer = new CronTimer(FarFuture);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await timer.WaitForNextTickAsync(cts.Token));
    }

    [Fact]
    public async Task WaitForNextTickAsync_TokenCanceledDuringWait_Throws()
    {
        using var timer = new CronTimer(FarFuture);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await timer.WaitForNextTickAsync(cts.Token));
    }

    [Fact]
    public async Task WaitForNextTickAsync_AfterCancellation_CanWaitAgain()
    {
        using var timer = new CronTimer(EverySecond);

        // Cancel a wait before the timer fires
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        try { await timer.WaitForNextTickAsync(cts.Token); }
        catch (OperationCanceledException) { }

        // Subsequent wait must still succeed
        using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Assert.True(await timer.WaitForNextTickAsync(cts2.Token));
    }

    [Fact]
    public async Task WaitForNextTickAsync_AfterCancellation_MultipleSubsequentWaitsSucceed()
    {
        using var timer = new CronTimer(EverySecond);

        // Cancel a wait before the timer fires
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        try { await timer.WaitForNextTickAsync(cts.Token); }
        catch (OperationCanceledException) { }

        // Multiple subsequent waits must each succeed — exercises MRVTSC reset after cancellation
        using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        for (var i = 0; i < 3; i++)
            Assert.True(await timer.WaitForNextTickAsync(cts2.Token));
    }

    // ───────────────────────────────────────────────
    //  WaitForNextTickAsync — single-awaiter enforcement
    // ───────────────────────────────────────────────

    [Fact]
    public async Task WaitForNextTickAsync_ConcurrentCallers_Throws()
    {
        using var timer = new CronTimer(FarFuture);
        // First caller blocks (far-future cron never fires)
        var firstWait = timer.WaitForNextTickAsync();
        // Second caller must throw synchronously
        Assert.Throws<InvalidOperationException>(() => timer.WaitForNextTickAsync());
        // Clean up — dispose wakes the pending waiter
        timer.Dispose();
        Assert.False(await firstWait);
    }

    // ───────────────────────────────────────────────
    //  WaitForNextTickAsync — after disposal
    // ───────────────────────────────────────────────

    [Fact]
    public async Task WaitForNextTickAsync_AfterDispose_ReturnsFalse()
    {
        var timer = new CronTimer(EverySecond);
        timer.Dispose();
        var vt = timer.WaitForNextTickAsync();
        Assert.True(vt.IsCompletedSuccessfully);
        Assert.False(await vt);
    }

    [Fact]
    public async Task WaitForNextTickAsync_DisposeWhileWaiting_ReturnsFalse()
    {
        var timer = new CronTimer(FarFuture);
        var waitTask = timer.WaitForNextTickAsync();
        timer.Dispose();
        Assert.False(await waitTask);
    }

    // ───────────────────────────────────────────────
    //  Dispose / DisposeAsync
    // ───────────────────────────────────────────────

    [Fact]
    public void Dispose_Idempotent()
    {
        var timer = new CronTimer(EverySecond);
        timer.Dispose();
        timer.Dispose(); // must not throw
    }

    [Fact]
    public async Task DisposeAsync_Works()
    {
        var timer = new CronTimer(EverySecond);
        await timer.DisposeAsync();
        Assert.False(await timer.WaitForNextTickAsync());
    }

    [Fact]
    public async Task DisposeAsync_Idempotent()
    {
        var timer = new CronTimer(EverySecond);
        await timer.DisposeAsync();
        await timer.DisposeAsync(); // must not throw
    }
}
