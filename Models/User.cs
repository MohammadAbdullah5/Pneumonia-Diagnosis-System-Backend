using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;

namespace backend.Models
{
	public class User
	{
		[BsonId] // Primary Key
		[BsonRepresentation(BsonType.ObjectId)] // Is a mongodb id
		public string Id { get; set; }
		public string? Name { get; set; }
		public string Email { get; set; } = null!;
		public string Password { get; set; } = null!;
		public string Role { get; set; } = "Patient";
		public bool IsProfileComplete { get; set; } = false;
		public string? PhoneNumber { get; set; }
		public string? Address { get; set; }
		public int? Age { get; set; }
		public string? Gender { get; set; }
		public string MedicalHistory { get; set; }
	}
}
