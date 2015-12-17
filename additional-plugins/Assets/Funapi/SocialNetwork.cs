// Copyright (C) 2013-2015 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Fun
{
    public enum SNResultCode
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


    // SocialNetwork class
    public abstract class SocialNetwork : MonoBehaviour
    {
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

        public int friend_list_count { get { return friends_.Count; } }
        public int invite_list_count { get { return invite_friends_.Count; } }

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


        // Picture-related functions
        protected IEnumerator RequestPicture (UserInfo info)
        {
            WWW www = new WWW(info.url);
            yield return www;

            if (www.texture != null) {
                DebugUtils.DebugLog("Gotten {0}'s profile picture.", info.name);
                info.picture = www.texture;
                OnPictureNotify(info);
            }
        }

        protected IEnumerator RequestPictureList (List<UserInfo> list)
        {
            if (list == null || list.Count <= 0)
                yield break;

            foreach (UserInfo user in list)
            {
                WWW www = new WWW(user.url);
                yield return www;

                if (www.texture != null) {
                    DebugUtils.DebugLog("Gotten {0}'s profile picture.", user.name);
                    user.picture = www.texture;
                    OnPictureNotify(user);
                }
            }
        }

        protected void OnEventNotify (SNResultCode result)
        {
            if (OnEventCallback != null)
                OnEventCallback(result);
        }

        protected void OnPictureNotify (UserInfo user)
        {
            if (OnPictureDownloaded != null)
                OnPictureDownloaded(user);
        }


        // User's id, name, picture
        public class UserInfo
        {
            public string id = "";
            public string name = "";
            public string url = "";  // url of picture
            public Texture2D picture = null;
        }


        // Event handler delegate
        public delegate void EventHandler (SNResultCode code);
        public delegate void PictureDownloaded (UserInfo user);

        // Registered event handlers.
        public event EventHandler OnEventCallback;
        public event PictureDownloaded OnPictureDownloaded;

        // Member variables.
        protected UserInfo my_info_ = new UserInfo();
        protected List<UserInfo> friends_ = new List<UserInfo>();            // app using friends
        protected List<UserInfo> invite_friends_ = new List<UserInfo>();     // non app using friends
    }
}
