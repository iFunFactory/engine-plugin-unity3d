// Copyright 2018 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using System.Collections;
using UnityEngine.TestTools;


public class TestSendFail
{
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
            session.TransportErrorCallback += onTransportError;

            session.SessionEventCallback += delegate (SessionEventType type, string sessionid)
            {
                if (type == SessionEventType.kStopped)
                {
                    ++test_step;
                    if (test_step < kStepCountMax)
                    {
                        session.Connect(protocol);
                    }
                    else
                    {
                        FunapiSession.Destroy(session);
                        isFinished = true;
                    }
                }
            };

            session.TransportEventCallback += delegate (TransportProtocol p, TransportEventType type)
            {
                if (isFinished)
                    return;

                if (type == TransportEventType.kStarted)
                {
                    sendEchoMessageWithCount(protocol, 3);
                }
            };

            session.ReceivedMessageCallback += delegate (string type, object message)
            {
                onReceivedEchoMessage(type, message);

                if (isReceivedAllMessages)
                {
                    if (session.Started)
                        session.Stop();
                }
            };

            setTimeoutCallbackWithFail(2f);

            ushort port = getPort("default", protocol, encoding);
            session.Connect(protocol, encoding, port);
        }


        const int kStepCountMax = 3;
        int test_step = 0;
    }
}
