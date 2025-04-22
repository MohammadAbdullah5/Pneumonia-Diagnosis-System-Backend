using backend.Models;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;

namespace backend.Services
{
	public class DoctorService
	{
		private readonly IMongoCollection<User> _usersCollection;
		private readonly IMongoCollection<DiagnosisRequest> _diagnosisCollection;

		public DoctorService(IMongoDatabase database)
		{

			_usersCollection = database.GetCollection<User>("Users");
			_diagnosisCollection = database.GetCollection<DiagnosisRequest>("DiagnosisRequests");
		}

		public async Task<List<object>> GetPatientsWithDiagnosisCountsAsync()
		{
			var patients = await _usersCollection.Find(u => u.Role == "patient").ToListAsync();

			var result = new List<object>();

			foreach (var patient in patients)
			{
				var diagnosisCount = await _diagnosisCollection
	.CountDocumentsAsync(Builders<DiagnosisRequest>.Filter.Eq("UserId", patient.Id));
				var diagnosisCount1 = await _diagnosisCollection
	.CountDocumentsAsync(Builders<DiagnosisRequest>.Filter.Eq("UserId", new ObjectId(patient.Id.ToString())));
				var diagnosisCount2 = await _diagnosisCollection
	.CountDocumentsAsync(Builders<DiagnosisRequest>.Filter.Eq("UserId", patient.Id.ToString()));


				Console.WriteLine(diagnosisCount);
				Console.WriteLine(diagnosisCount1);
				Console.WriteLine(diagnosisCount2);
				result.Add(new
				{
					Id = patient.Id,
					Name = patient.Name,
					Age = patient.Age ?? 0,
					Gender = patient.Gender ?? "Unknown",
					Diagnoses = diagnosisCount
				});
			}

			return result;
		}
	}
}
