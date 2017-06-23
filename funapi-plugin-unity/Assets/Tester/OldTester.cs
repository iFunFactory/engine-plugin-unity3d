// Copyright 2013-2016 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using MiniJSON;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// protobuf
using funapi.network.fun_message;
using plugin_messages;


public class OldTester : MonoBehaviour
{
    public string serverAddress = "127.0.0.1";

    public bool sessionReliability = false;
    public bool sequenceValidation = false;
    public FunEncoding encoding = FunEncoding.kJson;
    public int sendingCount = 10;

    [Header("TCP Option")]
    public EncryptionType tcpEncryption = EncryptionType.kDefaultEncryption;
    public bool autoReconnect = false;
    public bool disableNagle = false;
    public bool usePing = false;

    [Header("UDP Option")]
    public EncryptionType udpEncryption = EncryptionType.kDefaultEncryption;

    [Header("HTTP Option")]
    public EncryptionType httpEncryption = EncryptionType.kDefaultEncryption;
    public bool useWWW = false;

    [Header("Encryption")]
    public string encryptionPublicKey = "";


    void Awake ()
    {
        if (encryptionPublicKey.Length > 0)
            FunapiEncryptor.public_key = encryptionPublicKey;

        GameObject.Find("ServerIP").GetComponent<Text>().text = serverAddress;

        buttons_["create"] = GameObject.Find("ButtonCreate").GetComponent<Button>();
        buttons_["test"] = GameObject.Find("ButtonTest").GetComponent<Button>();
        buttons_["close"] = GameObject.Find("ButtonClose").GetComponent<Button>();

        setButtonState(false);
    }

    public void OnCreateNetwork ()
    {
        GameObject.Find("ServerIP").GetComponent<Text>().text = serverAddress;
        buttons_["create"].interactable = false;

        StartCoroutine(createNetwork());
    }

    public void OnCloseNetwork ()
    {
        closeNetwork();
    }

    public void OnNetworkTest ()
    {
        if (network_.Started == false && !network_.SessionReliability)
        {
            FunDebug.Log("You should connect first.");
            return;
        }

        for (int i = 0; i < sendingCount; ++i)
        {
            sendMessage(TransportProtocol.kTcp);
            sendMessage(TransportProtocol.kUdp);
            sendMessage(TransportProtocol.kHttp);
        }
    }


    IEnumerator createNetwork ()
    {
        if (network_ != null)
            yield break;

        network_ = new FunapiNetwork(sessionReliability);
        network_.SequenceNumberValidation = sequenceValidation;

        network_.OnSessionInitiated += onSessionInitiated;
        network_.StoppedAllTransportCallback += onStoppedAllTransport;

        network_.RegisterHandler("echo", onEcho);
        network_.RegisterHandler("pbuf_echo", onEchoProtobuf);

        addTransport(TransportProtocol.kTcp);
        addTransport(TransportProtocol.kUdp);
        addTransport(TransportProtocol.kHttp);

        network_.Start();
    }

    void closeNetwork ()
    {
        if (network_ != null)
        {
            network_.Stop();
            network_ = null;
        }
    }

    void addTransport (TransportProtocol protocol)
    {
        FunapiTransport transport = null;
        ushort port = getPort(protocol, encoding);

        if (protocol == TransportProtocol.kTcp)
        {
            FunapiTcpTransport tcp = new FunapiTcpTransport(serverAddress, port, encoding);
            tcp.AutoReconnect = autoReconnect;
            tcp.DisableNagle = disableNagle;
            tcp.EnablePing = usePing;

            if (tcpEncryption != EncryptionType.kDefaultEncryption)
                tcp.SetEncryption(tcpEncryption);

            transport = tcp;
        }
        else if (protocol == TransportProtocol.kUdp)
        {
            FunapiUdpTransport udp = new FunapiUdpTransport(serverAddress, port, encoding);

            if (udpEncryption != EncryptionType.kDefaultEncryption)
                udp.SetEncryption(udpEncryption);

            transport = udp;
        }
        else if (protocol == TransportProtocol.kHttp)
        {
            FunapiHttpTransport http = new FunapiHttpTransport(serverAddress, port, false, encoding);
            http.UseWWW = useWWW;

            if (httpEncryption != EncryptionType.kDefaultEncryption)
                http.SetEncryption(httpEncryption);

            transport = http;
        }

        transport.ConnectTimeout = 10f;

        network_.AttachTransport(transport);
    }

    ushort getPort (TransportProtocol protocol, FunEncoding encoding)
    {
        ushort port = 0;
        if (protocol == TransportProtocol.kTcp)
            port = (ushort)(encoding == FunEncoding.kJson ? 8012 : 8022);
        else if (protocol == TransportProtocol.kUdp)
            port = (ushort)(encoding == FunEncoding.kJson ? 8013 : 8023);
        else if (protocol == TransportProtocol.kHttp)
            port = (ushort)(encoding == FunEncoding.kJson ? 8018 : 8028);

        return port;
    }

    void sendMessage (TransportProtocol protocol)
    {
        if (encoding == FunEncoding.kJson)
        {
            Dictionary<string, object> message = new Dictionary<string, object>();
            message["message"] = string.Format("[{0}] hello world", protocol.ToString().Substring(1).ToLower());
            network_.SendMessage("echo", message, EncryptionType.kDefaultEncryption, protocol);
        }
        else if (encoding == FunEncoding.kProtobuf)
        {
            PbufEchoMessage echo = new PbufEchoMessage();
            echo.msg = string.Format("[{0}] hello proto", protocol.ToString().Substring(1).ToLower());
            FunMessage message = FunapiMessage.CreateFunMessage(echo, MessageType.pbuf_echo);
            network_.SendMessage(MessageType.pbuf_echo, message, EncryptionType.kDefaultEncryption, protocol);
        }
    }

    void setButtonState (bool enable)
    {
        buttons_["test"].interactable = enable;
        buttons_["close"].interactable = enable;
    }

    void onSessionInitiated (string session_id)
    {
        setButtonState(true);
    }

    void onStoppedAllTransport()
    {
        buttons_["create"].interactable = true;
        setButtonState(false);
    }

    void onEcho (string msg_type, object body)
    {
        FunDebug.Assert(body is Dictionary<string, object>);
        string strJson = Json.Serialize(body);
        FunDebug.Log("Received an echo message: {0}", strJson);
    }

    void onEchoProtobuf (string msg_type, object body)
    {
        FunDebug.Assert(body is FunMessage);
        FunMessage msg = body as FunMessage;
        PbufEchoMessage echo = FunapiMessage.GetMessage<PbufEchoMessage>(msg, MessageType.pbuf_echo);
        if (echo == null)
            return;

        FunDebug.Log("Received an echo message: {0}", echo.msg);
    }


    FunapiNetwork network_ = null;
    Dictionary<string, Button> buttons_ = new Dictionary<string, Button>();
}
