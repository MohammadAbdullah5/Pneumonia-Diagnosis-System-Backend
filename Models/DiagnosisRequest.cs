using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace backend.Models
{
	public class DiagnosisRequest
	{
		[BsonId]
		[BsonRepresentation(BsonType.ObjectId)]
		public string Id { get; set; }
		public string UserId { get; set; } = null!;
		public string ImageUrl { get; set; }
		public string AudioUrl { get; set; }
		public string Symptoms { get; set; }
		public string Status { get; set; } = "Pending";
		public string Remarks { get; set; }
		public string FinalDiagnosis { get; set; }
		public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
		public DateTime DiagnosedAt { get; set; }
		public string EncryptedAESKey { get; set; }
		public string IV { get; set; }
	}

}
