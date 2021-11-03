using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace MMAService
{
    public class PrerequisitesReply
    {

        public bool PLSLucatLocalAdministrator { get; set; } = false;
        public bool MLSLucatLocalAdministrator { get; set; } = false;
        public bool totpActivated { get; set; } = false;
        public bool frejaActivated { get; set; } = false;
        public string preferredService { get; set; } = null;
    }

    public class ValidateReply
    {
        public bool validated { get; set; }
    }

    public class Backend
    {
        private HttpClient client;

        public Backend(string baseAddress, string apiKey)
        {
            client = new HttpClient();
            client.DefaultRequestHeaders.Add("X-API-KEY", apiKey);
            if (!baseAddress.EndsWith("/"))
            {
                // https://stackoverflow.com/a/23438417
                baseAddress += "/";
            }
            client.BaseAddress = new Uri(baseAddress);
        }

        private string RemoveDomain(string username)
        {
            if (!username.Contains("\\"))
            {
                return username;
            }
            return username.Substring(username.IndexOf("\\") + 1);
        }

        public async Task<PrerequisitesReply> GetPrerequisites(string username)
        {
            username = RemoveDomain(username);
            string RequestUri = String.Format("prerequisites/{0}", username);

            var response = await client.GetAsync(RequestUri);
            response.EnsureSuccessStatusCode();
            var responseObject = await response.Content.ReadAsAsync<PrerequisitesReply>();
            return responseObject;
        }

        public async Task<bool> ValidateFreja(string username)
        {
            username = RemoveDomain(username);
            string RequestUri = String.Format("validate/freja/{0}", username);

            var response = await client.GetAsync(RequestUri);
            response.EnsureSuccessStatusCode();
            var responseObject = await response.Content.ReadAsAsync<ValidateReply>();
            return responseObject.validated;
        }
        public async Task<bool> ValidateTotp(string username, string twofactor)
        {
            username = RemoveDomain(username);
            string RequestUri = String.Format("validate/totp/{0}/{1}", username, twofactor);

            var response = await client.GetAsync(RequestUri);
            response.EnsureSuccessStatusCode();
            var responseObject = await response.Content.ReadAsAsync<ValidateReply>();
            return responseObject.validated;
        }
        public async Task<bool> ValidateTechnician(string username, string technicianUid, string computerName, bool isStudentComputer)
        {
            username = RemoveDomain(username);

            var response = await client.GetAsync("/validate/technician");
            response.EnsureSuccessStatusCode();
            var responseObject = await response.Content.ReadAsAsync<ValidateReply>();
            return responseObject.validated;
        }
    }
}
