using System;
using System.DirectoryServices.AccountManagement;
using System.Runtime.InteropServices;

namespace RevitApi_3
{
    internal static class WindowsUserHelper
    {
        [DllImport("Netapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int NetGetAnyDCName(string serverName, string domainName, out IntPtr bufptr);

        [DllImport("Netapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int NetUserGetInfo(string servername, string username, int level, out IntPtr bufptr);

        [DllImport("Netapi32.dll")]
        private static extern int NetApiBufferFree(IntPtr Buffer);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct USER_INFO_10
        {
            public string usri10_name;
            public string usri10_comment;
            public string usri10_usr_comment;
            public string usri10_full_name;
        }

        public static string GetFullName()
        {
            // 1) Самый простой и “чистый” путь
            try
            {
                var up = UserPrincipal.Current;
                if (up != null && !string.IsNullOrWhiteSpace(up.DisplayName))
                    return up.DisplayName.Trim();
            }
            catch { /* ignore */ }

            // 2) P/Invoke NetUserGetInfo (аналог win32net)
            try
            {
                string user = Environment.UserName;

                IntPtr pDc = IntPtr.Zero;
                string dcName = null;

                // dc может не найтись — тогда просто пробуем servername=null
                int resDc = NetGetAnyDCName(null, null, out pDc);
                if (resDc == 0 && pDc != IntPtr.Zero)
                {
                    dcName = Marshal.PtrToStringUni(pDc);
                    NetApiBufferFree(pDc);
                }

                IntPtr pInfo = IntPtr.Zero;
                int res = NetUserGetInfo(dcName, user, 10, out pInfo);
                if (res == 0 && pInfo != IntPtr.Zero)
                {
                    var info = (USER_INFO_10)Marshal.PtrToStructure(pInfo, typeof(USER_INFO_10));
                    NetApiBufferFree(pInfo);

                    if (!string.IsNullOrWhiteSpace(info.usri10_full_name))
                        return info.usri10_full_name.Trim();
                }
            }
            catch { /* ignore */ }

            // 3) Последний fallback
            return Environment.UserName;
        }
    }
}
