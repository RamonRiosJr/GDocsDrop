using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace GDocsDrop
{
    public class HistoryEntry
    {
        public string OriginalPath { get; set; }
        public string DrivePath { get; set; }
        public string FileName { get; set; }
        public string Extension { get; set; }
        public string Sha256 { get; set; }
        public string FileId { get; set; }
        public string Email { get; set; }
        public string Url { get; set; }
        public string LastOpenedUtc { get; set; }
    }

    public class AppConfig
    {
        public string TargetFolderName { get; set; }
        public string CustomDriveRoot { get; set; }
    }

    public class DriveStatusInfo
    {
        public bool IsAvailable { get; set; }
        public string MyDriveRoot { get; set; }
        public string TargetFolder { get; set; }
        public string AccountEmail { get; set; }
        public bool IsDriveFsRunning { get; set; }
    }

    public class SyncResult
    {
        public bool Success { get; set; }
        public string SourcePath { get; set; }
        public string DrivePath { get; set; }
        public string FileId { get; set; }
        public string Md5 { get; set; }
        public string Email { get; set; }
        public string Url { get; set; }
        public string Sha256 { get; set; }
        public bool ReusedCache { get; set; }
        public bool AlreadyInDrive { get; set; }
        public long ElapsedMs { get; set; }
        public string ErrorMessage { get; set; }
        public List<string> TraceLog { get; set; }

        public SyncResult()
        {
            TraceLog = new List<string>();
        }
    }

    internal static class DriveSync
    {
        private const string DefaultTargetFolderName = "QuickOpen in Google Docs";
        private const int MaxHistoryEntries = 50;

        private static readonly Regex CloudIdRegex = new Regex("^[A-Za-z0-9_-]{20,}$", RegexOptions.Compiled);
        private static readonly Regex GuidRegex = new Regex("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$", RegexOptions.Compiled);
        private static readonly object HistoryLock = new object();

        public static string GetAppDataDir()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string dir = Path.Combine(localAppData, "GDocsDrop");
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            return dir;
        }

        public static string GetHistoryFilePath()
        {
            return Path.Combine(GetAppDataDir(), "history.json");
        }

        public static string GetConfigFilePath()
        {
            return Path.Combine(GetAppDataDir(), "config.json");
        }

        public static AppConfig LoadConfig()
        {
            string path = GetConfigFilePath();
            AppConfig cfg = null;
            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    JavaScriptSerializer ser = new JavaScriptSerializer();
                    cfg = ser.Deserialize<AppConfig>(json);
                }
                catch
                {
                    cfg = null;
                }
            }
            if (cfg == null)
            {
                cfg = new AppConfig();
            }
            if (string.IsNullOrEmpty(cfg.TargetFolderName))
            {
                cfg.TargetFolderName = DefaultTargetFolderName;
            }
            return cfg;
        }

        public static string FindMyDriveRoot()
        {
            AppConfig cfg = LoadConfig();
            if (!string.IsNullOrEmpty(cfg.CustomDriveRoot) && Directory.Exists(cfg.CustomDriveRoot))
            {
                return cfg.CustomDriveRoot.TrimEnd('\\');
            }

            if (Directory.Exists(@"G:\My Drive"))
            {
                return @"G:\My Drive";
            }

            try
            {
                DriveInfo[] drives = DriveInfo.GetDrives();
                for (int i = 0; i < drives.Length; i++)
                {
                    DriveInfo d = drives[i];
                    try
                    {
                        if (!d.IsReady)
                        {
                            continue;
                        }
                        string candidate = Path.Combine(d.RootDirectory.FullName, "My Drive");
                        if (!Directory.Exists(candidate))
                        {
                            continue;
                        }
                        string label = string.Empty;
                        try
                        {
                            label = d.VolumeLabel ?? string.Empty;
                        }
                        catch
                        {
                            label = string.Empty;
                        }
                        if (label.IndexOf("Google Drive", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return candidate;
                        }
                    }
                    catch
                    {
                    }
                }

                for (int i = 0; i < drives.Length; i++)
                {
                    DriveInfo d = drives[i];
                    try
                    {
                        if (!d.IsReady)
                        {
                            continue;
                        }
                        if (string.Equals(d.RootDirectory.FullName, @"C:\", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        string candidate = Path.Combine(d.RootDirectory.FullName, "My Drive");
                        if (Directory.Exists(candidate))
                        {
                            return candidate;
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        public static bool IsDriveFsRunning()
        {
            try
            {
                return Process.GetProcessesByName("GoogleDriveFS").Length > 0;
            }
            catch
            {
                return false;
            }
        }

        public static DriveStatusInfo GetDriveStatus()
        {
            DriveStatusInfo info = new DriveStatusInfo();
            info.IsDriveFsRunning = IsDriveFsRunning();
            string root = FindMyDriveRoot();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                info.IsAvailable = false;
                return info;
            }

            AppConfig cfg = LoadConfig();
            info.IsAvailable = true;
            info.MyDriveRoot = root;
            info.TargetFolder = Path.Combine(root, cfg.TargetFolderName);
            info.AccountEmail = DetectDriveEmail(root);
            return info;
        }

        private static string DetectDriveEmail(string myDriveRoot)
        {
            if (string.IsNullOrEmpty(myDriveRoot))
            {
                return null;
            }

            string email = NativeMethods.ReadAlternateDataStream(myDriveRoot, "user.drive.email");
            if (!string.IsNullOrEmpty(email))
            {
                return email;
            }

            try
            {
                string[] files = Directory.GetFiles(myDriveRoot);
                int limit = Math.Min(files.Length, 10);
                for (int i = 0; i < limit; i++)
                {
                    email = NativeMethods.ReadAlternateDataStream(files[i], "user.drive.email");
                    if (!string.IsNullOrEmpty(email))
                    {
                        return email;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        public static bool LaunchGoogleDriveIfInstalled()
        {
            try
            {
                string baseDir = @"C:\Program Files\Google\Drive File Stream";
                string launchBat = Path.Combine(baseDir, "launch.bat");
                if (File.Exists(launchBat))
                {
                    ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", string.Format("/c \"{0}\"", launchBat));
                    psi.CreateNoWindow = true;
                    psi.WindowStyle = ProcessWindowStyle.Hidden;
                    psi.UseShellExecute = false;
                    Process.Start(psi);
                    return true;
                }

                if (Directory.Exists(baseDir))
                {
                    string[] subDirs = Directory.GetDirectories(baseDir);
                    Array.Sort(subDirs, StringComparer.OrdinalIgnoreCase);
                    for (int i = subDirs.Length - 1; i >= 0; i--)
                    {
                        string exe = Path.Combine(subDirs[i], "GoogleDriveFS.exe");
                        if (File.Exists(exe))
                        {
                            ProcessStartInfo psi = new ProcessStartInfo(exe);
                            psi.UseShellExecute = true;
                            Process.Start(psi);
                            return true;
                        }
                    }
                }
            }
            catch
            {
            }
            return false;
        }

        public static string GetBadgeText(string extension)
        {
            string ext = (extension ?? string.Empty).ToLowerInvariant();
            if (ext == ".docx" || ext == ".doc" || ext == ".docm" || ext == ".dotx" || ext == ".odt" || ext == ".rtf")
            {
                return "DOC";
            }
            if (ext == ".xlsx" || ext == ".xls" || ext == ".xlsm" || ext == ".xltx" || ext == ".ods")
            {
                return "XLS";
            }
            if (ext == ".pptx" || ext == ".ppt" || ext == ".pptm" || ext == ".potx" || ext == ".odp")
            {
                return "PPT";
            }
            return "DRV";
        }

        public static string BuildGoogleUrl(string fileId, string extension, string email)
        {
            string ext = (extension ?? string.Empty).ToLowerInvariant();
            string url;
            if (ext == ".docx" || ext == ".doc" || ext == ".docm" || ext == ".dotx" || ext == ".odt")
            {
                url = string.Format("https://docs.google.com/document/d/{0}/edit", fileId);
            }
            else if (ext == ".xlsx" || ext == ".xls" || ext == ".xlsm" || ext == ".xltx" || ext == ".ods")
            {
                url = string.Format("https://docs.google.com/spreadsheets/d/{0}/edit", fileId);
            }
            else if (ext == ".pptx" || ext == ".ppt" || ext == ".pptm" || ext == ".potx" || ext == ".odp")
            {
                url = string.Format("https://docs.google.com/presentation/d/{0}/edit", fileId);
            }
            else
            {
                url = string.Format("https://drive.google.com/file/d/{0}/view", fileId);
            }

            if (!string.IsNullOrEmpty(email))
            {
                url = string.Format("{0}?authuser={1}", url, Uri.EscapeDataString(email));
            }
            return url;
        }

        public static bool IsValidCloudId(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return false;
            }
            string trimmed = id.Trim();
            if (trimmed.Length < 20)
            {
                return false;
            }
            if (trimmed.StartsWith("local", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (GuidRegex.IsMatch(trimmed))
            {
                return false;
            }
            return CloudIdRegex.IsMatch(trimmed);
        }

        public static bool IsPathInsideGoogleDrive(string fullPath, string myDriveRoot)
        {
            if (string.IsNullOrEmpty(fullPath) || string.IsNullOrEmpty(myDriveRoot))
            {
                return false;
            }

            string normalizedFile = Path.GetFullPath(fullPath);
            string normalizedDrive = Path.GetFullPath(myDriveRoot).TrimEnd('\\') + "\\";
            if (normalizedFile.StartsWith(normalizedDrive, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string driveVolumeRoot = Path.GetPathRoot(myDriveRoot);
            if (!string.IsNullOrEmpty(driveVolumeRoot) &&
                !string.Equals(driveVolumeRoot, @"C:\", StringComparison.OrdinalIgnoreCase) &&
                normalizedFile.StartsWith(driveVolumeRoot, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        public static string ComputeSha256(string filePath)
        {
            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(fs);
                char[] chars = new char[hash.Length * 2];
                const string hex = "0123456789abcdef";
                for (int i = 0; i < hash.Length; i++)
                {
                    chars[i * 2] = hex[(hash[i] >> 4) & 0xF];
                    chars[i * 2 + 1] = hex[hash[i] & 0xF];
                }
                return new string(chars);
            }
        }

        public static List<HistoryEntry> LoadHistory()
        {
            lock (HistoryLock)
            {
                string path = GetHistoryFilePath();
                if (!File.Exists(path))
                {
                    return new List<HistoryEntry>();
                }
                try
                {
                    string json = File.ReadAllText(path);
                    JavaScriptSerializer ser = new JavaScriptSerializer();
                    List<HistoryEntry> list = ser.Deserialize<List<HistoryEntry>>(json);
                    return list ?? new List<HistoryEntry>();
                }
                catch
                {
                    return new List<HistoryEntry>();
                }
            }
        }

        public static void SaveHistory(List<HistoryEntry> entries)
        {
            lock (HistoryLock)
            {
                try
                {
                    JavaScriptSerializer ser = new JavaScriptSerializer();
                    string json = ser.Serialize(entries);
                    File.WriteAllText(GetHistoryFilePath(), json);
                }
                catch
                {
                }
            }
        }

        private static void RecordHistory(HistoryEntry item)
        {
            lock (HistoryLock)
            {
                List<HistoryEntry> list = LoadHistory();
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    HistoryEntry existing = list[i];
                    if (string.Equals(existing.DrivePath, item.DrivePath, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(item.Sha256) && string.Equals(existing.Sha256, item.Sha256, StringComparison.OrdinalIgnoreCase)))
                    {
                        list.RemoveAt(i);
                    }
                }
                list.Insert(0, item);
                if (list.Count > MaxHistoryEntries)
                {
                    list.RemoveRange(MaxHistoryEntries, list.Count - MaxHistoryEntries);
                }
                SaveHistory(list);
            }
        }

        private static string GetUniqueTargetPath(string targetFolder, string fileName)
        {
            string stem = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);
            string stamp = DateTime.Now.ToString("yyyy-MM-dd HHmmss");
            string candidate = Path.Combine(targetFolder, string.Format("{0} ({1}){2}", stem, stamp, ext));
            int counter = 1;
            while (File.Exists(candidate))
            {
                candidate = Path.Combine(targetFolder, string.Format("{0} ({1}_{2}){3}", stem, stamp, counter, ext));
                counter++;
            }
            return candidate;
        }

        private static bool WaitForDriveSync(
            string drivePath,
            long fileSizeBytes,
            Action<string> onStatus,
            List<string> trace,
            out string fileId,
            out string md5,
            out string email)
        {
            fileId = null;
            md5 = null;
            email = null;

            int timeoutMs = fileSizeBytes > (10L * 1024L * 1024L) ? 60000 : 45000;
            Stopwatch sw = Stopwatch.StartNew();
            string lastSeenState = null;
            int attempt = 0;

            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                attempt++;
                string currentId = NativeMethods.ReadAlternateDataStream(drivePath, "user.drive.id");
                string currentMd5 = NativeMethods.ReadAlternateDataStream(drivePath, "user.drive.md5");
                string currentEmail = NativeMethods.ReadAlternateDataStream(drivePath, "user.drive.email");

                string stateKey = string.Format("id={0}|md5={1}|email={2}",
                    currentId ?? "<null>",
                    currentMd5 ?? "<null>",
                    currentEmail ?? "<null>");

                if (!string.Equals(stateKey, lastSeenState, StringComparison.Ordinal))
                {
                    trace.Add(string.Format("[{0}ms poll #{1}] {2}", sw.ElapsedMilliseconds, attempt, stateKey));
                    lastSeenState = stateKey;
                }

                if (IsValidCloudId(currentId))
                {
                    Thread.Sleep(300);
                    string confirmId = NativeMethods.ReadAlternateDataStream(drivePath, "user.drive.id");
                    string confirmMd5 = NativeMethods.ReadAlternateDataStream(drivePath, "user.drive.md5");
                    string confirmEmail = NativeMethods.ReadAlternateDataStream(drivePath, "user.drive.email");

                    trace.Add(string.Format("[{0}ms confirm] id={1}|md5={2}|email={3}",
                        sw.ElapsedMilliseconds,
                        confirmId ?? "<null>",
                        confirmMd5 ?? "<null>",
                        confirmEmail ?? "<null>"));

                    if (IsValidCloudId(confirmId) && string.Equals(confirmId, currentId, StringComparison.Ordinal))
                    {
                        fileId = confirmId;
                        md5 = confirmMd5;
                        email = confirmEmail;
                        return true;
                    }
                }

                if (onStatus != null && attempt % 4 == 1)
                {
                    int secs = (int)(sw.ElapsedMilliseconds / 1000L);
                    if (secs >= 2)
                    {
                        onStatus(string.Format("Waiting for Google Drive sync... ({0}s)", secs));
                    }
                }

                Thread.Sleep(250);
            }

            return false;
        }

        public static SyncResult OpenInGoogleDocs(string rawPath, bool dryRun, Action<string> onStatus)
        {
            Stopwatch totalSw = Stopwatch.StartNew();
            SyncResult result = new SyncResult();

            try
            {
                if (string.IsNullOrEmpty(rawPath))
                {
                    result.ErrorMessage = "No file path was provided.";
                    return result;
                }

                string sourcePath = Path.GetFullPath(rawPath.Trim().Trim('"'));
                result.SourcePath = sourcePath;
                result.TraceLog.Add("source=" + sourcePath);

                if (Directory.Exists(sourcePath))
                {
                    result.ErrorMessage = "Please drop individual files rather than folders.";
                    return result;
                }

                if (!File.Exists(sourcePath))
                {
                    result.ErrorMessage = "File not found: " + sourcePath;
                    return result;
                }

                DriveStatusInfo driveInfo = GetDriveStatus();
                if (!driveInfo.IsAvailable)
                {
                    result.ErrorMessage = "Google Drive for Desktop (G:\\My Drive) was not found. Start Google Drive and retry.";
                    return result;
                }

                result.TraceLog.Add("myDriveRoot=" + driveInfo.MyDriveRoot);
                result.TraceLog.Add("driveFsRunning=" + driveInfo.IsDriveFsRunning);

                string ext = Path.GetExtension(sourcePath);
                long fileSize = new FileInfo(sourcePath).Length;
                string sha256 = ComputeSha256(sourcePath);
                result.Sha256 = sha256;
                result.TraceLog.Add("sha256=" + sha256);

                string drivePath = null;
                bool alreadyInDrive = IsPathInsideGoogleDrive(sourcePath, driveInfo.MyDriveRoot);
                bool reusedCache = false;

                if (alreadyInDrive)
                {
                    drivePath = sourcePath;
                    result.AlreadyInDrive = true;
                    result.TraceLog.Add("mode=already_in_drive");
                    if (onStatus != null)
                    {
                        onStatus("Reading Google Drive link...");
                    }
                }
                else
                {
                    if (onStatus != null)
                    {
                        onStatus("Checking Google Drive cache...");
                    }

                    List<HistoryEntry> history = LoadHistory();
                    for (int i = 0; i < history.Count; i++)
                    {
                        HistoryEntry h = history[i];
                        if (!string.IsNullOrEmpty(h.Sha256) &&
                            string.Equals(h.Sha256, sha256, StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrEmpty(h.DrivePath) &&
                            File.Exists(h.DrivePath))
                        {
                            string cachedId = NativeMethods.ReadAlternateDataStream(h.DrivePath, "user.drive.id");
                            if (IsValidCloudId(cachedId))
                            {
                                drivePath = h.DrivePath;
                                reusedCache = true;
                                result.TraceLog.Add("mode=cache_hit_history drivePath=" + drivePath);
                                break;
                            }
                        }
                    }

                    if (drivePath == null)
                    {
                        if (!Directory.Exists(driveInfo.TargetFolder))
                        {
                            Directory.CreateDirectory(driveInfo.TargetFolder);
                        }

                        string fileName = Path.GetFileName(sourcePath);
                        string candidatePath = Path.Combine(driveInfo.TargetFolder, fileName);

                        if (File.Exists(candidatePath))
                        {
                            string existingSha = ComputeSha256(candidatePath);
                            string existingId = NativeMethods.ReadAlternateDataStream(candidatePath, "user.drive.id");
                            if (string.Equals(existingSha, sha256, StringComparison.OrdinalIgnoreCase) && IsValidCloudId(existingId))
                            {
                                drivePath = candidatePath;
                                reusedCache = true;
                                result.TraceLog.Add("mode=cache_hit_target_folder drivePath=" + drivePath);
                            }
                            else
                            {
                                candidatePath = GetUniqueTargetPath(driveInfo.TargetFolder, fileName);
                                result.TraceLog.Add("mode=copy_renamed dest=" + candidatePath);
                            }
                        }
                        else
                        {
                            result.TraceLog.Add("mode=copy_new dest=" + candidatePath);
                        }

                        if (!reusedCache)
                        {
                            if (onStatus != null)
                            {
                                onStatus("Syncing to Google Drive...");
                            }
                            File.Copy(sourcePath, candidatePath, false);
                            drivePath = candidatePath;
                        }
                    }
                }

                result.DrivePath = drivePath;
                result.ReusedCache = reusedCache;

                string fileId;
                string md5;
                string email;
                bool synced = WaitForDriveSync(drivePath, fileSize, onStatus, result.TraceLog, out fileId, out md5, out email);

                if (!synced)
                {
                    result.ErrorMessage = driveInfo.IsDriveFsRunning
                        ? "Timed out waiting for Google Drive to assign a cloud ID. Check Google Drive sync status."
                        : "Google Drive for Desktop is not running. Start Google Drive and click Retry.";
                    return result;
                }

                if (string.IsNullOrEmpty(email))
                {
                    email = driveInfo.AccountEmail;
                }

                string url = BuildGoogleUrl(fileId, ext, email);
                result.FileId = fileId;
                result.Md5 = md5;
                result.Email = email;
                result.Url = url;
                result.Success = true;
                result.TraceLog.Add("finalUrl=" + url);

                HistoryEntry entry = new HistoryEntry();
                entry.OriginalPath = sourcePath;
                entry.DrivePath = drivePath;
                entry.FileName = Path.GetFileName(drivePath);
                entry.Extension = ext.ToLowerInvariant();
                entry.Sha256 = sha256;
                entry.FileId = fileId;
                entry.Email = email ?? string.Empty;
                entry.Url = url;
                entry.LastOpenedUtc = DateTime.UtcNow.ToString("o");
                RecordHistory(entry);

                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                result.TraceLog.Add("exception=" + ex.ToString());
                return result;
            }
            finally
            {
                result.ElapsedMs = totalSw.ElapsedMilliseconds;
            }
        }

        public static void OpenUrlInBrowser(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return;
            }
            ProcessStartInfo psi = new ProcessStartInfo(url);
            psi.UseShellExecute = true;
            Process.Start(psi);
        }
    }
}
