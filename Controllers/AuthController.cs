using backend.Services;
using Microsoft.AspNetCore.Mvc;
using backend.Models;
using Microsoft.AspNetCore.Identity.Data;
using System.ComponentModel.DataAnnotations;

namespace backend.Controllers
{
	[ApiController]
	[Route("api/[controller]")]
	public class AuthController : ControllerBase
	{
		private readonly AuthService authService;

		public AuthController(AuthService authService)
		{
			this.authService = authService;
		}

		[HttpPost("signup")]
		public async Task<IActionResult> SignUp([FromBody] SignUpRequest request)
		{
			if (!ModelState.IsValid)
			{
				return BadRequest(ModelState);
			}

			if (await authService.UserExists(request.Email))
			{
				return BadRequest(new { message = "User already exists" });
			}

			var user = new User
			{
				Email = request.Email,
				Password = request.Password,
				Role = "patient",
				IsProfileComplete = false,
			};
			var response = await authService.Register(user);
			return Ok(new {
				Token = response.Token,
				User = new
				{
					response.Id,
					response.Name,
					response.Email,
					response.Role
				}
			});
		}

		[HttpPost("login")]
		public async Task<IActionResult> Login([FromBody] LoginRequest login)
		{
			var user = await authService.GetUserByEmail(login.Email);
			var response = await authService.Login(login.Email, login.Password);

			if (user == null || !authService.VerifyPassword(login.Password, user.Password))
			{
				return Unauthorized(new { message = "Invalid Credentials" });
			}

			// Generate MFA code and send it
			var code = await authService.GenerateAndSendMfaCode(user.Id, user.Email);

			// Return a pending MFA response
			return Ok(new { message = "Verification code sent", RequiresMfa = true, UserId = user.Id });
		}

		[HttpPost("verify-2fa")]
		public async Task<IActionResult> VerifyMfa([FromBody] VerifyCodeRequest request)
		{
			var response = await authService.VerifyMfaCode(request.UserId, request.Code);
			if (!response)
			{
				return Unauthorized(new { message = "Invalid or expired verification code" });
			}

			var user = await authService.GetUserById(request.UserId);
			if (user == null)
			{
				return NotFound(new { message = "User not found" });
			}

			var token = authService.GenerateJwtToken(user);

			return Ok(new
			{
				Token = token,
				User = new
				{
					user.Id,
					user.Name,
					user.Email,
					user.Role
				}
			});
		}

		[HttpPost("resend-2fa")]
		public async Task<IActionResult> ResendMfa([FromBody] ResendCodeRequest request)
		{
			var user = await authService.GetUserById(request.UserId);
			if (user == null || user.Email != request.Email)
			{
				return NotFound(new { message = "User not found or email mismatch" });
			}

			var result = await authService.ResendMfaCode(request.UserId, request.Email);

			return Ok(new { message = "Verification code resent" });
		}


		public class VerifyCodeRequest
		{
			public string UserId { get; set; } = null!;
			public string Code { get; set; } = null!;
		}

		public class LoginRequest
		{
			public string Email { get; set; } = null!;
			public string Password { get; set; } = null!;
		}

		public class SignUpRequest
		{
			[Required]
			[EmailAddress(ErrorMessage = "Invalid email format")]
			public required string Email { get; set; }

			[Required]
			public required string Password { get; set; }
		}

		public class ResendCodeRequest
		{
			public string UserId { get; set; } = null!;
			public string Email { get; set; } = null!;
		}
	}
}
