using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace GDocsDrop
{
    internal static class FileAssociations
    {
        private class ExtensionGroup
        {
            public string ProgId;
            public string FriendlyTypeName;
            public string IconFileName;
            public string[] Extensions;
        }

        private static readonly ExtensionGroup[] Groups = new ExtensionGroup[]
        {
            new ExtensionGroup
            {
                ProgId = "GDocsDrop.Word",
                FriendlyTypeName = "Document (Google Docs)",
                IconFileName = "docs.ico",
                Extensions = new string[] { ".docx", ".doc", ".docm", ".dotx", ".odt", ".rtf" }
            },
            new ExtensionGroup
            {
                ProgId = "GDocsDrop.Sheet",
                FriendlyTypeName = "Spreadsheet (Google Sheets)",
                IconFileName = "sheets.ico",
                Extensions = new string[] { ".xlsx", ".xls", ".xlsm", ".xltx", ".ods" }
            },
            new ExtensionGroup
            {
                ProgId = "GDocsDrop.Slide",
                FriendlyTypeName = "Presentation (Google Slides)",
                IconFileName = "slides.ico",
                Extensions = new string[] { ".pptx", ".ppt", ".pptm", ".potx", ".odp" }
            }
        };

        public static string GetExecutablePath()
        {
            return System.Reflection.Assembly.GetExecutingAssembly().Location;
        }

        public static string ResolveIconPath(string iconFileName)
        {
            string appDir = Path.GetDirectoryName(GetExecutablePath());
            string localIcon = Path.Combine(Path.Combine(appDir, "icons"), iconFileName);
            if (File.Exists(localIcon))
            {
                return localIcon;
            }
            string driveIcon = Path.Combine(@"C:\Program Files\Google\Drive File Stream", iconFileName);
            if (File.Exists(driveIcon))
            {
                return driveIcon;
            }
            return GetExecutablePath();
        }

        private static bool HasNonOwnedUserChoice(string ext)
        {
            try
            {
                string keyPath = string.Format(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{0}\UserChoice", ext);
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(keyPath, false))
                {
                    if (key == null)
                    {
                        return false;
                    }
                    object val = key.GetValue("ProgId");
                    string progId = val != null ? val.ToString() : string.Empty;
                    if (!string.IsNullOrEmpty(progId) && !progId.StartsWith("GDocsDrop.", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }
            return false;
        }

        public static void RegisterAll()
        {
            RegisterAll(false);
        }

        public static void RegisterAll(bool force)
        {
            string exePath = GetExecutablePath();
            string openCommand = string.Format("\"{0}\" \"%1\"", exePath);
            List<string> allExtensions = new List<string>();

            for (int g = 0; g < Groups.Length; g++)
            {
                ExtensionGroup grp = Groups[g];
                string iconPath = ResolveIconPath(grp.IconFileName);

                using (RegistryKey progKey = Registry.CurrentUser.CreateSubKey(string.Format(@"Software\Classes\{0}", grp.ProgId)))
                {
                    if (progKey != null)
                    {
                        progKey.SetValue(null, grp.FriendlyTypeName);
                        progKey.SetValue("FriendlyTypeName", grp.FriendlyTypeName);

                        using (RegistryKey iconKey = progKey.CreateSubKey("DefaultIcon"))
                        {
                            if (iconKey != null)
                            {
                                iconKey.SetValue(null, string.Format("{0},0", iconPath));
                            }
                        }

                        using (RegistryKey cmdKey = progKey.CreateSubKey(@"shell\open\command"))
                        {
                            if (cmdKey != null)
                            {
                                cmdKey.SetValue(null, openCommand);
                            }
                        }
                    }
                }

                for (int e = 0; e < grp.Extensions.Length; e++)
                {
                    string ext = grp.Extensions[e];
                    allExtensions.Add(ext);

                    using (RegistryKey extKey = Registry.CurrentUser.CreateSubKey(string.Format(@"Software\Classes\{0}", ext)))
                    {
                        if (extKey != null)
                        {
                            if (force || !HasNonOwnedUserChoice(ext))
                            {
                                extKey.SetValue(null, grp.ProgId);
                            }

                            using (RegistryKey openWithKey = extKey.CreateSubKey("OpenWithProgids"))
                            {
                                if (openWithKey != null)
                                {
                                    openWithKey.SetValue(grp.ProgId, string.Empty);
                                }
                            }
                        }
                    }
                }
            }

            string appName = Path.GetFileName(exePath);
            using (RegistryKey appKey = Registry.CurrentUser.CreateSubKey(string.Format(@"Software\Classes\Applications\{0}", appName)))
            {
                if (appKey != null)
                {
                    appKey.SetValue("FriendlyAppName", "GDocsDrop (Google Docs)");
                    using (RegistryKey cmdKey = appKey.CreateSubKey(@"shell\open\command"))
                    {
                        if (cmdKey != null)
                        {
                            cmdKey.SetValue(null, openCommand);
                        }
                    }
                    using (RegistryKey supKey = appKey.CreateSubKey("SupportedTypes"))
                    {
                        if (supKey != null)
                        {
                            for (int i = 0; i < allExtensions.Count; i++)
                            {
                                supKey.SetValue(allExtensions[i], string.Empty);
                            }
                        }
                    }
                }
            }

            NativeMethods.NotifyAssocChanged();
        }

        public static void UnregisterAll()
        {
            for (int g = 0; g < Groups.Length; g++)
            {
                ExtensionGroup grp = Groups[g];
                try
                {
                    Registry.CurrentUser.DeleteSubKeyTree(string.Format(@"Software\Classes\{0}", grp.ProgId), false);
                }
                catch
                {
                }

                for (int e = 0; e < grp.Extensions.Length; e++)
                {
                    string ext = grp.Extensions[e];
                    string extSubPath = string.Format(@"Software\Classes\{0}", ext);
                    try
                    {
                        using (RegistryKey extKey = Registry.CurrentUser.OpenSubKey(extSubPath, true))
                        {
                            if (extKey == null)
                            {
                                continue;
                            }

                            object cur = extKey.GetValue(null);
                            if (cur != null && string.Equals(cur.ToString(), grp.ProgId, StringComparison.OrdinalIgnoreCase))
                            {
                                extKey.DeleteValue(string.Empty, false);
                            }

                            using (RegistryKey openWithKey = extKey.OpenSubKey("OpenWithProgids", true))
                            {
                                if (openWithKey != null)
                                {
                                    openWithKey.DeleteValue(grp.ProgId, false);
                                    if (openWithKey.ValueCount == 0 && openWithKey.SubKeyCount == 0)
                                    {
                                        extKey.DeleteSubKey("OpenWithProgids", false);
                                    }
                                }
                            }

                            if (extKey.ValueCount == 0 && extKey.SubKeyCount == 0)
                            {
                                Registry.CurrentUser.DeleteSubKey(extSubPath, false);
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }

            try
            {
                string appName = Path.GetFileName(GetExecutablePath());
                Registry.CurrentUser.DeleteSubKeyTree(string.Format(@"Software\Classes\Applications\{0}", appName), false);
            }
            catch
            {
            }

            NativeMethods.NotifyAssocChanged();
        }

        public static bool IsRegistered()
        {
            try
            {
                using (RegistryKey extKey = Registry.CurrentUser.OpenSubKey(@"Software\Classes\.docx", false))
                {
                    if (extKey == null)
                    {
                        return false;
                    }
                    object val = extKey.GetValue(null);
                    if (val == null || !string.Equals(val.ToString(), "GDocsDrop.Word", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }
                using (RegistryKey cmdKey = Registry.CurrentUser.OpenSubKey(@"Software\Classes\GDocsDrop.Word\shell\open\command", false))
                {
                    return cmdKey != null && cmdKey.GetValue(null) != null;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
