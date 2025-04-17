namespace backend.Models
{
	public class LoginResponse
	{
		public string Id { get; set; }
		public string Token { get; set; }
		public string Name { get; set; }
		public string Email { get; set; }
		public string Role { get; set; }
	}
}
