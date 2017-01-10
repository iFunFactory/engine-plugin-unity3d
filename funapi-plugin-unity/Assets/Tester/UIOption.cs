// Copyright 2013-2016 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using UnityEngine;


public partial class UIOption : MonoBehaviour
{
    public void Init ()
    {
        fields_ = fieldsObject.GetComponent<UIOptionFields>();
        fields_.Init();

        data_ = new Data();
        if (!PlayerPrefs.HasKey("serverAddress"))
            data_.Init();

        loadData();
    }

    public void OnClose ()
    {
        saveData();
        gameObject.SetActive(false);
    }


    void loadData ()
    {
        fields_.serverAddress.text = data_.serverAddress;
        fields_.sessionReliability.isOn = data_.sessionReliability;
        fields_.sequenceValidation.isOn = data_.sequenceValidation;

        fields_.connectTcp.isOn = data_.connectTcp;
        fields_.tcpPort.text = data_.tcpPort.ToString();
        fields_.tcpEncoding.typeInt = data_.tcpEncoding;
        fields_.tcpEncryption.typeInt = data_.tcpEncryption;
        fields_.autoReconnect.isOn = data_.autoReconnect;
        fields_.disableNagle.isOn = data_.disableNagle;
        fields_.usePing.isOn = data_.usePing;

        fields_.connectUdp.isOn = data_.connectUdp;
        fields_.udpPort.text = data_.udpPort.ToString();
        fields_.udpEncoding.typeInt = data_.udpEncoding;
        fields_.udpEncryption.typeInt = data_.udpEncryption;

        fields_.connectHttp.isOn = data_.connectHttp;
        fields_.httpPort.text = data_.httpPort.ToString();
        fields_.httpEncoding.typeInt = data_.httpEncoding;
        fields_.httpEncryption.typeInt = data_.httpEncryption;
        fields_.HTTPS.isOn = data_.HTTPS;
        fields_.useWWW.isOn = data_.useWWW;
    }

    void saveData ()
    {
        data_.serverAddress = fields_.serverAddress.text;
        data_.sessionReliability = fields_.sessionReliability.isOn;
        data_.sequenceValidation = fields_.sequenceValidation.isOn;

        data_.connectTcp = fields_.connectTcp.isOn;
        data_.tcpPort = int.Parse(fields_.tcpPort.text);
        data_.tcpEncoding = fields_.tcpEncoding.typeInt;
        data_.tcpEncryption = fields_.tcpEncryption.typeInt;
        data_.autoReconnect = fields_.autoReconnect.isOn;
        data_.disableNagle = fields_.disableNagle.isOn;
        data_.usePing = fields_.usePing.isOn;

        data_.connectUdp = fields_.connectUdp.isOn;
        data_.udpPort = int.Parse(fields_.udpPort.text);
        data_.udpEncoding = fields_.udpEncoding.typeInt;
        data_.udpEncryption = fields_.udpEncryption.typeInt;

        data_.connectHttp = fields_.connectHttp.isOn;
        data_.httpPort = int.Parse(fields_.httpPort.text);
        data_.httpEncoding = fields_.httpEncoding.typeInt;
        data_.httpEncryption = fields_.httpEncryption.typeInt;
        data_.HTTPS = fields_.HTTPS.isOn;
        data_.useWWW = fields_.useWWW.isOn;
    }


    public string serverAddress { get { return fields_.serverAddress.text; } }
    public bool sessionReliability { get { return fields_.sessionReliability.isOn; } }
    public bool sequenceValidation { get { return fields_.sequenceValidation.isOn; } }

    public bool connectTcp { get { return fields_.connectTcp.isOn; } }
    public ushort tcpPort { get { return ushort.Parse(fields_.tcpPort.text); } }
    public FunEncoding tcpEncoding { get { return fields_.tcpEncoding.type; } }
    public EncryptionType tcpEncryption { get { return fields_.tcpEncryption.type; } }
    public bool autoReconnect { get { return fields_.autoReconnect.isOn; } }
    public bool disableNagle { get { return fields_.disableNagle.isOn; } }
    public bool usePing { get { return fields_.usePing.isOn; } }

    public bool connectUdp { get { return fields_.connectUdp.isOn; } }
    public ushort udpPort { get { return ushort.Parse(fields_.udpPort.text); } }
    public FunEncoding udpEncoding { get { return fields_.udpEncoding.type; } }
    public EncryptionType udpEncryption { get { return fields_.udpEncryption.type; } }

    public bool connectHttp { get { return fields_.connectHttp.isOn; } }
    public ushort httpPort { get { return ushort.Parse(fields_.httpPort.text); } }
    public FunEncoding httpEncoding { get { return fields_.httpEncoding.type; } }
    public EncryptionType httpEncryption { get { return fields_.httpEncryption.type; } }
    public bool HTTPS { get { return fields_.HTTPS.isOn; } }
    public bool useWWW { get { return fields_.useWWW; } }

    public GameObject fieldsObject;

    Data data_;
    UIOptionFields fields_;
}
