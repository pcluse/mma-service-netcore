using System;
using System.Collections;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace MMAService
{
    public class LocalGroup
    {
        public static string AdminGroupName = RemoveDomain(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Translate(typeof(NTAccount)).Value);
        
        public static string AdminUserName;

        
        static LocalGroup() {
            AdminUserName = GetAdminUserName();
        }

        
        private static string RemoveDomain(string userOrGroup)
        {
            var index = userOrGroup.IndexOf('\\');
            if (index != -1)
            {
                return userOrGroup.Substring(index + 1);
            }
            return userOrGroup;
        }
        
        // https://www.pinvoke.net/default.aspx/netapi32.netlocalgroupgetmembers
        [DllImport("NetAPI32.dll", CharSet = CharSet.Unicode)]
        public extern static int NetLocalGroupGetMembers(
            [MarshalAs(UnmanagedType.LPWStr)] string servername,
            [MarshalAs(UnmanagedType.LPWStr)] string localgroupname,
            int level,
            out IntPtr bufptr,
            int prefmaxlen,
            out int entriesread,
            out int totalentries,
            IntPtr resume_handle);

        // https://www.pinvoke.net/default.aspx/advapi32.convertsidtostringsid
        [DllImport("advapi32", CharSet = CharSet.Auto, SetLastError = true)]
        static extern bool ConvertSidToStringSid(
            // [MarshalAs(UnmanagedType.LPArray)] byte[] pSID,
            IntPtr pSid,
            out IntPtr ptrSid);

        // https://www.pinvoke.net/default.aspx/kernel32.localfree
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr LocalFree(IntPtr hMem);

        // https://www.pinvoke.net/default.aspx/Enums.SID_NAME_USE
        public enum SID_NAME_USE
        {
            SidTypeUser = 1,
            SidTypeGroup,
            SidTypeDomain,
            SidTypeAlias,
            SidTypeWellKnownGroup,
            SidTypeDeletedAccount,
            SidTypeInvalid,
            SidTypeUnknown,
            SidTypeComputer
        }


        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct LOCALGROUP_MEMBERS_INFO_2
        {
            public IntPtr lgrmi2_sid;
            public SID_NAME_USE lgrmi2_sidusage;
            public string lgrmi2_domainandname;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct LOCALGROUP_MEMBERS_INFO_3 {
            public string lgrmi3_domainandname;
        }


        // https://www.pinvoke.net/default.aspx/netapi32.NetApiBufferFree
        [DllImport("Netapi32.dll", SetLastError = true)]
        static extern int NetApiBufferFree(IntPtr Buffer);

        public static string[] GetMembers(string GroupName)
        {
            var myList = new ArrayList();
            int EntriesRead;
            int TotalEntries;
            IntPtr Resume = IntPtr.Zero;
            IntPtr bufPtr;
            int val = NetLocalGroupGetMembers(null, GroupName, 3, out bufPtr, -1, out EntriesRead, out TotalEntries, Resume);
 
            var Names = new string[EntriesRead];
            if (EntriesRead > 0)
            {
                var Member = new LOCALGROUP_MEMBERS_INFO_3();
                IntPtr iter = bufPtr;
                var sizeOfStruct = Marshal.SizeOf(typeof(LOCALGROUP_MEMBERS_INFO_3));
                for (int i = 0; i < EntriesRead; i++)
                {
                    Member = (LOCALGROUP_MEMBERS_INFO_3)Marshal.PtrToStructure(iter, typeof(LOCALGROUP_MEMBERS_INFO_3));
                    iter = (IntPtr)(iter.ToInt64() + sizeOfStruct);
                    Names[i] = Member.lgrmi3_domainandname;
                }
                NetApiBufferFree(bufPtr);
            }
            return Names;
        }

        /*
         * https://support.microsoft.com/en-gb/help/243330/well-known-security-identifiers-in-windows-operating-systems
         * 
         * SID: S-1-5-21domain-500
         * Name: Administrator
         * Description: A user account for the system administrator.
         */
        private static string GetAdminUserName()
        {
            
            int EntriesRead;
            int TotalEntries;
            IntPtr Resume = IntPtr.Zero;
            IntPtr bufPtr;
            int val = NetLocalGroupGetMembers(null, AdminGroupName, 2, out bufPtr, -1, out EntriesRead, out TotalEntries, Resume);

            var Name = "";

            if (EntriesRead > 0)
            {
                var Member = new LOCALGROUP_MEMBERS_INFO_2();
                IntPtr iter = bufPtr;
                var sizeOfStruct = Marshal.SizeOf(typeof(LOCALGROUP_MEMBERS_INFO_2));
                for (int i = 0; i < EntriesRead; i++)
                {
                    Member = (LOCALGROUP_MEMBERS_INFO_2)Marshal.PtrToStructure(iter, typeof(LOCALGROUP_MEMBERS_INFO_2));
                    iter = (IntPtr)(iter.ToInt64() + sizeOfStruct);

                    // Console.WriteLine("{0} {1}", Member.lgrmi2_domainandname, Member.lgrmi2_sidusage);
                    if (Member.lgrmi2_sidusage == SID_NAME_USE.SidTypeUser)
                    {
                        if (ConvertSidToStringSid(Member.lgrmi2_sid, out IntPtr sidPtr))
                        {
                            var sid = Marshal.PtrToStringAuto(sidPtr);
                            // S-1-5-21domain-500
                            if (sid.StartsWith("S-1-5-21") && sid.EndsWith("-500"))
                            {
                                Name = Member.lgrmi2_domainandname;
                            }
                            LocalFree(sidPtr);
                        }
                    }
                }
                NetApiBufferFree(bufPtr);
            }
            return Name;
        }

        // https://www.pinvoke.net/default.aspx/netapi32.NetLocalGroupAddMembers
        [DllImport("NetApi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern Int32 NetLocalGroupAddMembers(
            string servername, //server name 
            string groupname, //group name 
            UInt32 level, //info level 
            ref LOCALGROUP_MEMBERS_INFO_3 buf, //Group info structure 
            UInt32 totalentries //number of entries 
        );

        public static int AddMember(string groupname, string user)
        {
            var buf = new LOCALGROUP_MEMBERS_INFO_3() { lgrmi3_domainandname = user };
            return NetLocalGroupAddMembers(null, groupname, 3, ref buf, 1);
        }

        // https://www.pinvoke.net/default.aspx/netapi32.NetLocalGroupDelMembers
        [DllImport("NetApi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern Int32 NetLocalGroupDelMembers(
            string servername,
            string groupname,
            UInt32 level,
            ref LOCALGROUP_MEMBERS_INFO_3 buf,
            UInt32 totalentries
        );

        public static int DeleteMember(string groupname, string user)
        {
            var buf = new LOCALGROUP_MEMBERS_INFO_3() { lgrmi3_domainandname = user };
            return NetLocalGroupDelMembers(null, groupname, 3, ref buf, 1);
        }

        /*
         * Never got code below to work. It only returned 1387 - objects not available
         
        // Try 1
        // https://docs.microsoft.com/en-us/windows/desktop/api/lmaccess/nf-lmaccess-netlocalgroupsetmembers
        [DllImport("NetApi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern Int32 NetLocalGroupSetMembers(
            string servername,
            string groupname,
            UInt32 level,
            ref LOCALGROUP_MEMBERS_INFO_3[] buf,
            UInt32 totalentries
        );

        public static int SetAdminAndGroupAsOnlyMembersOfAdminGroup(string group)
        {
            var buf = new LOCALGROUP_MEMBERS_INFO_3[2];
            buf[0].lgrmi3_domainandname = AdminUserName;
            buf[1].lgrmi3_domainandname = group;

            return NetLocalGroupSetMembers(null, AdminGroupName, 3, ref buf, 2);
        }

        // Try 2
        // https://docs.microsoft.com/en-us/windows/desktop/api/lmaccess/nf-lmaccess-netlocalgroupsetmembers
        [DllImport("NetApi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern Int32 NetLocalGroupSetMembers(
            string servername,
            string groupname,
            UInt32 level,
            ref LOCALGROUP_MEMBERS_INFO_3 buf,
            UInt32 totalentries
        );

        // https://www.pinvoke.net/default.aspx/netapi32.NetApiBufferAllocate
        [DllImport("netapi32.dll", SetLastError = true)]
        static extern int NetApiBufferAllocate(int ByteCount, out IntPtr Buffer);

        public static int SetAdminAndGroupAsOnlyMembersOfAdminGroup(string group)
        {
            // var buf_size = Marshal.SizeOf(typeof(LOCALGROUP_MEMBERS_INFO_3)) * 2; // 2 = antalet
            var sizeOfStruct = Marshal.SizeOf(typeof(LOCALGROUP_MEMBERS_INFO_3));
            IntPtr buf;
            NetApiBufferAllocate(sizeOfStruct * 2, out buf);
            var iter = buf;
            var Member = (LOCALGROUP_MEMBERS_INFO_3)Marshal.PtrToStructure(iter, typeof(LOCALGROUP_MEMBERS_INFO_3));
            Member.lgrmi3_domainandname = AdminUserName;
            iter = (IntPtr)(iter.ToInt64() + sizeOfStruct);
            Member = (LOCALGROUP_MEMBERS_INFO_3)Marshal.PtrToStructure(iter, typeof(LOCALGROUP_MEMBERS_INFO_3));
            Member.lgrmi3_domainandname = group;

            var buf2 = (LOCALGROUP_MEMBERS_INFO_3)Marshal.PtrToStructure(buf, typeof(LOCALGROUP_MEMBERS_INFO_3));
            var ret =  NetLocalGroupSetMembers(null, AdminGroupName, 3, ref buf2, 2);
            NetApiBufferFree(buf);
            return ret;
        }

        */

        public static bool IsAdmin(string user)
        {
            return GetMembers(AdminGroupName).Contains(user);
        }

        public static int AddToAdminGroup(string user)
        {
            return AddMember(AdminGroupName, user);
        }
    }
}
