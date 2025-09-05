using System.Collections;
using Mirror;
using UnityEngine;

/// <summary>
/// Controla 1–3 "pulsos temporais" sincronizados (Mirror).
/// - Servidor sorteia contagem (min..max) e intervalos (1–5 min por pulso).
/// - Cada pulso: loop -> espera intervalo -> "começa" (ativa bit) -> espera duração -> "termina" (limpa bit).
/// - Clientes só reagem a alterações da bitmask (sem Update, sem coroutines no cliente).
/// - Eventos de ScriptableObject avisam começo/fim para outros sistemas.
/// Foco em otimização: sem GC por frame; no servidor, até 3 coroutines (barato).
/// </summary>
[DisallowMultipleComponent]
public class TemporalPulseController : NetworkBehaviour
{
    [Header("Configuração de Pulsos")]
    [Tooltip("Mínimo e máximo de pulsos independentes.")]
    [Range(1,3)] public int minPulses = 1;
    [Range(1,3)] public int maxPulses = 3;

    [Tooltip("Intervalo aleatório (segundos) por pulso (fixo por pulso). 60–300 = 1–5 min.")]
    public Vector2 intervalSecondsRange = new Vector2(60f, 300f);

    [Tooltip("Duração (segundos) enquanto o pulso está ATIVO.")]
    public float pulseDurationSeconds = 10f;

    [Header("Eventos (ScriptableObject)")]
    [Tooltip("Asset de evento que recebe RaiseStarted/Ended.")]
    public TemporalPulseEvent pulseEvent;

    [Header("Parâmetros de Gameplay (placeholder)")]
    [SyncVar] public byte difficulty = 1; // TODO: usar para ajustar contagem/intervalos/duração do pulso
    [SyncVar] public byte intensity  = 1; // TODO: usar para modular efeitos visuais/sonoros/penalidades

    // Estado replicado: até 8 pulsos via bitmask; usamos bits 0..2.
    [SyncVar(hook = nameof(OnActiveMaskChanged))] private byte activeMask;

    // Servidor: quantidade e intervalos fixados no início
    private int pulseCount;
    private readonly float[] intervals = new float[3];
    private readonly WaitForSeconds[] waitIntervals = new WaitForSeconds[3];
    private WaitForSeconds waitDuration;

    private readonly Coroutine[] loops = new Coroutine[3];

    // ===== Ciclo de vida =====
    public override void OnStartServer()
    {
        base.OnStartServer();

        // Valida faixas
        if (minPulses < 1) minPulses = 1;
        if (maxPulses > 3) maxPulses = 3;
        if (maxPulses < minPulses) maxPulses = minPulses;

        // Sorteia quantidade de pulsos (1..3)
        pulseCount = Random.Range(minPulses, maxPulses + 1);

        // Prepara esperas sem realocação (intervalos fixos por pulso)
        for (int i = 0; i < pulseCount; i++)
        {
            float t = Random.Range(intervalSecondsRange.x, intervalSecondsRange.y);
            intervals[i] = Mathf.Max(1f, t);
            waitIntervals[i] = new WaitForSeconds(intervals[i]);
        }
        waitDuration = new WaitForSeconds(Mathf.Max(0.1f, pulseDurationSeconds));

        // Inicia loops (um por pulso) somente no servidor
        for (int i = 0; i < pulseCount; i++)
            loops[i] = StartCoroutine(PulseLoopServer(i));
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        // Para coroutines e limpa estado
        for (int i = 0; i < loops.Length; i++)
            if (loops[i] != null) StopCoroutine(loops[i]);
        SetPulseAllOffServer();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        // Ao entrar no meio do jogo, reprojeta estado atual:
        // dispara eventos "Started" para bits já ativos.
        if (activeMask != 0) OnActiveMaskChanged(0, activeMask);
    }

    // ===== Lógica do servidor =====
    private IEnumerator PulseLoopServer(int index)
    {
        // Espera o primeiro intervalo antes do primeiro pulso (padrão estável).
        yield return waitIntervals[index];

        while (true)
        {
            StartPulseServer(index);
            yield return waitDuration;
            EndPulseServer(index);
            yield return waitIntervals[index];
        }
    }

    [Server]
    private void StartPulseServer(int index)
    {
        byte bit = (byte)(1 << index);
        if ((activeMask & bit) != 0) return; // já ativo
        activeMask |= bit; // altera SyncVar -> hook dispara em server e clientes
    }

    [Server]
    private void EndPulseServer(int index)
    {
        byte bit = (byte)(1 << index);
        if ((activeMask & bit) == 0) return; // já inativo
        activeMask &= (byte)~bit; // altera SyncVar -> hook dispara em server e clientes
    }

    [Server]
    private void SetPulseAllOffServer()
    {
        if (activeMask == 0) return;
        activeMask = 0; // limpa tudo
    }

    // ===== Hook replicado (server + clients) =====
    private void OnActiveMaskChanged(byte oldMask, byte newMask)
    {
        // Detecta diferenças bit-a-bit e dispara eventos locais sem RPCs.
        byte diff = (byte)(oldMask ^ newMask);
        if (diff == 0) return;

        for (int i = 0; i < 3; i++)
        {
            byte bit = (byte)(1 << i);
            if ((diff & bit) == 0) continue;

            bool nowOn = (newMask & bit) != 0;
            RaiseLocalEvent(i, nowOn);
        }
    }

    private void RaiseLocalEvent(int pulseIndex, bool started)
    {
        if (!pulseEvent) return;
        if (started) pulseEvent.RaiseStarted(pulseIndex);
        else         pulseEvent.RaiseEnded(pulseIndex);
    }

    // ===== Utilidades (opcionais) =====
    /// <summary>Retorna o número de pulsos configurados pelo servidor (0..3 no cliente, após sync).</summary>
    public int GetPulseCount() => pulseCount;

    /// <summary>Retorna o intervalo (seg) do pulso (ou -1 se inválido).</summary>
    public float GetPulseIntervalSeconds(int index)
    {
        if ((uint)index >= (uint)pulseCount) return -1f;
        return intervals[index];
    }

    /// <summary>Indica se o pulso está ativo agora (espelha bitmask local).</summary>
    public bool IsPulseActive(int index)
    {
        if ((uint)index >= 3u) return false;
        return (activeMask & (1 << index)) != 0;
    }
}
