using Microsoft.Extensions.Options;
using MongoDB.Driver;
using backend.Models;

namespace backend.Services
{
	public class UserService
	{
		private readonly IMongoCollection<User> _users;
		public UserService(IOptions<MongoDbSettings> options)
		{
			var client = new MongoClient(options.Value.ConnectionString);
			var database = client.GetDatabase(options.Value.DatabaseName);
			_users = database.GetCollection<User>("Users");
		}

		public async Task<List<User>> GetAsync() =>
			await _users.Find(_ => true).ToListAsync();
		
		public async Task<User?> GetAsync(string id)
		{
			return await _users.Find(x => x.Id == id).FirstOrDefaultAsync();
		}

		public async Task DeleteUser(string id) =>
			await _users.DeleteOneAsync(x => x.Id == id);

		public async Task CreateUser(User user) =>
			await _users.InsertOneAsync(user);

		public async Task UpdateUser(string id, User updatedUser) =>
			await _users.ReplaceOneAsync(x => x.Id == id, updatedUser);

		public async Task<bool> DeleteUserByEmailAsync(string email)
		{
			var result = await _users.DeleteOneAsync(u => u.Email == email);
			return result.DeletedCount > 0;
		}
	}
}
