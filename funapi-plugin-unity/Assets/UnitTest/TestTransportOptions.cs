// Copyright 2018 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using System.Collections;
using UnityEngine.TestTools;


public class TestTransportOptions
{
    [UnityTest]
    public IEnumerator TCP_NagleOff ()
    {
        TcpTransportOption option = new TcpTransportOption();
        option.DisableNagle = true;

        yield return new TestImpl (TransportProtocol.kTcp, FunEncoding.kProtobuf, option, 10);
    }

    [UnityTest]
    public IEnumerator HTTP_UseWWW ()
    {
        HttpTransportOption option = new HttpTransportOption();
        option.UseWWW = true;

        yield return new TestImpl (TransportProtocol.kHttp, FunEncoding.kJson, option, 3);
    }


    class TestImpl : TestSessionBase
    {
        public TestImpl (TransportProtocol protocol, FunEncoding encoding, TransportOption option, int count)
        {
            session = FunapiSession.Create(TestInfo.ServerIp);

            session.TransportEventCallback += delegate (TransportProtocol p, TransportEventType type)
            {
                if (isFinished)
                    return;

                if (type == TransportEventType.kStarted)
                    sendEchoMessageWithCount(protocol, count);
            };

            session.ReceivedMessageCallback += delegate (string type, object message)
            {
                onReceivedEchoMessage(type, message);

                if (isReceivedAllMessages)
                {
                    onTestFinished();
                }
            };

            setTestTimeout(3f);

            ushort port = getPort("default", protocol, encoding);
            session.Connect(protocol, encoding, port, option);
        }
    }
}
