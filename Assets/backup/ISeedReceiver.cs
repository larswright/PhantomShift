// ISeedReceiver.cs
public interface ISeedReceiver
{
    /// <summary>
    /// Recebe a seed do gerenciador e aplica lógica local (ex.: spawn determinístico).
    /// </summary>
    void SetSeed(int seed);
}
