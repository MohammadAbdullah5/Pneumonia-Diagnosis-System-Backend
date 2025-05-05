using backend.Services;
using Microsoft.AspNetCore.Mvc;
using backend.Models;
using Microsoft.AspNetCore.Identity.Data;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authorization;

namespace backend.Controllers
{
	[ApiController]
	[Route("api/[controller]")]
	[EnableRateLimiting("auth-policy")]
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

			// Validate password strength
    		if (!IsValidPassword(request.Password))
    		{
    		    return BadRequest(new { message = "Password does not meet the required strength" });
    		}

			var user = new User
			{
				Email = request.Email,
				Password = request.Password,
				Role = "patient",
				IsProfileComplete = false,
			};
			var response = await authService.Register(user);
			// Send MFA code
			await authService.GenerateAndSendMfaCode(response.Id!, response.Email);
			return Ok(new
			{
				message = "Verification code sent",
				RequiresMfa = true,
				UserId = response.Id,
				Email = response.Email
			});

		}

		[HttpPost("login")]
		public async Task<IActionResult> Login([FromBody] LoginRequest login)
		{
			var user = await authService.GetUserByEmail(login.Email);
			var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
			if (ip != "unknown" && await authService.IsIpFlagged(ip))
			{
				return StatusCode(403, new { message = "Your IP has been temporarily blocked due to suspicious activity." });
			}

			var response = await authService.Login(login.Email, login.Password);

			if (user == null || !authService.VerifyPassword(login.Password, user.Password))
			{
				await authService.LogLoginAttempt(login.Email, ip, false);
				return Unauthorized(new { message = "Invalid Credentials" });
			}

			// Check for account lockout
			if (await authService.IsAccountLocked(login.Email))
			{
				await authService.LogLoginAttempt(login.Email, ip, false);

				return Unauthorized(new { message = "Account temporarily locked due to multiple failed attempts" });
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
			// Successful MFA verified → log successful login
			await authService.LogLoginAttempt(user.Email, HttpContext.Connection.RemoteIpAddress?.ToString(), true);

			// ✅ Clear failed attempts now that login is complete
			await authService.ClearFailedAttempts(user.Email);

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

		private bool IsValidPassword(string password)
		{
		    if (string.IsNullOrEmpty(password))
		        return false;

		    // Password must be at least 8 characters long
		    if (password.Length < 8)
		        return false;

		    // Check for at least one lowercase letter
		    if (!password.Any(char.IsLower))
		        return false;

		    // Check for at least one uppercase letter
		    if (!password.Any(char.IsUpper))
		        return false;

		    // Check for at least one digit
		    if (!password.Any(char.IsDigit))
		        return false;

		    // Check for at least one special character
		    var specialCharacters = "!@#$%^&*()_+[]{}|;:,.<>?/";
		    if (!password.Any(ch => specialCharacters.Contains(ch)))
		        return false;

		    return true;
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
