// Copyright 2013 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using UnityEngine;


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
        if (!checkChanges())
            return;

        data_.address = fields_.address.text;
        data_.port = int.Parse(fields_.port.text);
        data_.protocol = fields_.protocol.typeInt;
        data_.encoding = fields_.encoding.typeInt;
    }

    bool checkChanges ()
    {
        if (bChanged)
            return true;

        if (data_.address != fields_.address.text ||
            data_.port != int.Parse(fields_.port.text) ||
            data_.protocol != fields_.protocol.typeInt ||
            data_.encoding != fields_.encoding.typeInt)
        {
            bChanged = true;
        }

        return bChanged;
    }


    public string address { get { return fields_.address.text; } }
    public ushort port { get { return ushort.Parse(fields_.port.text); } }
    public TransportProtocol protocol { get { return fields_.protocol.type; } }
    public FunEncoding encoding { get { return fields_.encoding.type; } }

    public bool bChanged { get; set; }

    public GameObject fieldsObject;

    UIOptionFields fields_;
    Data data_;
}
