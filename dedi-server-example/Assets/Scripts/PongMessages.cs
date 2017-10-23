using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Networking.NetworkSystem;


public class PongMsgType
{
    public static short Start = 201;

    public static short Ready = 301;
    public static short BarPos = 305;
    public static short BallPos = 306;
}

public class StartMessage : MessageBase
{
    public int id;
    public bool host;
}

public class ReadyMessage : MessageBase
{
    public string uid;
    public string token;
}

public class BarPos : MessageBase
{
    public int id;
    public float px;
    public float time;
}

public class BallPos : MessageBase
{
    public int id;
    public float px;
    public float py;
    public float vx;
    public float vy;
}


public class PongMessage
{
    public static NetworkClient client
    {
        private get; set;
    }

    public static int connId
    {
        private get; set;
    }

    // Server messages
    public static void SendStart (int connectionId, bool isHost)
    {
        StartMessage msg = new StartMessage();
        msg.id = connectionId;
        msg.host = isHost;
        NetworkServer.SendToClient(connectionId, PongMsgType.Start, msg);
    }


    // Client messages
    public static void SendReady (string uid, string token)
    {
        if (client == null)
            return;

        ReadyMessage msg = new ReadyMessage();
        msg.uid = uid;
        msg.token = token;

        client.Send(PongMsgType.Ready, msg);
    }

    public static void SendBarPos (float px, float time)
    {
        if (client == null)
            return;

        BarPos msg = new BarPos();
        msg.id = connId;
        msg.px = px;
        msg.time = time;

        client.Send(PongMsgType.BarPos, msg);
    }

    public static void SendBallPos (float px, float py, float vx, float vy)
    {
        if (client == null)
            return;

        BallPos msg = new BallPos();
        msg.id = connId;
        msg.px = px;
        msg.py = py;
        msg.vx = vx;
        msg.vy = vy;

        client.Send(PongMsgType.BallPos, msg);
    }
}
