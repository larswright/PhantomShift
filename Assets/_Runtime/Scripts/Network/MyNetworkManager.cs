using System.Collections.Generic;
using Mirror;
using UnityEngine;

public class MyNetworkManager : NetworkManager
{
    // Se quiser forçar aleatório mesmo sem configurar no inspector
    [Header("Spawn")]
    public bool forceRandomSpawn = true;

    public override void OnServerAddPlayer(NetworkConnectionToClient conn)
    {
        Transform startPos = null;

        if (forceRandomSpawn)
        {
            // 'startPositions' é mantida pela classe base a partir dos componentes NetworkStartPosition na cena
            if (startPositions != null && startPositions.Count > 0)
            {
                int index = Random.Range(0, startPositions.Count);
                startPos = startPositions[index];
            }
        }
        else
        {
            // comportamento padrão (pode ser RoundRobin/Random dependendo do inspector da classe base)
            startPos = GetStartPosition();
        }

        GameObject player = startPos != null
            ? Instantiate(playerPrefab, startPos.position, startPos.rotation)
            : Instantiate(playerPrefab);

        NetworkServer.AddPlayerForConnection(conn, player);
    }
}
