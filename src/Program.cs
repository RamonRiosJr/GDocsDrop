using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace GDocsDrop
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            NativeMethods.EnableDpiAwareness();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool register = false;
            bool unregister = false;
            bool dryRun = false;
            string testOutPath = null;
            List<string> fileArgs = new List<string>();

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (string.Equals(a, "--register", StringComparison.OrdinalIgnoreCase))
                {
                    register = true;
                }
                else if (string.Equals(a, "--unregister", StringComparison.OrdinalIgnoreCase))
                {
                    unregister = true;
                }
                else if (string.Equals(a, "--dry-run", StringComparison.OrdinalIgnoreCase))
                {
                    dryRun = true;
                }
                else if (string.Equals(a, "--test-out", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length)
                    {
                        testOutPath = args[++i];
                    }
                }
                else if (!a.StartsWith("--", StringComparison.Ordinal))
                {
                    fileArgs.Add(a);
                }
            }

            if (register)
            {
                FileAssociations.RegisterAll();
                return 0;
            }

            if (unregister)
            {
                FileAssociations.UnregisterAll();
                return 0;
            }

            if (dryRun)
            {
                return RunDryRun(fileArgs, testOutPath);
            }

            if (fileArgs.Count > 0)
            {
                Application.Run(new QuickToastForm(fileArgs.ToArray()));
                return 0;
            }

            Application.Run(new DropWindow());
            return 0;
        }

        private static int RunDryRun(List<string> files, string testOutPath)
        {
            List<SyncResult> results = new List<SyncResult>();
            bool allOk = files.Count > 0;

            for (int i = 0; i < files.Count; i++)
            {
                SyncResult res = DriveSync.OpenInGoogleDocs(files[i], true, null);
                results.Add(res);
                if (!res.Success)
                {
                    allOk = false;
                }
            }

            if (!string.IsNullOrEmpty(testOutPath))
            {
                try
                {
                    JavaScriptSerializer ser = new JavaScriptSerializer();
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine("=== GDocsDrop Dry-Run Verification ===");
                    sb.AppendLine("TimestampUtc: " + DateTime.UtcNow.ToString("o"));
                    sb.AppendLine("FileCount: " + files.Count);
                    sb.AppendLine("AllSucceeded: " + allOk);
                    sb.AppendLine();
                    sb.AppendLine("JSON:");
                    sb.AppendLine(ser.Serialize(results));
                    sb.AppendLine();
                    for (int i = 0; i < results.Count; i++)
                    {
                        SyncResult r = results[i];
                        sb.AppendLine(string.Format("--- Result #{0} ---", i + 1));
                        sb.AppendLine("Success: " + r.Success);
                        sb.AppendLine("SourcePath: " + (r.SourcePath ?? string.Empty));
                        sb.AppendLine("DrivePath: " + (r.DrivePath ?? string.Empty));
                        sb.AppendLine("AlreadyInDrive: " + r.AlreadyInDrive);
                        sb.AppendLine("ReusedCache: " + r.ReusedCache);
                        sb.AppendLine("Sha256: " + (r.Sha256 ?? string.Empty));
                        sb.AppendLine("FileId: " + (r.FileId ?? string.Empty));
                        sb.AppendLine("Md5: " + (r.Md5 ?? string.Empty));
                        sb.AppendLine("Email: " + (r.Email ?? string.Empty));
                        sb.AppendLine("Url: " + (r.Url ?? string.Empty));
                        sb.AppendLine("ElapsedMs: " + r.ElapsedMs);
                        if (!string.IsNullOrEmpty(r.ErrorMessage))
                        {
                            sb.AppendLine("Error: " + r.ErrorMessage);
                        }
                        sb.AppendLine("Trace:");
                        for (int t = 0; t < r.TraceLog.Count; t++)
                        {
                            sb.AppendLine("  " + r.TraceLog[t]);
                        }
                    }

                    string outDir = Path.GetDirectoryName(Path.GetFullPath(testOutPath));
                    if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
                    {
                        Directory.CreateDirectory(outDir);
                    }
                    File.WriteAllText(testOutPath, sb.ToString(), Encoding.UTF8);
                }
                catch
                {
                }
            }

            return allOk ? 0 : 1;
        }
    }
}
