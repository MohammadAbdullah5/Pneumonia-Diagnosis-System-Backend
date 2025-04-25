using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace backend.Models
{
	public class LoginAttempt
	{
		[BsonId]
		[BsonRepresentation(BsonType.ObjectId)]
		public string Id { get; set; }
		public string Email { get; set; }
		public string IPAddress { get; set; }
		public DateTime AttemptTime { get; set; }
		public bool IsSuccessful { get; set; }
	}

}
