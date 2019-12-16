using System;
using System.Collections.Generic;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;

/*
 MMAClientEnabled	0=Av, 1=På
 MMALocalAdminGroup	Grupp (endast en) som ska vara lokaladmin på datorn t.ex. “Domain\Group”
 MMAServer	Adress för webbservice som verifierar behörighet och tvåfaktor t.ex. “verygoodserver.com/util/makemeadmin”, programmet lägger till https: före
 MMAServerThumbprint	Tumavtryck för certifikatet på servern ovan för att vara säker på att man pratar med rätt server
*/

namespace MMAService
{
    public class CCMCollectionVariables
    {
        private struct CCMCollectionVariable
        {
            public string Value;
            public string protectedValue;

            public CCMCollectionVariable(string Value, string protectedValue = null)
            {
                this.Value = Value;
                this.protectedValue = protectedValue;
            }
        }

        private readonly ManagementScope Scope = new ManagementScope(@"\\.\root\ccm\Policy\Machine\ActualConfig");
        private Dictionary<string, CCMCollectionVariable> Variables;
        private bool isTest = false;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DATA_BLOB
        {
            public int cbData;
            public System.IntPtr pbData;
        }

        [DllImport("Crypt32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CryptUnprotectData(
            ref DATA_BLOB pDataIn,
            String szDataDescr,
            ref int pOptionalEntropy,
            IntPtr pvReserved,
            ref int pPromptStruct,
            int dwFlags,
            ref DATA_BLOB pDataOut
        );

        public CCMCollectionVariables(bool isTest)
        {
            this.isTest = isTest;
            if (!isTest)
            {
                Variables = new Dictionary<string, CCMCollectionVariable>();

            }
            else
            {
                Variables = new Dictionary<string, CCMCollectionVariable>()
                {
                    ["MMAEnabled"] = new CCMCollectionVariable("1"),
                    ["MMALocalAdminGroup"] = new CCMCollectionVariable(@"DOMAIN\GROUP"),
                    ["MMAServer"] = new CCMCollectionVariable("verygoodserver.com"),
                };
            }
        }

        private string GetProtectedValue(string name) {
            // Define the query for collection variables
            var query = new ObjectQuery(String.Format("SELECT * FROM CCM_CollectionVariable WHERE Name = \"{0}\"", name));
            // create the search for collection variables
            var searcher = new ManagementObjectSearcher(Scope, query);

            try
            {
                foreach (ManagementObject v in searcher.Get())
                {
                        return (string)(v["value"]);
                }
            }
            catch (ManagementException)
            {
                // Invalid namespace - maybe only on non SCCM machines
                // Maybe fall back to registry values to be able to use this on non-sccm machines? /Robert
            }
            return null;
        }

        public string Get(string name)
        {
            if (isTest)
            {
                if (Variables.ContainsKey(name))
                {
                    return Variables[name].Value;
                }
                return null;
            }
            var protectedValue = GetProtectedValue(name);
            if (protectedValue == null)
            {
                return null;
            }
            if (Variables.ContainsKey(name))
            {
                var variable = Variables[name];
                if (variable.protectedValue == protectedValue)
                {
                    return variable.Value;
                }
                variable.protectedValue = protectedValue;
                variable.Value = Unprotect(protectedValue);
                return variable.Value;
            }
            
            var value = Unprotect(protectedValue);
            Variables.Add(name, new CCMCollectionVariable(value, protectedValue));
            return value;
        }

        private static string Unprotect(string strData)
        {
            // Remove <PolicySecret Version="1"><![CDATA[xxxxxxxx (43 chars) in beginning and 
            // ]]></PolicySecret> (18 chars) at end (xxx... is first 4 bytes)
            strData = strData.Substring(43, strData.Length - (43 + 18));

            // Chop string up into bytes (first 4 bytes already dropped
            var byteData = new Byte[strData.Length / 2];
            for (var i = 0; i < (strData.Length / 2); i++)
            {
                byteData[i] = Convert.ToByte(strData.Substring(i * 2, 2), 16);
            }

            // Create a Blob to contain the encrypted bytes
            var cipherTextBlob = new DATA_BLOB();
            cipherTextBlob.cbData = byteData.Length;
            cipherTextBlob.pbData = Marshal.AllocCoTaskMem(cipherTextBlob.cbData);

            // Copy data from original source to the BLOB structure
            Marshal.Copy(byteData, 0, cipherTextBlob.pbData, cipherTextBlob.cbData);

            // Create a Blob to contain the unencrypted bytes
            var plainTextBlob = new DATA_BLOB();

            var dummy = 0;
            var dummy2 = 0;
            //Decrypt the Blob with the encrypted bytes in it, and store in the plain text Blob
            // if ([PKI.Crypt32]::CryptUnprotectData([ref]$cipherTextBlob, $null, [ref][IntPtr]::Zero, [IntPtr]::Zero, [ref][IntPtr]::Zero, 1, [ref]$plainTextBlob))

            if (CryptUnprotectData(ref cipherTextBlob, null, ref dummy, IntPtr.Zero, ref dummy2, 1, ref plainTextBlob))
            {
                // If the decryption was sucessful, create a new byte array to contain
                // plainTextBlob ends with \0 so string should be two bytes shorter (one 16 bit character)
                var bytePlainText = new byte[plainTextBlob.cbData - 2];

                // Copy data from the plain text Blob to the new byte array
                Marshal.Copy(plainTextBlob.pbData, bytePlainText, 0, bytePlainText.Length);

                // Convert the unicode byte array to plain text string and return it
                return new UnicodeEncoding().GetString(bytePlainText);
            }
            else
            {
                return null;
            }

        }
    }
}
