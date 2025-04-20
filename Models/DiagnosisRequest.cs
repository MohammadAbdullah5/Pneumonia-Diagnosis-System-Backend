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
		public string Symptoms { get; set; }
		public string Status { get; set; } = "Pending";
		public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
	}

}
