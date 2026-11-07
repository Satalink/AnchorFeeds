using System.Security.Principal;

namespace AnchorFeeds.Services;

public sealed class SingleInstanceService : IDisposable
{
    private const string NamePrefix = @"Local\AnchorFeeds";
    private readonly Mutex _instanceMutex;
    private readonly EventWaitHandle? _activationEvent;
    private readonly RegisteredWaitHandle? _activationWait;
    private readonly bool _ownsMutex;
    private bool _disposed;

    public event Action? ActivationRequested;

    public bool IsPrimary => _ownsMutex;

    public SingleInstanceService()
    {
        var userId = WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("Could not determine the current Windows user.");
        var nameSuffix = userId.Replace('-', '_');
        _instanceMutex = new Mutex(false, $"{NamePrefix}.Instance.{nameSuffix}");

        try
        {
            _ownsMutex = _instanceMutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            _ownsMutex = true;
        }

        if (!_ownsMutex)
        {
            return;
        }

        _activationEvent = new EventWaitHandle(
            false,
            EventResetMode.AutoReset,
            $"{NamePrefix}.Activate.{nameSuffix}");
        _activationWait = ThreadPool.RegisterWaitForSingleObject(
            _activationEvent,
            (_, _) => ActivationRequested?.Invoke(),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    public bool RequestActivation(TimeSpan timeout)
    {
        if (_ownsMutex)
        {
            throw new InvalidOperationException("The primary instance cannot request its own activation.");
        }

        var userId = WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("Could not determine the current Windows user.");
        var eventName = $"{NamePrefix}.Activate.{userId.Replace('-', '_')}";
        var expiresAt = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < expiresAt)
        {
            try
            {
                using var activationEvent = EventWaitHandle.OpenExisting(eventName);
                return activationEvent.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                Thread.Sleep(50);
            }
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _activationWait?.Unregister(null);
        _activationEvent?.Dispose();
        if (_ownsMutex)
        {
            _instanceMutex.ReleaseMutex();
        }

        _instanceMutex.Dispose();
    }
}
