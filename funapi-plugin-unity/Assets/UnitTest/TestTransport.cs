// Copyright 2018 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using System.Collections;
using UnityEngine.TestTools;


public class TestTransport
{
    [UnityTest]
    public IEnumerator TCP_Connect_Stop ()
    {
        yield return new TestImpl (TransportProtocol.kTcp, FunEncoding.kJson);
    }


    class TestImpl : TestSessionBase
    {
        public TestImpl (TransportProtocol protocol, FunEncoding encoding)
        {
            session = FunapiSession.Create(TestInfo.ServerIp);

            session.SessionEventCallback += delegate (SessionEventType type, string sessionid)
            {
                if (isFinished)
                    return;

                if (type == SessionEventType.kStopped)
                    onTestFinished();
            };

            setTestTimeout(3f);

            ushort port = getPort("default", protocol, encoding);
            session.Connect(protocol, encoding, port);
            session.Connect(protocol, encoding, port);
            session.Stop();
            session.Stop();
        }
    }
}
