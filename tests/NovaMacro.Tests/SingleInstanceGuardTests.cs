using NovaMacro.App;

namespace NovaMacro.Tests;

public sealed class SingleInstanceGuardTests
{
    private static readonly TimeSpan ThreadTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void ProductionMutexName_HasApprovedLocalScopeAndStableName()
    {
        Assert.Equal(
            @"Local\NovaMacro.SingleInstance",
            SingleInstanceGuard.ProductionMutexName);
    }

    [Fact]
    public void TryAcquire_FreshUniqueNameSucceeds()
    {
        string name = UniqueMutexName();

        bool acquired = SingleInstanceGuard.TryAcquire(name, out var guard);

        Assert.True(acquired);
        Assert.NotNull(guard);
        guard.Dispose();
    }

    [Fact]
    public void TryAcquire_SameThreadDuplicateReturnsFalseNullAndDoesNotLeak()
    {
        string name = UniqueMutexName();
        Assert.True(SingleInstanceGuard.TryAcquire(name, out var firstGuard));
        Assert.NotNull(firstGuard);

        try
        {
            bool secondAcquired = SingleInstanceGuard.TryAcquire(
                name,
                out var secondGuard);

            Assert.False(secondAcquired);
            Assert.Null(secondGuard);
        }
        finally
        {
            firstGuard.Dispose();
        }

        Assert.True(SingleInstanceGuard.TryAcquire(name, out var reacquiredGuard));
        Assert.NotNull(reacquiredGuard);
        reacquiredGuard.Dispose();
    }

    [Fact]
    public void TryAcquire_DifferentThreadDuplicateReturnsFalseAndNull()
    {
        string name = UniqueMutexName();
        using var ownerReady = new ManualResetEventSlim(false);
        using var releaseOwner = new ManualResetEventSlim(false);
        SingleInstanceGuard? ownerGuard = null;
        Exception? ownerFailure = null;
        bool ownerAcquired = false;
        var ownerThread = new Thread(() =>
        {
            try
            {
                ownerAcquired = SingleInstanceGuard.TryAcquire(
                    name,
                    out ownerGuard);
                ownerReady.Set();

                if (ownerAcquired)
                {
                    releaseOwner.Wait();
                    ownerGuard!.Dispose();
                }
            }
            catch (Exception exception)
            {
                ownerFailure = exception;
                ownerReady.Set();
            }
        });

        ownerThread.Start();
        try
        {
            Assert.True(ownerReady.Wait(ThreadTimeout));
            Assert.Null(ownerFailure);
            Assert.True(ownerAcquired);
            Assert.NotNull(ownerGuard);

            bool duplicateAcquired = SingleInstanceGuard.TryAcquire(
                name,
                out var duplicateGuard);

            Assert.False(duplicateAcquired);
            Assert.Null(duplicateGuard);
        }
        finally
        {
            releaseOwner.Set();
            Assert.True(ownerThread.Join(ThreadTimeout));
        }

        Assert.Null(ownerFailure);
    }

    [Fact]
    public void TryAcquire_DifferentNamesAreIndependent()
    {
        string firstName = UniqueMutexName();
        string secondName = UniqueMutexName();
        Assert.True(SingleInstanceGuard.TryAcquire(firstName, out var firstGuard));

        try
        {
            Assert.True(SingleInstanceGuard.TryAcquire(
                secondName,
                out var secondGuard));
            Assert.NotNull(secondGuard);
            secondGuard.Dispose();
        }
        finally
        {
            firstGuard!.Dispose();
        }
    }

    [Fact]
    public void Dispose_ReleasesNameForReacquisition()
    {
        string name = UniqueMutexName();
        Assert.True(SingleInstanceGuard.TryAcquire(name, out var firstGuard));
        Assert.NotNull(firstGuard);

        firstGuard.Dispose();

        Assert.True(SingleInstanceGuard.TryAcquire(name, out var secondGuard));
        Assert.NotNull(secondGuard);
        secondGuard.Dispose();
    }

    [Fact]
    public void Dispose_TwiceIsIdempotentAndNameRemainsReusable()
    {
        string name = UniqueMutexName();
        Assert.True(SingleInstanceGuard.TryAcquire(name, out var guard));
        Assert.NotNull(guard);

        guard.Dispose();
        guard.Dispose();

        Assert.True(SingleInstanceGuard.TryAcquire(name, out var reacquiredGuard));
        Assert.NotNull(reacquiredGuard);
        reacquiredGuard.Dispose();
    }

    [Fact]
    public void Dispose_FromWrongThreadThrowsWithoutReleasingOwnership()
    {
        string name = UniqueMutexName();
        using var ownerReady = new ManualResetEventSlim(false);
        using var requestOwnerBookkeepingCheck = new ManualResetEventSlim(false);
        using var ownerBookkeepingChecked = new ManualResetEventSlim(false);
        using var releaseOwner = new ManualResetEventSlim(false);
        SingleInstanceGuard? ownerGuard = null;
        SingleInstanceGuard? ownerDuplicateGuard = null;
        Exception? ownerFailure = null;
        bool ownerAcquired = false;
        bool ownerDuplicateAcquired = true;
        var ownerThread = new Thread(() =>
        {
            try
            {
                ownerAcquired = SingleInstanceGuard.TryAcquire(
                    name,
                    out ownerGuard);
                ownerReady.Set();

                if (ownerAcquired)
                {
                    requestOwnerBookkeepingCheck.Wait();
                    try
                    {
                        ownerDuplicateAcquired = SingleInstanceGuard.TryAcquire(
                            name,
                            out ownerDuplicateGuard);
                    }
                    catch (Exception exception)
                    {
                        ownerFailure = exception;
                    }
                    finally
                    {
                        ownerBookkeepingChecked.Set();
                    }

                    releaseOwner.Wait();
                    ownerDuplicateGuard?.Dispose();
                    ownerGuard!.Dispose();
                }
            }
            catch (Exception exception)
            {
                ownerFailure = exception;
                ownerReady.Set();
                ownerBookkeepingChecked.Set();
            }
        });

        ownerThread.Start();
        try
        {
            Assert.True(ownerReady.Wait(ThreadTimeout));
            Assert.Null(ownerFailure);
            Assert.True(ownerAcquired);
            SingleInstanceGuard guard = Assert.IsType<SingleInstanceGuard>(
                ownerGuard);

            InvalidOperationException exception =
                Assert.Throws<InvalidOperationException>(guard.Dispose);
            Assert.Contains("thread", exception.Message);

            bool duplicateAcquired = SingleInstanceGuard.TryAcquire(
                name,
                out var duplicateGuard);
            Assert.False(duplicateAcquired);
            Assert.Null(duplicateGuard);

            requestOwnerBookkeepingCheck.Set();
            Assert.True(ownerBookkeepingChecked.Wait(ThreadTimeout));
            Assert.Null(ownerFailure);
            Assert.False(ownerDuplicateAcquired);
            Assert.Null(ownerDuplicateGuard);
        }
        finally
        {
            requestOwnerBookkeepingCheck.Set();
            releaseOwner.Set();
            Assert.True(ownerThread.Join(ThreadTimeout));
        }

        Assert.Null(ownerFailure);
        Assert.True(SingleInstanceGuard.TryAcquire(name, out var reacquiredGuard));
        Assert.NotNull(reacquiredGuard);
        reacquiredGuard.Dispose();
    }

    [Fact]
    public void TryAcquire_AbandonedOwnerThreadRecoversAndBookkeepingRemainsReusable()
    {
        string name = UniqueMutexName();
        SingleInstanceGuard? deliberatelyAbandonedGuard = null;
        Exception? ownerFailure = null;
        bool ownerAcquired = false;
        var ownerThread = new Thread(() =>
        {
            try
            {
                ownerAcquired = SingleInstanceGuard.TryAcquire(
                    name,
                    out deliberatelyAbandonedGuard);
            }
            catch (Exception exception)
            {
                ownerFailure = exception;
            }
        });

        ownerThread.Start();
        Assert.True(ownerThread.Join(ThreadTimeout));
        Assert.Null(ownerFailure);
        Assert.True(ownerAcquired);
        Assert.NotNull(deliberatelyAbandonedGuard);

        SingleInstanceGuard? recoveredGuard = null;
        try
        {
            bool recovered = SingleInstanceGuard.TryAcquire(
                name,
                out recoveredGuard);

            Assert.True(recovered);
            Assert.NotNull(recoveredGuard);
            recoveredGuard.Dispose();
            recoveredGuard = null;

            Assert.True(SingleInstanceGuard.TryAcquire(
                name,
                out var subsequentGuard));
            Assert.NotNull(subsequentGuard);
            subsequentGuard.Dispose();
        }
        finally
        {
            recoveredGuard?.Dispose();
            GC.KeepAlive(deliberatelyAbandonedGuard);
        }
    }

    [Fact]
    public void TryAcquire_NullNameThrows()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
            () => SingleInstanceGuard.TryAcquire(null!, out _));

        Assert.Equal("mutexName", exception.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryAcquire_EmptyOrWhitespaceNameThrows(string name)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => SingleInstanceGuard.TryAcquire(name, out _));

        Assert.Equal("mutexName", exception.ParamName);
    }

    private static string UniqueMutexName()
    {
        return @"Local\NovaMacro.Tests." + Guid.NewGuid().ToString("N");
    }
}
