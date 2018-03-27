// Copyright 2018 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using NUnit.Framework;
using System.Collections;
using UnityEngine.TestTools;


public class TestPing
{
    [UnityTest]
    public IEnumerator TCP_Json ()
    {
        TcpTransportOption option = new TcpTransportOption();
        option.SetPing(1, 3, true);

        yield return new TestImpl (TransportProtocol.kTcp, FunEncoding.kJson, option);
    }

    [UnityTest]
    public IEnumerator TCP_Protobuf ()
    {
        TcpTransportOption option = new TcpTransportOption();
        option.SetPing(1, 3, true);

        yield return new TestImpl (TransportProtocol.kTcp, FunEncoding.kProtobuf, option);
    }


    class TestImpl : TestSessionBase
    {
        public TestImpl (TransportProtocol protocol, FunEncoding encoding, TransportOption option)
        {
            session = FunapiSession.Create(TestInfo.ServerIp);

            session.TransportEventCallback += delegate (TransportProtocol p, TransportEventType type)
            {
                if (type == TransportEventType.kStopped)
                {
                    FunapiSession.Transport transport = session.GetTransport(protocol);
                    if (transport.LastErrorCode != TransportError.Type.kNone)
                    {
                        FunDebug.LogError("'Ping' Test has failed.");
                        isFinished = true;
                    }
                }
            };

            ushort port = getPort("default", protocol, encoding);
            session.Connect(protocol, encoding, port, option);

            timer.Add(new FunapiTimer("finish", 8f, onTestFinished));
        }
    }
}
