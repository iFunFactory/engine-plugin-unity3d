// Copyright 2018 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using NUnit.Framework;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// protobuf
using funapi.network.fun_message;
using plugin_messages;


public static class TestInfo
{
    public static string ServerIp { get { return "127.0.0.1"; } }
}


class TestBase : YieldIndication
{
    public TestBase ()
    {
        updater = new GameObject("TestUpdater").AddComponent<Updater>();
    }

    public override bool keepWaiting
    {
        get
        {
            if (isFinished)
                GameObject.Destroy(updater.gameObject);

            return !isFinished;
        }
    }

    protected void startCoroutine (IEnumerator func)
    {
        updater.StartCoroutine(func);
    }

    protected void setTestTimeout (float seconds)
    {
        updater.StartCoroutine(onTestTimedOut(seconds));
    }

    protected void setTestTimeoutWithoutFail (float seconds)
    {
        updater.StartCoroutine(onTestTimedOut(seconds, false));
    }

    IEnumerator onTestTimedOut (float seconds, bool with_fail = true)
    {
        yield return new SleepForSeconds(seconds);

        if (with_fail)
            Assert.Fail("'{0}' Test has timed out.", GetType().ToString());

        isFinished = true;
    }

    public FunapiTimerList timer { get { return updater.timer; } }


    class Updater : MonoBehaviour
    {
        void Update ()
        {
            if (timer_ != null)
            {
                // gets delta time
                long now = DateTime.UtcNow.Ticks;
                float delta = (now - prev_) / 10000000f;
                prev_ = now;

                timer_.Update(delta);
            }
        }

        public FunapiTimerList timer
        {
            get
            {
                if (timer_ == null)
                {
                    timer_ = new FunapiTimerList();
                    prev_ = DateTime.UtcNow.Ticks;
                }

                return timer_;
            }
        }

        long prev_;
        public FunapiTimerList timer_ = null;
    }


    protected bool isFinished = false;

    Updater updater = null;
}


class TestSessionBase : TestBase
{
    protected void setEchoMessage (string new_message)
    {
        echo_message = new_message;
    }

    protected void sendEchoMessageWithCount (TransportProtocol protocol, int count)
    {
        for (int i = 0; i < count; ++i)
            sendEchoMessage(protocol);
    }

    protected void keepSendingEchoMessages (TransportProtocol protocol, float interval_seconds)
    {
        startCoroutine(onKeepSendingEchos(protocol, interval_seconds));
    }

    IEnumerator onKeepSendingEchos (TransportProtocol protocol, float interval_seconds)
    {
        while (true)
        {
            if (isFinished)
                yield break;

            sendEchoMessage(protocol);
            yield return new SleepForSeconds(interval_seconds);
        }
    }

    protected void sendEchoMessage (TransportProtocol protocol,
                                    EncryptionType enc_type = EncryptionType.kDefaultEncryption)
    {
        if (isFinished)
            return;

        FunapiSession.Transport transport = session.GetTransport(protocol);
        if (transport == null)
        {
            FunDebug.LogWarning("sendEchoMessage - transport is null.");
            return;
        }

        if (transport.encoding == FunEncoding.kJson)
        {
            Dictionary<string, object> message = new Dictionary<string, object>();
            message["message"] = string.Format("[{0}] {1}", transport.str_protocol, echo_message);
            session.SendMessage("echo", message, protocol, enc_type);
        }
        else if (transport.encoding == FunEncoding.kProtobuf)
        {
            PbufEchoMessage echo = new PbufEchoMessage();
            echo.msg = string.Format("[{0}] {1}", transport.str_protocol, echo_message);
            FunMessage message = FunapiMessage.CreateFunMessage(echo, MessageType.pbuf_echo);
            session.SendMessage("pbuf_echo", message, protocol, enc_type);
        }

        ++sending_count;
    }

    protected void onReceivedEchoMessage (string type, object message)
    {
        if (type == "echo")
        {
            FunDebug.Assert(message is Dictionary<string, object>);
            Dictionary<string, object> json = message as Dictionary<string, object>;
            FunDebug.Log("Received an echo message: {0}", json["message"]);
        }
        else if (type == "pbuf_echo")
        {
            FunMessage msg = message as FunMessage;
            PbufEchoMessage echo = FunapiMessage.GetMessage<PbufEchoMessage>(msg, MessageType.pbuf_echo);
            FunDebug.Log("Received an echo message: {0}", echo.msg);
        }
        else
            return;

        --sending_count;
    }

    protected void resetSendingCount ()
    {
        sending_count = 0;
    }

    protected virtual void onTestFinished ()
    {
        FunapiSession.Destroy(session);
        isFinished = true;
    }


    // Did it received all the messages?
    protected bool isReceivedAllMessages
    {
        get { return sending_count <= 0; }
    }


    protected static TransportOption newTransportOption (TransportProtocol protocol)
    {
        if (protocol == TransportProtocol.kTcp)
            return new TcpTransportOption();
        else if (protocol == TransportProtocol.kUdp)
            return new TransportOption();
        else if (protocol == TransportProtocol.kHttp)
            return new HttpTransportOption();

        return null;
    }

    protected static ushort getPort (string flavor, TransportProtocol protocol, FunEncoding encoding)
    {
        ushort port = 0;

        // default
        if (protocol == TransportProtocol.kTcp)
            port = (ushort)(encoding == FunEncoding.kJson ? 8011 : 8017);
        else if (protocol == TransportProtocol.kUdp)
            port = (ushort)(encoding == FunEncoding.kJson ? 8012 : 8018);
        else if (protocol == TransportProtocol.kHttp)
            port = (ushort)(encoding == FunEncoding.kJson ? 8013 : 8019);
        else if (protocol == TransportProtocol.kWebsocket)
            port = (ushort)(encoding == FunEncoding.kJson ? 8011 : 8017);

        if (flavor == "default")
            return port; // 8011~
        else if (flavor == "whole")
            port += 10;  // 8021~
        else if (flavor == "encryption")
            port += 20;  // 8031~
        else if (flavor == "sequence")
            port += 30;  // 8041~
        else if (flavor == "multicast")
            port += 40;  // 8051~
        else if (flavor == "redirect")
            port += 50;  // 8061~
        else if (flavor == "compression")
            port += 60;  // 8071~
        else if (flavor == "websocket")
            port += 70;  // 8081~

        return port;
    }


    protected FunapiSession session;

    string echo_message = "hello";
    int sending_count = 0;
}
