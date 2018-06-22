// Copyright 2018 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using System.Collections;
using UnityEngine.TestTools;


public class TestResponseTimeout
{
    [UnityTest]
    public IEnumerator TCP_Json ()
    {
        yield return new TestImpl (TransportProtocol.kTcp, FunEncoding.kJson);
    }

    [UnityTest]
    public IEnumerator UDP_Json ()
    {
        yield return new TestImpl (TransportProtocol.kUdp, FunEncoding.kJson);
    }

    [UnityTest]
    public IEnumerator HTTP_Json ()
    {
        yield return new TestImpl (TransportProtocol.kHttp, FunEncoding.kJson);
    }

    [UnityTest]
    public IEnumerator TCP_Protobuf ()
    {
        yield return new TestProtoImpl (TransportProtocol.kTcp, FunEncoding.kProtobuf);
    }

    [UnityTest]
    public IEnumerator UDP_Protobuf ()
    {
        yield return new TestProtoImpl (TransportProtocol.kUdp, FunEncoding.kProtobuf);
    }

    [UnityTest]
    public IEnumerator HTTP_Protobuf ()
    {
        yield return new TestProtoImpl (TransportProtocol.kHttp, FunEncoding.kProtobuf);
    }


    class TestImpl : TestSessionBase
    {
        public TestImpl (TransportProtocol protocol, FunEncoding encoding)
        {
            session = FunapiSession.Create(TestInfo.ServerIp);

            session.TransportEventCallback += delegate (TransportProtocol p, TransportEventType type)
            {
                if (isFinished)
                    return;

                if (type == TransportEventType.kStarted)
                {
                    session.SetResponseTimeout("echo", 1f);
                }
            };

            session.ReceivedMessageCallback += delegate (string type, object message)
            {
                onReceivedEchoMessage(type, message);

                if (isReceivedAllMessages)
                    onTestFinished();
            };

            session.ResponseTimeoutCallback += delegate (string type)
            {
                FunDebug.LogWarning("ResponseTimeoutCallback called. type:{0}", type);
                session.SetResponseTimeout("echo", 1f);
                sendEchoMessage(protocol);
            };

            setTestTimeout(3f);

            ushort port = getPort("default", protocol, encoding);
            session.Connect(protocol, encoding, port);
        }
    }

    class TestProtoImpl : TestSessionBase
    {
        public TestProtoImpl (TransportProtocol protocol, FunEncoding encoding)
        {
            session = FunapiSession.Create(TestInfo.ServerIp);

            session.TransportEventCallback += delegate (TransportProtocol p, TransportEventType type)
            {
                if (isFinished)
                    return;

                if (type == TransportEventType.kStarted)
                {
                    session.SetResponseTimeout(MessageType.pbuf_echo, 1f);
                }
            };

            session.ReceivedMessageCallback += delegate (string type, object message)
            {
                onReceivedEchoMessage(type, message);

                if (isReceivedAllMessages)
                    onTestFinished();
            };

            session.ResponseTimeoutIntCallback += delegate (int type)
            {
                FunDebug.LogWarning("ResponseTimeoutCallback called. type:{0}", type);
                session.SetResponseTimeout(MessageType.pbuf_echo, 1f);
                sendPbufEchoMessage(protocol);
            };

            setTestTimeout(3f);

            ushort port = getPort("default", protocol, encoding);
            session.Connect(protocol, encoding, port);
        }
    }
}
