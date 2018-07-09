// Copyright 2018 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using System.Collections;
using UnityEngine.TestTools;


public class TestConnectStop
{
    [UnityTest]
    public IEnumerator Session_Json ()
    {
        yield return new TestImpl (FunEncoding.kJson);
    }

    [UnityTest]
    public IEnumerator Session_Protobuf ()
    {
        yield return new TestImpl (FunEncoding.kProtobuf);
    }


    class TestImpl : TestSessionBase
    {
        public TestImpl (FunEncoding encoding)
        {
            session = FunapiSession.Create(TestInfo.ServerIp);
            session.ReceivedMessageCallback += onReceivedEchoMessage;

            session.SessionEventCallback += delegate (SessionEventType type, string sessionid)
            {
                if (isFinished)
                    return;

                if (type == SessionEventType.kStopped)
                {
                    onTestFinished();
                }
            };

            session.TransportEventCallback += delegate (TransportProtocol protocol, TransportEventType type)
            {
                if (isFinished)
                    return;

                if (type == TransportEventType.kStarted)
                {
                    sendEchoMessage(protocol);
                }
            };

            setTestTimeout(10f);

            connect(TransportProtocol.kTcp, encoding);
            connect(TransportProtocol.kHttp, encoding);
            session.Stop(TransportProtocol.kTcp);
            connect(TransportProtocol.kUdp, encoding);
            session.Connect(TransportProtocol.kTcp);
            session.Stop(TransportProtocol.kUdp);
            session.Stop();
        }

        void connect (TransportProtocol protocol, FunEncoding encoding)
        {
            ushort port = getPort("default", protocol, encoding);
            session.Connect(protocol, encoding, port);
        }
    }
}
