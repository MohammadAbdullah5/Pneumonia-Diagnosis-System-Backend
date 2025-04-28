using backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("fixed")]
public class AdminController : ControllerBase
{
	private readonly AdminService _adminService;

	public AdminController(AdminService adminService)
	{
		_adminService = adminService;
	}

	[Authorize]
	[HttpGet("login-attempts")]
	public async Task<IActionResult> GetLoginAttempts()
	{
		var role = User.FindFirst(ClaimTypes.Role)?.Value;
		if (role != "admin") return Forbid("Access denied! Admin only.");
		var attempts = await _adminService.GetLoginAttempts();
		return Ok(attempts);
	}

	[HttpPost("flag-ip")]
	public async Task<IActionResult> FlagIp([FromBody] FlagIpRequest request)
	{
		await _adminService.FlagIpAddress(request.IpAddress, request.Reason);
		return Ok(new { message = $"IP {request.IpAddress} flagged." });
	}

	[HttpPost("unflag-ip")]
	public async Task<IActionResult> UnflagIp([FromBody] UnflagIpRequest request)
	{
		await _adminService.UnflagIpAddress(request.IpAddress);
		return Ok(new { message = $"IP {request.IpAddress} unflagged." });
	}

	[HttpPatch("doctor")]
	public async Task<IActionResult> EditDoctor([FromBody] EditDoctorRequest request)
	{
		if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
			return BadRequest("Username and password are required.");

		var success = await _adminService.EditDoctorAsync(request.Username, request.Password);
		if (!success)
			return NotFound("Doctor not found or update failed.");

		return Ok("Doctor updated successfully.");
	}

	public class FlagIpRequest
	{
		public string IpAddress { get; set; } = null!;
		public string Reason { get; set; } = "Suspicious behavior";
	}

	public class UnflagIpRequest
	{
		public string IpAddress { get; set; } = null!;
	}

	public class EditDoctorRequest
	{
		public string Username { get; set; }
		public string Password { get; set; }
	}

}
