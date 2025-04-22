using backend.Services;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace backend.Controllers
{
	[ApiController]
	[Route("api/[controller]")]
	public class DoctorController : ControllerBase
	{
		private readonly DoctorService _doctorService;

		public DoctorController(DoctorService doctorService)
		{
			_doctorService = doctorService;
		}

		[Authorize]
		[HttpGet("patients")]
		public async Task<IActionResult> GetPatients()
		{
			var role = User.FindFirst(ClaimTypes.Role)?.Value;
			if (role != "doctor") return Forbid("Access denied. Doctor only.");
			var patients = await _doctorService.GetPatientsWithDiagnosisCountsAsync();
			return Ok(patients);
		}
	}
}
