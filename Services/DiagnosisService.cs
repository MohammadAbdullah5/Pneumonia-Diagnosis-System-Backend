using MongoDB.Driver;
using backend.Models;
using System.Threading.Tasks;
using MongoDB.Bson;

namespace backend.Services
{
	public class DiagnosisService
	{
		private readonly IMongoCollection<DiagnosisRequest> _diagnosisCollection;

		public DiagnosisService(IMongoDatabase database)
		{
			_diagnosisCollection = database.GetCollection<DiagnosisRequest>("Diagnoses");
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

		public async Task<AIDiagnosisResponse> GetAIAnalysis(string imageUrl)
		{
			using var httpClient = new HttpClient();
			var imageBytes = await httpClient.GetByteArrayAsync(imageUrl);
			using var form = new MultipartFormDataContent();

			var fileExtension = Path.GetExtension(imageUrl).ToLower();

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
			form.Add(byteArrayContent, "file", Path.GetFileName(imageUrl));

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

			return result;
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
