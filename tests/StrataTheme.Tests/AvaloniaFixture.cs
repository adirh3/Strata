using Avalonia;
using Avalonia.Headless;

namespace StrataTheme.Tests;

[CollectionDefinition("Avalonia UI", DisableParallelization = true)]
public sealed class AvaloniaTestCollection : ICollectionFixture<AvaloniaFixture>;

public sealed class AvaloniaFixture : IDisposable
{
    private readonly HeadlessUnitTestSession _session;

    public AvaloniaFixture()
    {
        _session = HeadlessUnitTestSession.StartNew(
            typeof(StrataTestApp),
            AvaloniaTestIsolationLevel.PerTest);
    }

    public Task Dispatch(Action action, CancellationToken cancellationToken = default)
    {
        return _session.Dispatch(action, cancellationToken);
    }

    public Task<TResult> Dispatch<TResult>(Func<TResult> action, CancellationToken cancellationToken = default)
    {
        return _session.Dispatch(action, cancellationToken);
    }

    public Task Dispatch(Func<Task> action, CancellationToken cancellationToken = default) =>
        _session.Dispatch<bool>(async () =>
        {
            await action();
            return true;
        }, cancellationToken);

    public void Dispose()
    {
        try
        {
            _session.Dispose();
        }
        catch (NullReferenceException)
        {
            // Avalonia.Headless can throw during teardown after assertions have completed.
        }
    }
}

public sealed class StrataTestApp : Application;
