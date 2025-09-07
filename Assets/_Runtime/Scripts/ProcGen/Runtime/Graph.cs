using System.Collections.Generic;

public enum EdgeKind { Door, Corridor }

public class RoomNode {
    public int Id;
    public string ArchetypeId;
    public int W, H; // em células
}

public class RoomEdge {
    public int A, B;
    public EdgeKind Kind;
}

public class RoomGraph {
    public List<RoomNode> Nodes = new List<RoomNode>();
    public List<RoomEdge> Edges = new List<RoomEdge>();
}

