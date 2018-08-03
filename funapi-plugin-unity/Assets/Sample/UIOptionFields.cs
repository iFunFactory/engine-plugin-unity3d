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

        protocol = new OptionProtocol(drops["Protocol"]);
        encoding = new OptionEncoding(drops["Encoding"]);
    }


    public class Protocol
    {
        public Protocol (Dropdown dn)
        {
            list_ = dn;
            type_ = getType(dn.value);
        }

        public TransportProtocol type
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

        int getType (TransportProtocol value)
        {
            if (value == TransportProtocol.kTcp)
                return 0;
            else if (value == TransportProtocol.kUdp)
                return 1;
            else if (value == TransportProtocol.kHttp)
                return 2;
            else if (value == TransportProtocol.kWebsocket)
                return 3;

            return 0;
        }

        TransportProtocol getType (int value)
        {
            if (value == 0)
                return TransportProtocol.kTcp;
            else if (value == 1)
                return TransportProtocol.kUdp;
            else if (value == 2)
                return TransportProtocol.kHttp;
            else if (value == 3)
                return TransportProtocol.kWebsocket;

            return TransportProtocol.kDefault;
        }

        Dropdown list_;
        TransportProtocol type_;
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
            else
                return FunEncoding.kProtobuf;
        }

        Dropdown list_;
        FunEncoding type_;
    }


    public InputField address;
    public InputField port;
    public OptionProtocol protocol;
    public OptionEncoding encoding;
}
