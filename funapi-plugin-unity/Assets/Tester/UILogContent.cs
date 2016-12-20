// Copyright 2013-2016 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;


public class UILogContent : MonoBehaviour
{
    void Start ()
    {
        view_rect_ = transform.parent.GetComponent<RectTransform>().rect;
        content_rect_ = transform.GetComponent<RectTransform>();
    }

    void Update ()
    {
        lock (lock_)
        {
            if (!running_ && list_.Count > 0)
            {
                StartCoroutine(addLog(list_[0]));
                list_.RemoveAt(0);
            }
        }
    }

    public void AddLog (string text)
    {
        lock (lock_)
        {
            list_.Add(text);
        }
    }

    IEnumerator addLog (string text)
    {
        lock (lock_)
        {
            running_ = true;

            GameObject item = GameObject.Instantiate(Resources.Load("LogItem")) as GameObject;
            if (item != null)
            {
                item.name = "LogItem";
                item.transform.SetParent(transform);
                item.transform.localScale = Vector3.one;
                item.transform.localPosition = new Vector3(kBorder, content_height, 0f);

                Text item_text = item.transform.FindChild("Text").GetComponent<Text>();
                item_text.text = text;
                yield return new WaitForEndOfFrame();

                content_height -= item_text.preferredHeight;

                RectTransform item_rect = item.transform.GetComponent<RectTransform>();
                item_rect.sizeDelta = new Vector2(item_text.preferredWidth, item_text.preferredHeight);

                float width = Mathf.Max(item_text.preferredWidth + kBorder * 2, content_rect_.sizeDelta.x);
                float height = Mathf.Abs(content_height) + kBorder;
                content_rect_.sizeDelta = new Vector2(width, height);

                if (height > view_rect_.height)
                    content_rect_.localPosition = new Vector3(0f, height - view_rect_.height);
            }

            running_ = false;
        }
    }


    const float kBorder = 15f;

    Rect view_rect_;
    RectTransform content_rect_;
    float content_height = -kBorder;

    bool running_ = false;
    object lock_ = new object();
    List<string> list_ = new List<string>();
}
