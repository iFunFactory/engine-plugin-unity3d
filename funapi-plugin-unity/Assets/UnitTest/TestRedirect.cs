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
using System.Text;
using UnityEngine;
using UnityEngine.TestTools;

// protobuf
using funapi.network.fun_message;
using plugin_messages;


public class TestRedirect
{
    [UnityTest]
    public IEnumerator Redirect ()
    {
        yield return new TestImpl ();
    }

    [UnityTest]
    public IEnumerator Queueing ()
    {
        yield return new TestQueueImpl ();
    }


    // This test is about the basic redirect feature.
    class TestImpl : TestSessionBase
    {
        public TestImpl ()
        {
            SessionOption option = new SessionOption();
            option.sessionReliability = true;
            option.sendSessionIdOnlyOnce = true;

            session = FunapiSession.Create(TestInfo.ServerIp, option);
            session.ReceivedMessageCallback += onReceivedEchoMessage;
            //session.TransportOptionCallback += onTransportOption;

            session.SessionEventCallback += delegate (SessionEventType type, string sessionid)
            {
                if (isFinished)
                    return;

                startCoroutine(onSessionEvent(type));
            };

            session.TransportEventCallback += delegate (TransportProtocol protocol, TransportEventType type)
            {
                if (isFinished)
                    return;

                if (type == TransportEventType.kStarted)
                    sendEchoMessageWithCount(protocol, 3);
            };

            setTestTimeout(15f);

            ushort port = getPort("redirect", TransportProtocol.kTcp, FunEncoding.kProtobuf);
            session.Connect(TransportProtocol.kTcp, FunEncoding.kProtobuf, port);
        }

        IEnumerator onSessionEvent (SessionEventType type)
        {
            yield return new SleepForSeconds(1f);

            if (type == SessionEventType.kOpened)
            {
                if (test_step == 0)
                {
                    requestRedirect(TransportProtocol.kTcp);
                }
            }
            else if (type == SessionEventType.kRedirectSucceeded)
            {
                if (test_step >= kStepCountMax)
                {
                    onTestFinished();
                    yield break;
                }

                requestRedirect(TransportProtocol.kTcp);
            }
            else if (type == SessionEventType.kRedirectFailed)
            {
                isFinished = true;
                Assert.Fail("'Redirect' test has failed.");
            }
        }

        void requestRedirect (TransportProtocol protocol)
        {
            FunEncoding encoding = session.GetEncoding(protocol);
            if (encoding == FunEncoding.kNone)
                return;

            if (encoding == FunEncoding.kJson)
            {
                Dictionary<string, object> message = new Dictionary<string, object>();
                message["message"] = "request_redirect";
                session.SendMessage("echo", message, protocol);
            }
            else if (encoding == FunEncoding.kProtobuf)
            {
                PbufEchoMessage echo = new PbufEchoMessage();
                echo.msg = "request_redirect";
                FunMessage message = FunapiMessage.CreateFunMessage(echo, MessageType.pbuf_echo);
                session.SendMessage("pbuf_echo", message, protocol);
            }

            ++test_step;
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
                    option = new HttpTransportOption();
                }
            }
            else if (flavor == "beta")
            {
                if (protocol == TransportProtocol.kTcp)
                {
                    TcpTransportOption tcp_option = new TcpTransportOption();
                    tcp_option.Encryption = EncryptionType.kChaCha20Encryption;
                    option = tcp_option;
                }
                else if (protocol == TransportProtocol.kHttp)
                {
                    option = new HttpTransportOption();
                }
            }

            if (option == null)
                option = new TransportOption();

            return option;
        }


        const int kStepCountMax = 3;
        int test_step = 0;
    }


    // This test is for queueing messages while on redirect.
    class TestQueueImpl : TestSessionBase
    {
        public TestQueueImpl ()
        {
            SessionOption option = new SessionOption();
            option.sessionReliability = true;
            option.sendSessionIdOnlyOnce = true;
            option.useRedirectQueue = true;

            session = FunapiSession.Create(TestInfo.ServerIp, option);
            session.ReceivedMessageCallback += onReceivedEchoMessage;

            session.SessionEventCallback += delegate (SessionEventType type, string sessionid)
            {
                if (isFinished)
                    return;

                if (type == SessionEventType.kOpened)
                {
                    requestRedirect(TransportProtocol.kTcp);
                }
                else if (type == SessionEventType.kRedirectStarted)
                {
                    startCoroutine(onSendingJsonEchos(TransportProtocol.kTcp, 3));
                }
                else if (type == SessionEventType.kRedirectSucceeded)
                {
                    onTestFinished();
                }
                else if (type == SessionEventType.kRedirectFailed)
                {
                    isFinished = true;
                    Assert.Fail("'Redirect' test has failed.");
                }
            };

            session.TransportEventCallback += delegate (TransportProtocol protocol, TransportEventType type)
            {
                if (isFinished)
                    return;

                if (type == TransportEventType.kStarted)
                {
                    if (protocol == TransportProtocol.kTcp)
                    {
                        startCoroutine(onKeepSendingEchos(protocol, 0.01f));
                    }
                }
            };

            session.RedirectQueueCallback += delegate (TransportProtocol protocol,
                                                       List<string> current_tags, List<string> target_tags,
                                                       Queue<UnsentMessage> queue)
            {
                FunDebug.Log("RedirectQueueCallback called - queue:{0}", queue.Count);

                int skip = Math.Min(3, queue.Count);

                foreach (UnsentMessage msg in queue)
                {
                    if (skip > 0)
                    {
                        msg.discard = true;

                        FunEncoding encoding = session.GetEncoding(protocol);
                        if (encoding == FunEncoding.kJson)
                        {
                            Dictionary<string, object> json = msg.message as Dictionary<string, object>;
                            FunDebug.Log("'{0}' message is aborted.", json["message"]);
                        }
                        else if (encoding == FunEncoding.kProtobuf)
                        {
                            FunMessage pbuf = msg.message as FunMessage;
                            PbufEchoMessage echo = FunapiMessage.GetMessage<PbufEchoMessage>(pbuf, MessageType.pbuf_echo);
                            FunDebug.Log("'{0}' message is aborted.", echo.msg);
                        }

                        --skip;
                    }
                }
            };

            setTestTimeout(5f);

            ushort port = getPort("redirect", TransportProtocol.kTcp, FunEncoding.kProtobuf);
            session.Connect(TransportProtocol.kTcp, FunEncoding.kProtobuf, port);
        }

        void requestRedirect (TransportProtocol protocol)
        {
            FunEncoding encoding = session.GetEncoding(protocol);
            if (encoding == FunEncoding.kNone)
                return;

            if (encoding == FunEncoding.kJson)
            {
                Dictionary<string, object> message = new Dictionary<string, object>();
                message["message"] = "request_redirect";
                session.SendMessage("echo", message, protocol);
            }
            else if (encoding == FunEncoding.kProtobuf)
            {
                PbufEchoMessage echo = new PbufEchoMessage();
                echo.msg = "request_redirect";
                FunMessage message = FunapiMessage.CreateFunMessage(echo, MessageType.pbuf_echo);
                session.SendMessage("pbuf_echo", message, protocol);
            }
        }

        void sendEchoMessage (TransportProtocol protocol)
        {
            if (isFinished)
                return;

            FunapiSession.Transport transport = session.GetTransport(protocol);
            if (transport == null)
            {
                FunDebug.LogWarning("sendEchoMessage - transport is null.");
                return;
            }

            lock (lock_)
            {
                ++index;
                FunDebug.Log("send message - hello_{0}", index);

                if (transport.encoding == FunEncoding.kJson)
                {
                    Dictionary<string, object> message = new Dictionary<string, object>();
                    message["message"] = "hello_" + index;
                    session.SendMessage("echo", message, protocol);
                }
                else if (transport.encoding == FunEncoding.kProtobuf)
                {
                    PbufEchoMessage echo = new PbufEchoMessage();
                    echo.msg = "hello_" + index;
                    FunMessage message = FunapiMessage.CreateFunMessage(echo, MessageType.pbuf_echo);
                    session.SendMessage("pbuf_echo", message, protocol);
                }
            }
        }

        void sendEchoMessage (TransportProtocol protocol, FunEncoding encoding)
        {
            lock (lock_)
            {
                ++index;
                FunDebug.Log("send message - hello_{0} ({1})", index, encoding);

                if (encoding == FunEncoding.kJson)
                {
                    Dictionary<string, object> message = new Dictionary<string, object>();
                    message["message"] = "hello_" + index;
                    session.SendMessage("echo", message, protocol);
                }
                else if (encoding == FunEncoding.kProtobuf)
                {
                    PbufEchoMessage echo = new PbufEchoMessage();
                    echo.msg = "hello_" + index;
                    FunMessage message = FunapiMessage.CreateFunMessage(echo, MessageType.pbuf_echo);
                    session.SendMessage("pbuf_echo", message, protocol);
                }
            }
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

        IEnumerator onSendingJsonEchos (TransportProtocol protocol, int count)
        {
            yield return new SleepForSeconds(0.1f);

            for (int i = 0; i < count; ++i)
            {
                sendEchoMessage(TransportProtocol.kTcp, FunEncoding.kJson);
                yield return new SleepForSeconds(0.01f);
            }
        }


        int index = 0;
        object lock_ = new object();
    }
}
