using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace backend.Models
{ 
	public class FlaggedIp
	{ 
		[BsonId]
		[BsonRepresentation(BsonType.ObjectId)]
		public string? Id { get; set; }
		public string IpAddress { get; set; } = null!;
		public string Reason { get; set; } = null!;
		public DateTime FlaggedAt { get; set; }
	}
}