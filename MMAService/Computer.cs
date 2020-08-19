using System;
using System.Collections.Generic;
using System.Management;
using System.Runtime.InteropServices;

namespace MMAService
{
    public class Computer
    {
        // https://stackoverflow.com/questions/132620/how-do-you-retrieve-a-list-of-logged-in-connected-users-in-net

        [DllImport("wtsapi32.dll")]
        static extern IntPtr WTSOpenServer([MarshalAs(UnmanagedType.LPStr)] String pServerName);

        [DllImport("wtsapi32.dll")]
        static extern void WTSCloseServer(IntPtr hServer);

        [DllImport("wtsapi32.dll")]
        static extern Int32 WTSEnumerateSessions(
            IntPtr hServer,
            [MarshalAs(UnmanagedType.U4)] Int32 Reserved,
            [MarshalAs(UnmanagedType.U4)] Int32 Version,
            ref IntPtr ppSessionInfo,
            [MarshalAs(UnmanagedType.U4)] ref Int32 pCount);

        [DllImport("wtsapi32.dll")]
        static extern void WTSFreeMemory(IntPtr pMemory);

        [DllImport("Wtsapi32.dll")]
        static extern bool WTSQuerySessionInformation(
            System.IntPtr hServer, int sessionId, WTS_INFO_CLASS wtsInfoClass, out System.IntPtr ppBuffer, out uint pBytesReturned);

        [StructLayout(LayoutKind.Sequential)]
        private struct WTS_SESSION_INFO
        {
            public Int32 SessionID;

            [MarshalAs(UnmanagedType.LPStr)]
            public String pWinStationName;

            public WTS_CONNECTSTATE_CLASS State;
        }

        public enum WTS_INFO_CLASS
        {
            WTSInitialProgram,
            WTSApplicationName,
            WTSWorkingDirectory,
            WTSOEMId,
            WTSSessionId,
            WTSUserName,
            WTSWinStationName,
            WTSDomainName,
            WTSConnectState,
            WTSClientBuildNumber,
            WTSClientName,
            WTSClientDirectory,
            WTSClientProductId,
            WTSClientHardwareId,
            WTSClientAddress,
            WTSClientDisplay,
            WTSClientProtocolType
        }
        public enum WTS_CONNECTSTATE_CLASS
        {
            WTSActive,
            WTSConnected,
            WTSConnectQuery,
            WTSShadow,
            WTSDisconnected,
            WTSIdle,
            WTSListen,
            WTSReset,
            WTSDown,
            WTSInit
        }

        // @TODO rewrite using cim over win32_loggedinusers???
        public static List<string> GetLoggedInUsers()
        {
            var userList = new List<string>();
            var serverHandle = WTSOpenServer(Environment.MachineName); ;

            try
            {
                IntPtr SessionInfoPtr = IntPtr.Zero;
                IntPtr userPtr = IntPtr.Zero;
                IntPtr domainPtr = IntPtr.Zero;
                Int32 sessionCount = 0;
                Int32 retVal = WTSEnumerateSessions(serverHandle, 0, 1, ref SessionInfoPtr, ref sessionCount);
                Int32 dataSize = Marshal.SizeOf(typeof(WTS_SESSION_INFO));
                IntPtr currentSession = SessionInfoPtr;
                uint bytes = 0;

                if (retVal != 0)
                {
                    for (int i = 0; i < sessionCount; i++)
                    {
                        WTS_SESSION_INFO si = (WTS_SESSION_INFO)Marshal.PtrToStructure((System.IntPtr)currentSession, typeof(WTS_SESSION_INFO));
                        currentSession += dataSize;

                        // Only active sessions
                        // Tested with two logged in users. One had state active and the other disconnected
                        // But other session have disconnected but only real accounts
                        // return username and domainname (JD)
                        
                        WTSQuerySessionInformation(serverHandle, si.SessionID, WTS_INFO_CLASS.WTSUserName, out userPtr, out bytes);
                        WTSQuerySessionInformation(serverHandle, si.SessionID, WTS_INFO_CLASS.WTSDomainName, out domainPtr, out bytes);

                        var user = Marshal.PtrToStringAnsi(domainPtr) + "\\" + Marshal.PtrToStringAnsi(userPtr);
                        if (user != "\\")
                        {
                            userList.Add(user);
                        }
                        // Console.WriteLine("{0} state {1}", user, si.State);
                        WTSFreeMemory(userPtr);
                        WTSFreeMemory(domainPtr);
                        
                    }
                    WTSFreeMemory(SessionInfoPtr);
                }
            }
            finally
            {
                WTSCloseServer(serverHandle);

            }
            return userList;
        }

        // https://docs.microsoft.com/sv-se/windows/desktop/DMWmiBridgeProv/mdm-sharedpc
        public static bool IsSharedPC()
        {
            var result = false;

            var scope = new ManagementScope(@"\\.\root\cimv2\mdm\dmmap");
            // Define the query for shared pc mode
            var query = new ObjectQuery("SELECT * FROM MDM_SharedPC");
            // create the search for shared pc mode
            using (var searcher = new ManagementObjectSearcher(scope, query)) {
                foreach (ManagementObject row in searcher.Get())
                {
                    var value = row["EnableSharedPCMode"];
                    if (value != null)
                    {
                        if ((bool)value)
                        {
                            result = true;
                        }
                    }
                }
            }
            return result;
        }

        public static List<string> GetPrimaryUsers()
        {
            var users = new List<string>();
            var scope = new ManagementScope(@"\\.\root\ccm\Policy\Machine");
            // Define the query for shared pc mode
            var query = new ObjectQuery("SELECT * FROM CCM_UserAffinity");
            // create the search for shared pc mode
            using (var searcher = new ManagementObjectSearcher(scope, query)) {
                foreach (ManagementObject row in searcher.Get())
                {
                    users.Add(((string)row["ConsoleUser"]).ToUpper());
                }
            }
            return users;
        }
    }
}

