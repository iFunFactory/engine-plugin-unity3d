﻿// Copyright 2018 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using NUnit.Framework;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.TestTools;

// protobuf
using funapi.network.fun_message;
using plugin_messages;


public class TestRedirect
{
    [OneTimeSetUp]
    protected void Init ()
    {
        FunapiEncryptor.public_key = "0b8504a9c1108584f4f0a631ead8dd548c0101287b91736566e13ead3f008f5d";
    }

    [UnityTest]
    public IEnumerator Test ()
    {
        yield return new TestImpl ();
    }


    class TestImpl : TestSessionBase
    {
        public TestImpl ()
        {
            SessionOption option = new SessionOption();
            option.sessionReliability = true;

            session = FunapiSession.Create(TestInfo.ServerIp, option);
            session.ReceivedMessageCallback += onReceivedEchoMessage;
            session.TransportOptionCallback += onTransportOption;

            session.SessionEventCallback += delegate (SessionEventType type, string sessionid)
            {
                startCoroutine(onSessionEvent(type));
            };

            session.TransportEventCallback += delegate (TransportProtocol protocol, TransportEventType type)
            {
                if (isFinished)
                    return;

                if (type == TransportEventType.kStarted)
                    sendEchoMessage(protocol);
            };

            setTimeoutCallbackWithFail(10f);

            ushort port = getPort("redirect", TransportProtocol.kTcp, FunEncoding.kJson);
            session.Connect(TransportProtocol.kTcp, FunEncoding.kJson, port);
        }

        IEnumerator onSessionEvent (SessionEventType type)
        {
            yield return new WaitForSeconds(1f);

            if (type == SessionEventType.kOpened)
            {
                if (test_step == 0)
                {
                    ++test_step;
                    requestRedirect(TransportProtocol.kTcp);
                }
            }
            else if (type == SessionEventType.kRedirectSucceeded)
            {
                ++test_step;
                if (test_step > kStepCountMax)
                    isFinished = true;
                else
                    requestRedirect(TransportProtocol.kTcp);
            }
        }

        public void requestRedirect (TransportProtocol protocol)
        {
            FunapiSession.Transport transport = session.GetTransport(protocol);
            if (transport == null)
                return;

            if (transport.encoding == FunEncoding.kJson)
            {
                Dictionary<string, object> message = new Dictionary<string, object>();
                message["message"] = "request_redirect";
                session.SendMessage("echo", message, protocol);
            }
            else if (transport.encoding == FunEncoding.kProtobuf)
            {
                PbufEchoMessage echo = new PbufEchoMessage();
                echo.msg = "request_redirect";
                FunMessage message = FunapiMessage.CreateFunMessage(echo, MessageType.pbuf_echo);
                session.SendMessage("pbuf_echo", message, protocol);
            }
        }

        TransportOption onTransportOption (string flavor, TransportProtocol protocol)
        {
            TransportOption option = null;

            if (flavor == "alpha")
            {
                if (protocol == TransportProtocol.kTcp)
                {
                    TcpTransportOption tcp_option = new TcpTransportOption();
                    tcp_option.Encryption = EncryptionType.kAes128Encryption;
                    option = tcp_option;
                }
                else if (protocol == TransportProtocol.kHttp)
                {
                    HttpTransportOption http_option = new HttpTransportOption();
                    http_option.Encryption = EncryptionType.kIFunEngine2Encryption;
                    option = http_option;
                }
            }
            else if (flavor == "beta")
            {
                if (protocol == TransportProtocol.kTcp)
                {
                    TcpTransportOption tcp_option = new TcpTransportOption();
                    tcp_option.Encryption = EncryptionType.kIFunEngine1Encryption;
                    option = tcp_option;
                }
                else if (protocol == TransportProtocol.kHttp)
                {
                    HttpTransportOption http_option = new HttpTransportOption();
                    http_option.Encryption = EncryptionType.kIFunEngine2Encryption;
                    option = http_option;
                }
            }

            if (option == null)
                option = new TransportOption();

            return option;
        }


        const int kStepCountMax = 3;
        int test_step = 0;
    }
}
