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
    public IEnumerator SendEchoWithJson ()
    {
        yield return new TestImpl (FunEncoding.kJson);
    }

    [UnityTest]
    public IEnumerator SendEchoWithProtobuf ()
    {
        yield return new TestImpl (FunEncoding.kProtobuf);
    }


    class TestImpl : TestSessionBase
    {
        public TestImpl (FunEncoding encoding)
        {
            session = FunapiSession.Create(TestInfo.ServerIp);

            session.SessionEventCallback += delegate (SessionEventType type, string sessionid)
            {
                if (type == SessionEventType.kStopped)
                {
                    FunapiSession.Destroy(session);
                    isFinished = true;
                }
            };

            session.TransportEventCallback += delegate (TransportProtocol protocol, TransportEventType type)
            {
                if (isFinished)
                    return;

                if (type == TransportEventType.kStarted)
                    sendEchoMessageWithCount(protocol, 3);
            };

            session.ReceivedMessageCallback += delegate (string type, object message)
            {
                onReceivedEchoMessage(type, message);

                if (isReceivedAllMessages)
                    session.Stop();
            };

            setTimeoutCallbackWithFail(3f);

            ushort port = getPort("default", TransportProtocol.kTcp, encoding);
            session.Connect(TransportProtocol.kTcp, encoding, port);

            port = getPort("default", TransportProtocol.kUdp, encoding);
            session.Connect(TransportProtocol.kUdp, encoding, port);

            port = getPort("default", TransportProtocol.kHttp, encoding);
            session.Connect(TransportProtocol.kHttp, encoding, port);
        }
    }
}
