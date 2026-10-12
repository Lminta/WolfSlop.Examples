using System.Collections.Concurrent;
using WolfNET.Game.Core.EventSystem.Interfaces;

namespace WolfNET.Game.Core.EventSystem
{
    public abstract class SequentialEventSystemBase<TDerived> : UnorderedEventSystemBase<TDerived>
        where TDerived : SequentialEventSystemBase<TDerived>
    {
        protected readonly ConcurrentQueue<IEvent<TDerived>> EventQueue = new();
        protected readonly object ProcessLock = new();
        private Task? _processTask;
        private TaskCompletionSource? _pauseSource;

        public override Task TriggerEvent<TEvent>(TEvent gameEvent)
        {
            EventQueue.Enqueue(gameEvent);
            return ProcessEvents();
        }

        public void SetPaused(bool paused)
        {
            if (paused)
            {
                if (_pauseSource is { Task.IsCompleted: false })
                {
                    return;
                }
                else
                {
                    _pauseSource?.TrySetCanceled();
                    _pauseSource = new TaskCompletionSource();
                }
            }
            else
            {
                _pauseSource?.TrySetResult();
                _pauseSource = null;
            }
        }

        public void Clear()
        {
            EventQueue.Clear();
            _pauseSource?.TrySetResult();
            _pauseSource = null;
        }

        private Task ProcessEvents()
        {
            lock (ProcessLock)
            {
                if (_processTask is { IsCompleted: false })
                    return _processTask;

                _processTask = ProcessEventsInternal();
                return _processTask;
            }
        }

        private async Task ProcessEventsInternal()
        {
            while (EventQueue.TryDequeue(out var gameEvent))
            {
                if (_pauseSource != null)
                    await _pauseSource.Task;
                    
                await base.TriggerEvent(gameEvent);
                
                if (_pauseSource != null)
                    await _pauseSource.Task;
            }
        }
    }
}

