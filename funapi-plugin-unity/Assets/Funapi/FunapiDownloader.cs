// Copyright 2013 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using MiniJSON;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;


namespace Fun
{
    // Download file-related.
    public class DownloadFileInfo
    {
        public string path;         // save file path
        public uint size;           // file size
        public string hash;         // md5 hash
        public string hash_front;   // front part of file (1MB)
    }

    public enum DownloadResult
    {
        SUCCESS,
        PAUSED,
        FAILED
    }


    public class FunapiHttpDownloader : FunapiMono.Listener
    {
        public FunapiHttpDownloader ()
        {
            // List file handler
            web_client_.DownloadDataCompleted += downloadDataCompleteCb;

            // Download file handler
            web_client_.DownloadProgressChanged += downloadProgressChangedCb;
            web_client_.DownloadFileCompleted += downloadFileCompleteCb;
        }

        public override void OnQuit ()
        {
            Stop();
        }

        // Start downloading
        public void GetDownloadList (string hostname_or_ip, UInt16 port, bool https,
                                     string target_path, string file_path = "")
        {
            string url = string.Format("{0}://{1}:{2}",
                                       (https ? "https" : "http"), hostname_or_ip, port);

            GetDownloadList(url, target_path, file_path);
        }

        public void GetDownloadList (string url, string target_path, string file_path = "")
        {
            lock (lock_)
            {
                if (ReadyCallback == null)
                {
                    FunDebug.LogError("Downloader.GetDownloadList - You must register the ReadyCallback first.");
                    return;
                }

                if (IsDownloading)
                {
                    FunDebug.LogWarning("The resource file is being downloaded. (Url: {0})\n" +
                                        "Please try again after the download is completed.", url);
                    return;
                }

                state_ = State.Start;

                string host_url = url;
                if (host_url[host_url.Length - 1] != '/')
                    host_url += "/";
                host_url_ = host_url;
                FunDebug.Log("[Downloader] Download from {0}", host_url_);

                target_path_ = target_path;
                if (target_path_[target_path_.Length - 1] != '/')
                    target_path_ += "/";
                target_path_ += kRootPath + "/";
                FunDebug.Log("[Downloader] Save to {0}", target_path_);

                cur_download_count_ = 0;
                cur_download_size_ = 0;
                total_download_count_ = 0;
                total_download_size_ = 0;
                partial_download_size_ = 0;

                setMonoListener();
                loadCachedList();

                // Gets list file
                string request_url = host_url_;
                if (!string.IsNullOrEmpty(file_path))
                {
                    if (file_path[0] == '/')
                        file_path = file_path.Substring(1);

                    request_url += file_path;
                }

                downloadListFile(request_url);
            }
        }

        public void StartDownload ()
        {
            lock (lock_)
            {
                if (state_ != State.Ready)
                {
                    FunDebug.LogWarning("Downloader.StartDownload - You must call GetDownloadList() first " +
                                        "or wait until ready to download.");
                    return;
                }

                if (partial_downloading_)
                {
                    FunDebug.Log("[Downloader] Ready to download {0} file(s) ({1})",
                                 partial_download_list_.Count, getSizeString(partial_download_size_));
                }
                else if (total_download_count_ > 0)
                {
                    FunDebug.Log("[Downloader] Ready to download {0} file(s) ({1})",
                                 total_download_count_, getSizeString(total_download_size_));
                }

                state_ = State.Downloading;
                retry_download_count_ = 0;
                download_time_.Start();

                // Starts download
                downloadResourceFile();
            }
        }

        public void StartDownload (string prefix_path)
        {
            lock (lock_)
            {
                partial_download_list_ = download_list_.FindAll(
                    item => { return item.path.StartsWith(prefix_path); });

                if (partial_download_list_.Count <= 0)
                {
                    FunDebug.Log("[Downloader] There's no '{0}' file to download.", prefix_path);
                    Stop();
                    return;
                }

                List<string> list = new List<string>();

                partial_download_size_ = 0;
                partial_download_list_.ForEach(
                    item => { partial_download_size_ += item.size; list.Add(item.path); });

                download_list_.RemoveAll(item => { return list.Contains(item.path); });

                partial_downloading_ = true;
            }

            // Starts download
            StartDownload();
        }

        public void ContinueDownload ()
        {
            lock (lock_)
            {
                if (state_ == State.Paused &&
                    (download_list_.Count > 0 || partial_download_list_.Count > 0))
                {
                    state_ = State.Downloading;
                    retry_download_count_ = 0;
                    download_time_.Start();

                    setMonoListener();
                    downloadResourceFile();
                }
            }
        }

        public void Stop ()
        {
            lock (lock_)
            {
                if (state_ == State.None || state_ == State.Completed)
                    return;

                if (state_ == State.Downloading)
                {
                    state_ = State.Paused;
                    web_client_.CancelAsync();
                    download_time_.Stop();
                    FunDebug.Log("[Downloader] Paused.");

                    onFinished(DownloadResult.PAUSED);
                }
                else
                {
                    state_ = State.None;
                    FunDebug.Log("[Downloader] Stopped.");

                    onFinished(DownloadResult.FAILED);
                }
            }
        }

        public override string name { get { return "FunapiDownloader"; } }

        public bool IsPaused { get { return state_ == State.Paused; } }

        public bool IsDownloading { get { return state_ >= State.Start && state_ <= State.Downloading; } }

        public string DownloadPath { get { return target_path_; } }

        public int CurrentDownloadFileCount { get { return cur_download_count_; } }

        public int TotalDownloadFileCount { get { return total_download_count_; } }

        public UInt64 CurDownloadFileSize { get { return cur_download_size_; } }

        public UInt64 TotalDownloadFileSize { get { return total_download_size_; } }


        void loadCachedList ()
        {
            cached_list_.Clear();

            string path = target_path_ + kCachedFileName;
            if (!File.Exists(path))
                return;

            StreamReader stream = File.OpenText(path);
            string data = stream.ReadToEnd();
            stream.Close();

            if (data.Length <= 0)
            {
                FunDebug.LogWarning("Downloader.loadCachedList - Failed to load a cached file list.");
                return;
            }

            Dictionary<string, object> json = Json.Deserialize(data) as Dictionary<string, object>;
            List<object> list = json["list"] as List<object>;

            foreach (Dictionary<string, object> node in list)
            {
                DownloadFileInfo info = new DownloadFileInfo();
                info.path = node["path"] as string;
                info.size = Convert.ToUInt32(node["size"]);
                info.hash = node["hash"] as string;
                if (node.ContainsKey("front"))
                    info.hash_front = node["front"] as string;
                else
                    info.hash_front = "";

                cached_list_.Add(info);
            }

            FunDebug.LogDebug("[Downloader] Cached list loaded : {0}", cached_list_.Count);
        }

        void updateCachedList ()
        {
            StringBuilder data = new StringBuilder();
            data.Append("{ \"list\": [ ");

            int index = 0;
            foreach (DownloadFileInfo info in cached_list_)
            {
                data.AppendFormat("{{ \"path\":\"{0}\", ", info.path);
                data.AppendFormat("\"size\":{0}, ", info.size);
                if (info.hash_front.Length > 0)
                    data.AppendFormat("\"front\":\"{0}\", ", info.hash_front);
                data.AppendFormat("\"hash\":\"{0}\" }}", info.hash);

                if (++index < cached_list_.Count)
                    data.Append(", ");
            }

            data.Append(" ] }");

            string path = target_path_ + kCachedFileName;
            FileStream file = File.Open(path, FileMode.Create);
            StreamWriter stream = new StreamWriter(file);
            stream.Write(data.ToString());
            stream.Flush();
            stream.Close();

            FunDebug.LogDebug("[Downloader] Updates cached list : {0}", cached_list_.Count);
        }

        // Checks download file list
        IEnumerator checkFileList (List<DownloadFileInfo> list)
        {
            List<DownloadFileInfo> tmp_list = new List<DownloadFileInfo>(list);
            List<string> verify_file_list = new List<string>();
            List<string> remove_list = new List<string>();
            Queue<int> rnd_list = new Queue<int>();
            bool verify_success = true;
            int rnd_index = -1;

            DateTime cached_time = File.GetLastWriteTime(target_path_ + kCachedFileName);
            Stopwatch elapsed_time = new Stopwatch();
            elapsed_time.Start();

            delete_file_list_.Clear();

            // Randomly check list
            if (cached_list_.Count > 0)
            {
                int max_count = cached_list_.Count;
                int count = Math.Min(Math.Max(1, max_count / 10), 10);
                System.Random rnd = new System.Random((int)DateTime.Now.Ticks);

                while (rnd_list.Count < count)
                {
                    rnd_index = rnd.Next(1, max_count + 1) - 1;
                    if (!rnd_list.Contains(rnd_index))
                        rnd_list.Enqueue(rnd_index);
                }
                FunDebug.LogDebug("[Downloader] {0} files are randomly selected for check.", rnd_list.Count);

                rnd_index = rnd_list.Count > 0 ? rnd_list.Dequeue() : -1;
            }

            // Checks local files
            int index = 0;
            foreach (DownloadFileInfo file in cached_list_)
            {
                DownloadFileInfo item = list.Find(i => i.path == file.path);
                if (item != null)
                {
                    string path = target_path_ + file.path;
                    FileInfo info = new FileInfo(path);

                    if (!File.Exists(path) || item.size != info.Length || item.hash != file.hash)
                    {
                        remove_list.Add(file.path);
                        FunDebug.LogWarning("'{0}' file has been changed or deleted.", file.path);
                    }
                    else
                    {
                        string filename = Path.GetFileName(item.path);
                        if (filename[0] == '_' || index == rnd_index ||
                            File.GetLastWriteTime(path).Ticks > cached_time.Ticks)
                        {
                            if (index == rnd_index) {
                                rnd_index = rnd_list.Count > 0 ? rnd_list.Dequeue() : -1;
                            }

                            verify_file_list.Add(file.path);

                            StartCoroutine(MD5Async.Compute(path, item,
                                delegate (string p, DownloadFileInfo f, bool is_match)
                                {
                                    if (VerifyCallback != null)
                                        VerifyCallback(p);

                                    verify_file_list.Remove(f.path);

                                    if (is_match)
                                    {
                                        list.Remove(f);
                                    }
                                    else
                                    {
                                        remove_list.Add(f.path);
                                        verify_success = false;
                                    }
                                }
                            ));
                        }
                        else
                        {
                            list.Remove(item);
                        }
                    }
                }
                else
                {
                    remove_list.Add(file.path);
                }

                ++index;
            }

            while (verify_file_list.Count > 0)
            {
                yield return new SleepForSeconds(0.1f);
            }

            removeCachedList(remove_list);

            FunDebug.LogDebug("[Downloader] Random validation has {0}",
                               (verify_success ? "succeeded" : "failed"));

            // Checks all local files
            if (!verify_success)
            {
                foreach (DownloadFileInfo file in cached_list_)
                {
                    DownloadFileInfo item = tmp_list.Find(i => i.path == file.path);
                    if (item != null)
                    {
                        verify_file_list.Add(file.path);

                        string path = target_path_ + file.path;
                        StartCoroutine(MD5Async.Compute(path, item,
                            delegate (string p, DownloadFileInfo f, bool is_match)
                            {
                                if (VerifyCallback != null)
                                    VerifyCallback(p);

                                verify_file_list.Remove(f.path);

                                if (!is_match)
                                {
                                    remove_list.Add(f.path);

                                    if (!list.Contains(f))
                                        list.Add(f);
                                }
                            }
                        ));
                    }
                }

                while (verify_file_list.Count > 0)
                {
                    yield return new SleepForSeconds(0.1f);
                }

                removeCachedList(remove_list);
            }

            elapsed_time.Stop();

            FunDebug.Log("[Downloader] Took {0:F2}s to check local files.",
                         elapsed_time.ElapsedMilliseconds / 1000f);

            total_download_count_ = list.Count;

            foreach (DownloadFileInfo item in list)
            {
                total_download_size_ += item.size;
            }

            // Deletes files
            deleteLocalFiles();

            if (total_download_count_ > 0)
            {
                state_ = State.Ready;

                event_.Add (delegate
                {
                    FunDebug.Log("[Downloader] Ready to download.");

                    if (ReadyCallback != null)
                        ReadyCallback(total_download_count_, total_download_size_);
                });
            }
            else
            {
                updateCachedList();

                state_ = State.Completed;
                FunDebug.Log("[Downloader] All resources are up to date.");
                onFinished(DownloadResult.SUCCESS);
            }
        }

        void removeCachedList (List<string> remove_list)
        {
            if (remove_list.Count <= 0)
                return;

            cached_list_.RemoveAll(
                item => { return remove_list.Contains(item.path); });

            foreach (string path in remove_list)
            {
                if (File.Exists(path))
                    delete_file_list_.Add(target_path_ + path);
            }

            remove_list.Clear();
        }

        void deleteLocalFiles ()
        {
            if (delete_file_list_.Count <= 0)
                return;

            FunDebug.Log("[Downloader] Try to delete {0} local files.", delete_file_list_.Count);

            foreach (string path in delete_file_list_)
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    FunDebug.LogDebug("'{0}' file deleted.\npath: {1}", Path.GetFileName(path), path);
                }
            }

            delete_file_list_.Clear();
        }

        void downloadListFile (string url)
        {
            try
            {
                // Request a list of download files.
                FunDebug.Log("[Downloader] Getting list file from {0}", url);
                web_client_.DownloadDataAsync(new Uri(url));
            }
            catch (Exception e)
            {
                FunDebug.LogError("Failure in Downloader.downloadListFile: {0}", e.ToString());
                Stop();
            }
        }

        // Downloading files.
        void downloadResourceFile ()
        {
            if (state_ != State.Downloading)
                return;

            if (download_list_.Count <= 0 ||
                (partial_downloading_ && partial_download_list_.Count <= 0))
            {
                updateCachedList();

                download_time_.Stop();
                FunDebug.Log("[Downloader] Took {0:F2}s for downloading all files.",
                             download_time_.ElapsedMilliseconds / 1000f);

                if (partial_downloading_)
                {
                    if (download_list_.Count > 0)
                        state_ = State.Ready;
                    else
                        state_ = State.Completed;

                    partial_downloading_ = false;
                    FunDebug.Log("[Downloader] Partial downloading completed.");
                }
                else
                {
                    state_ = State.Completed;
                    FunDebug.Log("[Downloader] Download completed.");
                }

                onFinished(DownloadResult.SUCCESS);
            }
            else
            {
                DownloadFileInfo info = null;

                if (partial_downloading_)
                    info = partial_download_list_[0];
                else
                    info = download_list_[0];

                // Check directory
                string path = target_path_;
                int offset = info.path.LastIndexOf('/');
                if (offset > 0)
                    path += info.path.Substring(0, offset);

                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);

                string file_path = target_path_ + info.path;
                if (File.Exists(file_path))
                    File.Delete(file_path);

                // Requests a file.
                string request_url = host_url_ + info.path;
                FunDebug.LogDebug("Download a file - {0}\nSave to {1}\n", request_url, file_path);
                cur_download_path_ = Path.GetDirectoryName(file_path);
                cur_download_path_ += "/" + Path.GetRandomFileName();

                web_client_.DownloadFileAsync(new Uri(request_url), cur_download_path_, info);
            }
        }

        // Callback function for list of files
        void downloadDataCompleteCb (object sender, DownloadDataCompletedEventArgs ar)
        {
            bool failed = false;
            try
            {
                lock (lock_)
                {
                    if (ar.Error != null)
                    {
                        FunDebug.LogError("[Downloader] Exception error: {0}", ar.Error);
                        failed = true;
                    }
                    else
                    {
                        // It can be true when CancelAsync() called in Stop().
                        if (ar.Cancelled)
                            return;

                        // Parse json
                        string data = Encoding.UTF8.GetString(ar.Result);
                        Dictionary<string, object> json = Json.Deserialize(data) as Dictionary<string, object>;

                        //FunDebug.Log("Json data >> {0}", data);

                        // Redirect url
                        if (json.ContainsKey("url"))
                        {
                            string url = json["url"] as string;
                            if (url[url.Length - 1] != '/')
                                url += "/";

                            host_url_ = url;
                            FunDebug.Log("[Downloader] Redirect Url: {0}", host_url_);
                        }

                        List<object> list = json["data"] as List<object>;
                        if (list.Count <= 0)
                        {
                            FunDebug.LogWarning("Invalid list data. List count is 0.");
                            FunDebug.Assert(false);
                            failed = true;
                        }
                        else
                        {
                            download_list_.Clear();
                            partial_download_list_.Clear();

                            foreach (Dictionary<string, object> node in list)
                            {
                                DownloadFileInfo info = new DownloadFileInfo();
                                info.path = node["path"] as string;
                                info.size = Convert.ToUInt32(node["size"]);
                                info.hash = node["md5"] as string;
                                if (node.ContainsKey("md5_front"))
                                    info.hash_front = node["md5_front"] as string;
                                else
                                    info.hash_front = "";

                                download_list_.Add(info);
                            }

                            // Checks files
                            event_.Add(() => StartCoroutine(checkFileList(download_list_)));
                        }
                    }
                }
            }
            catch (Exception e)
            {
                FunDebug.LogError("Failure in Downloader.downloadDataCompleteCb: {0}", e.ToString());
                failed = true;
            }

            if (failed)
            {
                Stop();
            }
        }

        // Callback function for download progress.
        void downloadProgressChangedCb (object sender, DownloadProgressChangedEventArgs ar)
        {
            if (UpdateCallback == null)
                return;

            var info = (DownloadFileInfo)ar.UserState;
            if (info != null)
            {
                UpdateCallback(info.path, ar.BytesReceived, ar.TotalBytesToReceive, ar.ProgressPercentage);
            }
        }

        // Callback function for downloaded file.
        void downloadFileCompleteCb (object sender, System.ComponentModel.AsyncCompletedEventArgs ar)
        {
            bool failed = false;
            try
            {
                lock (lock_)
                {
                    // It can be true when CancelAsync() called in Stop().
                    if (ar.Cancelled)
                    {
                        File.Delete(cur_download_path_);
                        return;
                    }

                    if (ar.Error != null)
                    {
                        FunDebug.LogError("[Downloader] Exception error: {0}", ar.Error);
                        failed = true;
                    }
                    else
                    {
                        var info = (DownloadFileInfo)ar.UserState;
                        if (info == null)
                        {
                            FunDebug.LogWarning("[Downloader] DownloadFileInfo object is null.");
                            failed = true;
                        }
                        else
                        {
                            string path = target_path_ + info.path;
                            File.Move(cur_download_path_, path);

                            ++cur_download_count_;
                            retry_download_count_ = 0;
                            cur_download_size_ += info.size;
                            cached_list_.Add(info);

                            if (partial_downloading_)
                                partial_download_list_.Remove(info);
                            else
                                download_list_.Remove(info);

                            downloadResourceFile();
                        }
                    }
                }
            }
            catch (Exception e)
            {
                FunDebug.LogError("Failure in Downloader.downloadFileCompleteCb: {0}", e.ToString());
                failed = true;
            }

            if (failed)
            {
                web_client_.Dispose();
                File.Delete(cur_download_path_);

                if (retry_download_count_ < kRetryCountMax)
                {
                    ++retry_download_count_;
                    event_.Add(() => StartCoroutine(retryDownloadFile()));
                }
                else
                {
                    Stop();
                }
            }
        }

        IEnumerator retryDownloadFile ()
        {
            float time = 1f;
            for (int i = 0; i < retry_download_count_; ++i)
                time *= 2f;
            yield return new SleepForSeconds(time);

            downloadResourceFile();
        }

        void onFinished (DownloadResult code)
        {
            event_.Add (delegate
            {
                FunDebug.Log("[Downloader] Finished.");

                releaseMonoListener();

                if (FinishedCallback != null)
                    FinishedCallback(code);
            });
        }

        static string getSizeString (UInt64 target_size)
        {
            float size;
            string unit;
            if (target_size < 1024 * 1024) {
                size = target_size / 1024f;
                unit = "K";
            }
            else {
                size = target_size / (1024f * 1024f);
                unit = "M";
            }

            return string.Format("{0}{1}", Math.Round(size, 3), unit);
        }


        enum State
        {
            None,
            Start,
            Ready,
            Downloading,
            Paused,
            Completed
        }

        public event Action<string> VerifyCallback;                   // path
        public event Action<int, UInt64> ReadyCallback;               // total count, total size
        public event Action<string, long, long, int> UpdateCallback;  // path, received bytes, total bytes, percentage
        public event Action<DownloadResult> FinishedCallback;         // result code

        // Save file-related constants.
        const string kRootPath = "client_data";
        const string kCachedFileName = "cached_files";
        const int kRetryCountMax = 3;


        // member variables.
        State state_ = State.None;
        string host_url_ = "";
        string target_path_ = "";
        string cur_download_path_ = "";
        int cur_download_count_ = 0;
        int total_download_count_ = 0;
        UInt64 cur_download_size_ = 0;
        UInt64 total_download_size_ = 0;
        int retry_download_count_ = 0;

        object lock_ = new object();
        WebClient web_client_ = new WebClient();
        List<DownloadFileInfo> cached_list_ = new List<DownloadFileInfo>();
        List<DownloadFileInfo> download_list_ = new List<DownloadFileInfo>();
        List<string> delete_file_list_ = new List<string>();
        Stopwatch download_time_ = new Stopwatch();

        // partial download-related variables
        bool partial_downloading_ = false;
        UInt64 partial_download_size_ = 0;
        List<DownloadFileInfo> partial_download_list_ = new List<DownloadFileInfo>();
    }
}
