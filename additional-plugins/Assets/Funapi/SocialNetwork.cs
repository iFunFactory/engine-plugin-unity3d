// Copyright (C) 2013 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

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
        kDownloadCompleted,     // When the all requested profile pictures has been downloaded.
        kError
    }


    // SocialNetwork class
    public abstract class SocialNetwork : MonoBehaviour
    {
        public string MyId
        {
            get { return my_info_.id; }
        }

        public string MyName
        {
            get { return my_info_.name; }
        }

        public Texture2D MyPicture
        {
            get { return my_info_.picture; }
        }


        // Friends-related functions

        public UserInfo FindFriend (string id)
        {
            lock (friend_list_)
            {
                return friend_list_.Find(item => item.id == id);
            }
        }

        public UserInfo FindFriend (int index)
        {
            lock (friend_list_)
            {
                if (index < 0 || index >= friend_list_.Count)
                    return null;

                return friend_list_[index];
            }
        }

        public UserInfo FindFriendToInvite (string id)
        {
            lock (invite_list_)
            {
                return invite_list_.Find(item => item.id == id);
            }
        }

        public UserInfo FindFriendToInvite (int index)
        {
            lock (invite_list_)
            {
                if (index < 0 || index >= invite_list_.Count)
                    return null;

                return invite_list_[index];
            }
        }

        // Creates a shallow copy list of a range of elements in the friend list.
        // Do not call this function from a update routine.
        public List<UserInfo> GetFriendList (int start = 0, int count = 0)
        {
            lock (friend_list_)
            {
                return getRangeOfList(friend_list_, start, count);
            }
        }

        // Creates a shallow copy list of a range of elements in the invite list.
        // Do not call this function from a update routine.
        public List<UserInfo> GetInviteList (int start = 0, int count = 0)
        {
            lock (invite_list_)
            {
                return getRangeOfList(invite_list_, start, count);
            }
        }

        List<UserInfo> getRangeOfList (List<UserInfo> list, int start, int count)
        {
            if (start < 0 || start >= list.Count || count < 0 || start + count > list.Count)
            {
                FunDebug.LogWarning("getRangeOfList - Invalid boundary index. " +
                                    "start:{0} count:{1} list:{2}", start, count, list.Count);
                return null;
            }

            if (count == 0)
                count = list.Count;

            return list.GetRange(start, count);
        }

        public int FriendListCount
        {
            get { lock (friend_list_) { return friend_list_.Count; } }
        }

        public int InviteListCount
        {
            get { lock (invite_list_) { return invite_list_.Count; } }
        }


        // Post-related functions

        public virtual void Post (string message)
        {
            Debug.LogWarning("This plugin does not support Post() function.");
        }

        public virtual void PostWithImage (string message, byte[] image)
        {
            Debug.LogWarning("This plugin does not support PostWithImage() function.");
        }

        public virtual void PostWithScreenshot (string message)
        {
            Debug.LogWarning("This plugin does not support PostWithScreenshot() function.");
        }


        // Picture-related functions

        // start: index at which the range starts.
        public void RequestPictures (List<UserInfo> list, int start, int count)
        {
            if (list == null || list.Count <= 0)
            {
                FunDebug.LogWarning("SocialNetwork.RequestPictures - Invalid list.");
                return;
            }

            StartCoroutine(RequestPictures(list));
        }

        protected IEnumerator RequestPicture (UserInfo info)
        {
            WWW www = new WWW(info.url);
            yield return www;

            if (www.texture != null)
            {
                FunDebug.DebugLog1("{0}'s profile picture downloaded.", info.name);
                info.picture = www.texture;
                OnPictureNotify(info);
            }
        }

        protected IEnumerator RequestPictures (List<UserInfo> list)
        {
            if (list == null || list.Count <= 0)
                yield break;

            foreach (UserInfo user in list)
            {
                WWW www = new WWW(user.url);
                yield return www;

                if (www.texture != null)
                {
                    FunDebug.DebugLog1("{0}'s profile picture downloaded.", user.name);
                    user.picture = www.texture;
                    OnPictureNotify(user);
                }
            }

            FunDebug.Log("The all requested profile pictures has been downloaded.");
            OnEventNotify(SNResultCode.kDownloadCompleted);
        }


        // Event callbacks

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
        protected List<UserInfo> friend_list_ = new List<UserInfo>();     // app using friends
        protected List<UserInfo> invite_list_ = new List<UserInfo>();     // non app using friends
    }
}
