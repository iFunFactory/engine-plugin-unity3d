// Copyright 2018 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using System.Collections;
using UnityEngine.TestTools;


public class TestSequenceValidation
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

            int count = 0;
            session.TransportEventCallback += delegate (TransportProtocol p, TransportEventType type)
            {
                if (isFinished)
                    return;

                if (type == TransportEventType.kStarted)
                {
                    sendEchoMessageWithCount(protocol, 3);
                }
                else if (type == TransportEventType.kStopped)
                {
                    ++count;
                    if (count >= 3)
                        isFinished = true;
                    else
                        session.Connect(protocol);
                }
            };

            session.ReceivedMessageCallback += delegate (string type, object message)
            {
                onReceivedEchoMessage(type, message);

                if (IsReceivedAllMessages)
                    session.Stop();
            };

            setTimeoutCallbackWithFail(3f);

            ushort port = getPort("sequence", protocol, encoding);
            TransportOption option = newTransportOption(protocol);
            option.SequenceValidation = true;

            session.Connect(protocol, encoding, port, option);
        }
    }
}
