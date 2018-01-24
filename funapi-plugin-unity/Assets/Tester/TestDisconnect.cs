// Copyright 2018 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using System.Collections;
using UnityEngine;
using UnityEngine.TestTools;


public class TestDisconnect
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


    class TestImpl : TestSessionBase
    {
        public TestImpl (TransportProtocol protocol, FunEncoding encoding)
        {
            SessionOption option = new SessionOption();
            option.sendSessionIdOnlyOnce = true;

            if (protocol == TransportProtocol.kTcp)
                option.sessionReliability = true;

            session = FunapiSession.Create(TestInfo.ServerIp, option);
            session.ReceivedMessageCallback += onReceivedEchoMessage;

            session.TransportEventCallback += delegate (TransportProtocol p, TransportEventType type)
            {
                if (isFinished)
                    return;

                if (type == TransportEventType.kStarted)
                {
                    if (test_step == 0)
                        keepSendingEchoMessages(protocol, 0.3f);

                    startCoroutine(forcedDisconnect(protocol));
                }
                else if (type == TransportEventType.kStopped)
                {
                    ++test_step;
                    if (test_step >= kStepCountMax)
                    {
                        isFinished = true;
                    }
                    else
                    {
                        if (protocol == TransportProtocol.kUdp)
                        {
                            FunapiSession.Transport transport = session.GetTransport(protocol);
                            transport.SendSessionId = false;
                        }

                        session.Connect(protocol);
                    }
                }
            };

            setTimeoutCallbackWithFail(5f);

            ushort port = getPort("whole", protocol, encoding);
            session.Connect(protocol, encoding, port);
        }

        IEnumerator forcedDisconnect (TransportProtocol protocol)
        {
            yield return new WaitForSeconds(1f);

            FunapiSession.Transport transport = session.GetTransport(protocol);
            transport.ForcedDisconnect();
        }


        const int kStepCountMax = 3;
        int test_step = 0;
    }
}
