using MongoDB.Driver;
using backend.Models;
using System.Threading.Tasks;

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
	}
}
