using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace backend.Models
{
	public class TwoFactorCode
	{
		[BsonId]
		[BsonRepresentation(BsonType.ObjectId)]
		public string Id { get; set; }
		public string UserId { get; set; } = null!;
		public string Code { get; set; } = null!;
		public DateTime ExpiresAt { get; set; }
		public DateTime SentAt { get; set; }
	}
}
