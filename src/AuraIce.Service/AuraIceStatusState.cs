using AuraIce.Core.Status;

namespace AuraIce.Service;

public sealed class AuraIceStatusState
{
    private readonly object _sync = new();
    private AuraIceStatusSnapshot _snapshot = AuraIceStatusSnapshot.Empty;

    public AuraIceStatusSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return _snapshot;
            }
        }
    }

    public void Update(AuraIceStatusSnapshot snapshot)
    {
        lock (_sync)
        {
            _snapshot = snapshot;
        }
    }
}
