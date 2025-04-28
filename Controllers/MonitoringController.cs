using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using backend.Services;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace backend.Controllers
{

	[ApiController]
	[Route("[controller]")]
	public class MonitoringController : ControllerBase
	{
		[Authorize]
		[HttpGet("metrics")]
		public IActionResult GetMetrics()
		{
			var role = User.FindFirst(ClaimTypes.Role)?.Value;
			if (role != "admin") return Forbid("Access denied! Admin only.");
			var logs = MonitoringMiddleware.GetRequestLogs();
			return Ok(logs);
		}
	}

}
