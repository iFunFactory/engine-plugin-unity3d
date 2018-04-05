// Copyright 2018 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using System.Collections;
using UnityEngine;
using UnityEngine.TestTools;


public class TestDelayedAck
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


    class TestImpl : TestSessionBase
    {
        public TestImpl (TransportProtocol protocol, FunEncoding encoding)
        {
            SessionOption option = new SessionOption();
            option.sessionReliability = true;
            option.delayedAckInterval = 0.3f;

            session = FunapiSession.Create(TestInfo.ServerIp, option);
            session.ReceivedMessageCallback += onReceivedEchoMessage;

            session.TransportEventCallback += delegate (TransportProtocol p, TransportEventType type)
            {
                if (isFinished)
                    return;

                if (type == TransportEventType.kStarted)
                {
                    startCoroutine(onStarted(protocol));
                }
            };

            setTestTimeout(4f);

            ushort port = getPort("whole", protocol, encoding);
            session.Connect(protocol, encoding, port);
        }

        IEnumerator onStarted (TransportProtocol protocol)
        {
            keepSendingEchoMessages(protocol, 0.1f);
            yield return new SleepForSeconds(2f);

            onTestFinished();
        }
    }
}
