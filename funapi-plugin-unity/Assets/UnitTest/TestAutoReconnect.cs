// Copyright 2018 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using System.Collections;
using UnityEngine;
using UnityEngine.TestTools;


public class TestAutoReconect
{
    [UnityTest]
    public IEnumerator TCP_WrongPort ()
    {
        yield return new TestImpl (TransportProtocol.kTcp);
    }

    [UnityTest]
    public IEnumerator TCP_Disconnect ()
    {
        yield return new TestImpl (TransportProtocol.kTcp, FunEncoding.kJson);
    }


    class TestImpl : TestSessionBase
    {
        public TestImpl (TransportProtocol protocol)
        {
            session = FunapiSession.Create(TestInfo.ServerIp);

            session.TransportEventCallback += delegate (TransportProtocol p, TransportEventType type)
            {
                if (type == TransportEventType.kStopped)
                {
                    FunapiSession.Transport transport = session.GetTransport(protocol);
                    if (transport.LastErrorCode == TransportError.Type.kConnectionTimeout)
                    {
                        onTestFinished();
                    }
                }
            };

            setTestTimeout(8f);

            TcpTransportOption option = new TcpTransportOption();
            option.AutoReconnect = true;
            option.ConnectionTimeout = 5f;

            session.Connect(protocol, FunEncoding.kJson, 80, option);
        }


        public TestImpl (TransportProtocol protocol, FunEncoding encoding)
        {
            session = FunapiSession.Create(TestInfo.ServerIp);

            session.TransportEventCallback += delegate (TransportProtocol p, TransportEventType type)
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
                {
                    ++test_step;
                    if (test_step >= kStepCountMax)
                    {
                        onTestFinished();
                    }
                }
            };

            setTestTimeout(10f);

            TcpTransportOption option = new TcpTransportOption();
            option.AutoReconnect = true;
            option.ConnectionTimeout = 8f;

            ushort port = getPort("default", protocol, encoding);
            session.Connect(protocol, encoding, port, option);

            startCoroutine(onDisconnectLoop(protocol));
        }

        IEnumerator onDisconnectLoop (TransportProtocol protocol)
        {
            while (!isFinished)
            {
                FunapiSession.Transport transport = session.GetTransport(protocol);
                if (transport == null)
                    yield break;

                if (transport.IsEstablished && !transport.Reconnecting)
                    transport.ForcedDisconnect();

                yield return new SleepForSeconds(1f);
            }
        }


        const int kStepCountMax = 5;
        int test_step = 0;
    }
}
