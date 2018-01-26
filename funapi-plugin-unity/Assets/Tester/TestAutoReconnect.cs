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
    public IEnumerator TCP_Default ()
    {
        TcpTransportOption option = new TcpTransportOption();
        option.AutoReconnect = true;

        yield return new TestImpl (TransportProtocol.kTcp, FunEncoding.kJson, option);
    }


    class TestImpl : TestSessionBase
    {
        public TestImpl (TransportProtocol protocol, FunEncoding encoding, TransportOption option)
        {
            session = FunapiSession.Create(TestInfo.ServerIp);
            session.ReceivedMessageCallback += onReceivedEchoMessage;

            session.SessionEventCallback += delegate (SessionEventType type, string sessionid)
            {
                if (type == SessionEventType.kStopped)
                    isFinished = true;
            };

            session.TransportEventCallback += delegate (TransportProtocol p, TransportEventType type)
            {
                if (isFinished)
                    return;

                if (type == TransportEventType.kStarted || type == TransportEventType.kReconnecting)
                    sendEchoMessage(protocol);
            };

            setTimeoutCallback(30f);

            ushort port = getPort("default", protocol, encoding);
            option.ConnectionTimeout = 20f;
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

                yield return new WaitForSeconds(0.1f);
            }
        }
    }
}
