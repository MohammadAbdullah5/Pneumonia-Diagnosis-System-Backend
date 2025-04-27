using MongoDB.Driver;
using backend.Models;
using System.Threading.Tasks;
using MongoDB.Bson;

namespace backend.Services
{
	public class DiagnosisService
	{
		private readonly IMongoCollection<DiagnosisRequest> _diagnosisCollection;
		private readonly UserService _userService;
		private readonly IEmailService _emailService;

		public DiagnosisService(IMongoDatabase database, IEmailService emailService, UserService userService, IMongoCollection<User> userCollection)
		{
			_diagnosisCollection = database.GetCollection<DiagnosisRequest>("Diagnoses");
			_emailService = emailService;
			_userService = userService;
		}

		public async Task SaveDiagnosisAsync(DiagnosisRequest diagnosisRequest)
		{
			await _diagnosisCollection.InsertOneAsync(diagnosisRequest);
		}

		public async Task<List<PendingDiagnosisDto>> GetPendingDiagnosis()
		{
			var pipeline = new[] {
			new BsonDocument("$match", new BsonDocument("Status", "Pending")),
			new BsonDocument("$addFields", new BsonDocument("UserIdObj", new BsonDocument("$toObjectId", "$UserId"))),
			new BsonDocument("$lookup", new BsonDocument
			{
				{ "from", "Users" },
				{ "localField", "UserIdObj" },
				{ "foreignField", "_id" },
				{ "as", "UserInfo" }
			}),
			new BsonDocument("$unwind", "$UserInfo"),
			new BsonDocument("$project", new BsonDocument
			{
				{ "Id", "$_id" },
				{ "UserId", 1 },
				{ "ImageUrl", 1 },
				{ "AudioUrl", 1 },
				{ "Symptoms", 1 },
				{ "IV", 1 },
				{ "EncryptedAESKey", 1 },
				{ "Status", 1 },
				{ "Remarks", 1 },
				{ "FinalDiagnosis", 1 },
				{ "SubmittedAt", 1 },
				{ "UserName", "$UserInfo.Name" },
				{ "UserAge", "$UserInfo.Age" },
				{ "UserGender", "$UserInfo.Gender" }})
			}; 
			var documents = await _diagnosisCollection.Aggregate<BsonDocument>(pipeline).ToListAsync();
			var result = documents.Select(doc => new PendingDiagnosisDto
			{
				Id = doc["Id"].AsObjectId.ToString(),
				UserId = doc["UserId"].AsString,
				ImageUrl = doc["ImageUrl"].AsString,
				IV = doc["IV"].AsString,
				EncryptedAESKey = doc["EncryptedAESKey"].AsString,
				AudioUrl = doc["AudioUrl"].AsString,
				Symptoms = doc["Symptoms"].AsString,
				Status = doc["Status"].AsString,
				SubmittedAt = doc["SubmittedAt"].ToUniversalTime(),
				UserName = doc["UserName"].AsString,
				UserAge = doc.GetValue("UserAge", BsonNull.Value).IsBsonNull ? null : doc["UserAge"].AsInt32,
				UserGender = doc.GetValue("UserGender", "").AsString
			}).ToList();
			return result;
		}

		public async Task<DiagnosisRequest> GetById(string id)
		{
			if (string.IsNullOrEmpty(id))
				throw new ArgumentException("Diagnosis ID must not be null or empty.", nameof(id));

			var diagnosis = await _diagnosisCollection.Find(d => d.Id == id).FirstOrDefaultAsync();

			if (diagnosis == null)
				throw new Exception("Diagnosis not found.");

			return diagnosis;
		}

		public async Task<AIDiagnosisResponse> GetAIAnalysis(byte[] imageBytes, string signature)
		{
			using var httpClient = new HttpClient();
			using var form = new MultipartFormDataContent();

			string fileExtension = ".jpg";
			
			// Step 3: Determine the Content-Type based on the file extension
			string contentType = fileExtension switch
			{
				".jpg" or ".jpeg" => "image/jpeg",
				".png" => "image/png",
				_ => throw new Exception("Unsupported image format")
			};

			var byteArrayContent = new ByteArrayContent(imageBytes);
			byteArrayContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);

			// Add the image file to the form with the key 'file'
			form.Add(byteArrayContent, "file", "image" + fileExtension);
			// ❗ Use the frontend-provided signature
			httpClient.DefaultRequestHeaders.Add("X-Signature", signature);

			var response = await httpClient.PostAsync("http://localhost:5000/predict", form);

			if (!response.IsSuccessStatusCode)
			{
				throw new Exception("AI model analysis failed.");
			}

			var result = await response.Content.ReadFromJsonAsync<AIDiagnosisResponse>();

			if (result == null)
			{
				throw new Exception("No response from model");
			}
			var confidencePercent = CalculateConfidencePercentage(result.Score);

			return new AIDiagnosisResponse { Prediction = result.Prediction, Score = confidencePercent };
		}

		public async Task SendDiagnosisNotificationEmail(string diagnosisId)
		{
			var diagnosis = await _diagnosisCollection.Find(d => d.Id == diagnosisId).FirstOrDefaultAsync();
			if (diagnosis == null)
			{
				throw new Exception("Diagnosis not found for email notification.");
			}

			var user = await _userService.GetAsync(diagnosis.UserId);
			if (user == null || string.IsNullOrEmpty(user.Email))
			{
				throw new Exception("User not found or email is missing.");
			}

			string subject = "Your Diagnosis Has Been Reviewed";
			string body = $@"
        Dear {user.Name}, 

        Your recent pneumonia diagnosis has been reviewed by one of our doctors. 


        Thank you for using PneumoScan!

        Best regards,
        PneumoScan Team";

			await _emailService.SendEmailAsync(user.Email, subject, body);
		}

		public async Task<List<DiagnosisRequest>> GetDiagnosedReportsByUserAsync(string userId)
		{
			var filter = Builders<DiagnosisRequest>.Filter.And(
				Builders<DiagnosisRequest>.Filter.Eq(r => r.UserId, userId),
				Builders<DiagnosisRequest>.Filter.Ne(r => r.FinalDiagnosis, null)
			);

			return await _diagnosisCollection
				.Find(filter)
				.SortByDescending(r => r.DiagnosedAt)
				.ToListAsync();
		}

		public async Task<List<object>> GetAllDiagnosisReportsAsync()
		{
			var diagnoses = await _diagnosisCollection.Find(_ => true).ToListAsync();
			var reports = new List<object>();

			foreach (var diagnosis in diagnoses)
			{
				var user = await _userService.GetUserByDiagnosis(diagnosis.UserId);

				if (user != null)
				{
					reports.Add(new
					{
						Id = diagnosis.Id,
						PatientName = user.Name,
						Age = user.Age ?? 0,
						Gender = user.Gender ?? "Unknown",
						Diagnosis = diagnosis.FinalDiagnosis ?? "Not Available",
						DiagnosedOn = diagnosis.SubmittedAt.ToString("yyyy-MM-dd") ?? "Pending"
					});
				}
			}

			return reports;
		}


		public async Task MarkAsDiagnosed(string diagnosisId, string finalDiagnosis, string remarks)
		{
			var filter = Builders<DiagnosisRequest>.Filter.Eq(d => d.Id, diagnosisId);
			var update = Builders<DiagnosisRequest>.Update
				.Set(d => d.Status, "Diagnosed")
				.Set(d => d.FinalDiagnosis, finalDiagnosis)
				.Set(d => d.Remarks, remarks)
				.Set(d => d.DiagnosedAt, DateTime.UtcNow);

			var result = await _diagnosisCollection.UpdateOneAsync(filter, update);

			if (result.MatchedCount == 0)
			{
				throw new Exception("Diagnosis not found");
			}
		}
		public float CalculateConfidencePercentage(float score)
		{
			float distanceFromCenter = Math.Abs(score - 0.5f);
			float confidence = distanceFromCenter * 2; // Normalize to range [0, 1]
			return (float)Math.Round(confidence * 100, 2); // Convert to percentage
		}
	}


	public class AIDiagnosisResponse
	{
		public string Prediction { get; set; }
		public float Score { get; set; }
	}

	public class PendingDiagnosisDto
	{
		public string Id { get; set; }
		public string UserId { get; set; }
		public string ImageUrl { get; set; }
		public string AudioUrl { get; set; }
		public string IV { get; set; }
		public string EncryptedAESKey { get; set; }
		public string Symptoms { get; set; }
		public string Status { get; set; }
		public string Remarks { get; set; }
		public string FinalDiagnosis { get; set; }
		public DateTime SubmittedAt { get; set; }
		public string UserName { get; set; }
		public int? UserAge { get; set; }
		public string UserGender { get; set; }
	}

}
