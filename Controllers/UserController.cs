using Microsoft.AspNetCore.Mvc;
using backend.Services;
using backend.Models;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace backend.Controllers
{
	[ApiController]
	[Route("api/[controller]")]
	public class UserController : ControllerBase
	{
		private readonly UserService _userService;

		public UserController(UserService userService)
		{
			_userService = userService;
		}

		[HttpGet]
		public async Task<List<User>> Get()
		{
			return await _userService.GetAsync();
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
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier); 
			if (userId == null) return Unauthorized();

			var user = await _userService.GetAsync(userId);
			if (user == null) return NotFound("User Not Found");

			user.Name = request.Name;
			user.IsProfileComplete = true;

			await _userService.UpdateUser(user.Id, user);

			return Ok(new { message = "Profile completed successfully" });
		}

		[HttpDelete("delete")]
		public async Task<IActionResult> DeleteUser(string email)
		{
			bool deleted = await _userService.DeleteUserByEmailAsync(email);
			if (!deleted)
				return NotFound("User not found.");

			return Ok("User deleted successfully.");
		}


		public class CompleteProfileRequest
		{
			public string Name { get; set; }
		}
	}
}
