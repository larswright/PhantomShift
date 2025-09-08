// ISeedReceiver.cs
public interface ISeedReceiver
{
    /// <summary>
    /// Recebe a seed do gerenciador e aplica l�gica local (ex.: spawn determin�stico).
    /// </summary>
    void SetSeed(int seed);
}
