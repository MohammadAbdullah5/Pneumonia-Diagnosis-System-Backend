using backend.Models;
using backend.Services;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.AspNetCore.RateLimiting;

[ApiController]
[EnableRateLimiting("auth-policy")]
public class DiagnosisController : ControllerBase
{
	private readonly Cloudinary _cloudinary;
	private readonly DiagnosisService _service;
	private readonly IWebHostEnvironment _env;

	public DiagnosisController(Cloudinary cloudinary, DiagnosisService diagnosisService, IWebHostEnvironment env)
	{
		_cloudinary = cloudinary;
		_service = diagnosisService;
		_env = env;
	}

	[Authorize]
	[HttpPost("submit-diagnosis")]
	public async Task<IActionResult> SubmitDiagnosis([FromForm] DiagnosisRequestDto request)
	{
		var file = request.File;
		var audio = request.Audio;
		var userId = request.UserId;
		var symptoms = request.Symptoms;

		var role = User.FindFirst(ClaimTypes.Role)?.Value;
		if (role != "patient") return Forbid("Access denied. Patients only.");

		if (file == null || file.Length == 0)
			return BadRequest(new { message = "No file uploaded." });
		if (audio == null || audio.Length == 0)
			return BadRequest(new { message = "Audio file not uploaded." });
		var allowedExtensions = new[] { ".jpg", ".jpeg", ".png" };
		var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
		if (!allowedExtensions.Contains(extension))
			return BadRequest(new { message = "Only JPG, JPEG, PNG files are allowed." });

		try
		{
			// Upload the image to Cloudinary
			var uploadParams = new RawUploadParams()
			{
				File = new FileDescription(file.FileName, file.OpenReadStream())
			};
			var uploadResult = await _cloudinary.UploadAsync(uploadParams);

			// Save image URL to your database along with other diagnosis data
			var imageUrl = uploadResult.SecureUrl.ToString();

			var audioUploadParams = new RawUploadParams()
			{
				File = new FileDescription(audio.FileName, audio.OpenReadStream())
			};
			var audioResult = await _cloudinary.UploadAsync(audioUploadParams);
			var audioUrl = audioResult.SecureUrl.ToString();

			var diagnosisRequest = new DiagnosisRequest
			{
				UserId = userId,
				AudioUrl = audioUrl,
				ImageUrl = uploadResult.SecureUrl.ToString(),
				Symptoms = symptoms,
				Status = "Pending",
				IV = request.IV,
				EncryptedAESKey = request.EncryptedAESKey
			};

			await _service.SaveDiagnosisAsync(diagnosisRequest);

			return Ok(new { ImageUrl = imageUrl, Message = "Diagnosis submitted successfully." });
		}
		catch (Exception ex)
		{
			return StatusCode(500, new { Message = "Error uploading image to Cloudinary.", Error = ex.Message });
		}
	}

	[Authorize]
	[HttpGet("pending")]
	public async Task<IActionResult> GetPending()
	{
		var role = User.FindFirst(ClaimTypes.Role)?.Value;
		if (role != "doctor") return Forbid("Access denied. Doctor only.");
		var diagnoses = await _service.GetPendingDiagnosis();
		return Ok(diagnoses);
	}

	[Authorize]
	[HttpPost("ai-suggestion")]
	[EnableRateLimiting("ai-diagnosis")]
	public async Task<IActionResult> AnalyzeImage([FromForm] IFormFile file, [FromForm] string signature)
	{
		try
		{
			var role = User.FindFirst(ClaimTypes.Role)?.Value;
			if (role != "doctor") return Forbid("Access denied. Doctor only.");
			if (file == null || file.Length == 0)
				return BadRequest("No file uploaded");
			if (string.IsNullOrEmpty(signature))
				return BadRequest("Signature missing");
			byte[] fileBytes;
			using (var memoryStream = new MemoryStream())
			{
				await file.CopyToAsync(memoryStream);
				fileBytes = memoryStream.ToArray();
			}
			var aiResult = await _service.GetAIAnalysis(fileBytes, signature);
			return Ok(new { Diagnosis = aiResult });
		}
		catch (Exception ex)
		{
			return StatusCode(500, new { Error = ex.Message });
		}
	}

	[Authorize]
	[HttpPost("submit")]
	public async Task<IActionResult> SubmitDoctorDiagnosis([FromBody] SubmitDiagnosisDto dto)
	{
		try
		{
			var role = User.FindFirst(ClaimTypes.Role)?.Value;
			if (role != "doctor") return Forbid("Access denied. Doctor only.");
			await _service.MarkAsDiagnosed(dto.DiagnosisId, dto.Diagnosis, dto.Remarks);
			await _service.SendDiagnosisNotificationEmail(dto.DiagnosisId);
			return Ok(new { message = "Diagnosis submitted" });
		}

		catch (Exception ex)
		{
			return StatusCode(500, new { Error = ex.Message });
		}
	}

	[HttpGet("my-reports")]
	[Authorize]
	public async Task<IActionResult> GetMyDiagnosedReports()
	{
		try
		{
			var role = User.FindFirst(ClaimTypes.Role)?.Value;
			if (role != "patient") return Forbid("Access denied. Patients only.");
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

			if (string.IsNullOrEmpty(userId))
				return Unauthorized("Invalid user.");

			var reports = await _service.GetDiagnosedReportsByUserAsync(userId);

			var result = reports.Select(r => new
			{
				id = r.Id,
				diagnosis = r.FinalDiagnosis,
				remarks = r.Remarks,
				diagnosedAt = r.DiagnosedAt
			});

			return Ok(result);
		}
		catch (Exception ex)
		{
			return StatusCode(500, new { Message = ex.Message });
		}
	}

	[HttpGet("reports")]
	public async Task<IActionResult> GetDiagnosisReports()
	{
		var role = User.FindFirst(ClaimTypes.Role)?.Value;
		if (role != "doctor") return Forbid("Access denied. Doctor only.");
		var reports = await _service.GetAllDiagnosisReportsAsync();
		return Ok(reports);
	}

	[HttpGet("public-key")]
	public IActionResult GetPublicKey()
	{
		var keyPath = Path.Combine(_env.ContentRootPath, "Keys", "doctor_public_key.pem");
		if (!System.IO.File.Exists(keyPath))
		{
			return NotFound("Public key not found.");
		}

		var publicKey = System.IO.File.ReadAllText(keyPath);
		return Ok(publicKey);
	}


	public class SubmitDiagnosisDto
	{
		public string DiagnosisId { get; set; }
		public string Diagnosis { get; set; }
		public string Remarks { get; set; }
	}

	public class DiagnosisRequestDto
	{
		public string Symptoms { get; set; }
		public string UserId { get; set; }
		public IFormFile File { get; set; }
		public IFormFile Audio { get; set; }
		public string EncryptedAESKey { get; set; }
		public string IV { get; set; }
	}
}
