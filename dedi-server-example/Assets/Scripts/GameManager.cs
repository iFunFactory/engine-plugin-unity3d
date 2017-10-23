using Fun;
using Prototype.NetworkLobby;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Networking.NetworkSystem;
using UnityEngine.UI;


public class GameManager : NetworkBehaviour
{
    public GameObject resultPopup;

    public static GameManager instance { get; private set; }


    void Awake ()
    {
        instance = this;
    }

    [ServerCallback]
    void Start ()
    {
        NetworkServer.RegisterHandler(PongMsgType.Ready, OnReadyMessage);
        NetworkServer.RegisterHandler(PongMsgType.BarPos, OnBarPosMessage);
        NetworkServer.RegisterHandler(PongMsgType.BallPos, OnBallPosMessage);

        StartCoroutine(OnUpdate());
    }

    public override void OnStartServer ()
    {
        transform.Find("Panel").gameObject.SetActive(false);
    }

    public override void OnStartClient ()
    {
        CanvasScaler scaler = transform.GetComponent<CanvasScaler>();
        Bar.deltaScaler = scaler.referenceResolution.y / Screen.height;

        Transform panel = transform.Find("Panel");
        oppBar = panel.Find("OppBar").GetComponent<Bar>();
        ball = panel.Find("Ball").GetComponent<Ball>();
        ball.Reset();

        if (NetworkClient.allClients.Count > 0)
        {
            NetworkClient client = NetworkClient.allClients[0];
            client.RegisterHandler(PongMsgType.Start, OnStartMessage);
            PongMessage.client = client;
        }

        string uid, token;
        FunapiManager.GetUidAndToken(out uid, out token);
        PongMessage.SendReady(uid, token);
    }

    public void OnClose ()
    {
        PongMessage.client = null;
        LobbyManager.s_Singleton.StopClientClbk();
    }

    IEnumerator OnUpdate ()
    {
        if (players_.Count < 2)
            yield return null;

        yield return new WaitForSeconds(1f);

        yield return StartCoroutine(OnStart());

        yield return StartCoroutine(OnPlay());

        yield return StartCoroutine(OnEnd());

        Application.Quit();
    }

    IEnumerator OnStart ()
    {
        RpcStart();

        yield return null;
    }

    IEnumerator OnPlay ()
    {
        while (true)
        {
            if (ballPos.y > (barPosY + kOutOfBounds) || ballPos.y < -(barPosY + kOutOfBounds))
            {
                winnerId = ballPos.y > 0 ? hostId : guestId;
                Debug.Log("GAME ENDED!!!");
                break;
            }

            yield return new WaitForFixedUpdate();
        }
    }

    IEnumerator OnEnd ()
    {
        RpcEnd(winnerId);

        Dictionary<string, object> result = new Dictionary<string, object>();
        result["winner"] = winnerId;
        Dictionary<string, object> json = new Dictionary<string, object>();
        json["result"] = result;

        string json_string = FunapiMessage.JsonHelper.Serialize(json);
        FunapiDedicatedServer.SendResult(json_string);
        yield return new WaitForSeconds(1f);

        foreach (Player player in players_.Values)
        {
            onPlayerLeft(player);
        }

        FunapiDedicatedServer.Stop();
        yield return new WaitForSeconds(3f);

        LobbyManager.s_Singleton.StopServerClbk();

        players_.Clear();
    }


    public void AddPlayer (GameObject gamePlayer, LobbyPlayer player)
    {
        Player newPlayer = new Player();
        newPlayer.playerId = player.playerControllerId;
        newPlayer.connectionId = player.connectionToClient.connectionId;
        newPlayer.nickname = player.nameInput.text;
        players_.Add(newPlayer.connectionId, newPlayer);
    }

    void onPlayerJoined (Player player)
    {
        if (player != null)
            Fun.FunapiDedicatedServer.SendJoined(player.uid);
    }

    void onPlayerLeft (Player player)
    {
        if (player != null)
            Fun.FunapiDedicatedServer.SendLeft(player.uid);
    }

    Player findPlayer (int connectionId)
    {
        if (players_.ContainsKey(connectionId))
            return players_[connectionId];

        return null;
    }


    // Game Messages
    void OnStartMessage (NetworkMessage msg)
    {
        StartMessage start = msg.ReadMessage<StartMessage>();

        PongMessage.connId = start.id;
        myId = start.id;
        if (start.host)
        {
            ball.isHost = true;

            float vx = (ballSpeed + UnityEngine.Random.value) * (UnityEngine.Random.value < 0.5f ? -1 : 1);
            float vy = (ballSpeed + UnityEngine.Random.value) * (UnityEngine.Random.value < 0.5f ? -1 : 1);
            ball.SetVelocity(vx, vy);
            ball.SendProperties();
        }
        else
        {
            ball.Incapacitation();
        }
    }

    void OnReadyMessage (NetworkMessage msg)
    {
        ReadyMessage ready = msg.ReadMessage<ReadyMessage>();

        if (FunapiManager.instance.AuthUser(ready.uid, ready.token))
        {
            Player player = findPlayer(msg.conn.connectionId);

            if (player != null)
            {
                player.uid = ready.uid;
                player.token = ready.token;

                if (hostId == 0)
                    hostId = player.connectionId;
                else
                    guestId = player.connectionId;

                onPlayerJoined(player);

                PongMessage.SendStart(player.connectionId, hostId == player.connectionId);
            }
        }
    }

    void OnBarPosMessage (NetworkMessage msg)
    {
        BarPos bar = msg.ReadMessage<BarPos>();
        RpcSetOppBarPosition(bar.id, bar.px, bar.time);
    }

    void OnBallPosMessage (NetworkMessage msg)
    {
        BallPos pos = msg.ReadMessage<BallPos>();
        ballPos.x = pos.px;
        ballPos.y = pos.py;

        RpcSetBallProperties(pos.id, pos.px, pos.py, pos.vx, pos.vy);
    }

    [ClientRpc]
    void RpcStart ()
    {
    }

    [ClientRpc]
    void RpcEnd (int winner)
    {
        if (resultPopup != null)
        {
            Text text = resultPopup.transform.Find("Text").GetComponent<Text>();
            if (text != null)
                text.text = winner == myId ? "YOU WIN!" : "YOU LOSE!";

            resultPopup.SetActive(true);
        }
    }

    [ClientRpc]
    void RpcSetOppBarPosition (int id, float x, float time)
    {
        if (id == myId)
            return;

        oppBar.SetPosX(-x);
    }

    [ClientRpc]
    void RpcSetBallProperties (int id, float x, float y, float vx, float vy)
    {
        if (id == myId)
            return;

        ball.SetPos(-x, -y);
    }


    class Player
    {
        public short playerId;
        public int connectionId;
        public string nickname;
        public string uid;
        public string token;
    }

    const float ballSpeed = 1.5f;
    const float barPosY = 270f;
    const float kOutOfBounds = 60f;

    Dictionary<int, Player> players_ = new Dictionary<int, Player>();

    int myId = 0;
    int hostId = 0;
    int guestId = 0;
    int winnerId = 0;

    Ball ball = null;
    Bar oppBar = null;
    Vector2 ballPos = Vector2.zero;
}
