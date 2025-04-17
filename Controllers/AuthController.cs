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
				return BadRequest("User already exists");
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
			var response = await authService.Login(login.Email, login.Password);

			if (response?.Token == null)
			{
				return Unauthorized("Invalid Credentials");
			}

			return Ok(new { response });
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
			public string Email { get; set; }

			[Required]
			public string Password { get; set; }
		}
	}
}
