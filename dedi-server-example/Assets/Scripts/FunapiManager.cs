using Fun;
using Prototype.NetworkLobby;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;


public class FunapiManager : MonoBehaviour
{
    // host address & port
    public string kServerAddr = "127.0.0.1";
    public ushort kServerPort = 8012;
    public bool manualTest = false;


    public static void StartPlay ()
    {
        UnityEditor.EditorApplication.isPlaying = true;
    }

    void Awake ()
    {
        FunapiDedicatedServer.version = "{ \"version\": \"1.0.0.1\" }";

        if (instance_ == null)
        {
            instance_ = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (instance_ != this)
        {
            Destroy(gameObject);
        }
    }

    void Start ()
    {
        if (started_ || manualTest)
            return;

        started_ = true;

        if (!FunapiDedicatedServer.Init())
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(0);
            //UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
            return;
        }

        if (FunapiDedicatedServer.isServer)
        {
            FunapiDedicatedServer.UserDataCallback += onReceivedUserData;
            FunapiDedicatedServer.MatchDataCallback += onReceivedMatchData;

            StartCoroutine(startServer());
        }
        else
        {
            StartClient();
        }
    }

    IEnumerator startServer ()
    {
        while (LobbyManager.s_Singleton == null)
            yield return new WaitForSeconds(0.1f);

        LobbyManager.s_Singleton.networkPort = FunapiDedicatedServer.serverPort;
        LobbyManager.s_Singleton.StartServer();

        yield return new WaitForSeconds(0.5f);

        FunapiDedicatedServer.Start();
    }

    public void StartClient ()
    {
        session_ = FunapiSession.Create(kServerAddr);
        session_.SessionEventCallback += onSessionEvent;
        session_.TransportEventCallback += onTransportEvent;
        session_.ReceivedMessageCallback += onReceived;

        TcpTransportOption option = new TcpTransportOption();
        option.ConnectionTimeout = 10f;

        session_.Connect(TransportProtocol.kTcp, FunEncoding.kJson, kServerPort, option);
    }

    public bool AuthUser (string uid, string token)
    {
        if (manualTest)
            return true;

        return FunapiDedicatedServer.AuthUser(uid, token);
    }

    public static void GetUidAndToken (out string uid, out string token)
    {
        uid = uid_;
        token = token_;
    }

    void onSessionEvent (SessionEventType type, string session__id)
    {
        if (type == SessionEventType.kOpened)
        {
            uid_ = session__id;
        }
    }

    void onTransportEvent (TransportProtocol protocol, TransportEventType type)
    {
        if (type == TransportEventType.kStarted)
        {
            Dictionary<string, object> body = new Dictionary<string, object>();
            body["name"] = uid_;
            session_.SendMessage("login", body);
        }
    }

    void onReceived (string msg_type, object body)
    {
        Dictionary<string, object> message = body as Dictionary<string, object>;

        if (msg_type == "_sc_dedicated_server")
        {
            Dictionary<string, object> redirect = message["redirect"] as Dictionary<string, object>;
            string ip = redirect["host"] as string;
            int port = Convert.ToInt32(redirect["port"]);
            token_ = redirect["token"] as string;

            LobbyManager.s_Singleton.networkAddress = ip;
            LobbyManager.s_Singleton.networkPort = port;

            GameObject panel = GameObject.Find("MainPanel");
            if (panel != null)
            {
                LobbyMainMenu mainMenu = panel.GetComponent<LobbyMainMenu>();
                if (mainMenu != null)
                    mainMenu.StartJoin();
            }
        }
    }

    void onReceivedUserData (string uid, string json_string)
    {
    }

    void onReceivedMatchData (string json_string)
    {
        if (send_ready_)
            return;

        send_ready_ = true;
        FunapiDedicatedServer.Ready();
    }


    public static FunapiManager instance { get { return instance_; } }
    static FunapiManager instance_ = null;

    FunapiSession session_ = null;
    bool started_ = false;
    bool send_ready_ = false;

    static string uid_ = "";
    static string token_ = "";
}
