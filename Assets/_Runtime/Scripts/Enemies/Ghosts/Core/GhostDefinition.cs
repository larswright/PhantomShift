using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "PhantomShift/Ghost Definition")]
public class GhostDefinition : ScriptableObject {
    public GhostArchetype baseArchetype;
    public List<GhostTraitSO> traits = new List<GhostTraitSO>();
}

