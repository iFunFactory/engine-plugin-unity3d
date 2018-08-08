// Copyright 2013 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using UnityEngine;
using UnityEngine.UI;


public partial class UIOption : MonoBehaviour
{
    void Awake()
    {
        fields_ = fieldsObject.GetComponent<UIOptionFields>();
        fields_.Init();

        data_ = new Data();
        if (!PlayerPrefs.HasKey("address"))
            data_.Init();

        loadData();
    }

    void OnDestroy()
    {
        saveData();
        gameObject.SetActive(false);
    }


    void loadData ()
    {
        fields_.address.text = data_.address;
        fields_.port.text = data_.port.ToString();
        fields_.protocol.typeInt = data_.protocol;
        fields_.encoding.typeInt = data_.encoding;
    }

    void saveData ()
    {
        data_.address = fields_.address.text;
        data_.port = int.Parse(fields_.port.text);
        data_.protocol = fields_.protocol.typeInt;
        data_.encoding = fields_.encoding.typeInt;
    }

    public string address { get { return fields_.address.text; } }
    public ushort port { get { return ushort.Parse(fields_.port.text); } }
    public TransportProtocol protocol { get { return fields_.protocol.type; } }
    public FunEncoding encoding { get { return fields_.encoding.type; } }

    public GameObject fieldsObject;

    UIOptionFields fields_;
    Data data_;
}


public class OptionProtocol
{
    public OptionProtocol (Dropdown dn)
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
            return 1;
        else if (value == TransportProtocol.kUdp)
            return 2;
        else if (value == TransportProtocol.kHttp)
            return 3;
        else if (value == TransportProtocol.kWebsocket)
            return 4;

        return 0;
    }

    TransportProtocol getType (int value)
    {
        if (value == 1)
            return TransportProtocol.kTcp;
        else if (value == 2)
            return TransportProtocol.kUdp;
        else if (value == 3)
            return TransportProtocol.kHttp;
        else if (value == 4)
            return TransportProtocol.kWebsocket;

        return TransportProtocol.kDefault;
    }

    Dropdown list_;
    TransportProtocol type_;
}


public class OptionEncoding
{
    public OptionEncoding (Dropdown dn)
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
        if (value == FunEncoding.kJson)
            return 1;
        else if (value == FunEncoding.kProtobuf)
            return 2;

        return 0;
    }

    FunEncoding getType (int value)
    {
        if (value == 1)
            return FunEncoding.kJson;
        else if (value == 2)
            return FunEncoding.kProtobuf;

        return FunEncoding.kNone;
    }

    Dropdown list_;
    FunEncoding type_;
}
