using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace MMAService
{

    public class CheckRequest
    {
        public string uid { get; set; }
    }

    public class Reply
    {
        public bool response { get; set; } = false;
    }

    public class VerifyRequest
    {
        public string uid { get; set; }
        public string code { get; set; }
        public string[] ips = Program.GetLocalIPAddresses();
        public string hostname = System.Environment.MachineName;
    }

    public class VerifyReply
    {
        public bool response { get; set; }
    }

    public interface IRestClient
    {
        Task<bool> CheckLucatAdmin(string user);
        Task<bool> CheckTFA(string user);
        Task<bool> Verify(string user, string twofactor);
    }

    public class RestClient : IRestClient
    {
        private HttpClient client;
        private HttpClientHandler httpClientHandler;
        private string ApiKey;
        private string CertificateThumbprint;

        public RestClient(string baseAddress, string Thumbprint, string InitApiKey)
        {
            if (Thumbprint != null)
            {
                CertificateThumbprint = Thumbprint.Replace(":", "").ToUpper();
            }

            if (InitApiKey != null)
            {
                ApiKey = InitApiKey;
            }
            // Define own to allow connecting to site with self signed ssl
            // Could later be used to control thumprint of certificate
            httpClientHandler = new HttpClientHandler();
            httpClientHandler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => {
                /*
                 * Certificate validation (Process CRL etc) should be done and after its been validated we check that its the right thumbprint
                 */
                if (CertificateThumbprint == null || cert.Thumbprint == CertificateThumbprint)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            };
            client = new HttpClient(httpClientHandler);
            client.BaseAddress = new Uri(baseAddress);
            client.Timeout = new TimeSpan(0,0,15);
        }

        private string CleanUsername(string username)
        {
            string cleanUsername = "";
            if (username.Contains("\\"))
            {
                cleanUsername = username.Substring(username.IndexOf("\\")+1);
            }
            else
            {
                cleanUsername = username;
            }
            return cleanUsername;
        }
        private async Task<bool> CallApi(string function,string username,string code = null)
        {
            // Api does not use domain, only send username
            string usernameWithoutDomain = CleanUsername(username);
            object requestObject = null;
            if (!String.IsNullOrEmpty(code))
            {
                requestObject = new VerifyRequest() { uid = usernameWithoutDomain, code = code };
            }
            else
            {
                requestObject = new CheckRequest() { uid = usernameWithoutDomain };
            }

            string RequestUri = String.Format("/api/{0}/?passwd={1}", function, ApiKey);
            
            var response = await client.PostAsJsonAsync(RequestUri, requestObject);
            var responseObject = await response.Content.ReadAsAsync<Reply>();
            return responseObject.response;
        }
        public async Task<bool> CheckLucatAdmin(string user)
        {
            return await CallApi("pls_lucat_admin", user);
        }

        public async Task<bool> CheckTFA(string user)
        {
            return await CallApi("totp_activated", user);
        }
        public async Task<bool> Verify(string user, string twofactor)
        {
            return await CallApi("totp_verify", user, twofactor);
        }
    }
}
