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
	public class AuthService
	{
		private readonly IMongoCollection<User> _users;
		private readonly IConfiguration _config;
		private readonly IMongoCollection<TwoFactorCode> _mfaCodes;
		private readonly IMongoCollection<LoginAttempt> _attempts;
		private readonly IMongoCollection<FlaggedIp> _flagIps;

		private readonly IEmailService _emailService;


		public AuthService(IOptions<MongoDbSettings> options, IConfiguration config, IEmailService service)
		{
			var client = new MongoClient(options.Value.ConnectionString);
			var database = client.GetDatabase(options.Value.DatabaseName);
			_users = database.GetCollection<User>("Users");
			_mfaCodes = database.GetCollection<TwoFactorCode>("MfaCodes");
			_config = config;
			_emailService = service;
			_attempts = database.GetCollection<LoginAttempt>("LoginAttempts");
			_flagIps = database.GetCollection<FlaggedIp>("FlaggedIps");
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

		private readonly int bruteForceThreshold = 20;
		private readonly TimeSpan bruteForceWindow = TimeSpan.FromMinutes(1);

		public async Task LogLoginAttempt(string email, string ipAddress, bool success)
		{
			var attempt = new LoginAttempt
			{
				Email = email,
				IPAddress = ipAddress,
				AttemptTime = DateTime.UtcNow,
				IsSuccessful = success
			};

			await _attempts.InsertOneAsync(attempt);

			// If it's a failed attempt, check for brute-force behavior
			if (!success)
			{
				var recentFailedAttempts = await _attempts
					.Find(x => x.IPAddress == ipAddress && !x.IsSuccessful && x.AttemptTime >= DateTime.UtcNow - bruteForceWindow)
					.CountDocumentsAsync();

				if (recentFailedAttempts >= bruteForceThreshold)
				{
					await FlagIpAddress(ipAddress, $"Brute-force suspected: {recentFailedAttempts} failed attempts in {bruteForceWindow.TotalMinutes} minutes.");
				}
			}

		}

		public async Task<bool> IsIpFlagged(string ip)
		{
			// Get all failed login attempts for this IP within the last 1 minute
			var failedAttempts = await _attempts
				.Find(x => x.IPAddress == ip && !x.IsSuccessful && x.AttemptTime >= DateTime.UtcNow - bruteForceWindow)
				.ToListAsync();

			// If there are more than the threshold of failed attempts, the IP is flagged
			return failedAttempts.Count >= bruteForceThreshold;
		}



		public async Task<bool> IsAccountLocked(string email)
		{
			var threshold = 10;
			var window = TimeSpan.FromMinutes(10);
			var since = DateTime.UtcNow.Subtract(window);

			var failedCount = await _attempts.CountDocumentsAsync(
				a => a.Email == email && !a.IsSuccessful && a.AttemptTime >= since
			);

			return failedCount >= threshold;
		}

		public async Task FlagIpAddress(string ipAddress, string reason)
		{
			var existing = await _flagIps.Find(x => x.IpAddress == ipAddress).FirstOrDefaultAsync();
			if (existing == null)
			{
				await _flagIps.InsertOneAsync(new FlaggedIp
				{
					IpAddress = ipAddress,
					Reason = reason,
					FlaggedAt = DateTime.UtcNow
				});
			}
		}

		public async Task UnflagIpAddress(string ipAddress)
		{
			await _flagIps.DeleteOneAsync(x => x.IpAddress == ipAddress);
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

		public async Task ClearFailedAttempts(string email)
		{
			await _attempts.DeleteManyAsync(a => a.Email == email && !a.IsSuccessful);
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

		public async Task<List<LoginAttempt>> GetLoginAttempts()
		{
			return _attempts.Find(_ => true).ToList();
		}

		public async Task<bool> EditDoctorAsync(string email, string newPassword)
		{
			var user = await _users.Find(u => u.Role == "doctor").FirstOrDefaultAsync();
			if (user == null)
				return false;

			var update = Builders<User>.Update
				.Set(u => u.Email, email)
				.Set(u => u.Password, HashPassword(newPassword)); 

			var result = await _users.UpdateOneAsync(u => u.Id == user.Id, update);
			return result.ModifiedCount > 0;
		}

	}
}
