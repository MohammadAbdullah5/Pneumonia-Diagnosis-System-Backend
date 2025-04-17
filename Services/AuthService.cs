using Microsoft.AspNetCore.Mvc;
using backend.Models;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;

namespace backend.Services
{
	public class AuthService : Controller
	{
		private readonly IMongoCollection<User> _users;
		private readonly IConfiguration _config;

		public AuthService(IOptions<MongoDbSettings> options, IConfiguration config)
		{
			var client = new MongoClient(options.Value.ConnectionString);
			var database = client.GetDatabase(options.Value.DatabaseName);
			_users = database.GetCollection<User>("Users");
			_config = config;
		}

		public async Task<bool> UserExists(string email)
		{
			return await _users.Find(x => x.Email == email).AnyAsync();
		}

		public async Task<LoginResponse?> Login(string email, string password)
		{
			var user = await _users.Find(x => x.Email == email).FirstOrDefaultAsync();
			if (user == null || !VerifyPassword(password, user.Password))
			{
				return null;
			}
			var token = GenerateJwtToken(user);
			return new LoginResponse {
				Id = user.Id,
				Token = token,
				Name = user.Name,
				Email = user.Email,
				Role = user.Role
			};
		}

		public async Task<LoginResponse> Register(User user)
		{
			user.Password = HashPassword(user.Password);
			await _users.InsertOneAsync(user);
			var createdUser = await _users.Find(u => u.Email == user.Email).FirstOrDefaultAsync();

			var token = GenerateJwtToken(createdUser);
			return new LoginResponse
			{
				Id = createdUser.Id,
				Token = token,
				Name = createdUser.Name,
				Email = createdUser.Email,
				Role = createdUser.Role
			};
		}

		private string HashPassword(string password)
		{
			using var sha = SHA256.Create();
			var bytes = Encoding.UTF8.GetBytes(password);
			return Convert.ToBase64String(sha.ComputeHash(bytes));
		}

		private bool VerifyPassword(string inputPassword, string storedPassword)
		{
			return HashPassword(inputPassword) == storedPassword;
		}

		private string GenerateJwtToken(User user)
		{
			var tokenHandler = new JwtSecurityTokenHandler();
			var key = Encoding.ASCII.GetBytes(_config["Jwt:Key"]);

			var tokenDescriptor = new SecurityTokenDescriptor
			{
				Subject = new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.NameIdentifier, user.Id!),
					new Claim(ClaimTypes.Email, user.Email),
					new Claim(ClaimTypes.Role, user.Role)
				}),
				Expires = DateTime.UtcNow.AddDays(7),
				SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key),
				SecurityAlgorithms.HmacSha256Signature)
			};

			var token = tokenHandler.CreateToken(tokenDescriptor);
			return tokenHandler.WriteToken(token);
		}
	}
}
