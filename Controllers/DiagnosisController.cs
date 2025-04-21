using backend.Models;
using backend.Services;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

public class DiagnosisController : ControllerBase
{
	private readonly Cloudinary _cloudinary;
	private readonly DiagnosisService _service;
	public DiagnosisController(Cloudinary cloudinary, DiagnosisService diagnosisService)
	{
		_cloudinary = cloudinary;
		_service = diagnosisService;
	}

	[HttpPost("submit-diagnosis")]
	public async Task<IActionResult> SubmitDiagnosis(IFormFile file, string symptoms, string userId, IFormFile audio)
	{
		if (file == null || file.Length == 0)
			return BadRequest(new { message = "No file uploaded." });
		if (audio == null || audio.Length == 0)
			return BadRequest(new { message = "Audio file not uploaded." });

		try
		{
			// Upload the image to Cloudinary
			var uploadParams = new ImageUploadParams()
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
				Status = "Pending"
			};

			await _service.SaveDiagnosisAsync(diagnosisRequest);

			return Ok(new { ImageUrl = imageUrl, Message = "Diagnosis submitted successfully." });
		}
		catch (Exception ex)
		{
			return StatusCode(500, new { Message = "Error uploading image to Cloudinary.", Error = ex.Message });
		}
	}

	[HttpGet("pending")]
	public async Task<IActionResult> GetPending()
	{
		var diagnoses = await _service.GetPendingDiagnosis();
		return Ok(diagnoses);
	}

	[HttpGet("ai-suggestion/{id}")]
	public async Task<IActionResult> AnalyzeImage(string id)
	{
		try
		{
			var diagnosis = await _service.GetById(id);
			var aiResult = await _service.GetAIAnalysis(diagnosis.ImageUrl);
			return Ok(new { Diagnosis = aiResult });
		}
		catch (Exception ex)
		{
			return StatusCode(500, new { Error = ex.Message });
		}
	}

	[HttpPost("submit")]
	public async Task<IActionResult> SubmitDoctorDiagnosis([FromBody] SubmitDiagnosisDto dto)
	{
		await _service.MarkAsDiagnosed(dto.DiagnosisId, dto.Diagnosis, dto.Remarks);
		return Ok(new { message = "Diagnosis submitted" });
	}

	public class SubmitDiagnosisDto
	{
		public string DiagnosisId { get; set; }
		public string Diagnosis { get; set; }
		public string Remarks { get; set; }
	}

}
