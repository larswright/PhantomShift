using System;

public static class Rng {
    [ThreadStatic] static Random _r;
    public static Random Get(int seed) => new Random(seed);
    public static Random Shared => _r ?? (_r = new Random(Environment.TickCount));
}

