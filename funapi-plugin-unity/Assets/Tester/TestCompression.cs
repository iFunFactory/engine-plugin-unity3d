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
    public IEnumerator Zstd_Json ()
    {
        yield return new TestImpl (FunEncoding.kJson, FunCompressionType.kZstd, false);
    }

    [UnityTest]
    public IEnumerator Zstd_Protobuf ()
    {
        yield return new TestImpl (FunEncoding.kProtobuf, FunCompressionType.kZstd, false);
    }

    [UnityTest]
    public IEnumerator Zstd_Json_Dic ()
    {
        yield return new TestImpl (FunEncoding.kJson, FunCompressionType.kZstd, true);
    }

    [UnityTest]
    public IEnumerator Zstd_Protobuf_Dic ()
    {
        yield return new TestImpl (FunEncoding.kProtobuf, FunCompressionType.kZstd, true);
    }

    [UnityTest]
    public IEnumerator Zstd_Json_Enc ()
    {
        yield return new TestImpl (FunEncoding.kJson, FunCompressionType.kZstd, false, true);
    }

    [UnityTest]
    public IEnumerator Zstd_Protobuf_Enc ()
    {
        yield return new TestImpl (FunEncoding.kProtobuf, FunCompressionType.kZstd, false, true);
    }


    class TestImpl : TestSessionBase
    {
        public TestImpl (FunEncoding encoding, FunCompressionType comp_type, bool with_dic, bool with_enc = false)
        {
            session = FunapiSession.Create(TestInfo.ServerIp);

            session.SessionEventCallback += delegate (SessionEventType type, string sessionid)
            {
                if (type == SessionEventType.kStopped)
                {
                    if (compressor_ != null)
                    {
                        if (comp_type == FunCompressionType.kZstd)
                            ((FunapiZstdCompressor)compressor_).Destroy();
                    }

                    isFinished = true;
                }
            };

            session.CreateCompressorCallback += delegate (TransportProtocol protocol)
            {
                if (comp_type == FunCompressionType.kZstd)
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

            startConnect(TransportProtocol.kTcp, encoding, comp_type, with_enc);
            startConnect(TransportProtocol.kUdp, encoding, comp_type, with_enc);
            startConnect(TransportProtocol.kHttp, encoding, comp_type, with_enc);
        }

        void startConnect (TransportProtocol protocol, FunEncoding encoding,
                           FunCompressionType comp_type, bool with_enc = false)
        {
            TransportOption option = newTransportOption(protocol);
            option.CompressionType = comp_type;

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


        static readonly string dummyText = "{\"id\":12032,\"pos_x\":31.01,\"pos_z\":45.5293984741," +
                                           "\"dir_x\":-14.199799809265137,\"dir_z\":11.899918530274," +
                                           "\"look_x\":1.100000381469727,\"look_z\":11.600100381469727," +
                                           "\"_msgtype\":\"request_move\",\"_nick\":\"snooooooow\"}";

        FunapiCompressor compressor_ = null;
    }
}
