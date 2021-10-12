using System;
using Microsoft.AspNetCore.Mvc;

namespace MMAService.Controllers
{
    public class PrerequisiteReply
    {
        public string preferredService { get; set; }
        public string message { get; set; }
    }

    public class ValidateReply
    {
        public string message { get; set; }
        public bool validated { get; set; }
    }

    /*
     * Use this controller when no controller is specified in the address
     */
    [Route("/")]
    [ApiController]
    // ControllerBase instead of Controller because minimal framework
    public class AdminController : ControllerBase
    {
        [HttpGet("/prerequisite/{uid}")]
        public ActionResult<PrerequisiteReply> GetPrerequisiteUid(string uid)
        {
            Console.WriteLine("uid {0}", uid);
            var (success, message) = Program.CheckCanBecomeAdmin(uid);
            if (!success)
            {
                return new PrerequisiteReply() { message = message };

            }
            return new PrerequisiteReply() { preferredService = message };
        }

        [HttpGet("/validate/totp/{uid}/{code}")]
        public ActionResult<ValidateReply> GetValidateTotpUidCode(string uid, string code)
        {
            var (success, message) = Program.AddAdmin("totp", uid, code);
            if (!success)
            {
                return new ValidateReply() { validated = false, message = message };
            }
            return new ValidateReply() { validated = true };
        }

        [HttpGet("/validate/freja/{uid}")]
        public ActionResult<ValidateReply> GetValidateFrejaUid(string uid)
        {
            var (success, message) = Program.AddAdmin("freja", uid, "");
            if (!success)
            {
                return new ValidateReply() { validated = false, message = message };
            }
            return new ValidateReply() { validated = success };
        }
    }
}
