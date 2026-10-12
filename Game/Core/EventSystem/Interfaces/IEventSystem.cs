namespace WolfNET.Game.Core.EventSystem.Interfaces
{
    public interface IEventSystem<TDerived> where TDerived : IEventSystem<TDerived>
    {
        void AddListener<TEvent>(IEventListener<TDerived, TEvent> listener) where TEvent : IEvent<TDerived>;
        void RemoveListener<TEvent>(IEventListener<TDerived, TEvent> listener) where TEvent : IEvent<TDerived>;
        Task TriggerEvent<TEvent>(TEvent gameEvent) where TEvent : IEvent<TDerived>;
        void ClearListeners<TEvent>() where TEvent : IEvent<TDerived>;
        void ClearAllListeners();
    }
}