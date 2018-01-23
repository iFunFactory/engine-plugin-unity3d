// Copyright 2013 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;


public class UIOptionFields : MonoBehaviour
{
    public void Init ()
    {
        Dropdown[] dnlist = transform.GetComponentsInChildren<Dropdown>();
        Dictionary<string, Dropdown> drops = new Dictionary<string, Dropdown>();
        foreach (Dropdown d in dnlist)
            drops[d.name] = d;

        tcpEncoding = new Encoding(drops["TcpEncoding"]);
        tcpEncryption = new Encryption(drops["TcpEncryption"]);

        udpEncoding = new Encoding(drops["UdpEncoding"]);
        udpEncryption = new Encryption(drops["UdpEncryption"]);

        httpEncoding = new Encoding(drops["HttpEncoding"]);
        httpEncryption = new Encryption(drops["HttpEncryption"]);
    }

    public class Encoding
    {
        public Encoding (Dropdown dn)
        {
            list_ = dn;
            type_ = getType(dn.value);
        }

        public FunEncoding type
        {
            get
            {
                type_ = getType(list_.value);
                return type_;
            }
            set
            {
                type_ = value;
                list_.value = getType(value);
            }
        }

        public int typeInt
        {
            get
            {
                type_ = getType(list_.value);
                return list_.value;
            }
            set
            {
                type_ = getType(value);
                list_.value = value;
            }
        }

        int getType (FunEncoding value)
        {
            return value == FunEncoding.kJson ? 0 : 1;
        }

        FunEncoding getType (int value)
        {
            if (value == 0)
                return FunEncoding.kJson;
            else if (value == 1)
                return FunEncoding.kProtobuf;
            else
                return FunEncoding.kNone;
        }

        Dropdown list_;
        FunEncoding type_;
    }

    public class Encryption
    {
        public Encryption (Dropdown dn)
        {
            list_ = dn;
            type_ = getType(dn.value);
        }

        public EncryptionType type
        {
            get
            {
                type_ = getType(list_.value);
                return type_;
            }
            set
            {
                type_ = value;
                list_.value = getType(value);
            }
        }

        public int typeInt
        {
            get
            {
                type_ = getType(list_.value);
                return list_.value;
            }
            set
            {
                type_ = getType(value);
                list_.value = value;
            }
        }

        int getType (EncryptionType value)
        {
            if (list_.options.Count == 2)
            {
                if (value == EncryptionType.kIFunEngine2Encryption)
                    return 1;
                else
                    return 0;
            }

            if (value == EncryptionType.kIFunEngine1Encryption)
                return 1;
            else if (value == EncryptionType.kIFunEngine2Encryption)
                return 2;
            else if (value == EncryptionType.kChaCha20Encryption)
                return 3;
            else if (value == EncryptionType.kAes128Encryption)
                return 4;
            else
                return 0;
        }

        EncryptionType getType (int value)
        {
            if (list_.options.Count == 2)
            {
                if (value == 1)
                    return EncryptionType.kIFunEngine2Encryption;
                else
                    return EncryptionType.kDefaultEncryption;
            }

            if (value == 1)
                return EncryptionType.kIFunEngine1Encryption;
            else if (value == 2)
                return EncryptionType.kIFunEngine2Encryption;
            else if (value == 3)
                return EncryptionType.kChaCha20Encryption;
            else if (value == 4)
                return EncryptionType.kAes128Encryption;
            else
                return EncryptionType.kDefaultEncryption;
        }

        Dropdown list_;
        EncryptionType type_;
    }


    public InputField serverAddress;
    public Toggle sessionReliability;
    public Toggle sequenceValidation;
    public Toggle sendSessionIdOnlyOnce;

    public Toggle connectTcp;
    public InputField tcpPort;
    public Encoding tcpEncoding;
    public Encryption tcpEncryption;
    public Toggle autoReconnect;
    public Toggle disableNagle;
    public Toggle usePing;

    public Toggle connectUdp;
    public InputField udpPort;
    public Encoding udpEncoding;
    public Encryption udpEncryption;

    public Toggle connectHttp;
    public InputField httpPort;
    public Encoding httpEncoding;
    public Encryption httpEncryption;
    public Toggle HTTPS;
    public Toggle useWWW;
}
