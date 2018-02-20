// Copyright 2018 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using NUnit.Framework;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.TestTools;

// protobuf
using funapi.network.fun_message;
using plugin_messages;


public class TestCompression
{
    [OneTimeSetUp]
    protected void Init ()
    {
        FunapiEncryptor.public_key = "0b8504a9c1108584f4f0a631ead8dd548c0101287b91736566e13ead3f008f5d";
    }

    [UnityTest]
    public IEnumerator Default_Json ()
    {
        yield return new TestImpl (FunEncoding.kJson);
    }

    [UnityTest]
    public IEnumerator Default_Protobuf ()
    {
        yield return new TestImpl (FunEncoding.kProtobuf);
    }

    [UnityTest]
    public IEnumerator Encrypt_Json ()
    {
        yield return new TestImpl (FunEncoding.kJson, true);
    }

    [UnityTest]
    public IEnumerator Encrypt_Protobuf ()
    {
        yield return new TestImpl (FunEncoding.kProtobuf, true);
    }

    [UnityTest]
    [Ignore("Waiting to make a dictionary tool.")]
    public IEnumerator Default_Json_Dic ()
    {
        yield return new TestImpl (FunEncoding.kJson, false, true);
    }

    [UnityTest]
    [Ignore("Waiting to make a dictionary tool.")]
    public IEnumerator Default_Protobuf_Dic ()
    {
        yield return new TestImpl (FunEncoding.kJson, false, true);
    }

    [UnityTest]
    [Ignore("Waiting to make a dictionary tool.")]
    public IEnumerator Encrypt_Json_Dic ()
    {
        yield return new TestImpl (FunEncoding.kProtobuf, true, true);
    }

    [UnityTest]
    [Ignore("Waiting to make a dictionary tool.")]
    public IEnumerator Encrypt_Protobuf_Dic ()
    {
        yield return new TestImpl (FunEncoding.kProtobuf, true, true);
    }


    class TestImpl : TestSessionBase
    {
        public TestImpl (FunEncoding encoding, bool with_enc = false, bool with_dic = false)
        {
            session = FunapiSession.Create(TestInfo.ServerIp);

            session.SessionEventCallback += delegate (SessionEventType type, string sessionid)
            {
                if (type == SessionEventType.kStopped)
                {
                    FunapiSession.Destroy(session);

                    if (compressor_ != null)
                        compressor_.Destroy();

                    isFinished = true;
                }
            };

            session.CreateCompressorCallback += delegate (TransportProtocol protocol)
            {
                if (with_dic)
                {
                    var compressor = new FunapiZstdCompressor();
                    compressor.compression_threshold = 128;

                    if (with_dic)
                    {
                        TextAsset text = Resources.Load<TextAsset>("zstd_dict");
                        if (text != null)
                        {
                            byte[] zstd_dic = Convert.FromBase64String(text.text);
                            compressor.Create(zstd_dic);
                        }
                        else
                        {
                            FunDebug.LogWarning("Couldn't find a dictionary file in your resources folder.");
                        }
                    }

                    compressor_ = compressor;
                    return compressor;
                }

                return null;
            };

            session.TransportEventCallback += delegate (TransportProtocol protocol, TransportEventType type)
            {
                if (isFinished)
                    return;

                if (type == TransportEventType.kStarted)
                {
                    if (with_enc)
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

            setEchoMessage(dummyText);
            setTimeoutCallbackWithFail(3f);

            startConnect(TransportProtocol.kTcp, encoding, with_enc);
            startConnect(TransportProtocol.kUdp, encoding, with_enc);
            startConnect(TransportProtocol.kHttp, encoding, with_enc);
        }

        void startConnect (TransportProtocol protocol, FunEncoding encoding, bool with_enc)
        {
            TransportOption option = newTransportOption(protocol);
            option.CompressionType = getCompressType(protocol, with_enc);

            if (with_enc)
            {
                if (protocol == TransportProtocol.kTcp)
                    option.Encryption = EncryptionType.kIFunEngine1Encryption;
                else
                    option.Encryption = EncryptionType.kIFunEngine2Encryption;

                ushort port = getPort("compression-enc", protocol, encoding);
                session.Connect(protocol, encoding, port, option);
            }
            else
            {
                ushort port = getPort("compression", protocol, encoding);
                session.Connect(protocol, encoding, port, option);
            }
        }

        FunCompressionType getCompressType (TransportProtocol protocol, bool with_enc)
        {
            if (protocol == TransportProtocol.kTcp || protocol == TransportProtocol.kUdp)
                return with_enc ? FunCompressionType.kZstd : FunCompressionType.kDeflate;
            else
                return with_enc ? FunCompressionType.kDeflate : FunCompressionType.kZstd;
        }


        static readonly string dummyText = "{\"id\":12032,\"pos_x\":31.01,\"pos_z\":45.5293984741," +
                                           "\"dir_x\":-14.199799809265137,\"dir_z\":11.899918530274," +
                                           "\"look_x\":1.100000381469727,\"look_z\":11.600100381469727," +
                                           "\"_msgtype\":\"request_move\",\"_nick\":\"snooooooow\"}";

        FunapiZstdCompressor compressor_ = null;
    }
}
