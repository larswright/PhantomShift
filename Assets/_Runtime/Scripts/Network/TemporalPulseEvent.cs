using UnityEngine;
using UnityEngine.Events;

[CreateAssetMenu(fileName = "TemporalPulseEvent", menuName = "Events/Temporal Pulse Event")]
public class TemporalPulseEvent : ScriptableObject
{
    [System.Serializable] public class IntEvent : UnityEvent<int> {}

    [SerializeField] private IntEvent onPulseStarted = new IntEvent();
    [SerializeField] private IntEvent onPulseEnded   = new IntEvent();

    // Observadores podem assinar no inspetor.
    public void RaiseStarted(int pulseIndex) => onPulseStarted?.Invoke(pulseIndex);
    public void RaiseEnded(int pulseIndex)   => onPulseEnded?.Invoke(pulseIndex);
}
