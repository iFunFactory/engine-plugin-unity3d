using Prototype.NetworkLobby;
using UnityEngine;
using UnityEngine.Networking;


public class PongLobbyHook : LobbyHook
{
    public override void OnLobbyServerSceneLoadedForPlayer (NetworkManager manager, GameObject lobbyPlayer, GameObject gamePlayer)
    {
        if (lobbyPlayer == null)
            return;

        LobbyPlayer player = lobbyPlayer.GetComponent<LobbyPlayer>();
        if (player != null)
            GameManager.instance.AddPlayer(gamePlayer, player);
    }
}
