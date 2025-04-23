using Microsoft.AspNetCore.Mvc;
using backend.Services;
using backend.Models;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.AspNetCore.RateLimiting;

namespace backend.Controllers
{
	[ApiController]
	[Route("api/[controller]")]
	[EnableRateLimiting("fixed")]
	public class UserController : ControllerBase
	{
		private readonly UserService _userService;

		public UserController(UserService userService)
		{
			_userService = userService;
		}

		[Authorize]
		[HttpGet]
		public async Task<IActionResult> Get()
		{
			var role = User.FindFirst(ClaimTypes.Role)?.Value;
			if (role != "admin") return Forbid("Admins only");

			return Ok(await _userService.GetAsync());
		}

		[HttpPost]
		public async Task<IActionResult> Post(User newUser)
		{
			await _userService.CreateUser(newUser);
			return CreatedAtAction(nameof(Get), new { id = newUser.Id }, newUser);
		}

		/*
		 Store the returned token. Check if IsProfileComplete == false. Redirect to a “complete your profile” screen.
		 Make a PATCH /api/user/profile call with username and token. */
		[Authorize]
		[HttpPatch("profile")]
		public async Task<IActionResult> CompleteProfile([FromBody] CompleteProfileRequest request)
		{
			var role = User.FindFirst(ClaimTypes.Role)?.Value;
			if (role != "patient") return Forbid("Patients only");

			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier); 
			if (userId == null) return Unauthorized();

			var user = await _userService.GetAsync(userId);
			if (user == null) return NotFound("User Not Found");

			user.Name = request.Name;
			user.Address = request.Address;
			user.Age = request.Age;
			user.PhoneNumber = request.PhoneNumber;
			user.Gender = request.Gender;
			user.MedicalHistory = request.MedicalHistory;
			user.IsProfileComplete = true;

			await _userService.UpdateUser(user.Id, user);

			return Ok(new { message = "Profile completed successfully" });
		}

		[Authorize]
		[HttpDelete("delete")]
		public async Task<IActionResult> DeleteUser(string email)
		{
			var role = User.FindFirst(ClaimTypes.Role)?.Value;
			if (role != "admin") return Forbid("Admins only");

			bool deleted = await _userService.DeleteUserByEmailAsync(email);
			if (!deleted)
				return NotFound("User not found.");

			return Ok("User deleted successfully.");
		}

		[Authorize]
		// In UserController.cs
		[HttpGet("profile")]
		public async Task<IActionResult> GetUserProfile()
		{
			try
			{
				var role = User.FindFirst(ClaimTypes.Role)?.Value;
				if (role != "patient") return Forbid("Patient only");
				var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

				if (string.IsNullOrEmpty(userId))
				{
					return Unauthorized(new { message = "User is not authenticated" });
				}

				var user = await _userService.GetAsync(userId);
				if (user == null)
				{
					return NotFound(new { message = "User not found" });
				}

				return Ok(user);
			}
			catch (Exception ex)
			{
				return StatusCode(500, $"Internal server error: {ex.Message}");
			}
		}

		[Authorize]
		// In UserController.cs
		[HttpPatch("edit-profile")]
		public async Task<IActionResult> UpdateUserProfile([FromBody] User updatedUser)
		{
			try
			{
				var user = await _userService.GetAsync(updatedUser.Id);
				if (user == null)
				{
					return NotFound(new { message = "User not found" });
				}

				user.Name = updatedUser.Name;
				user.PhoneNumber = updatedUser.PhoneNumber;
				user.Address = updatedUser.Address;
				user.Age = updatedUser.Age;
				user.Gender = updatedUser.Gender;
				user.MedicalHistory = updatedUser.MedicalHistory;

				await _userService.UpdateUser(user.Id, user);

				return Ok(user);
			}
			catch (Exception ex)
			{
				return StatusCode(500, $"Internal server error: {ex.Message}");
			}
		}




		public class CompleteProfileRequest
		{
			public string Name { get; set; }
			public string PhoneNumber { get; set; }
			public string Address { get; set; }
			public int Age { get; set; }
			public string Gender { get; set; }
			public string MedicalHistory { get; set; }
		}
	}
}
