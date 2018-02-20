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
    public IEnumerator TCP_Json_Ife1 ()
    {
        yield return new TestImpl (TransportProtocol.kTcp, FunEncoding.kJson,
                                   EncryptionType.kIFunEngine1Encryption, true);
    }

    [UnityTest]
    public IEnumerator TCP_Json_Ife2 ()
    {
        yield return new TestImpl (TransportProtocol.kTcp, FunEncoding.kJson,
                                   EncryptionType.kIFunEngine2Encryption);
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
    public IEnumerator UDP_Json_Ife2 ()
    {
        yield return new TestImpl (TransportProtocol.kUdp, FunEncoding.kJson,
                                   EncryptionType.kIFunEngine2Encryption, true);
    }

    [UnityTest]
    public IEnumerator HTTP_Json_Ife2 ()
    {
        yield return new TestImpl (TransportProtocol.kHttp, FunEncoding.kJson,
                                   EncryptionType.kIFunEngine2Encryption, true);
    }

    [UnityTest]
    public IEnumerator TCP_Protobuf_Ife1 ()
    {
        yield return new TestImpl (TransportProtocol.kTcp, FunEncoding.kProtobuf,
                                   EncryptionType.kIFunEngine1Encryption, true);
    }

    [UnityTest]
    public IEnumerator TCP_Protobuf_Ife2 ()
    {
        yield return new TestImpl (TransportProtocol.kTcp, FunEncoding.kProtobuf,
                                   EncryptionType.kIFunEngine2Encryption);
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

    [UnityTest]
    public IEnumerator UDP_Protobuf_Ife2 ()
    {
        yield return new TestImpl (TransportProtocol.kUdp, FunEncoding.kProtobuf,
                                   EncryptionType.kIFunEngine2Encryption, true);
    }

    [UnityTest]
    public IEnumerator HTTP_Protobuf_Ife2 ()
    {
        yield return new TestImpl (TransportProtocol.kHttp, FunEncoding.kProtobuf,
                                   EncryptionType.kIFunEngine2Encryption, true);
    }


    class TestImpl : TestSessionBase
    {
        public TestImpl (TransportProtocol protocol, FunEncoding encoding,
                         EncryptionType enc_type, bool send_all_types = false)
        {
            session = FunapiSession.Create(TestInfo.ServerIp);

            session.SessionEventCallback += delegate (SessionEventType type, string sessionid)
            {
                if (type == SessionEventType.kStopped)
                {
                    FunapiSession.Destroy(session);
                    isFinished = true;
                }
            };

            session.TransportEventCallback += delegate (TransportProtocol p, TransportEventType type)
            {
                if (isFinished)
                    return;

                if (type == TransportEventType.kStarted)
                {
                    if (send_all_types)
                    {
                        sendEchoMessage(protocol, EncryptionType.kDummyEncryption);
                        sendEchoMessage(protocol, EncryptionType.kIFunEngine2Encryption);

                        if (protocol == TransportProtocol.kTcp)
                        {
                            sendEchoMessage(protocol, EncryptionType.kIFunEngine1Encryption);
                            sendEchoMessage(protocol, EncryptionType.kChaCha20Encryption);
                            sendEchoMessage(protocol, EncryptionType.kAes128Encryption);
                        }
                    }
                    else
                    {
                        sendEchoMessageWithCount(protocol, 3);
                    }
                }
            };

            session.ReceivedMessageCallback += delegate (string type, object message)
            {
                onReceivedEchoMessage(type, message);

                if (isReceivedAllMessages)
                    session.Stop();
            };

            setTimeoutCallbackWithFail(3f);

            ushort port = getPort("encryption", protocol, encoding);
            TransportOption option = newTransportOption(protocol);
            option.Encryption = enc_type;

            session.Connect(protocol, encoding, port, option);
        }
    }
}
