// Copyright (C) 2013-2015 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

namespace Fun
{
    public enum SnResultCode
    {
        kInitialized = 1,
        kLoggedIn,
        kLoginFailed,
        kMyProfile,
        kFriendList,
        kInviteList,
        kPosted,
        kPostFailed,
        kError
    }

    // Event handler delegate
    public delegate void SnEventHandler (SnResultCode code);

    // SocialNetwork class
    public abstract class SocialNetwork : MonoBehaviour
    {
        #region public abstract implementation
        public abstract void Init (params object[] param);

        public virtual void Post (string message)
        {
            Debug.Log("Does not support Post() function.");
        }

        public virtual void PostWithImage (string message, byte[] image)
        {
            Debug.Log("Does not support PostWithImage() function.");
        }

        public virtual void PostWithScreenshot (string message)
        {
            Debug.Log("Does not support PostWithScreenshot() function.");
        }

        public string my_id { get { return my_info_.id; } }
        public string my_name { get { return my_info_.name; } }
        public Texture2D my_picture { get { return my_info_.picture; } }

        public UserInfo FindFriendInfo (string id)
        {
            foreach (UserInfo info in friends_)
            {
                if (info.id == id)
                    return info;
            }

            return null;
        }

        public UserInfo FindFriendInfo (int index)
        {
            if (index < 0 || index >= friends_.Count)
                return null;

            return friends_[index];
        }

        public int FriendsCount { get { return friends_.Count; } }

        public UserInfo FindInviteFriendInfo (string id)
        {
            foreach (UserInfo info in invite_friends_)
            {
                if (info.id == id)
                    return info;
            }

            return null;
        }

        public UserInfo FindInviteFriendInfo (int index)
        {
            if (index < 0 || index >= invite_friends_.Count)
                return null;

            return invite_friends_[index];
        }

        public int InviteFriendsCount { get { return invite_friends_.Count; } }
        #endregion


        #region internal implementation
        protected void OnEventHandler (SnResultCode result)
        {
            if (EventCallback != null)
                EventCallback(result);
        }
        #endregion


        #region user's information
        public class UserInfo
        {
            public string id = "";
            public string name = "";
            public string url = "";  // url of picture
            public Texture2D picture = null;
        }
        #endregion


        // Registered event handlers.
        public event SnEventHandler EventCallback;

        // Member variables.
        protected UserInfo my_info_ = new UserInfo();
        protected List<UserInfo> friends_ = new List<UserInfo>();            // app using friends
        protected List<UserInfo> invite_friends_ = new List<UserInfo>();     // non app using friends
    }
}
