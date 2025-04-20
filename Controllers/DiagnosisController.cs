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
	public async Task<IActionResult> SubmitDiagnosis(IFormFile file, string symptoms, string userId)
	{
		if (file == null || file.Length == 0)
			return BadRequest(new { message = "No file uploaded." });

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

			var diagnosisRequest = new DiagnosisRequest
			{
				UserId = userId,
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
}
