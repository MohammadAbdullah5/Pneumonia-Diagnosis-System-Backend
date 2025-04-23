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
		private readonly IMongoCollection<TwoFactorCode> _mfaCodes;
		private readonly IEmailService _emailService;

		public AuthService(IOptions<MongoDbSettings> options, IConfiguration config, IEmailService service)
		{
			var client = new MongoClient(options.Value.ConnectionString);
			var database = client.GetDatabase(options.Value.DatabaseName);
			_users = database.GetCollection<User>("Users");
			_mfaCodes = database.GetCollection<TwoFactorCode>("MfaCodes");
			_config = config;
			_emailService = service;
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

		public bool VerifyPassword(string inputPassword, string storedPassword)
		{
			return HashPassword(inputPassword) == storedPassword;
		}

		public async Task<bool> GenerateAndSendMfaCode(string userId, string email)
		{
			var code = new Random().Next(100000, 999999).ToString(); // 6-digit
			var mfa = new TwoFactorCode
			{
				UserId = userId,
				Code = code,
				ExpiresAt = DateTime.UtcNow.AddMinutes(5)
			};
			await _mfaCodes.InsertOneAsync(mfa);

			await _emailService.SendEmailAsync(email, "Your verification code", $"Your code is: <b>{code}</b>");

			return true;
		}


		public string GenerateJwtToken(User user)
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

		public async Task<User?> GetUserByEmail(string email)
		{
			return await _users.Find(u => u.Email == email).FirstOrDefaultAsync();
		}

		public async Task<User?> GetUserById(string id)
		{
			return await _users.Find(u => u.Id == id).FirstOrDefaultAsync();
		}

		public async Task<bool> VerifyMfaCode(string userId, string code)
		{
			var filter = Builders<TwoFactorCode>.Filter.And(
				Builders<TwoFactorCode>.Filter.Eq(c => c.UserId, userId),
				Builders<TwoFactorCode>.Filter.Eq(c => c.Code, code),
				Builders<TwoFactorCode>.Filter.Gt(c => c.ExpiresAt, DateTime.UtcNow)
			);

			var twoFactor = await _mfaCodes.Find(filter).FirstOrDefaultAsync();
			if (twoFactor == null)
			{
				return false;
			}

			// Optionally delete the code after successful verification
			await _mfaCodes.DeleteOneAsync(c => c.Id == twoFactor.Id);

			return true;
		}

		public async Task<bool> ResendMfaCode(string userId, string email)
		{
			var filter = Builders<TwoFactorCode>.Filter.Eq(c => c.UserId, userId);
			var existingCode = await _mfaCodes.Find(filter).FirstOrDefaultAsync();

			if (existingCode != null && existingCode.SentAt > DateTime.UtcNow.AddMinutes(-1))
			{
				// Rate limiting: code sent less than 1 min ago
				return false;
			}

			// Remove previous code if exists
			if (existingCode != null)
				await _mfaCodes.DeleteOneAsync(c => c.Id == existingCode.Id);

			await GenerateAndSendMfaCode(userId, email);
			return true;
		}


	}
}
