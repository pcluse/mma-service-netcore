using System;
using Microsoft.AspNetCore.Mvc;

namespace MMAService.Controllers
{
    public class PrerequisitesReply
    {
        public string preferredService { get; set; }
        public string message { get; set; }
    }

    public class PrerequisitesTechnicianReply
    {
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
        [HttpGet("/prerequisites")]
        public ActionResult<PrerequisitesReply> GetPrerequisites()
        {
            if (! Program.IsMMAPossible())
            {
                Response.StatusCode = 403;
                return new PrerequisitesReply() { message = "error.not_allowed_on_this_computer" };
            }
            var (success, message) = Program.CheckPrerequisites();
            if (!success)
            {
                Response.StatusCode = 403;
                return new PrerequisitesReply() { message = message };

            }
            return new PrerequisitesReply() { preferredService = message };
        }

        [HttpGet("/validate/totp/{code}")]
        public ActionResult<ValidateReply> GetValidateTotpCode(string code)
        {
            if (!Program.IsMMAPossible())
            {
                Response.StatusCode = 403;
                return new ValidateReply() { message = "error.not_allowed_on_this_computer" };
            }
            var (success, validated, message) = Program.ValidateTotp(code);
            if (!success)
            {
                Response.StatusCode = 403;
                return new ValidateReply() { validated = false, message = message };
            }
            return new ValidateReply() { validated = validated };
        }

        [HttpGet("/validate/freja")]
        public ActionResult<ValidateReply> GetValidateFreja()
        {
            if (!Program.IsMMAPossible())
            {
                Response.StatusCode = 403;
                return new ValidateReply() { message = "error.not_allowed_on_this_computer" };
            }
            var (success, validated, message) = Program.ValidateFreja();
            if (!success)
            {
                Response.StatusCode = 403;
                return new ValidateReply() { validated = false, message = message };
            }
            return new ValidateReply() { validated = validated };
        }

        [HttpGet("/prerequisites/technician")]
        public ActionResult<PrerequisitesTechnicianReply> GetPrerequitesTechnician()
        {
            if (!Program.IsMMAPossible())
            {
                Response.StatusCode = 403;
                return new PrerequisitesTechnicianReply() { message = "error.not_allowed_on_this_computer" };
            }
            var user = Computer.GetLoggedInUsers()[0];
            if (LocalGroup.IsAdmin(user))
            {
                Response.StatusCode = 403;
                return new PrerequisitesTechnicianReply() { message = "error.user_already_admin" };
            }

            return new PrerequisitesTechnicianReply();
        }

        [HttpGet("/validate/technician/{technicianUid}")]
        public ActionResult<ValidateReply> GetValidateTechnician(string technicianUid)
        {
            if (!Program.IsMMAPossible())
            {
                Response.StatusCode = 403;
                return new ValidateReply() { message = "error.not_allowed_on_this_computer" };
            }
            var (success, validated, message) = Program.ValidateTechnician(technicianUid);
            if (!success)
            {
                Response.StatusCode = 403;
                return new ValidateReply() { validated = false, message = message };
            }
            return new ValidateReply() { validated = validated };
        }
    }
}
