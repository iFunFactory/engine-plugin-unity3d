// Copyright 2018 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using NUnit.Framework;
using System.Collections;
using UnityEngine.TestTools;


public class TestEncryption
{
    [OneTimeSetUp]
    protected void Init ()
    {
        FunapiEncryptor.public_key = "0b8504a9c1108584f4f0a631ead8dd548c0101287b91736566e13ead3f008f5d";
    }

    [UnityTest]
    public IEnumerator TCP_Json_Chacha20 ()
    {
        yield return new TestImpl (TransportProtocol.kTcp, FunEncoding.kJson,
                                   EncryptionType.kChaCha20Encryption);
    }

    [UnityTest]
    public IEnumerator TCP_Json_Aes128 ()
    {
        yield return new TestImpl (TransportProtocol.kTcp, FunEncoding.kJson,
                                   EncryptionType.kAes128Encryption);
    }

    [UnityTest]
    public IEnumerator TCP_Protobuf_Chacha20 ()
    {
        yield return new TestImpl (TransportProtocol.kTcp, FunEncoding.kProtobuf,
                                   EncryptionType.kChaCha20Encryption);
    }

    [UnityTest]
    public IEnumerator TCP_Protobuf_Aes128 ()
    {
        yield return new TestImpl (TransportProtocol.kTcp, FunEncoding.kProtobuf,
                                   EncryptionType.kAes128Encryption);
    }


    class TestImpl : TestSessionBase
    {
        public TestImpl (TransportProtocol protocol, FunEncoding encoding, EncryptionType enc_type)
        {
            session = FunapiSession.Create(TestInfo.ServerIp);

            session.TransportEventCallback += delegate (TransportProtocol p, TransportEventType type)
            {
                if (isFinished)
                    return;

                if (type == TransportEventType.kStarted)
                {
                    sendEchoMessage(protocol, EncryptionType.kDummyEncryption);
                    sendEchoMessageWithCount(protocol, 2);
                }
            };

            session.ReceivedMessageCallback += delegate (string type, object message)
            {
                onReceivedEchoMessage(type, message);

                if (isReceivedAllMessages)
                    onTestFinished();
            };

            setTestTimeout(3f);

            ushort port = getPort("encryption", protocol, encoding);
            TransportOption option = newTransportOption(protocol);
            option.Encryption = enc_type;

            session.Connect(protocol, encoding, port, option);
        }
    }
}
