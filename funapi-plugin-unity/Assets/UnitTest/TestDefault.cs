// Copyright 2018 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using System.Collections;
using UnityEngine.TestTools;


public class TestDefault
{
    [UnityTest]
    public IEnumerator ALL_Json ()
    {
        yield return new TestImpl (FunEncoding.kJson);
    }

    [UnityTest]
    public IEnumerator ALL_Protobuf ()
    {
        yield return new TestImpl (FunEncoding.kProtobuf);
    }

    [UnityTest]
    public IEnumerator TCP_Json ()
    {
        yield return new TestImpl (TransportProtocol.kTcp, FunEncoding.kJson);
    }

    [UnityTest]
    public IEnumerator TCP_Protobuf ()
    {
        yield return new TestImpl (TransportProtocol.kTcp, FunEncoding.kProtobuf);
    }

    [UnityTest]
    public IEnumerator UDP_Json ()
    {
        yield return new TestImpl (TransportProtocol.kUdp, FunEncoding.kJson);
    }

    [UnityTest]
    public IEnumerator UDP_Protobuf ()
    {
        yield return new TestImpl (TransportProtocol.kUdp, FunEncoding.kProtobuf);
    }

    [UnityTest]
    public IEnumerator HTTP_Json ()
    {
        yield return new TestImpl (TransportProtocol.kHttp, FunEncoding.kJson);
    }

    [UnityTest]
    public IEnumerator HTTP_Protobuf ()
    {
        yield return new TestImpl (TransportProtocol.kHttp, FunEncoding.kProtobuf);
    }


    class TestImpl : TestSessionBase
    {
        public TestImpl (TransportProtocol protocol, FunEncoding encoding)
        {
            session = FunapiSession.Create(TestInfo.ServerIp);

            session.SessionEventCallback += delegate (SessionEventType type, string sessionid)
            {
                if (type == SessionEventType.kConnected)
                    sendEchoMessageWithCount(protocol, 10);
            };

            session.ReceivedMessageCallback += delegate (string type, object message)
            {
                onReceivedEchoMessage(type, message);

                if (isReceivedAllMessages)
                    onTestFinished();
            };

            setTestTimeout(2f);

            ushort port = getPort("default", protocol, encoding);
            session.Connect(protocol, encoding, port);
        }

        // Tests all protocols
        public TestImpl (FunEncoding encoding)
        {
            session = FunapiSession.Create(TestInfo.ServerIp);

            session.SessionEventCallback += delegate (SessionEventType type, string sessionid)
            {
                if (type == SessionEventType.kConnected)
                {
                    sendEchoMessageWithCount(TransportProtocol.kTcp, 10);
                    sendEchoMessageWithCount(TransportProtocol.kUdp, 10);
                    sendEchoMessageWithCount(TransportProtocol.kHttp, 10);
                }
            };

            session.ReceivedMessageCallback += delegate (string type, object message)
            {
                onReceivedEchoMessage(type, message);

                if (isReceivedAllMessages)
                    onTestFinished();
            };

            setTestTimeout(3f);

            ushort port = getPort("default", TransportProtocol.kTcp, encoding);
            session.Connect(TransportProtocol.kTcp, encoding, port);

            port = getPort("default", TransportProtocol.kUdp, encoding);
            session.Connect(TransportProtocol.kUdp, encoding, port);

            port = getPort("default", TransportProtocol.kHttp, encoding);
            session.Connect(TransportProtocol.kHttp, encoding, port);
        }
    }
}
