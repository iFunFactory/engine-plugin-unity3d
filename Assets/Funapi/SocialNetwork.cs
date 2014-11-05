// Copyright (C) 2014 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

// Event handler delegate
public delegate void EventHandlerLoggedIn();

// SocialNetwork class
public abstract class SocialNetwork : MonoBehaviour
{
    #region public abstract implementation
    public abstract void Init(params object[] param);
    public abstract void Login();
    public abstract void Logout();

    public virtual void Post (string message)
    {
        Debug.Log("Does not support Post() function.");
    }

    public virtual void PostWithScreenshot (string message)
    {
        Debug.Log("Does not support PostWithScreenshot() function.");
    }

    public string MyId { get { return my_info_.id; } }
    public string MyName { get { return my_info_.name; } }
    public Texture2D MyPicture { get { return my_info_.picture; } }

    public UserInfo GetFriendInfo (int index)
    {
        if (index < 0 || index >= friends_.Count)
            return null;

        return friends_[index];
    }
    public int GetFriendCount { get { return friends_.Count; } }
    #endregion


    #region internal implementation
    protected void OnLoggedIn()
    {
        EventLoggedIn();
    }
    #endregion


    #region user's information
    public class UserInfo
    {
        public string id
        {
            get { return id_; }
            set
            {
                id_ = value;

                string path = FunapiUtils.GetLocalDataPath + "/" + id_ + ".png";
                if (File.Exists(path))
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    picture_ = new Texture2D(128, 128);
                    picture_.LoadImage(bytes);
                    bytes = null;
                }
                else
                {
                    picture_ = null;
                }
            }
        }

        public Texture2D picture
        {
            get { return picture_; }
            set
            {
                picture_ = value;

                if (picture_ != null)
                {
                    string path = FunapiUtils.GetLocalDataPath + "/" + id_ + ".png";
                    if (File.Exists(path))
                        File.Delete(path);

                    byte[] data = picture_.EncodeToPNG();
                    File.WriteAllBytes(path, data);
                }
            }
        }

        public string name { get; set; }
        public string url { get; set; }     // Url of picture

        private string id_;
        private Texture2D picture_;
    }
    #endregion


    // Registered event handlers.
    public event EventHandlerLoggedIn EventLoggedIn;

    // Member variables.
    protected UserInfo my_info_ = new UserInfo();
    protected List<UserInfo> friends_ = new List<UserInfo>();
}
