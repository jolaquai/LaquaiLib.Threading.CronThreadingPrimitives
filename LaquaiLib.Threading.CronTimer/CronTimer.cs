using System.Threading.Tasks.Sources;

namespace LaquaiLib.Threading.CronTimer;

/// <summary>
/// Implements a timer that supports asynchronous waiting for ticks as dictated by one or more <c>cron</c> expressions.
/// </summary>
/// <remarks>
/// Similar to <see cref="PeriodicTimer"/>, only a single call to <see cref="WaitForNextTickAsync(CancellationToken)"/> may be in flight at any given time.
/// </remarks>
public sealed class CronTimer : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// The maximum delay that can be passed to <see cref="Timer.Change(TimeSpan, TimeSpan)"/> without exceeding the internal millisecond limit.
    /// Longer cron delays are handled by intermediate re-arms.
    /// </summary>
    private static readonly TimeSpan MaxTimerDelay = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    private readonly CronExpression[] _crons;
    private readonly TimeZoneInfo _timeZone;
    private readonly State _state;
    private readonly Timer _timer;
    // Stored as UTC ticks (long) so Interlocked.Read/Exchange give atomic 64-bit access on all platforms.
    private long _nextTickUtcTicks;
    private volatile bool _disposed;

    /// <summary>
    /// Initializes a new <see cref="CronTimer"/> that uses <see cref="TimeZoneInfo.Utc"/> to evaluate occurrences.
    /// </summary>
    /// <param name="crons">One or more <c>cron</c> expressions. All must use the same format (5-field standard or 6-field with seconds).</param>
    /// <exception cref="ArgumentException"><paramref name="crons"/> is empty or contains invalid expressions.</exception>
    public CronTimer(params string[] crons) : this(TimeZoneInfo.Utc, crons) { }

    /// <summary>
    /// Initializes a new <see cref="CronTimer"/> that uses the specified <see cref="TimeZoneInfo"/> to evaluate occurrences.
    /// </summary>
    /// <param name="timeZone">The time zone used to evaluate <c>cron</c> occurrences.</param>
    /// <param name="crons">One or more <c>cron</c> expressions. All must use the same format (5-field standard or 6-field with seconds).</param>
    /// <exception cref="ArgumentNullException"><paramref name="timeZone"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="crons"/> is empty or contains invalid expressions.</exception>
    public CronTimer(TimeZoneInfo timeZone, params string[] crons)
    {
        if (timeZone is null)
            throw new ArgumentNullException(nameof(timeZone));
        if (crons.Length == 0)
            throw new ArgumentException("At least one cron expression must be provided.", nameof(crons));
        if (!CronHelpers.TryParse(crons, out var parsedCrons) || parsedCrons is not { Length: > 0 })
            throw new ArgumentException("One or more of the provided cron expressions were invalid.", nameof(crons));

        _crons = parsedCrons;
        _timeZone = timeZone;
        _state = new State();
        _timer = new Timer(static state => ((CronTimer)state).TimerCallback(), this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        ScheduleNextTick();
    }

    /// <summary>
    /// Initializes a new <see cref="CronTimer"/> that uses <see cref="TimeZoneInfo.Utc"/> to evaluate occurrences from pre-parsed <see cref="CronExpression"/> instances.
    /// </summary>
    /// <param name="crons">One or more pre-parsed <see cref="CronExpression"/> instances. These may use different formats.</param>
    /// <exception cref="ArgumentException"><paramref name="crons"/> is empty.</exception>
    public CronTimer(params CronExpression[] crons) : this(TimeZoneInfo.Utc, crons) { }

    /// <summary>
    /// Initializes a new <see cref="CronTimer"/> that uses the specified <see cref="TimeZoneInfo"/> to evaluate occurrences from pre-parsed <see cref="CronExpression"/> instances.
    /// </summary>
    /// <param name="timeZone">The time zone used to evaluate <c>cron</c> occurrences.</param>
    /// <param name="crons">One or more pre-parsed <see cref="CronExpression"/> instances. These may use different formats.</param>
    /// <exception cref="ArgumentNullException"><paramref name="timeZone"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="crons"/> is empty or contains a <see langword="null"/> element.</exception>
    public CronTimer(TimeZoneInfo timeZone, params CronExpression[] crons)
    {
        if (timeZone is null)
            throw new ArgumentNullException(nameof(timeZone));
        if (crons is null)
            throw new ArgumentNullException(nameof(crons));
        if (crons.Length == 0)
            throw new ArgumentException("At least one cron expression must be provided.", nameof(crons));
        for (var i = 0; i < crons.Length; i++)
            if (crons[i] is null)
                throw new ArgumentException($"Cron expression at index {i} is null.", nameof(crons));

        // Defensive copy: prevents the caller from mutating (e.g. nulling) elements after construction.
        _crons = crons.ToArray();
        _timeZone = timeZone;
        _state = new State();
        _timer = new Timer(static state => ((CronTimer)state).TimerCallback(), this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        ScheduleNextTick();
    }

    /// <summary>
    /// Waits for the next tick of the timer.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> to observe while waiting for the next tick.</param>
    /// <returns><see langword="true"/> when the timer fires; <see langword="false"/> when the timer has been disposed.</returns>
    /// <exception cref="InvalidOperationException">A second <see cref="WaitForNextTickAsync(CancellationToken)"/> call was issued before a preceding one completed.</exception>
    public ValueTask<bool> WaitForNextTickAsync(CancellationToken cancellationToken = default) => _state.WaitForNextTickAsync(this, cancellationToken);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _timer.Dispose();
        _state.SignalStop();
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return default;
    }

    private DateTime? GetEarliestNextOccurrence(DateTime fromUtc)
    {
        DateTime? earliest = null;
        for (var i = 0; i < _crons.Length; i++)
        {
            var next = _crons[i].GetNextOccurrence(fromUtc, _timeZone);
            if (next.HasValue && (earliest is null || next.Value < earliest.Value))
                earliest = next;
        }
        return earliest;
    }

    private void ScheduleNextTick()
    {
        var next = GetEarliestNextOccurrence(DateTime.UtcNow);
        if (!next.HasValue)
            return;
        Interlocked.Exchange(ref _nextTickUtcTicks, next.Value.Ticks);
        ArmTimer();
    }

    private void ArmTimer()
    {
        var nextTicks = Interlocked.Read(ref _nextTickUtcTicks);
        var delay = new DateTime(nextTicks, DateTimeKind.Utc) - DateTime.UtcNow;
        if (delay < TimeSpan.Zero)
            delay = TimeSpan.Zero;
        if (delay > MaxTimerDelay)
            delay = MaxTimerDelay;
        try
        {
            _timer.Change(delay, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // Timer was disposed concurrently with this callback — safe to ignore.
        }
    }

    private void TimerCallback()
    {
        if (_disposed)
            return;

        var nextTicks = Interlocked.Read(ref _nextTickUtcTicks);
        if (DateTime.UtcNow.Ticks < nextTicks)
        {
            // Intermediate wake for long-delayed schedules — re-arm toward the real target.
            ArmTimer();
            return;
        }

        _state.Signal();
        ScheduleNextTick();
    }

    /// <summary>
    /// Implements the <see cref="IValueTaskSource{TResult}"/> pattern for the single-awaiter <see cref="CronTimer.WaitForNextTickAsync(CancellationToken)"/> method.
    /// Modeled after the inner <c>State</c> class in <c>System.Threading.PeriodicTimer</c>.
    /// </summary>
    private sealed class State : IValueTaskSource<bool>
    {
        private ManualResetValueTaskSourceCore<bool> _mrvtsc;
        private CancellationTokenRegistration _ctr;
        private bool _stopped;
        private bool _signaled;
        private bool _activeWait;

        public State()
        {
            _mrvtsc.RunContinuationsAsynchronously = true;
        }

        /// <summary>
        /// Registers a wait for the next tick, enforcing the single-awaiter invariant.
        /// </summary>
        public ValueTask<bool> WaitForNextTickAsync(CronTimer owner, CancellationToken cancellationToken)
        {
            lock (this)
            {
                if (_activeWait)
                    throw new InvalidOperationException("Only a single caller may wait at any given time.");

                if (_stopped)
                    return new ValueTask<bool>(false);

                if (cancellationToken.IsCancellationRequested)
                    return ValueTask.FromCanceled<bool>(cancellationToken);

                if (_signaled)
                {
                    _signaled = false;
                    return new ValueTask<bool>(true);
                }

                _activeWait = true;

                if (cancellationToken.CanBeCanceled)
                {
                    _ctr = cancellationToken.UnsafeRegister(static (state, ct) =>
                    {
                        var s = (State)state;
                        // Acquire the lock only to flip _activeWait; the MRVTSC write must
                        // happen outside the lock to avoid a deadlock with CTR.Dispose().
                        // Also clear any coalesced signal that arrived between _activeWait being
                        // cleared and SetException being called — that signal belongs to this
                        // cancelled wait and must not be surfaced to the next caller.
                        lock (s)
                        {
                            if (!s._activeWait)
                                return;
                            s._activeWait = false;
                            s._signaled = false;
                        }
                        s._mrvtsc.SetException(new OperationCanceledException(ct));
                    }, this);
                }

                return new ValueTask<bool>(this, _mrvtsc.Version);
            }
        }

        /// <summary>
        /// Called by the timer callback when a cron tick fires.
        /// </summary>
        public void Signal()
        {
            CancellationTokenRegistration ctr;
            lock (this)
            {
                if (_stopped)
                    return;

                if (!_activeWait)
                {
                    // No waiter — coalesce.
                    _signaled = true;
                    return;
                }

                _activeWait = false;
                ctr = _ctr;
            }

            // Signal outside the lock: CTR.Dispose() may block on a concurrent callback
            // that itself takes the lock, so keeping SetResult outside prevents deadlock.
            ctr.Dispose();
            _mrvtsc.SetResult(true);
        }

        /// <summary>
        /// Called by <see cref="Dispose()"/> to wake any pending waiter with <see langword="false"/>.
        /// </summary>
        public void SignalStop()
        {
            CancellationTokenRegistration ctr;
            lock (this)
            {
                _stopped = true;

                if (!_activeWait)
                    return;

                _activeWait = false;
                ctr = _ctr;
            }

            ctr.Dispose();
            _mrvtsc.SetResult(false);
        }

        bool IValueTaskSource<bool>.GetResult(short token)
        {
            // Only ever called by the single awaiting consumer after completion —
            // no lock required. Reset must run even when GetResult throws (cancellation),
            // otherwise the MRVTSC stays completed and the next SetResult/SetException blows up.
            try
            {
                return _mrvtsc.GetResult(token);
            }
            finally
            {
                _mrvtsc.Reset();
            }
        }

        ValueTaskSourceStatus IValueTaskSource<bool>.GetStatus(short token) => _mrvtsc.GetStatus(token);

        void IValueTaskSource<bool>.OnCompleted(Action<object> continuation, object state, short token, ValueTaskSourceOnCompletedFlags flags)
            => _mrvtsc.OnCompleted(continuation, state, token, flags);
    }
}
