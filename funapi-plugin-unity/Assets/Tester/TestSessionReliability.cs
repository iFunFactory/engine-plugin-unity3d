// Copyright 2018 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using System.Collections;
using UnityEngine;
using UnityEngine.TestTools;


public class TestSessionReliability
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

            session = FunapiSession.Create(TestInfo.ServerIp, option);
            session.ReceivedMessageCallback += onReceivedEchoMessage;

            session.SessionEventCallback += delegate (SessionEventType type, string sessionid)
            {
                if (type == SessionEventType.kOpened)
                {
                    keepSendingEchoMessages(protocol, 0.2f);
                }
            };

            session.TransportEventCallback += delegate (TransportProtocol p, TransportEventType type)
            {
                if (isFinished)
                    return;

                if (type == TransportEventType.kStarted)
                {
                    startCoroutine(onTransportStarted());
                }
                else if (type == TransportEventType.kStopped)
                {
                    if (test_step < kStepCountMax)
                    {
                        sendEchoMessageWithCount(protocol, 2);
                        session.Connect(protocol);
                    }
                }
            };

            setTestTimeout(7f);

            ushort port = getPort("whole", protocol, encoding);
            session.Connect(protocol, encoding, port);
        }

        IEnumerator onTransportStarted ()
        {
            yield return new SleepForSeconds(0.5f);

            ++test_step;
            if (test_step >= kStepCountMax)
            {
                onTestFinished();
                yield break;
            }

            session.Stop();
        }


        const int kStepCountMax = 3;
        int test_step = 0;
    }
}
