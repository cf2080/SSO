﻿using DataAccessLayer.Database;
using DataAccessLayer.Models;
using ManagerLayer.Login;
using System;
using System.Data.Entity.Validation;
using System.Net;
using System.Web.Http;
using ManagerLayer;
using ServiceLayer.Exceptions;
using System.ComponentModel.DataAnnotations;
using ManagerLayer.PasswordManagement;
using ServiceLayer.Services;
using ManagerLayer.UserManagement;

namespace KFC_WebAPI.Controllers
{
    public class UserRegistrationRequest
    {
        [Required]
        public string email { get; set; }
        [Required]
        public string password { get; set; }
        [Required]
        public DateTime dob { get; set; }
        [Required]
        public string city { get; set; }
        [Required]
        public string state { get; set; }
        [Required]
        public string country { get; set; }
        [Required]
        public string securityQ1 { get; set; }
        [Required]
        public string securityQ1Answer { get; set; }
        [Required]
        public string securityQ2 { get; set; }
        [Required]
        public string securityQ2Answer { get; set; }
        [Required]
        public string securityQ3 { get; set; }
        [Required]
        public string securityQ3Answer { get; set; }
    }

    public class UpdatePasswordRequest
    {
        [Required]
        public string emailAddress { get; set; }
        [Required]
        public string sessionToken { get; set; }
        [Required]
        public string oldPassword { get; set; }
        [Required]
        public string newPassword { get; set; }
    }

    public class UsersController : ApiController
    {
        [HttpPost]
        [Route("api/users/register")]
        public IHttpActionResult Register([FromBody, Required]UserRegistrationRequest request)
        {
            if (!ModelState.IsValid || request == null)
            {
                return Content((HttpStatusCode)412, ModelState);
            }

            using (var _db = new DatabaseContext())
            {
                User user;
                try
                {
                    UserManager userManager = new UserManager();
                    user = userManager.CreateUser(
                        _db,
                        request.email,
                        request.password,
                        request.dob,
                        request.city,
                        request.state,
                        request.country,
                        request.securityQ1,
                        request.securityQ1Answer,
                        request.securityQ2,
                        request.securityQ2Answer,
                        request.securityQ3,
                        request.securityQ3Answer);
                } catch (ArgumentException)
                {
                    return Conflict();
                } catch (FormatException)
                {
                    return Content((HttpStatusCode)406, "Invalid email address.");
                }
                catch (PasswordInvalidException)
                {
                    return Content((HttpStatusCode)401, "That password is too short. Password must be between 12 and 2000 characters.");
                }
                catch (PasswordPwnedException)
                {
                    return Content((HttpStatusCode)401, "That password has been hacked before. Please choose a more secure password.");
                }
                catch (InvalidDobException)
                {
                    return Content((HttpStatusCode)401, "This software is intended for persons over 18 years of age.");
                }

                AuthorizationManager authorizationManager = new AuthorizationManager();
                Session session = authorizationManager.CreateSession(_db, user);
                try
                {
                    _db.SaveChanges();
                }
                catch (DbEntityValidationException)
                {
                    _db.Entry(user).State = System.Data.Entity.EntityState.Detached;
                    _db.Entry(session).State = System.Data.Entity.EntityState.Detached;
                    return InternalServerError();
                }

                return Ok(new
                {
                    token = session.Token
                });
            }
        }

        [HttpGet]
        [Route("api/users/{token}")]
        public string GetEmail(string token)
        {
            UserManagementManager umm = new UserManagementManager();
            SessionService ss = new SessionService();
            Session session = new Session();
            User user;
            string email;

            using (var _db = new DatabaseContext())
            {
                session = ss.GetSession(_db,token);
                Console.WriteLine(session);
            }

            var id = session.UserId;
            user = umm.GetUser(id);
            email = user.Email;

            return email;
        }

        [HttpPost]
        [Route("api/users/login")]
        public IHttpActionResult Login([FromBody] LoginRequest request)
        {
            LoginManager loginM = new LoginManager();
            if (loginM.LoginCheckUserExists(request) == false)
            {
                //400
                return Content(HttpStatusCode.BadRequest, "Invalid Username/Password");
            }
            else
            {
                if (loginM.LoginCheckUserDisabled(request))
                {
                    //401
                    return Content(HttpStatusCode.Unauthorized, "User is Disabled");
                }
                else
                {
                    if (loginM.LoginCheckPassword(request))
                    {
                        return Ok(loginM.LoginAuthorized(request));
                    }
                    else
                    {
                        //400
                        return Content(HttpStatusCode.BadRequest, "Invalid Username/Password");
                    }
                }
            }
        }

        [HttpPost]
        [Route("api/users/updatepassword")]
        public IHttpActionResult UpdatePassword([FromBody] UpdatePasswordRequest request)
        {
            using(var _db = new DatabaseContext())
            {
                UserManagementManager umm = new UserManagementManager();
                User retrievedUser = umm.GetUser(request.emailAddress);
                if (retrievedUser != null)
                {
                    SessionService ss = new SessionService();
                    Session session = ss.GetSession(_db, request.sessionToken);
                    if (session != null)
                    {
                        PasswordManager pm = new PasswordManager();
                        string oldPasswordHashed = pm.HashPassword(request.oldPassword, retrievedUser.PasswordSalt);
                        if (oldPasswordHashed == retrievedUser.PasswordHash)
                        {
                            if (request.newPassword.Length >= 12 || request.newPassword.Length <= 2000)
                            {
                                if (!pm.CheckIsPasswordPwned(request.newPassword))
                                {
                                    string newPasswordHashed = pm.HashPassword(request.newPassword, retrievedUser.PasswordSalt);
                                    if (pm.UpdatePassword(retrievedUser, newPasswordHashed))
                                    {
                                        return Content(HttpStatusCode.OK, "Password has been updated");
                                    }
                                    return Content(HttpStatusCode.BadRequest, "New password matches old password");
                                }
                                return Content(HttpStatusCode.BadRequest, "Password has been pwned, please use a different password");
                            }
                            return Content(HttpStatusCode.BadRequest, "New password does not meet minimum password requirements");
                        }
                        return Content(HttpStatusCode.BadRequest, "Current password inputted does not match current password");
                    }
                    return Content(HttpStatusCode.Unauthorized, "Session invalid");
                }
                return Content(HttpStatusCode.BadRequest, "Email address is not valid");
            }
        }
    }
}
