using System.Collections.Concurrent;
using WolfNET.Game.Core.EventSystem.Interfaces;

namespace WolfNET.Game.Core.EventSystem
{
    public abstract class UnorderedEventSystemBase<TDerived> : IEventSystem<TDerived>
        where TDerived : UnorderedEventSystemBase<TDerived>
    {
        protected readonly ConcurrentDictionary<Type, List<IEventListener<TDerived, IEvent<TDerived>>>> Listeners =
            new();

        protected readonly object ListenersLock = new();

        public virtual void AddListener<TEvent>(IEventListener<TDerived, TEvent> listener)
            where TEvent : IEvent<TDerived>
        {
            lock (ListenersLock)
            {
                var eventType = typeof(TEvent);
                if (!Listeners.ContainsKey(eventType))
                {
                    Listeners[eventType] = new List<IEventListener<TDerived, IEvent<TDerived>>>();
                }

                var wrapper = new EventListenerWrapper<TDerived, TEvent>(listener);
                Listeners[eventType].Add(wrapper);
            }
        }

        public virtual void RemoveListener<TEvent>(IEventListener<TDerived, TEvent> listener)
            where TEvent : IEvent<TDerived>
        {
            lock (ListenersLock)
            {
                var eventType = typeof(TEvent);
                if (Listeners.TryGetValue(eventType, out var eventListeners))
                {
                    var wrapperToRemove = eventListeners.FirstOrDefault(wrapper => 
                        wrapper is EventListenerWrapper<TDerived, TEvent> typedWrapper && 
                        typedWrapper.WrapsListener(listener));
                
                    if (wrapperToRemove != null)
                    {
                        eventListeners.Remove(wrapperToRemove);
                    }
                }
            }
        }

        public virtual Task TriggerEvent<TEvent>(TEvent gameEvent) where TEvent : IEvent<TDerived>
        {
            return ProcessEvents<TEvent>(gameEvent);
        }

        private async Task ProcessEvents<TEvent>(TEvent gameEvent) where TEvent : IEvent<TDerived>
        {
            var tasks = new List<Task>();
            var processedTypes = new HashSet<Type>(); 
    
            lock (ListenersLock)
            {
                var eventType = gameEvent.GetType();
                ProcessEventTypeHierarchy(eventType, gameEvent, tasks, processedTypes);
            }

            await Task.WhenAll(tasks);
        }

        private void ProcessEventTypeHierarchy(Type type, IEvent<TDerived> gameEvent, List<Task> tasks, HashSet<Type> processedTypes)
        {
            // Skip if already processed
            if (!processedTypes.Add(type)) return;
    
            // Look for listeners for this type
            if (Listeners.TryGetValue(type, out var listeners))
            {
                foreach (var listener in listeners)
                {
                    tasks.Add(listener.OnEvent(gameEvent));
                }
            }
    
            // Process all interfaces that derive from IEvent<TDerived>
            foreach (var interfaceType in type.GetInterfaces())
            {
                if (typeof(IEvent<TDerived>).IsAssignableFrom(interfaceType))
                {
                    ProcessEventTypeHierarchy(interfaceType, gameEvent, tasks, processedTypes);
                }
            }
        }

        public virtual void ClearListeners<TEvent>() where TEvent : IEvent<TDerived>
        {
            lock (ListenersLock)
            {
                var eventType = typeof(TEvent);
                Listeners.TryRemove(eventType, out _);
            }
        }

        public virtual void ClearAllListeners()
        {
            lock (ListenersLock)
            {
                Listeners.Clear();
            }
        }
        
        private class EventListenerWrapper<T, TEvent> : IEventListener<T, IEvent<T>>
            where TEvent : IEvent<T>
            where T : UnorderedEventSystemBase<T>
        {
            private readonly IEventListener<T, TEvent> _listener;

            public EventListenerWrapper(IEventListener<T, TEvent> listener)
            {
                _listener = listener;
            }

            public async Task OnEvent(IEvent<T> gameEvent)
            {
                if (gameEvent is TEvent typedEvent)
                {
                    await _listener.OnEvent(typedEvent);
                }
            }
            
            public bool WrapsListener(IEventListener<T, TEvent> listener)
            {
                return ReferenceEquals(_listener, listener);
            }
        }
    }
}