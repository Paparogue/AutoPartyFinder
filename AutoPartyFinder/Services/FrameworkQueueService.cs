using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;

namespace AutoPartyFinder.Services;

public class FrameworkQueueService : IDisposable
{
    private readonly IFramework _framework;
    private readonly IPluginLog _pluginLog;
    private readonly Queue<(Action action, DateTime executeTime)> _frameworkQueue = new();
    private readonly object _queueLock = new();

    public FrameworkQueueService(IFramework framework, IPluginLog pluginLog)
    {
        _framework = framework;
        _pluginLog = pluginLog;
        _framework.Update += OnFrameworkUpdate;
    }

    private void OnFrameworkUpdate(IFramework fw)
    {
        var now = DateTime.UtcNow;
        List<(Action action, DateTime executeTime)> actionsToExecute = new();

        lock (_queueLock)
        {
            while (_frameworkQueue.Count > 0)
            {
                var (action, executeTime) = _frameworkQueue.Peek();
                if (now >= executeTime)
                {
                    actionsToExecute.Add(_frameworkQueue.Dequeue());
                }
                else
                {
                    break;
                }
            }
        }

        foreach (var (action, _) in actionsToExecute)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                _pluginLog.Error(ex, "Error executing queued framework action");
            }
        }
    }

    public void QueueAction(Action action, int delayMs = 0)
    {
        lock (_queueLock)
        {
            var executeTime = DateTime.UtcNow.AddMilliseconds(delayMs);
            _frameworkQueue.Enqueue((action, executeTime));
        }
    }

    public void ClearQueue()
    {
        lock (_queueLock)
        {
            _frameworkQueue.Clear();
        }
    }

    public void Dispose()
    {
        _framework.Update -= OnFrameworkUpdate;
        ClearQueue();
    }
}