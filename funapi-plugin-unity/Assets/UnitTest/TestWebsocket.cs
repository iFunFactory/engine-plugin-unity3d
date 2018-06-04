// Copyright 2018 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using NUnit.Framework;
using System.Collections;
using UnityEngine.TestTools;


public class TestWebsocket
{
    [UnityTest]
    public IEnumerator TCP_Json ()
    {
        yield return new TestImpl (FunEncoding.kJson);
    }

    [UnityTest]
    public IEnumerator TCP_Protobuf ()
    {
        yield return new TestImpl (FunEncoding.kProtobuf);
    }


    class TestImpl : TestSessionBase
    {
        public TestImpl (FunEncoding encoding)
        {
            session = FunapiSession.Create(TestInfo.ServerIp);

            session.TransportEventCallback += delegate (TransportProtocol protocol, TransportEventType type)
            {
                if (isFinished)
                    return;

                if (type == TransportEventType.kStarted)
                    sendEchoMessageWithCount(protocol, 10);
            };

            session.ReceivedMessageCallback += delegate (string type, object message)
            {
                onReceivedEchoMessage(type, message);

                if (isReceivedAllMessages)
                    onTestFinished();
            };

            setTestTimeout(5f);

            ushort port = getPort("websocket", TransportProtocol.kWebsocket, encoding);
            session.Connect(TransportProtocol.kWebsocket, encoding, port);
        }
    }
}
